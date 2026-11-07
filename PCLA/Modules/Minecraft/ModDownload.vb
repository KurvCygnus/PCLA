Imports System.Text.Json.Nodes
Imports PCLCS.Download
Imports PCLCS.Net

Public Module ModDownload

    Sub New()
        'C# 数据面（ModRequests）对宿主层全局状态的接线：网络请求的 UI 线程守卫回调（与 ModNet.Sub New 同模式）
        ModRequests.RunInUiProvider = AddressOf RunInUi
        '本模块是下载路径枢纽，可先于 ModNet 成员被触达：幂等接线 PCLCS 网络/登录核心（同 ModLaunch.Sub New）
        ModNet.WirePclcsProviders()
    End Sub

#Region "DlClient* | Minecraft 客户端"

    ''' <summary>
    ''' 返回某 Minecraft 版本对应的原版主 Jar 文件的下载信息，要求对应依赖版本已存在。
    ''' 失败则抛出异常，不需要下载则返回 Nothing。
    ''' </summary>
    Public Function DlClientJarGet(Instance As McInstance, ReturnNothingOnFileUseable As Boolean) As NetFile
        '获取底层继承版本
        Try
            Do While Not String.IsNullOrEmpty(Instance.InheritName)
                Instance = New McInstance(Instance.InheritName)
            Loop
        Catch ex As Exception
            Logger.Warn(ex, "获取底层继承版本失败")
        End Try
        '检查 Json 是否标准
        If Instance.JsonObject("downloads") Is Nothing OrElse Instance.JsonObject("downloads")("client") Is Nothing OrElse Instance.JsonObject("downloads")("client")("url") Is Nothing Then
            Throw New Exception("底层版本 " & Instance.Name & " 中无 jar 文件下载信息")
        End If
        '检查文件
        Dim Checker As New FileChecker With {.MinSize = 1024, .ActualSize = If(Instance.JsonObject("downloads")("client")("size"), -1), .Hash = Instance.JsonObject("downloads")("client")("sha1")}
        If ReturnNothingOnFileUseable AndAlso Checker.Check(Instance.PathVersion & Instance.Name & ".jar") Is Nothing Then Return Nothing '通过校验
        '返回下载信息
        Dim JarUrl As String = Instance.JsonObject("downloads")("client")("url")
        Return New NetFile(DlSourceLauncherOrMetaGet(JarUrl), Instance.PathVersion & Instance.Name & ".jar", Checker)
    End Function

    ''' <summary>
    ''' 返回某 Minecraft 版本对应的原版主 AssetIndex 文件的下载信息，要求对应依赖版本已存在。
    ''' 若未找到，则会返回 Legacy 资源文件或 Nothing。
    ''' </summary>
    Public Function DlClientAssetIndexGet(Instance As McInstance) As NetFile
        '获取底层继承版本
        Do While Not String.IsNullOrEmpty(Instance.InheritName)
            Instance = New McInstance(Instance.InheritName)
        Loop
        '获取信息
        Dim IndexInfo = McAssetsGetIndex(Instance, True, True)
        Dim IndexAddress As String = McFolderSelected & "assets\indexes\" & IndexInfo("id").ToString & ".json"
        Logger.Info($"版本 {Instance.Name} 对应的资源文件索引为 {IndexInfo("id")}")
        Dim IndexUrl As String = If(IndexInfo("url"), "")
        If IndexUrl = "" Then
            Return Nothing
        Else
            Return New NetFile(DlSourceLauncherOrMetaGet(IndexUrl), IndexAddress, New FileChecker With {.IsJson = True})
        End If
    End Function

    ''' <summary>
    ''' 构造补全某 Minecraft 版本的所有文件的加载器列表。失败会抛出异常。
    ''' </summary>
    Public Function DlClientFix(Instance As McInstance, CheckAssetsHash As Boolean, DownloadAssetIndexInBackground As Boolean) As List(Of LoaderBase)
        Dim Loaders As New List(Of LoaderBase)

#Region "下载支持库文件"
        Dim LoadersLib As New List(Of LoaderBase) From {
            New LoaderTask(Of String, List(Of NetFile))("分析缺失支持库文件", Sub(Task As LoaderTask(Of String, List(Of NetFile))) Task.Output = McLibNetFilesFromInstance(Instance)) With {.ProgressWeight = 1},
            New LoaderDownload("下载支持库文件", New List(Of NetFile)) With {.ProgressWeight = 15}
        }
        '构造加载器
        Loaders.Add(New LoaderCombo(Of String)("下载支持库文件（主加载器）", LoadersLib) With {.Block = False, .Show = False, .ProgressWeight = 16})
#End Region

#Region "下载资源文件"
        If ShouldIgnoreFileCheck(Instance) Then
            Logger.Warn("设置要求跳过所有 Assets 检查，文件可能不是最新的", LogBehavior.None)
        Else
            Dim LoadersAssets As New List(Of LoaderBase)
            '获取资源文件索引地址
            Dim TempAddressMain As String = Nothing
            Dim RealAddressMain As String = Nothing
            LoadersAssets.Add(New LoaderTask(Of String, List(Of NetFile))("分析资源文件索引地址",
            Sub(Task As LoaderTask(Of String, List(Of NetFile)))
                Try
                    Dim IndexFile = DlClientAssetIndexGet(Instance)
                    If IndexFile Is Nothing Then
                        Task.Output = New List(Of NetFile)
                        Logger.Warn($"未找到版本 {Instance.Name} 的合适的资源索引下载地址，游戏 assets 可能缺失")
                        Return
                    End If
                    Dim IndexBackgroundFile = PathTemp & "Cache\" & IndexFile.LocalName & "_background"
                    If FileUtils.Exists(IndexBackgroundFile) AndAlso
                       FileUtils.GetInfo(IndexBackgroundFile).LastWriteTime.Date > FileUtils.GetInfo(IndexFile.LocalPath).LastWriteTime.Date Then
                        Logger.Info($"发现后台更新的资源索引缓存文件：{IndexBackgroundFile}")
                        FileUtils.Move(IndexBackgroundFile, IndexFile.LocalPath)
                    End If
                    If DownloadAssetIndexInBackground Then
                        '允许后台更新索引
                        If IndexFile.Check.Check(IndexFile.LocalPath) Is Nothing Then
                            Task.Output = New List(Of NetFile)
                        Else
                            Task.Output = New List(Of NetFile) From {IndexFile}
                        End If
                    Else
                        '强制要求重新下载
                        RealAddressMain = IndexFile.LocalPath
                        TempAddressMain = PathTemp & "Cache\" & IndexFile.LocalName
                        IndexFile.LocalPath = TempAddressMain
                        Task.Output = New List(Of NetFile) From {IndexFile}
                    End If
                Catch ex As Exception
                    Throw New Exception("分析资源文件索引地址失败", ex)
                End Try
            End Sub) With {.ProgressWeight = 0.5, .Show = False})
            '下载资源文件索引
            LoadersAssets.Add(New LoaderDownload("下载资源文件索引", New List(Of NetFile)) With {.ProgressWeight = 2})
            '获取资源文件地址
            LoadersAssets.Add(New LoaderTask(Of String, List(Of NetFile))("获取资源文件列表",
            Sub(Task)
                If TempAddressMain IsNot Nothing Then FileUtils.Move(TempAddressMain, RealAddressMain)
                Task.Output = McAssetsFixList(Instance, CheckAssetsHash, Task)
            End Sub) With {.ProgressWeight = 0.01})
            '下载资源文件
            LoadersAssets.Add(New LoaderDownload("下载资源文件", New List(Of NetFile)) With {.ProgressWeight = 25})
            '构造加载器
            Loaders.Add(New LoaderCombo(Of String)("下载资源文件（主加载器）", LoadersAssets) With {.Block = False, .Show = False, .ProgressWeight = 27})

            '后台独立更新索引
            If DownloadAssetIndexInBackground Then
                Dim RealAddressBg As String = Nothing
                Dim TempAddressBg As String = Nothing
                Dim LoadersAssetsUpdate As New List(Of LoaderBase)
                LoadersAssetsUpdate.Add(New LoaderTask(Of String, List(Of NetFile))("后台分析资源文件索引地址",
                Sub(Task As LoaderTask(Of String, List(Of NetFile)))
                    Dim IndexFile As NetFile = DlClientAssetIndexGet(Instance)
                    If IndexFile Is Nothing Then
                        Logger.Warn($"未找到版本 {Instance.Name} 的合适的资源索引下载地址，游戏 assets 可能缺失")
                        Task.Cancel()
                        Throw New OperationCanceledException
                    End If
                    RealAddressBg = IndexFile.LocalPath
                    TempAddressBg = PathTemp & "Cache\" & IndexFile.LocalName & "_background"
                    IndexFile.LocalPath = TempAddressBg
                    Task.Output = New List(Of NetFile) From {IndexFile}
                    '检查是否需要更新：每天只更新一次
                    Dim LastUpdate As Date = FileUtils.GetInfo(RealAddressBg).LastWriteTime.Date
                    If FileUtils.Exists(RealAddressBg) AndAlso Math.Abs((LastUpdate - Now.Date).TotalDays) < 1 Then
                        Logger.Info($"无需更新资源文件索引，上次更新于 {LastUpdate}")
                        Task.Cancel()
                    End If
                End Sub))
                LoadersAssetsUpdate.Add(New LoaderDownload("后台下载资源文件索引", New List(Of NetFile)))
                Dim Updater As New LoaderCombo(Of String)("后台更新资源文件索引", LoadersAssetsUpdate)
                Logger.Info("开始后台检查资源文件索引")
                Updater.Start()
            End If
        End If
#End Region

        Return Loaders
    End Function

#End Region

#Region "DlClientList | Minecraft 客户端 版本列表"

    ''' <summary>
    ''' 所有正式版的 Minecraft Drop 序数。
    ''' 若从未完成过获取，返回 Nothing；否则必定存在元素，且从高到低排列。
    ''' </summary>
    ''' <remarks>解析面已迁入 C#（PCLCS.Download.Drops.AllDrops）；本属性为宿主层转发，保持既有调用方的名称与赋值用法。</remarks>
    Public Property AllDrops As List(Of Integer)
        Get
            Return Drops.AllDrops
        End Get
        Set(value As List(Of Integer)) '参数类型声明按 VB 风格保留（冗余形参声明为可读性豁免）
            Drops.AllDrops = value
        End Set
    End Property

    '主加载器
    'DlClientListResult 已迁入 C#（PCLCS.Download.DownloadModels.cs），经文件头 Imports PCLCS.Download 逐名转发
    ''' <summary>
    ''' Minecraft 客户端 版本列表，主加载器。
    ''' 若要求镜像源必须包含某个版本，则将该版本 ID 作为输入（#5195）。
    ''' </summary>
    Public DlClientListLoader As New LoaderTask(Of String, DlClientListResult)("DlClientList Main", AddressOf DlClientListMain)
    Private Sub DlClientListMain(Loader As LoaderTask(Of String, DlClientListResult))
        Dim Plan = PCLCS.Minecraft.Download.SourcePolicy.GetPlan(Settings.Get(Of Integer)("ToolDownloadVersion"))
        DlSourceLoader(Loader, DlSourceLoaderList(Plan, DlClientListMojangLoader, DlClientListBmclapiLoader), Loader.IsForceRestarting)
        '提取所有 Drop 序数（结果值已随模型收敛为 STJ JsonObject，无需 ToJsonNode 中转）
        AllDrops = PCLCS.Minecraft.Download.ClientListParser.ComputeDrops(
            Loader.Output.Value.AsObject()).ToList
    End Sub

    '各个下载源的分加载器
    ''' <summary>
    ''' Minecraft 客户端 版本列表，Mojang 官方源加载器。
    ''' </summary>
    Public DlClientListMojangLoader As New LoaderTask(Of String, DlClientListResult)("DlClientList Mojang", AddressOf DlClientListMojangMain)
    Private Sub DlClientListMojangMain(Loader As LoaderTask(Of String, DlClientListResult))
        Dim StartTime As Long = GetTimeMs()
        Dim ResultText As String = NetRequestByClientRetry("https://launchermeta.mojang.com/mc/game/version_manifest.json", RequireJson:=True)
        Try
            Dim Json = PCLCS.Minecraft.Download.ClientListParser.Parse(ResultText, "Mojang 官方源")
            '确定官方源是否可用
            If Not DlPreferMojang Then
                Dim DeltaTime = GetTimeMs() - StartTime
                DlPreferMojang = DeltaTime < 4000
                Logger.Info($"Mojang 官方源加载耗时：{DeltaTime}ms，{If(DlPreferMojang, "可优先使用官方源", "不优先使用官方源")}")
            End If
            '返回（结果值已随模型收敛为 STJ JsonObject，直接赋值）
            Loader.Output = New DlClientListResult With {.IsOfficial = True, .SourceName = "Mojang 官方源", .Value = Json}
            'MC 更新提示
            Static IsHinted As Boolean = False
            Dim Version As String
            '快照版
            Version = Json("latest")("snapshot")
            If Settings.Get(Of Boolean)("ToolUpdateSnapshot") AndAlso Not Settings.Get(Of String)("ToolUpdateSnapshotLast") = "" AndAlso
               Settings.Get(Of String)("ToolUpdateSnapshotLast") <> Version AndAlso Not IsHinted Then
                IsHinted = True
                McDownloadClientUpdateHint(Version, Json.ToJToken())
            End If
            Settings.Set("ToolUpdateSnapshotLast", If(Version, "Nothing"))
            '正式版
            Version = Json("latest")("release")
            If Settings.Get(Of Boolean)("ToolUpdateRelease") AndAlso Not Settings.Get(Of String)("ToolUpdateReleaseLast") = "" AndAlso
               Settings.Get(Of String)("ToolUpdateReleaseLast") <> Version AndAlso Not IsHinted Then
                IsHinted = True
                McDownloadClientUpdateHint(Version, Json.ToJToken())
            End If
            Settings.Set("ToolUpdateReleaseLast", Version)
        Catch ex As Exception
            Throw New Exception("Minecraft 官方源版本列表解析失败", ex)
        End Try
    End Sub
    ''' <summary>
    ''' Minecraft 客户端 版本列表，BMCLAPI 源加载器。
    ''' </summary>
    Public DlClientListBmclapiLoader As New LoaderTask(Of String, DlClientListResult)("DlClientList Bmclapi", AddressOf DlClientListBmclapiMain)
    Private Sub DlClientListBmclapiMain(Loader As LoaderTask(Of String, DlClientListResult))
        Dim ResultText As String = NetRequestByClientRetry("https://bmclapi2.bangbang93.com/mc/game/version_manifest.json", RequireJson:=True)
        Try
            Dim Json = PCLCS.Minecraft.Download.ClientListParser.Parse(ResultText, "BMCLAPI")
            '添加 PCL 特供项
            If FileUtils.Exists(PathTemp & "Cache\download.json") Then
                Dim Extra = LegacyFileUtils.ReadAsJson(PathTemp & "Cache\download.json")
                Dim Versions = CType(JsonNode.Parse(Json.ToJsonString()), System.Text.Json.Nodes.JsonObject)
                For Each Pair In CType(Extra.ToJsonNode(), System.Text.Json.Nodes.JsonObject)
                    Versions(Pair.Key) = Pair.Value.DeepClone()
                Next
                Json = Versions
            End If
            '检查是否有要求的版本（#5195）
            If Not String.IsNullOrEmpty(Loader.Input) AndAlso
               Not PCLCS.Minecraft.Download.ClientListParser.ContainsVersion(Json, Loader.Input) Then
                Throw New PCLCS.Minecraft.Download.DownloadSourceException(
                    PCLCS.Minecraft.Download.DownloadFailureKind.TargetVersionMissing,
                    $"BMCLAPI 源未包含目标版本 {Loader.Input}", "BMCLAPI")
            End If
            '返回（结果值已随模型收敛为 STJ JsonObject，直接赋值）
            Loader.Output = New DlClientListResult With {.IsOfficial = False, .SourceName = "BMCLAPI", .Value = Json}
        Catch ex As Exception
            Throw New Exception("Minecraft BMCLAPI 版本列表解析失败（" & ResultText & "）", ex)
        End Try
    End Sub

    ''' <summary>
    ''' 获取某个版本的 Json 下载地址，若失败则返回 Nothing。必须在工作线程执行。
    ''' </summary>
    Public Function DlClientListGet(Id As String)
        Try
            '确认版本格式标准
            Id = Id.Replace("_", "-") '1.7.10_pre4 在版本列表中显示为 1.7.10-pre4
            If Id <> "1.0" AndAlso Id.EndsWithF(".0") Then Id = Left(Id, Id.Length - 2) 'OptiFine 1.8 的下载会触发此问题，显示版本为 1.8.0
            '获取 Minecraft 版本列表
            Select Case DlClientListLoader.State
                Case LoadState.Finished
                    '从当前的结果获取目标版本…（结果值已随模型收敛为 STJ JsonObject，遍历走 AsArray；CStr 对齐 JToken→String 的取值语义）
                    For Each Version As JsonObject In DlClientListLoader.Output.Value("versions").AsArray()
                        If CStr(Version("id")) = Id Then Return Version("url").ToString
                    Next
                    '…如果没有，则重新尝试获取（在版本刚更新时可能出现这种情况，#5195）
                    DlClientListLoader.WaitForExit(Id, IsForceRestart:=True)
                Case LoadState.Loading
                    DlClientListLoader.WaitForExit(Id)
                Case LoadState.Failed, LoadState.Canceled, LoadState.Waiting
                    DlClientListLoader.WaitForExit(Id, IsForceRestart:=True)
            End Select
            '重新查找版本
            For Each Version As JsonObject In DlClientListLoader.Output.Value("versions").AsArray()
                If CStr(Version("id")) = Id Then Return Version("url").ToString
            Next
            Logger.Warn($"未发现版本 {Id} 的 json 下载地址，版本列表返回为：{vbCrLf}{DlClientListLoader.Output.Value}")
            Return Nothing
        Catch ex As Exception
            Logger.Warn(ex, $"获取版本 {Id} 的 json 下载地址失败")
            Return Nothing
        End Try
    End Function

#End Region

#Region "DlOptiFineList | OptiFine 版本列表"

    'DlOptiFineListResult / DlOptiFineListEntry 已迁入 C#（PCLCS.Download.DownloadModels.cs），经文件头 Imports PCLCS.Download 逐名转发

    ''' <summary>
    ''' OptiFine 版本列表，主加载器。
    ''' </summary>
    Public DlOptiFineListLoader As New LoaderTask(Of Integer, DlOptiFineListResult)("DlOptiFineList Main", AddressOf DlOptiFineListMain)
    Private Sub DlOptiFineListMain(Loader As LoaderTask(Of Integer, DlOptiFineListResult))
        Dim Plan = PCLCS.Minecraft.Download.SourcePolicy.GetPlan(Settings.Get(Of Integer)("ToolDownloadVersion"))
        DlSourceLoader(Loader, DlSourceLoaderList(Plan, DlOptiFineListOfficialLoader, DlOptiFineListBmclapiLoader), Loader.IsForceRestarting)
    End Sub

    ''' <summary>
    ''' OptiFine 版本列表，官方源。
    ''' </summary>
    Public DlOptiFineListOfficialLoader As New LoaderTask(Of Integer, DlOptiFineListResult)("DlOptiFineList Official", AddressOf DlOptiFineListOfficialMain)
    Private Sub DlOptiFineListOfficialMain(Loader As LoaderTask(Of Integer, DlOptiFineListResult))
        Dim Result As String = NetRequestByClientRetry("https://optifine.net/downloads", Encoding:=Encoding.Default)
        Dim Parsed = PCLCS.Minecraft.Download.OptiFineListParser.ParseOfficialHtml(Result)
        Dim Versions As New List(Of DlOptiFineListEntry)
        For Each Entry In Parsed
            Versions.Add(New DlOptiFineListEntry With {
                .DisplayName = Entry.DisplayName,
                .FileName = Entry.FileName,
                .InstanceName = Entry.InstanceName,
                .IsPreview = Entry.IsPreview,
                .Inherit = Entry.Inherit,
                .ReleaseTime = Entry.ReleaseTime,
                .RequiredForgeVersion = Entry.RequiredForgeVersion
            })
        Next
        Loader.Output = New DlOptiFineListResult With {.IsOfficial = True, .SourceName = "OptiFine 官方源", .Value = Versions}
    End Sub

    ''' <summary>
    ''' OptiFine 版本列表，BMCLAPI。
    ''' </summary>
    Public DlOptiFineListBmclapiLoader As New LoaderTask(Of Integer, DlOptiFineListResult)("DlOptiFineList Bmclapi", AddressOf DlOptiFineListBmclapiMain)
    Private Sub DlOptiFineListBmclapiMain(Loader As LoaderTask(Of Integer, DlOptiFineListResult))
        Dim Result As String = NetRequestByClientRetry("https://bmclapi2.bangbang93.com/optifine/versionList", RequireJson:=True)
        Dim Parsed = PCLCS.Minecraft.Download.OptiFineListParser.ParseBmclapiJson(Result)
        Dim Versions As New List(Of DlOptiFineListEntry)
        For Each Entry In Parsed
            Versions.Add(New DlOptiFineListEntry With {
                .DisplayName = Entry.DisplayName,
                .FileName = Entry.FileName,
                .InstanceName = Entry.InstanceName,
                .IsPreview = Entry.IsPreview,
                .Inherit = Entry.Inherit,
                .ReleaseTime = Entry.ReleaseTime,
                .RequiredForgeVersion = Entry.RequiredForgeVersion
            })
        Next
        Loader.Output = New DlOptiFineListResult With {.IsOfficial = False, .SourceName = "BMCLAPI", .Value = Versions}
    End Sub

#End Region

#Region "DlForgeList | Forge Minecraft 版本列表"

    Public Structure DlForgeListResult
        ''' <summary>
        ''' 数据来源名称，如“Official”，“BMCLAPI”。
        ''' </summary>
        Public SourceName As String
        ''' <summary>
        ''' 是否为官方的实时数据。
        ''' </summary>
        Public IsOfficial As Boolean
        ''' <summary>
        ''' 获取到的数据。
        ''' </summary>
        Public Value As List(Of String)
    End Structure

    ''' <summary>
    ''' Forge 版本列表，主加载器。
    ''' </summary>
    Public DlForgeListLoader As New LoaderTask(Of Integer, DlForgeListResult)("DlForgeList Main", AddressOf DlForgeListMain)
    Private Sub DlForgeListMain(Loader As LoaderTask(Of Integer, DlForgeListResult))
        Dim Plan = PCLCS.Minecraft.Download.SourcePolicy.GetPlan(Settings.Get(Of Integer)("ToolDownloadVersion"))
        DlSourceLoader(Loader, DlSourceLoaderList(Plan, DlForgeListOfficialLoader, DlForgeListBmclapiLoader), Loader.IsForceRestarting)
    End Sub

    ''' <summary>
    ''' Forge 版本列表，官方源。
    ''' </summary>
    Public DlForgeListOfficialLoader As New LoaderTask(Of Integer, DlForgeListResult)("DlForgeList Official", AddressOf DlForgeListOfficialMain)
    Private Sub DlForgeListOfficialMain(Loader As LoaderTask(Of Integer, DlForgeListResult))
        Dim Result As String = NetRequestByClientRetry("https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_1.2.4.html", Encoding:=Encoding.Default, Accept:="text/html", SimulateBrowserHeaders:=True)
        Loader.Output = New DlForgeListResult With {.IsOfficial = True, .SourceName = "Forge 官方源",
            .Value = PCLCS.Minecraft.Download.ForgeVersionParser.ParseOfficialHtml(Result).ToList}
    End Sub

    ''' <summary>
    ''' Forge 版本列表，BMCLAPI。
    ''' </summary>
    Public DlForgeListBmclapiLoader As New LoaderTask(Of Integer, DlForgeListResult)("DlForgeList Bmclapi", AddressOf DlForgeListBmclapiMain)
    Private Sub DlForgeListBmclapiMain(Loader As LoaderTask(Of Integer, DlForgeListResult))
        Dim Result As String = NetRequestByClientRetry("https://bmclapi2.bangbang93.com/forge/minecraft",
                                                       Encoding:=Encoding.Default, RequireJson:=True)
        Loader.Output = New DlForgeListResult With {.IsOfficial = False, .SourceName = "BMCLAPI",
            .Value = PCLCS.Minecraft.Download.ForgeVersionParser.ParseBmclapiJson(Result).ToList}
    End Sub

#End Region

#Region "DlForgeVersion | Forge 版本列表"

    'DlForgelikeEntry / DlForgeVersionEntry 已迁入 C#（PCLCS.Download.DownloadModels.cs，含 :561-563 构造特殊分支），经文件头 Imports PCLCS.Download 逐名转发

    ''' <summary>
    ''' Forge 版本列表，主加载器。
    ''' </summary>
    Public Sub DlForgeVersionMain(Loader As LoaderTask(Of String, List(Of DlForgeVersionEntry)))
        Dim DlForgeVersionOfficialLoader As New LoaderTask(Of String, List(Of DlForgeVersionEntry))("DlForgeVersion Official", AddressOf DlForgeVersionOfficialMain)
        Dim DlForgeVersionBmclapiLoader As New LoaderTask(Of String, List(Of DlForgeVersionEntry))("DlForgeVersion Bmclapi", AddressOf DlForgeVersionBmclapiMain)
        Dim Plan = PCLCS.Minecraft.Download.SourcePolicy.GetPlan(Settings.Get(Of Integer)("ToolDownloadVersion"))
        DlSourceLoader(Loader, DlSourceLoaderList(Plan, DlForgeVersionOfficialLoader, DlForgeVersionBmclapiLoader), Loader.IsForceRestarting)
    End Sub

    ''' <summary>
    ''' Forge 版本列表，官方源。
    ''' </summary>
    Public Sub DlForgeVersionOfficialMain(Loader As LoaderTask(Of String, List(Of DlForgeVersionEntry)))
        Dim Result As String
        Try
            Result = NetRequestByLoader("https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_" &
                                          Loader.Input.Replace("-", "_") & '兼容 Forge 1.7.10-pre4，#4057
                                          ".html", SimulateBrowserHeaders:=True)
        Catch ex As HttpRequestCodeException
            If ex.StatusCode <> HttpStatusCode.NotFound Then Throw
            Throw New PCLCS.Minecraft.Download.DownloadSourceException(
                PCLCS.Minecraft.Download.DownloadFailureKind.NotFound, "官方源没有该版本的构建列表", "Forge 官方源")
        End Try
        Loader.Output = PCLCS.Minecraft.Download.ForgeBuildParser.ParseOfficialHtml(Result, Loader.Input).
            Select(Function(Entry) New DlForgeVersionEntry(Entry.VersionName, Entry.Branch, Entry.Inherit) With {
                .Category = Entry.Category,
                .IsRecommended = Entry.IsRecommended,
                .Hash = Entry.Hash,
                .ReleaseTime = Entry.ReleaseTime}).ToList
    End Sub

    ''' <summary>
    ''' Forge 版本列表，BMCLAPI。
    ''' </summary>
    Public Sub DlForgeVersionBmclapiMain(Loader As LoaderTask(Of String, List(Of DlForgeVersionEntry)))
        Dim Result As String = NetRequestByClientRetry("https://bmclapi2.bangbang93.com/forge/minecraft/" &
            Loader.Input.Replace("-", "_"), RequireJson:=True) '兼容 Forge 1.7.10-pre4，#4057
        Dim Recommended As String = McDownloadForgeRecommendedGet(Loader.Input)
        Loader.Output = PCLCS.Minecraft.Download.ForgeBuildParser.ParseBmclapiJson(Result, Loader.Input, Recommended).
            Select(Function(Entry) New DlForgeVersionEntry(Entry.VersionName, Entry.Branch, Entry.Inherit) With {
                .Category = Entry.Category,
                .IsRecommended = Entry.IsRecommended,
                .Hash = Entry.Hash,
                .ReleaseTime = Entry.ReleaseTime}).ToList
    End Sub

#End Region

#Region "DlNeoForgeList | NeoForge 版本列表"

    'DlNeoForgeListResult / DlNeoForgeListEntry 已迁入 C#（PCLCS.Download.DownloadModels.cs），经文件头 Imports PCLCS.Download 逐名转发

    ''' <summary>
    ''' NeoForge 版本列表，主加载器。
    ''' </summary>
    Public DlNeoForgeListLoader As New LoaderTask(Of Integer, DlNeoForgeListResult)("DlNeoForgeList Main", AddressOf DlNeoForgeListMain)
    Private Sub DlNeoForgeListMain(Loader As LoaderTask(Of Integer, DlNeoForgeListResult))
        Dim Plan = PCLCS.Minecraft.Download.SourcePolicy.GetPlan(Settings.Get(Of Integer)("ToolDownloadVersion"))
        DlSourceLoader(Loader, DlSourceLoaderList(Plan, DlNeoForgeListOfficialLoader, DlNeoForgeListBmclapiLoader), Loader.IsForceRestarting)
    End Sub

    ''' <summary>
    ''' NeoForge 版本列表，官方源。
    ''' </summary>
    Public DlNeoForgeListOfficialLoader As New LoaderTask(Of Integer, DlNeoForgeListResult)("DlNeoForgeList Official", AddressOf DlNeoForgeListOfficialMain)
    Private Sub DlNeoForgeListOfficialMain(Loader As LoaderTask(Of Integer, DlNeoForgeListResult))
        '获取版本列表 JSON
        Dim ResultLatest As String = NetRequestByClientRetry("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", RequireJson:=True)
        Dim ResultLegacy As String = NetRequestByClientRetry("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/forge", RequireJson:=True)
        Loader.Output = New DlNeoForgeListResult With {.IsOfficial = True, .SourceName = "NeoForge 官方源",
            .Value = PCLCS.Minecraft.Download.NeoForgeParser.Parse(ResultLatest, ResultLegacy).
                Select(Function(Entry) New DlNeoForgeListEntry(Entry.ApiName)).ToList}
    End Sub

    ''' <summary>
    ''' NeoForge 版本列表，BMCLAPI。
    ''' </summary>
    Public DlNeoForgeListBmclapiLoader As New LoaderTask(Of Integer, DlNeoForgeListResult)("DlNeoForgeList Bmclapi", AddressOf DlNeoForgeListBmclapiMain)
    Public Sub DlNeoForgeListBmclapiMain(Loader As LoaderTask(Of Integer, DlNeoForgeListResult))
        '获取版本列表 JSON
        Dim ResultLatest As String = NetRequestByClientRetry("https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge", RequireJson:=True)
        Dim ResultLegacy As String = NetRequestByClientRetry("https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/forge", RequireJson:=True)
        '//? 既存缺陷：BMCLAPI 源也标记为官方数据，会误导上层对来源的判断；修复需要单独评估 UI 影响。
        Loader.Output = New DlNeoForgeListResult With {.IsOfficial = True, .SourceName = "BMCLAPI",
            .Value = PCLCS.Minecraft.Download.NeoForgeParser.Parse(ResultLatest, ResultLegacy).
                Select(Function(Entry) New DlNeoForgeListEntry(Entry.ApiName)).ToList}
    End Sub

#End Region

#Region "DlLiteLoaderList | LiteLoader 版本列表"

    'DlLiteLoaderListResult / DlLiteLoaderListEntry 已迁入 C#（PCLCS.Download.DownloadModels.cs），经文件头 Imports PCLCS.Download 逐名转发

    ''' <summary>
    ''' LiteLoader 版本列表，主加载器。
    ''' </summary>
    Public DlLiteLoaderListLoader As New LoaderTask(Of Integer, DlLiteLoaderListResult)("DlLiteLoaderList Main", AddressOf DlLiteLoaderListMain)
    Private Sub DlLiteLoaderListMain(Loader As LoaderTask(Of Integer, DlLiteLoaderListResult))
        Dim Plan = PCLCS.Minecraft.Download.SourcePolicy.GetPlan(Settings.Get(Of Integer)("ToolDownloadVersion"))
        DlSourceLoader(Loader, DlSourceLoaderList(Plan, DlLiteLoaderListOfficialLoader, DlLiteLoaderListBmclapiLoader), Loader.IsForceRestarting)
    End Sub

    ''' <summary>
    ''' LiteLoader 版本列表，官方源。
    ''' </summary>
    Public DlLiteLoaderListOfficialLoader As New LoaderTask(Of Integer, DlLiteLoaderListResult)("DlLiteLoaderList Official", AddressOf DlLiteLoaderListOfficialMain)
    Private Sub DlLiteLoaderListOfficialMain(Loader As LoaderTask(Of Integer, DlLiteLoaderListResult))
        Dim Result As String = NetRequestByClientRetry("https://dl.liteloader.com/versions/versions.json", RequireJson:=True)
        Dim Versions As New List(Of DlLiteLoaderListEntry)
        For Each Entry In PCLCS.Minecraft.Download.LiteLoaderParser.Parse(Result)
            Versions.Add(New DlLiteLoaderListEntry With {
                .FileName = Entry.FileName,
                .IsPreview = Entry.IsPreview,
                .Inherit = Entry.Inherit,
                .IsLegacy = Entry.IsLegacy,
                .ReleaseTime = Entry.ReleaseTime,
                .MD5 = Entry.MD5,
                .JsonToken = Entry.JsonToken})
        Next
        Loader.Output = New DlLiteLoaderListResult With {.IsOfficial = True, .SourceName = "LiteLoader 官方源", .Value = Versions}
    End Sub

    ''' <summary>
    ''' LiteLoader 版本列表，BMCLAPI。
    ''' </summary>
    Public DlLiteLoaderListBmclapiLoader As New LoaderTask(Of Integer, DlLiteLoaderListResult)("DlLiteLoaderList Bmclapi", AddressOf DlLiteLoaderListBmclapiMain)
    Private Sub DlLiteLoaderListBmclapiMain(Loader As LoaderTask(Of Integer, DlLiteLoaderListResult))
        Dim Result As String = NetRequestByClientRetry("https://bmclapi2.bangbang93.com/maven/com/mumfrey/liteloader/versions.json", RequireJson:=True)
        Dim Versions As New List(Of DlLiteLoaderListEntry)
        For Each Entry In PCLCS.Minecraft.Download.LiteLoaderParser.Parse(Result)
            Versions.Add(New DlLiteLoaderListEntry With {
                .FileName = Entry.FileName,
                .IsPreview = Entry.IsPreview,
                .Inherit = Entry.Inherit,
                .IsLegacy = Entry.IsLegacy,
                .ReleaseTime = Entry.ReleaseTime,
                .MD5 = Entry.MD5,
                .JsonToken = Entry.JsonToken})
        Next
        Loader.Output = New DlLiteLoaderListResult With {.IsOfficial = False, .SourceName = "BMCLAPI", .Value = Versions}
    End Sub

#End Region

#Region "DlFabricList | Fabric 列表"

    'DlFabricListResult 已迁入 C#（PCLCS.Download.DownloadModels.cs），经文件头 Imports PCLCS.Download 逐名转发

    ''' <summary>
    ''' Fabric 列表，主加载器。
    ''' </summary>
    Public DlFabricListLoader As New LoaderTask(Of Integer, DlFabricListResult)("DlFabricList Main", AddressOf DlFabricListMain)
    Private Sub DlFabricListMain(Loader As LoaderTask(Of Integer, DlFabricListResult))
        Dim Plan = PCLCS.Minecraft.Download.SourcePolicy.GetPlan(Settings.Get(Of Integer)("ToolDownloadVersion"))
        DlSourceLoader(Loader, DlSourceLoaderList(Plan, DlFabricListOfficialLoader, DlFabricListBmclapiLoader), Loader.IsForceRestarting)
    End Sub

    ''' <summary>
    ''' Fabric 列表，官方源。
    ''' </summary>
    Public DlFabricListOfficialLoader As New LoaderTask(Of Integer, DlFabricListResult)("DlFabricList Official", AddressOf DlFabricListOfficialMain)
    Private Sub DlFabricListOfficialMain(Loader As LoaderTask(Of Integer, DlFabricListResult))
        Dim Result As String = NetRequestByClientRetry("https://meta.fabricmc.net/v2/versions", RequireJson:=True)
        '结果值已随模型收敛为 STJ JsonObject，直接赋值
        Loader.Output = New DlFabricListResult With {.IsOfficial = True, .SourceName = "Fabric 官方源",
            .Value = PCLCS.Minecraft.Download.FabricListParser.ParseOrThrow(Result)}
    End Sub

    ''' <summary>
    ''' Fabric 列表，BMCLAPI。
    ''' </summary>
    Public DlFabricListBmclapiLoader As New LoaderTask(Of Integer, DlFabricListResult)("DlFabricList Bmclapi", AddressOf DlFabricListBmclapiMain)
    Private Sub DlFabricListBmclapiMain(Loader As LoaderTask(Of Integer, DlFabricListResult))
        Dim Result As String = NetRequestByClientRetry("https://bmclapi2.bangbang93.com/fabric-meta/v2/versions", RequireJson:=True)
        '结果值已随模型收敛为 STJ JsonObject，直接赋值
        Loader.Output = New DlFabricListResult With {.IsOfficial = False, .SourceName = "BMCLAPI",
            .Value = PCLCS.Minecraft.Download.FabricListParser.ParseOrThrow(Result)}
    End Sub

    ''' <summary>
    ''' Fabric API 列表，官方源。
    ''' </summary>
    Public DlFabricApiLoader As New LoaderTask(Of Integer, List(Of ResourceVersion))("Fabric API List Loader",
        Sub(Task As LoaderTask(Of Integer, List(Of ResourceVersion))) Task.Output = ResourceVersion.FromProjectId("fabric-api", ResourcePlatforms.Modrinth, LoadDependencies:=False))

    ''' <summary>
    ''' OptiFabric 列表，官方源。
    ''' </summary>
    Public DlOptiFabricLoader As New LoaderTask(Of Integer, List(Of ResourceVersion))("OptiFabric List Loader",
        Sub(Task As LoaderTask(Of Integer, List(Of ResourceVersion))) Task.Output = ResourceVersion.FromProjectId("322385", ResourcePlatforms.CurseForge, LoadDependencies:=False))

#End Region

#Region "DlMod | Mod 镜像源请求"

    ''' <summary>
    ''' 对可能涉及 Mod 镜像源的请求进行处理，返回 JToken。
    ''' 调用 NetRequest，会进行重试。
    ''' </summary>
    ''' <remarks>策略核已迁入 C#（PCLCS.Download.ModRequests.DlModRequest：MCIM 源排列与 ToolDownloadMod 三档策略）；本方法为宿主层转发。</remarks>
    Public Function DlModRequest(Url As String, Optional Method As HttpMethod = Nothing,
                                 Optional Content As String = Nothing, Optional ContentType As String = Nothing) As JToken
        Return ModRequests.DlModRequest(Url, Method, Content, ContentType).ToJToken()
    End Function

#End Region

#Region "DlSource | 镜像下载源"

    Private DlPreferMojang As Boolean = False
    ''' <summary>
    ''' 下载文件（而非获取版本列表）的时候，是否优先使用官方源。
    ''' </summary>
    Public ReadOnly Property DlSourcePreferMojang As Boolean
        Get
            Return Settings.Get(Of Integer)("ToolDownloadSource") = 2 OrElse (Settings.Get(Of Integer)("ToolDownloadSource") = 1 AndAlso DlPreferMojang)
        End Get
    End Property
    ''' <summary>
    ''' 下载文件（而非获取版本列表）的时候，根据是否优先使用官方源决定使用 Url 的顺序。
    ''' </summary>
    Public Function DlSourceOrder(OfficialUrls As IEnumerable(Of String), MirrorUrls As IEnumerable(Of String)) As IEnumerable(Of String)
        Return PCLCS.Minecraft.Download.DownloadSources.Order(OfficialUrls, MirrorUrls, DlSourcePreferMojang)
    End Function
    ''' <summary>
    ''' 获取版本列表（而非下载文件）的时候，是否优先使用官方源。
    ''' </summary>
    Public ReadOnly Property DlVersionListPreferMojang As Boolean
        Get
            Return PCLCS.Minecraft.Download.SourcePolicy.PreferMojangForVersionList(Settings.Get(Of Integer)("ToolDownloadVersion"), DlPreferMojang)
        End Get
    End Property
    ''' <summary>
    ''' 获取版本列表（而非下载文件）的时候，根据是否优先使用官方源决定使用 Url 的顺序。
    ''' </summary>
    Public Function DlVersionListOrder(OfficialUrls As IEnumerable(Of String), MirrorUrls As IEnumerable(Of String)) As IEnumerable(Of String)
        Return PCLCS.Minecraft.Download.DownloadSources.Order(OfficialUrls, MirrorUrls, DlVersionListPreferMojang)
    End Function

    ''' <summary>
    ''' 下载 Assets 文件。
    ''' </summary>
    Public Function DlSourceAssetsGet(Original As String) As IEnumerable(Of String)
        Return PCLCS.Minecraft.Download.DownloadSources.Assets(Original, DlSourcePreferMojang)
    End Function
    ''' <summary>
    ''' 下载 Libraries 文件。
    ''' </summary>
    Public Function DlSourceLibraryGet(Original As String) As IEnumerable(Of String)
        Return PCLCS.Minecraft.Download.DownloadSources.Library(Original, DlSourcePreferMojang)
    End Function
    ''' <summary>
    ''' 下载 Launcher 或 Meta 文件。
    ''' 不应使用它来获取版本列表（因为它只使用文件下载源设置来决定源顺序）。
    ''' </summary>
    Public Function DlSourceLauncherOrMetaGet(Original As String) As IEnumerable(Of String)
        If Original Is Nothing Then Throw New Exception("无对应的 json 下载地址")
        Return PCLCS.Minecraft.Download.DownloadSources.LauncherOrMeta(Original, DlSourcePreferMojang)
    End Function

    'Mod 下载源
    Public Function DlSourceModGet(Original As String) As String
        Return PCLCS.Minecraft.Download.DownloadSources.Mod(Original)
    End Function

    'Loader 自动切换
    Private Sub DlSourceLoader(Of InputType, OutputType)(MainLoader As LoaderTask(Of InputType, OutputType),
                                                         LoaderList As List(Of KeyValuePair(Of LoaderTask(Of InputType, OutputType), Integer)),
                                                         Optional IsForceRestart As Boolean = False)
        Dim WaitCycle As Integer = 0
        Do While True
            '检查状态
            Dim BeforeLoadersAllFailed As Boolean = True
            For Each SubLoader In LoaderList
                If WaitCycle = 0 Then '判断是否可以不加载，直接使用已经加载好的结果
                    If IsForceRestart Then Continue For '强制刷新，不行
                    If (SubLoader.Key.Input Is Nothing Xor MainLoader.Input Is Nothing) OrElse
                       (SubLoader.Key.Input IsNot Nothing AndAlso Not SubLoader.Key.Input.Equals(MainLoader.Input)) Then Continue For '父子加载器的输入不一样，也不行
                End If
                If SubLoader.Key.State <> LoadState.Failed Then BeforeLoadersAllFailed = False
                If SubLoader.Key.State = LoadState.Finished Then
                    '检查加载器成功
                    MainLoader.Output = SubLoader.Key.Output
                    DlSourceLoaderCancel(LoaderList)
                    Return
                ElseIf BeforeLoadersAllFailed Then
                    '此前的加载器全部失败，直接启动后续加载器
                    If WaitCycle < SubLoader.Value * 100 Then WaitCycle = SubLoader.Value * 100
                End If
            Next
            '第一轮时：既然不直接使用已经加载好的结果，那就启动第一个加载器
            If WaitCycle = 0 Then
                LoaderList.First.Key.Start(MainLoader.Input, IsForceRestart)
                For Each Loader In LoaderList.Skip(1)
                    Loader.Key.State = LoadState.Waiting '将其他源标记为未启动，以确保可以切换下载源（#184）
                Next
            End If
            '检查加载器失败或超时
            For i = 0 To LoaderList.Count - 1
                If WaitCycle <> LoaderList(i).Value * 100 Then Continue For
                If i < LoaderList.Count - 1 AndAlso Not LoaderList.All(Function(l) l.Key.State = LoadState.Failed) Then
                    '若还有下一个源，则启动下一个源
                    LoaderList(i + 1).Key.Start(MainLoader.Input, IsForceRestart)
                Else
                    '若没有，则失败
                    Dim ErrorInfo As Exception = Nothing
                    For ii = 0 To LoaderList.Count - 1
                        LoaderList(ii).Key.Input = Nothing '重置输入，以免以同样的输入“重试加载”时直接失败
                        If LoaderList(ii).Key.Error IsNot Nothing Then
                            If ErrorInfo Is Nothing OrElse IsUnavailableSourceError(LoaderList(ii).Key.Error) Then
                                ErrorInfo = LoaderList(ii).Key.Error
                            End If
                        End If
                    Next
                    If ErrorInfo Is Nothing Then ErrorInfo = New PCLCS.Minecraft.Download.DownloadSourceException(
                        PCLCS.Minecraft.Download.DownloadFailureKind.Timeout, "下载源连接超时")
                    DlSourceLoaderCancel(LoaderList)
                    Throw ErrorInfo
                End If
                Exit For
            Next
            '计时
            Thread.Sleep(10)
            WaitCycle += 1
            '检查父加载器中断
            If MainLoader.IsCanceled Then
                DlSourceLoaderCancel(LoaderList)
                Return
            End If
        Loop
    End Sub
    Private Sub DlSourceLoaderCancel(Of InputType, OutputType)(LoaderList As List(Of KeyValuePair(Of LoaderTask(Of InputType, OutputType), Integer)))
        For Each Loader In LoaderList
            If Loader.Key.State = LoadState.Loading Then Loader.Key.Cancel()
        Next
    End Sub

    ''' <summary>
    ''' 判断异常链中是否包含“该下载源没有目标数据”的错误，这类错误比网络错误更能说明失败原因。
    ''' </summary>
    Private Function IsUnavailableSourceError(ErrorInfo As Exception) As Boolean
        Dim Current As Exception = ErrorInfo
        Do While Current IsNot Nothing
            If TypeOf Current Is PCLCS.Minecraft.Download.DownloadSourceException Then
                Return CType(Current, PCLCS.Minecraft.Download.DownloadSourceException).IsUnavailable
            End If
            If TypeOf Current Is PCLCS.Results.PclaException Then
                Dim InnerError = CType(Current, PCLCS.Results.PclaException).Error
                If TypeOf InnerError Is PCLCS.Results.DownloadError Then
                    Return CType(InnerError, PCLCS.Results.DownloadError).IsUnavailable
                End If
            End If
            Current = Current.InnerException
        Loop
        Return False
    End Function

    ''' <summary>
    ''' 按策略把官方源与镜像源排列为加载器列表，超时来自 <see cref="PCLCS.Minecraft.Download.SourcePlan"/>。
    ''' </summary>
    Private Function DlSourceLoaderList(Of TInput, TOutput)(
        Plan As PCLCS.Minecraft.Download.SourcePlan,
        Official As LoaderTask(Of TInput, TOutput),
        Bmclapi As LoaderTask(Of TInput, TOutput)) As List(Of KeyValuePair(Of LoaderTask(Of TInput, TOutput), Integer))
        Return New List(Of KeyValuePair(Of LoaderTask(Of TInput, TOutput), Integer)) From {
            New KeyValuePair(Of LoaderTask(Of TInput, TOutput), Integer)(If(Plan.OfficialFirst, Official, Bmclapi), Plan.FirstSeconds),
            New KeyValuePair(Of LoaderTask(Of TInput, TOutput), Integer)(If(Plan.OfficialFirst, Bmclapi, Official), Plan.SecondSeconds)
        }
    End Function

#End Region

End Module