Imports System.Collections.ObjectModel
Imports System.Text.Json.Nodes
Imports System.Text.RegularExpressions
Imports PCLCS.Install 'McInstallRequest 已迁入 C#
Imports PCLCS.Modpack 'ModpackDetect / ModpackDeploy 已迁入 C#
Imports PCLCS.Net
Imports PCLCS.Settings 'SettingsService 版本级设置 instanceKey 面

Public Module ModModpack

    '触发整合包安装的外部接口
    ''' <summary>
    ''' 弹窗要求选择一个整合包文件并进行安装。
    ''' </summary>
    Public Sub ModpackInstall()
        If RestrictMcDownload Then
            Hint("为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能，非常抱歉 qwq……", HintType.Red)
            Return
        End If
        Dim File As String = Dialogs.SelectFile("选择整合包压缩文件", False, filter:={({"rar", "zip", "mrpack"}, "整合包文件")}).FirstOrDefault() '选择整合包文件
        If String.IsNullOrEmpty(File) Then Return
        RunInThread(
        Sub()
            Try
                ModpackInstall(File)
            Catch ex As Exception
                ex.ThrowIfCanceled()
                Logger.Error(ex, "手动安装整合包失败", LogBehavior.Alert)
            End Try
        End Sub)
    End Sub
    ''' <summary>
    ''' 构建并启动安装给定的整合包文件的加载器，并返回该加载器。若失败则抛出异常。
    ''' 必须在工作线程执行。
    ''' </summary>
    Public Function ModpackInstall(File As String, Optional InstanceName As String = Nothing, Optional Logo As String = Nothing) As LoaderCombo(Of String)
        Logger.Info($"整合包安装请求：{If(File, "null")}")
        '字符校验
        Dim TargetFolder As String = $"{McFolderSelected}versions\{InstanceName}\"
        If TargetFolder.Contains("!") OrElse TargetFolder.Contains(";") Then Hint("游戏路径中不能含有感叹号或分号：" & TargetFolder, HintType.Red) : Throw New OperationCanceledException
        '获取整合包种类与关键 Json（识别链已迁 C#：ModpackDetect.Detect，:44-79 判定链逐行；
        'rar 拒绝与打开失败的三分支包装（:80-88）随识别核一并迁入）
        Dim ArchiveBaseFolder As String = ""
        Dim PackType As Integer
        Select Case ModpackDetect.Detect(File, ArchiveBaseFolder)
            Case ModpackKind.CurseForge : PackType = 0
            Case ModpackKind.Hmcl : PackType = 1
            Case ModpackKind.Mcm : PackType = 2
            Case ModpackKind.Mcbbs : PackType = 3
            Case ModpackKind.Modrinth : PackType = 4
            Case ModpackKind.LauncherPack : PackType = 9
            Case Else : PackType = -1
        End Select
        '执行对应的安装方法
        If RestrictMcDownload Then Throw New Exception("为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能，非常抱歉 qwq……")
        Dim Archive As ZipArchive = FileUtils.OpenZip(File)
        Try
            Select Case PackType
                Case 0
                    Logger.Info("整合包种类：CurseForge")
                    Return InstallPackCurseForge(File, Archive, ArchiveBaseFolder, InstanceName, Logo)
                Case 1
                    Logger.Info("整合包种类：HMCL")
                    Return InstallPackHMCL(File, Archive, ArchiveBaseFolder)
                Case 2
                    Logger.Info("整合包种类：MMC")
                    Return InstallPackMMC(File, Archive, ArchiveBaseFolder)
                Case 3
                    Logger.Info("整合包种类：MCBBS")
                    Return InstallPackMCBBS(File, Archive, ArchiveBaseFolder, InstanceName)
                Case 4
                    Logger.Info("整合包种类：Modrinth")
                    Return InstallPackModrinth(File, Archive, ArchiveBaseFolder, InstanceName, Logo)
                Case 9
                    Logger.Info("整合包种类：带启动器的压缩包")
                    Return InstallPackLauncherPack(File, Archive, ArchiveBaseFolder)
                Case Else
                    Logger.Info("整合包种类：未能识别，假定为压缩包")
                    Return InstallPackCompress(File, Archive)
            End Select
        Finally
            Archive.Dispose()
        End Try
    End Function

    Private Sub ExtractModpackFiles(InstallTemp As String, FileAddress As String, Optional c As CancellationToken = Nothing, Optional p As ProgressProvider = Nothing)
        DirectoryUtils.Delete(InstallTemp)
        c.ThrowIfCancellationRequested()
        FileUtils.ExtractToDirectory(FileAddress, InstallTemp, c, p)
    End Sub
    ''' <summary>
    ''' 从整合包的 override 目录复制文件，同时设置 PCL 的配置文件与版本隔离。
    ''' 对路径末尾是否为 \ 没有要求。
    ''' </summary>
    ''' <remarks>决策核已迁 C#（ModpackDeploy.CopyOverrideDirectory，:126-151：目录复制、实体 ini 键与
    ''' VersionArgumentIndie/V2 的版本级设置 instanceKey 面、IniClearCache 等价的缓存重置）；Loader 参数已随决策面移除。</remarks>
    Private Sub CopyOverrideDirectory(OverridesFolder As String, VersionFolder As String)
        ModpackDeploy.CopyOverrideDirectory(OverridesFolder, VersionFolder, VersionFolder)
    End Sub
    ''' <summary>
    ''' 弹窗提示整合包中存在不兼容的加载器。
    ''' 如果取消，则抛出 CancelledException。
    ''' </summary>
    Private Sub NotifyIncompatibleLoader(LoaderName As String)
        If MyMsgBox($"整合包中存在不兼容的加载器：{LoaderName}{vbCrLf}如果你知道如何手动安装它，可以先选择跳过，然后在整合包下载结束后手动安装。",
            "不兼容的加载器", "取消", $"不安装 {LoaderName} 并继续") = 1 Then Throw New OperationCanceledException
    End Sub

#Region "不同类型整合包的安装方法"

    'CurseForge
    Private Function InstallPackCurseForge(FileAddress As String, Archive As ZipArchive, ArchiveBaseFolder As String,
                                           Optional InstanceName As String = Nothing, Optional Logo As String = Nothing) As LoaderCombo(Of String)

        '读取 Json 文件（读取与解析留宿主层，解析失败包装随宿主层保留）
        Dim ManifestJson As String, Json As JObject
        Try
            ManifestJson = Archive.GetEntry(ArchiveBaseFolder & "manifest.json").Open().ReadString()
            Json = ManifestJson.DeserializeJson()
        Catch ex As Exception
            Throw New Exception("CurseForge 整合包安装信息存在问题", ex)
        End Try
        '预检以保持旧顺序（对话框前抛出；C# 计划核内有同检）
        If Json("minecraft") Is Nothing OrElse Json("minecraft")("version") Is Nothing Then Throw New Exception("CurseForge 整合包未提供 Minecraft 版本信息")

        '获取版本名
        If InstanceName Is Nothing Then
            InstanceName = If(Json("name"), "")
            Dim Validate As New ValidateFolderName(McFolderSelected & "versions")
            If Validate.Validate(InstanceName) <> "" Then InstanceName = ""
            If InstanceName = "" Then InstanceName = MyMsgBoxInput("输入版本名称", "", "", New Collection(Of Validate) From {Validate})
            If String.IsNullOrEmpty(InstanceName) Then Throw New OperationCanceledException
        End If
        Dim VersionFolder As String = PathUtils.AddSlashSuffix(Path.Combine(McFolderSelected, "versions", InstanceName) & "\")

        'manifest 解析、modLoaders 映射与 files 分类已迁 C#（ModpackDeploy.BuildCurseForgePlan，:167-231；
        '不兼容加载器经回调走 NotifyIncompatibleLoader，缺字段条目经回调走 Hint）
        Dim Plan = ModpackDeploy.BuildCurseForgePlan(ManifestJson, VersionFolder, AddressOf NotifyIncompatibleLoader, Sub(Message) Hint(Message, HintType.Red))
        '解压
        Dim InstallTemp As String = RequestTaskTempFolder()
        Dim InstallLoaders As New List(Of LoaderBase)
        Dim OverrideHome As String = Plan.OverridesHome
        If OverrideHome <> "" Then
            '覆写文件夹定位与逃逸校验已迁 C#（ModpackDeploy.ResolveCurseForgeOverridesFolder，#5613，:212-213）
            Dim OverridesFolder As String = ModpackDeploy.ResolveCurseForgeOverridesFolder(InstallTemp, ArchiveBaseFolder, OverrideHome)
            InstallLoaders.Add(New LoaderTask(Of String, Integer)("解压整合包文件",
            Sub(Task As LoaderTask(Of String, Integer))
                ExtractModpackFiles(InstallTemp, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage:=0.6))
                CopyOverrideDirectory(OverridesFolder, VersionFolder)
            End Sub) With {
            .ProgressWeight = FileUtils.GetInfo(FileAddress).Length / 1024 / 1024 / 6, .Block = False}) '每 6M 需要 1s
        End If
        '获取 Mod 列表（已随 BuildCurseForgePlan 迁 C#：Plan.FileIds / Plan.OptionalFileIds，:224-231）
        If Plan.FileIds.Any Then
            Dim ModDownloadLoaders As New List(Of LoaderBase)
            '获取 Mod 下载信息
            ModDownloadLoaders.Add(New LoaderTask(Of Integer, JArray)("获取 Mod 下载信息",
            Sub(Task As LoaderTask(Of Integer, JArray))
                '批量请求组装已迁 C#（ModpackDeploy.BuildCurseForgeBatchRequest，:237）；请求执行走宿主层 DlModRequest 转发
                Dim Request = ModpackDeploy.BuildCurseForgeBatchRequest(Plan.FileIds)
                Task.Output = DlModRequest(Request.Url, Request.Method, Request.Content, Request.ContentType)("data")
                If Plan.FileIds.Count = Task.Output.Count Then Return
                '如果文件已被删除，则 API 会跳过那一项，导致返回的列表数量小于请求的数量。后面的都是对此的错误处理。
                '获取缺失的 fileId 列表与 projectId 映射已迁 C#（FindMissingCurseForgeFileIds / MapMissingCurseForgeFiles，:241-245）
                Dim MissingIds = ModpackDeploy.FindMissingCurseForgeFileIds(Plan.FileIds, CType(CType(Task.Output, JArray).ToJsonNode(), JsonArray))
                Logger.Info($"缺失的 fileIds：{MissingIds.Join("、")}")
                Dim Missings = ModpackDeploy.MapMissingCurseForgeFiles(Plan, MissingIds)
                Logger.Info($"缺失项：{String.Join("、", Missings)}")
                '尝试获取对应的工程信息（ResourceProject 属宿主层 Resource 模块，留 VB）
                Dim Projects As New List(Of ResourceProject)
                Try
                    Projects = ResourceProject.FromProjectIds(Missings.Select(Function(e) e.Item1.ToString), ResourcePlatforms.CurseForge).ToList
                Catch ex As Exception
                    Logger.Warn(ex, "获取缺失的工程信息失败")
                End Try
                Dim MissingDesc As New List(Of String)
                For Each Missing In Missings
                    Dim Project = Projects.FirstOrDefault(Function(p) p.Id = Missing.Item1)
                    If Project Is Nothing Then
                        MissingDesc.Add($"未知 CurseForge 项目 (项目 ID {Missing.Item1}，文件 ID {Missing.Item2})")
                    Else
                        MissingDesc.Add($"{Project.TranslatedName} (文件 ID {Missing.Item2})")
                    End If
                Next
                If MyMsgBox($"未找到该整合包所需的以下内容，它们可能已被原作者删除：{vbCrLf}- {MissingDesc.Join(vbCrLf + "- ")}{vbCrLf}{vbCrLf}是否继续安装？缺失这些内容可能导致整合包出现异常！",
                    "整合包内容缺失", "继续", "取消", IsWarn:=True) = 2 Then
                    Task.Cancel()
                End If
            End Sub) With {.ProgressWeight = Plan.FileIds.Count / 10}) '每 10 Mod 需要 1s
            '构造 NetFile（组装决策已迁 C#：ModpackDeploy.AssembleCurseForgeFiles，:269-308 的重复跳过/可选提示/modules 分类；
            'ResourceVersion.FromPlatformJson/ToNetFile 属宿主层 Resource 模块，经回调接线）
            ModDownloadLoaders.Add(New LoaderTask(Of JArray, List(Of NetFile))("构造 Mod 下载信息",
            Sub(Task As LoaderTask(Of JArray, List(Of NetFile)))
                Dim ProgressUnit = 1 / (1 + Plan.FileIds.Count)
                Task.Output = ModpackDeploy.AssembleCurseForgeFiles(
                    CType(CType(Task.Input, JArray).ToJsonNode(), JsonArray), Plan,
                    Function(DisplayName As String) MyMsgBox("是否要下载整合包中的可选文件 " & DisplayName & "？", "下载可选文件", "是", "否") <> 2,
                    Function(ModJson As JsonObject, TargetFolder As String, Kind As ModpackResourceKind)
                        'modules 分类结果 → ResourceTypes（宿主层 Resource 模块的一对一映射）
                        Dim Type As ResourceTypes
                        Select Case Kind
                            Case ModpackResourceKind.ResourcePack : Type = ResourceTypes.ResourcePack
                            Case ModpackResourceKind.Shader : Type = ResourceTypes.Shader
                            Case ModpackResourceKind.Map : Type = ResourceTypes.Map
                            Case Else : Type = ResourceTypes.Mod
                        End Select
                        Dim File = ResourceVersion.FromPlatformJson(CType(ModJson.ToJToken(), JObject), Type)
                        If Not File.DownloadAvailable Then Return Nothing
                        Return File.ToNetFile(PathUtils.AddSlashSuffix(Path.Combine(VersionFolder, TargetFolder)),
                            ResourceVersion.DownloadReason.ModPack, Plan.MinecraftVersion,
                            Plan.ModLoaderVersions.Keys.Aggregate(ModLoaders.None, Function(Flags, LoaderFlag) Flags Or LoaderFlag)) '旧 TargetVersion.ModLoaders 的旗标聚合
                    End Function,
                    Sub() Task.Progress += ProgressUnit)
            End Sub) With {.ProgressWeight = Plan.FileIds.Count / 200, .Show = False}) '每 200 Mod 需要 1s
            '下载 Mod 文件
            ModDownloadLoaders.Add(New LoaderDownload("下载 Mod", New List(Of NetFile)) With {.ProgressWeight = Plan.FileIds.Count * 1.5}) '每个 Mod 需要 1.5s
            '构造加载器
            InstallLoaders.Add(New LoaderCombo(Of Integer)("下载 Mod（主加载器）", ModDownloadLoaders) With
                {.Show = False, .ProgressWeight = ModDownloadLoaders.Sum(Function(l) l.ProgressWeight)})
        End If

        '构造加载器
        Dim Target As New McComponents(Plan.MinecraftVersion, Plan.ModLoaderVersions)
        Dim MergeLoaders = McInstallLoadersGet(New McInstallRequest With {
            .NewInstanceName = InstanceName, .InstanceFolder = VersionFolder, .Components = Target})
        '构造总加载器
        Dim Loaders As New List(Of LoaderBase)
        Loaders.Add(New LoaderCombo(Of String)("整合包安装", InstallLoaders) With {.Show = False, .Block = False, .ProgressWeight = InstallLoaders.Sum(Function(l) l.ProgressWeight)})
        Loaders.Add(New LoaderCombo(Of String)("游戏安装", MergeLoaders) With {.Show = False, .ProgressWeight = MergeLoaders.Sum(Function(l) l.ProgressWeight)})
        Loaders.Add(New LoaderTask(Of String, String)("最终整理文件",
        Sub(Task As LoaderTask(Of String, String))
            '设置图标
            If Logo IsNot Nothing AndAlso FileUtils.Exists(Logo) Then
                FileUtils.Copy(Logo, VersionFolder & "PCL\Logo.png")
                WriteIni(VersionFolder & "PCL\Setup.ini", "Logo", "PCL\Logo.png")
                WriteIni(VersionFolder & "PCL\Setup.ini", "LogoCustom", "True")
                Logger.Info($"已设置整合包 Logo：{Logo}")
            End If
            '解压地图文件
            Dim SaveFolder As String = $"{VersionFolder}saves\"
            For Each MapFile In DirectoryUtils.EnumerateFiles(SaveFolder, searchPattern:="*.zip")
                FileUtils.ExtractToDirectory(MapFile, SaveFolder, Task.CreateCancellationToken())
                FileUtils.Delete(MapFile)
            Next
            '删除原始整合包文件
            For Each FileToDelete As String In {VersionFolder & "原始整合包.zip", VersionFolder & "原始整合包.mrpack"}
                If FileUtils.Exists(FileToDelete) Then
                    Logger.Info($"删除原始整合包文件：{FileToDelete}")
                    FileUtils.Delete(FileToDelete)
                End If
            Next
            If FileUtils.Exists(FileAddress) AndAlso PathUtils.GetFileNameWithoutExtension(FileAddress) = "modpack" Then
                Logger.Info($"删除安装整合包文件：{FileAddress}")
                FileUtils.Delete(FileAddress)
            End If
        End Sub) With {.ProgressWeight = 0.1, .Show = False})

        '重复任务检查
        Dim LoaderName As String = "CurseForge 整合包安装：" & InstanceName & " "
        If LoaderTaskbar.Any(Function(l) l.Name = LoaderName) Then
            Hint("该整合包正在安装中！", HintType.Red)
            Throw New OperationCanceledException
        End If

        '启动
        Dim Loader As New LoaderCombo(Of String)(LoaderName, Loaders) With {.OnStateChanged = AddressOf McInstallState}
        Loader.Start(VersionFolder)
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        Return Loader
    End Function

    'Modrinth
    Private Function InstallPackModrinth(FileAddress As String, Archive As ZipArchive, ArchiveBaseFolder As String, Optional InstanceName As String = Nothing, Optional Logo As String = Nothing) As LoaderCombo(Of String)

        '读取 Json 文件（读取留宿主层，解析失败包装随宿主层保留）
        Dim IndexJson As String
        Try
            IndexJson = Archive.GetEntry(ArchiveBaseFolder & "modrinth.index.json").Open().ReadString()
        Catch ex As Exception
            Throw New Exception("Modrinth 整合包安装信息存在问题", ex)
        End Try
        '依赖映射、env 过滤、路径校验与 NetFile 组装已迁 C#（ModpackDeploy.BuildModrinthPlan，:366-491）；
        '版本名决策经 resolveVersionFolder 回调，保持旧对话框顺序（依赖映射 → 版本名 → 文件组装）
        Dim ResolvedInstanceName As String = ""
        Dim Plan = ModpackDeploy.BuildModrinthPlan(IndexJson, Nothing,
            Function(SuggestedName As String)
                If InstanceName IsNot Nothing Then
                    ResolvedInstanceName = InstanceName '显式传入名：跳过对话框（旧 If InstanceName Is Nothing 分支）
                    Return Path.Combine(McFolderSelected, "versions", InstanceName) & "\"
                End If
                ResolvedInstanceName = SuggestedName
                Dim Validate As New ValidateFolderName(McFolderSelected & "versions")
                If Validate.Validate(ResolvedInstanceName) <> "" Then ResolvedInstanceName = ""
                If ResolvedInstanceName = "" Then ResolvedInstanceName = MyMsgBoxInput("输入版本名称", "", "", New Collection(Of Validate) From {Validate})
                If String.IsNullOrEmpty(ResolvedInstanceName) Then Throw New OperationCanceledException
                Return Path.Combine(McFolderSelected, "versions", ResolvedInstanceName) & "\"
            End Function,
            AddressOf NotifyIncompatibleLoader,
            Function(DisplayName As String) MyMsgBox("是否要下载可选文件 " & DisplayName & "？", "下载可选文件", "是", "否") <> 2,
            Sub(Message) MyMsgBox(Message, "文件路径校验失败", IsWarn:=True),
            Function(RawUrls As IReadOnlyList(Of String), McVersionName As String, TrackLoader As ModLoaders)
                'URL 镜像展开与来源追踪（:432-435 的宿主面；ResourceVersion 模块未迁；:436 的 Distinct 在 C# 计划核）
                Dim Urls = RawUrls.SelectMany(Function(t) ResourceVersion.ParseCurseForgeDownloadUrls(t)).ToList
                Urls.AddRange(Urls.Select(Function(u) DlSourceModGet(u)).ToList)
                Return ResourceVersion.ParseModrinthTrackArguments(Urls,
                    ResourceVersion.DownloadReason.ModPack, McVersionName, TrackLoader).ToList 'Modrinth 来源追踪信息
            End Function)
        InstanceName = ResolvedInstanceName
        Dim VersionFolder As String = Plan.VersionFolder
        '解压
        Dim InstallTemp As String = RequestTaskTempFolder()
        Dim InstallLoaders As New List(Of LoaderBase)
        InstallLoaders.Add(New LoaderTask(Of String, Integer)("解压整合包文件",
        Sub(Task As LoaderTask(Of String, Integer))
            ExtractModpackFiles(InstallTemp, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage:=0.5))
            CopyOverrideDirectory(InstallTemp & ArchiveBaseFolder & "overrides", VersionFolder)
            CopyOverrideDirectory(InstallTemp & ArchiveBaseFolder & "client-overrides", VersionFolder)
        End Sub) With {.ProgressWeight = FileUtils.GetInfo(FileAddress).Length / 1024 / 1024 / 6, .Block = False}) '每 6M 需要 1s
        '获取下载文件列表（已在 C# 计划核组装：Plan.Files）
        If Plan.Files.Any Then
            InstallLoaders.Add(New LoaderDownload("下载额外文件", Plan.Files) With {.ProgressWeight = Plan.Files.Count * 1.5}) '每个 Mod 需要 1.5s
        End If

        '构造加载器
        Dim Target As New McComponents(Plan.MinecraftVersion, Plan.ModLoaderVersions)
        Dim MergeLoaders = McInstallLoadersGet(New McInstallRequest With {
            .NewInstanceName = InstanceName, .InstanceFolder = VersionFolder,
            .Components = Target})
        '构造总加载器
        Dim Loaders As New List(Of LoaderBase)
        Loaders.Add(New LoaderCombo(Of String)("整合包安装", InstallLoaders) With {.Show = False, .Block = False, .ProgressWeight = InstallLoaders.Sum(Function(l) l.ProgressWeight)})
        Loaders.Add(New LoaderCombo(Of String)("游戏安装", MergeLoaders) With {.Show = False, .ProgressWeight = MergeLoaders.Sum(Function(l) l.ProgressWeight)})
        Loaders.Add(New LoaderTask(Of String, String)("最终整理文件",
        Sub(Task As LoaderTask(Of String, String))
            '设置图标
            If Logo IsNot Nothing AndAlso FileUtils.Exists(Logo) Then
                FileUtils.Copy(Logo, VersionFolder & "PCL\Logo.png")
                WriteIni(VersionFolder & "PCL\Setup.ini", "Logo", "PCL\Logo.png")
                WriteIni(VersionFolder & "PCL\Setup.ini", "LogoCustom", "True")
                Logger.Info($"已设置整合包 Logo：{Logo}")
            End If
            '删除原始整合包文件
            For Each FileToDelete As String In {VersionFolder & "原始整合包.zip", VersionFolder & "原始整合包.mrpack"}
                If FileUtils.Exists(FileToDelete) Then
                    Logger.Info($"删除原始整合包文件：{FileToDelete}")
                    FileUtils.Delete(FileToDelete)
                End If
            Next
            If FileUtils.Exists(FileAddress) AndAlso PathUtils.GetFileNameWithoutExtension(FileAddress) = "modpack" Then
                Logger.Info($"删除安装整合包文件：{FileAddress}")
                FileUtils.Delete(FileAddress)
            End If
        End Sub) With {.ProgressWeight = 0.1, .Show = False})

        '重复任务检查
        Dim LoaderName As String = $"Modrinth 整合包安装：{InstanceName} "
        If LoaderTaskbar.Any(Function(l) l.Name = LoaderName) Then
            Hint("该整合包正在安装中！", HintType.Red)
            Throw New OperationCanceledException
        End If

        '启动
        Dim Loader As New LoaderCombo(Of String)(LoaderName, Loaders) With {.OnStateChanged = AddressOf McInstallState}
        Loader.Start(VersionFolder)
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        Return Loader
    End Function

    'HMCL
    Private Function InstallPackHMCL(FileAddress As String, Archive As ZipArchive, ArchiveBaseFolder As String) As LoaderCombo(Of String)
        '读取 Json 文件（读取留宿主层，解析失败包装随宿主层保留）
        Dim ModpackJson As String
        Try
            ModpackJson = Archive.GetEntry(ArchiveBaseFolder & "modpack.json").Open().ReadString(Encoding.UTF8)
        Catch ex As Exception
            Throw New Exception("HMCL 整合包安装信息存在问题", ex)
        End Try
        '解析、建议名与 gameVersion 判定已迁 C#（ModpackDeploy.BuildHmclPlan，:496-521）
        Dim Plan = ModpackDeploy.BuildHmclPlan(ModpackJson)
        '获取版本名
        Dim InstanceName As String = Plan.SuggestedName
        Dim Validate As New ValidateFolderName(McFolderSelected & "versions")
        If Validate.Validate(InstanceName) <> "" Then InstanceName = ""
        If InstanceName = "" Then InstanceName = MyMsgBoxInput("输入版本名称", "", "", New Collection(Of Validate) From {Validate})
        If String.IsNullOrEmpty(InstanceName) Then Throw New OperationCanceledException
        Dim VersionFolder As String = Path.Combine(McFolderSelected, "versions", InstanceName) & "\"
        '解压
        Dim InstallTemp As String = RequestTaskTempFolder()
        Dim InstallLoaders As New List(Of LoaderBase)
        InstallLoaders.Add(New LoaderTask(Of String, Integer)("解压整合包文件",
        Sub(Task As LoaderTask(Of String, Integer))
            ExtractModpackFiles(InstallTemp, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage:=0.6))
            CopyOverrideDirectory(InstallTemp & ArchiveBaseFolder & "minecraft", VersionFolder)
        End Sub) With {.ProgressWeight = FileUtils.GetInfo(FileAddress).Length / 1024 / 1024 / 6, .Block = False}) '每 6M 需要 1s
        '构造游戏本体安装加载器（gameVersion 判定已在 C# 计划核完成）
        Dim Target As New McComponents(Plan.GameVersion, New Dictionary(Of ModLoaders, String))
        Dim MergeLoaders = McInstallLoadersGet(New McInstallRequest With {
            .NewInstanceName = InstanceName, .InstanceFolder = VersionFolder,
            .Components = Target})
        '构造总加载器
        Dim Loaders As New List(Of LoaderBase) From {
            New LoaderCombo(Of String)("整合包安装", InstallLoaders) With {.Show = False, .Block = False, .ProgressWeight = InstallLoaders.Sum(Function(l) l.ProgressWeight)},
            New LoaderCombo(Of String)("游戏安装", MergeLoaders) With {.Show = False, .ProgressWeight = MergeLoaders.Sum(Function(l) l.ProgressWeight)}
        }
        '重复任务检查
        Dim LoaderName As String = "HMCL 整合包安装：" & InstanceName & " "
        If LoaderTaskbar.Any(Function(l) l.Name = LoaderName) Then
            Hint("该整合包正在安装中！", HintType.Red)
            Throw New OperationCanceledException
        End If
        '启动
        Dim Loader As New LoaderCombo(Of String)(LoaderName, Loaders) With {.OnStateChanged = AddressOf McInstallState}
        Loader.Start(VersionFolder)
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        Return Loader
    End Function

    'MMC
    Private Function InstallPackMMC(FileAddress As String, Archive As ZipArchive, ArchiveBaseFolder As String) As LoaderCombo(Of String)
        '读取 Json 文件
        Dim PackJson As JObject, PackInstance As String
        Try
            PackJson = Archive.GetEntry(ArchiveBaseFolder & "mmc-pack.json").Open().ReadString(Encoding.UTF8).DeserializeJson()
            PackInstance = Archive.GetEntry(ArchiveBaseFolder & "instance.cfg").Open().ReadString(Encoding.UTF8)
        Catch ex As Exception
            Throw New Exception("MMC 整合包安装信息存在问题", ex)
        End Try
        '获取版本名
        Dim InstanceName As String = If(PackInstance.RegexSeek("(?<=\nname\=)[^\n]+"), "")
        Dim Validate As New ValidateFolderName(McFolderSelected & "versions")
        If Validate.Validate(InstanceName) <> "" Then InstanceName = ""
        If InstanceName = "" Then InstanceName = MyMsgBoxInput("输入版本名称", "", "", New Collection(Of Validate) From {Validate})
        If String.IsNullOrEmpty(InstanceName) Then Throw New OperationCanceledException
        Dim VersionFolder As String = Path.Combine(McFolderSelected, "versions", InstanceName) & "\"
        '解压
        Dim InstallTemp As String = RequestTaskTempFolder()
        Dim SetupFile As String = VersionFolder & "PCL\Setup.ini"
        Dim InstallLoaders As New List(Of LoaderBase)
        InstallLoaders.Add(New LoaderTask(Of String, Integer)("解压整合包文件",
        Sub(Task As LoaderTask(Of String, Integer))
            ExtractModpackFiles(InstallTemp, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage:=0.55))
            'Prism 优先使用 minecraft/ 而非 .minecraft/（#9272）
            Dim MmcGameRoot As String
            If DirectoryUtils.Exists(InstallTemp & ArchiveBaseFolder & ".minecraft") AndAlso Not DirectoryUtils.Exists(InstallTemp & ArchiveBaseFolder & "minecraft") Then
                MmcGameRoot = InstallTemp & ArchiveBaseFolder & ".minecraft"
            Else
                MmcGameRoot = InstallTemp & ArchiveBaseFolder & "minecraft"
            End If
            CopyOverrideDirectory(MmcGameRoot, VersionFolder)
            '读取 MMC 设置文件（#2655）
            Try
                Dim MmcSetupFile As String = InstallTemp & ArchiveBaseFolder & "instance.cfg"
                If FileUtils.Exists(MmcSetupFile) Then
                    '将其中的等号替换为冒号，以符合 ini 文件格式
                    Dim Lines As New List(Of String)
                    For Each Line In FileUtils.ReadAsLines(MmcSetupFile, True)
                        If Not Line.Contains("=") Then Continue For
                        Lines.Add(Line.BeforeFirst("=") & ":" & Line.AfterFirst("="))
                    Next
                    FileUtils.Write(MmcSetupFile, Lines.Join(vbCrLf))
                    '读取文件
                    If ReadIni(MmcSetupFile, "OverrideCommands", False) Then
                        Dim PreLaunchCommand As String = ReadIni(MmcSetupFile, "PreLaunchCommand")
                        If PreLaunchCommand <> "" Then
                            PreLaunchCommand = PreLaunchCommand.Replace("\""", """").
                                Replace("$INST_JAVA", "{java}java.exe").
                                Replace("$INST_MC_DIR\", "{minecraft}").Replace("$INST_MC_DIR", "{minecraft}").
                                Replace("$INST_DIR\", "{verpath}").Replace("$INST_DIR", "{verpath}").
                                Replace("$INST_ID", "{name}").Replace("$INST_NAME", "{name}")
                            WriteIni(SetupFile, "VersionAdvanceRun", PreLaunchCommand)
                            Logger.Info($"迁移 MultiMC 版本独立设置：启动前执行命令：{PreLaunchCommand}")
                        End If
                    End If
                    If ReadIni(MmcSetupFile, "JoinServerOnLaunch", False) Then
                        Dim ServerAddress As String = ReadIni(MmcSetupFile, "JoinServerOnLaunchAddress").Replace("\""", """")
                        WriteIni(SetupFile, "VersionServerEnter", ServerAddress)
                        Logger.Info($"迁移 MultiMC 版本独立设置：自动进入服务器：{ServerAddress}")
                    End If
                    Dim Logo As String = ReadIni(MmcSetupFile, "iconKey", "")
                    If Logo <> "" AndAlso FileUtils.Exists($"{InstallTemp}{ArchiveBaseFolder}{Logo}.png") Then
                        WriteIni(SetupFile, "LogoCustom", True)
                        WriteIni(SetupFile, "Logo", "PCL\Logo.png")
                        FileUtils.Copy($"{InstallTemp}{ArchiveBaseFolder}{Logo}.png", VersionFolder & "PCL\Logo.png")
                        Logger.Info($"迁移 MultiMC 版本独立设置：版本图标（{Logo}.png）")
                    End If
                    'JVM 参数
                    Dim JvmArgs As String = ReadIni(MmcSetupFile, "JvmArgs", "")
                    If JvmArgs <> "" Then
                        If ReadIni(MmcSetupFile, "OverrideJavaArgs", False) Then
                            WriteIni(SetupFile, "VersionAdvanceJvm", JvmArgs)
                            Logger.Info($"迁移 MultiMC 版本独立设置：JVM 参数（覆盖）：{JvmArgs}")
                        Else
                            JvmArgs += " " & Settings.Get(Of String)("LaunchAdvanceJvm")
                            WriteIni(SetupFile, "VersionAdvanceJvm", JvmArgs)
                            Logger.Info($"迁移 MultiMC 版本独立设置：JVM 参数（追加）：{JvmArgs}")
                        End If
                    End If
                End If
            Catch ex As Exception
                Logger.Warn(ex, $"读取 MMC 配置文件失败（{InstallTemp}{ArchiveBaseFolder}instance.cfg）")
            End Try
        End Sub) With {.ProgressWeight = FileUtils.GetInfo(FileAddress).Length / 1024 / 1024 / 6, .Block = False}) '每 6M 需要 1s
        '构造版本安装请求
        If PackJson("components") Is Nothing Then Throw New Exception("该 MMC 整合包未提供游戏版本信息，无法安装！")
        'components 的 uid 映射与安装目标构造已迁 C#（ModpackDeploy.MapMmcComponents / BuildMmcComponents，
        ':631-654：四已知 uid、LWJGL 跳过 #8210、未知项经回调走 NotifyIncompatibleLoader）
        Dim UidMap = ModpackDeploy.MapMmcComponents(CType(CType(PackJson("components"), JArray).ToJsonNode(), JsonArray), AddressOf NotifyIncompatibleLoader)
        Dim Target As McComponents = ModpackDeploy.BuildMmcComponents(UidMap)
        Dim MergeLoaders = McInstallLoadersGet(New McInstallRequest With {
            .NewInstanceName = InstanceName, .InstanceFolder = VersionFolder,
            .Components = Target})
        '构造总加载器
        Dim Loaders As New List(Of LoaderBase)
        Loaders.Add(New LoaderCombo(Of String)("整合包安装", InstallLoaders) With {.Show = False, .Block = False, .ProgressWeight = InstallLoaders.Sum(Function(l) l.ProgressWeight)})
        Loaders.Add(New LoaderCombo(Of String)("游戏安装", MergeLoaders) With {.Show = False, .ProgressWeight = MergeLoaders.Sum(Function(l) l.ProgressWeight)})

        '重复任务检查
        Dim LoaderName As String = "MMC 整合包安装：" & InstanceName & " "
        If LoaderTaskbar.Any(Function(l) l.Name = LoaderName) Then
            Hint("该整合包正在安装中！", HintType.Red)
            Throw New OperationCanceledException
        End If

        '启动
        Dim Loader As New LoaderCombo(Of String)(LoaderName, Loaders) With {.OnStateChanged = AddressOf McInstallState}
        Loader.Start(VersionFolder)
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        Return Loader
    End Function

    'MCBBS
    Private Function InstallPackMCBBS(FileAddress As String, Archive As ZipArchive, ArchiveBaseFolder As String,
                                      Optional InstanceName As String = Nothing) As LoaderCombo(Of String)
        '读取 Json 文件（读取与解析留宿主层，解析失败包装随宿主层保留——旧 :684-688 的同一 Try 形态；
        '解析结果复用于版本名流，C# 计划核按字符串契约重解析，与 CF 的宿主预解析形态一致）
        Dim McbbsJson As String, Json As JObject
        Try
            Dim Entry = If(Archive.GetEntry(ArchiveBaseFolder & "mcbbs.packmeta"), Archive.GetEntry(ArchiveBaseFolder & "manifest.json"))
            McbbsJson = Entry.Open().ReadString(Encoding.UTF8)
            Json = McbbsJson.DeserializeJson()
        Catch ex As Exception
            Throw New Exception("MCBBS 整合包安装信息存在问题", ex)
        End Try
        '获取版本名
        If InstanceName Is Nothing Then
            InstanceName = If(Json("name"), "")
            Dim Validate As New ValidateFolderName(McFolderSelected & "versions")
            If Validate.Validate(InstanceName) <> "" Then InstanceName = ""
            If InstanceName = "" Then InstanceName = MyMsgBoxInput("输入版本名称", "", "", New Collection(Of Validate) From {Validate})
            If String.IsNullOrEmpty(InstanceName) Then Throw New OperationCanceledException
        End If
        Dim VersionFolder As String = Path.Combine(McFolderSelected, "versions", InstanceName) & "\"
        'addons 映射、Quilt 拒绝与 OptiFine 匹配已迁 C#（ModpackDeploy.BuildMcbbsPlan，:714-739；
        'Quilt 提示经回调走 Hint，OptiFine 列表等待与取值经懒回调留宿主层）
        Dim Plan = ModpackDeploy.BuildMcbbsPlan(McbbsJson,
            Function()
                If DlOptiFineListBmclapiLoader.State <> LoadState.Finished Then DlOptiFineListBmclapiLoader.WaitForExit()
                Return DlOptiFineListBmclapiLoader.Output.Value
            End Function,
            Sub() Hint("PCL 不支持安装 Quilt 整合包！", HintType.Red))
        '解压
        Dim InstallTemp As String = RequestTaskTempFolder()
        Dim InstallLoaders As New List(Of LoaderBase)
        InstallLoaders.Add(New LoaderTask(Of String, Integer)("解压整合包文件",
        Sub(Task As LoaderTask(Of String, Integer))
            ExtractModpackFiles(InstallTemp, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage:=0.6))
            CopyOverrideDirectory(InstallTemp & ArchiveBaseFolder & "overrides", VersionFolder)
            'JVM 参数（键值组装已迁 C#：Plan.LaunchJvmArguments / Plan.LaunchGameArguments，:709-710；
            '目录键经版本级设置 instanceKey 面 写入，替代旧 WriteIni SetupFile）
            If Plan.LaunchJvmArguments IsNot Nothing Then SettingsService.Set("VersionAdvanceJvm", Plan.LaunchJvmArguments, VersionFolder)
            If Plan.LaunchGameArguments IsNot Nothing Then SettingsService.Set("VersionAdvanceGame", Plan.LaunchGameArguments, VersionFolder)
        End Sub) With {.ProgressWeight = FileUtils.GetInfo(FileAddress).Length / 1024 / 1024 / 6, .Block = False}) '每 6M 需要 1s
        Dim Target As New McComponents(Plan.MinecraftVersion, Plan.ModLoaderVersions, Plan.OptiFineEntry)
        Dim MergeLoaders = McInstallLoadersGet(New McInstallRequest With {
            .NewInstanceName = InstanceName, .InstanceFolder = VersionFolder,
            .Components = Target})
        '构造总加载器
        Dim Loaders As New List(Of LoaderBase)
        Loaders.Add(New LoaderCombo(Of String)("整合包安装", InstallLoaders) With {.Show = False, .Block = False, .ProgressWeight = InstallLoaders.Sum(Function(l) l.ProgressWeight)})
        Loaders.Add(New LoaderCombo(Of String)("游戏安装", MergeLoaders) With {.Show = False, .ProgressWeight = MergeLoaders.Sum(Function(l) l.ProgressWeight)})

        '重复任务检查
        Dim LoaderName As String = "MCBBS 整合包安装：" & InstanceName & " "
        If LoaderTaskbar.Any(Function(l) l.Name = LoaderName) Then
            Hint("该整合包正在安装中！", HintType.Red)
            Throw New OperationCanceledException
        End If

        '启动
        Dim Loader As New LoaderCombo(Of String)(LoaderName, Loaders) With {.OnStateChanged = AddressOf McInstallState}
        'If Archive IsNot Nothing Then Archive.Dispose() '解除占用，以免在加载器中触发 “正由另一进程使用，因此该进程无法访问此文件”
        Loader.Start(VersionFolder)
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        Return Loader
    End Function

    '带启动器的压缩包
    Private Function InstallPackLauncherPack(FileAddress As String, Archive As ZipArchive, ArchiveBaseFolder As String) As LoaderCombo(Of String)
        '获取解压路径
        MyMsgBox("接下来请选择一个空文件夹，它会被安装到这个文件夹里。", "安装", "继续", ForceWait:=True)
        Dim TargetFolder As String = Dialogs.SelectFolder("选择安装目标（必须是一个空文件夹）", False).FirstOrDefault
        If TargetFolder Is Nothing Then Throw New OperationCanceledException
        If Not DirectoryUtils.IsEmpty(TargetFolder) Then Hint("请选择一个空文件夹作为安装目标！", HintType.Red) : Throw New OperationCanceledException
        '解压
        Dim Loader As New LoaderCombo(Of String)("解压压缩包", {
            New LoaderTask(Of String, Integer)("解压压缩包",
            Sub(Task As LoaderTask(Of String, Integer))
                ExtractModpackFiles(TargetFolder, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage:=0.9))
                OpenExplorer(TargetFolder)
                Thread.Sleep(400) '避免文件争用
                '查找解压后的 exe 文件
                Dim Launcher As String = Nothing
                For Each ExeFile In DirectoryUtils.EnumerateFiles(TargetFolder, searchPattern:="*.exe")
                    Dim Info = FileVersionInfo.GetVersionInfo(ExeFile)
                    Logger.Info($"文件 {ExeFile} 的产品名标识为 {Info.ProductName}")
                    If Info.ProductName = "Plain Craft Launcher" OrElse
                       Info.ProductName = "Plain Craft Launcher (PCL) Alternative" Then
                        Launcher = ExeFile
                        Logger.Info($"发现整合包附带的 PCL 启动器：{ExeFile}")
                    ElseIf (Info.ProductName.ContainsIgnoreCase("Launcher") OrElse Info.ProductName.Contains("启动")) AndAlso
                        Not Info.ProductName = "Plain Craft Launcher Admin Manager" Then
                        If Launcher Is Nothing Then
                            Launcher = ExeFile
                            Logger.Info($"发现整合包附带的疑似第三方启动器：{ExeFile}")
                        End If
                    End If
                Next
                Task.Progress = 0.95
                '尝试使用附带的启动器打开
                If Launcher IsNot Nothing Then
                    Logger.Info($"找到压缩包中附带的启动器：{Launcher}")
                    If MyMsgBox($"整合包里似乎自带了启动器，是否换用它继续安装？{vbCrLf}即将打开：{Launcher}", "换用整合包启动器？", "换用", "不换用") = 1 Then
                        StartProcess(Launcher, "--wait") '要求等待已有的 PCL 退出
                        Logger.Info("为换用整合包中的启动器启动，强制结束程序")
                        FrmMain.EndProgram(False)
                        Return
                    End If
                Else
                    Logger.Info("未找到压缩包中附带的启动器")
                End If
                '加入文件夹列表
                Dim InstanceName As String = PathUtils.GetLastPart(TargetFolder)
                DirectoryUtils.Create(TargetFolder & ".minecraft\")
                PageSelectLeft.AddFolder(
                    TargetFolder & ".minecraft\" & ArchiveBaseFolder.Replace("/", "\").TrimStart("\"), '格式例如：包裹文件夹\.minecraft\（最短为空字符串）
                    InstanceName, False)
                '调用 modpack 文件进行安装
                Dim ModpackFile = DirectoryUtils.EnumerateFiles(TargetFolder, True, "modpack.*").First
                Logger.Info($"调用 modpack 文件继续安装：{ModpackFile}")
                ModpackInstall(ModpackFile)
            End Sub)
        })
        Loader.Start(TargetFolder)
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        Return Loader
    End Function

    '普通压缩包
    Private Function InstallPackCompress(FileAddress As String, Archive As ZipArchive) As LoaderCombo(Of String)
        '尝试定位 .minecraft 文件夹：寻找形如 “/versions/XXX/XXX.json” 的路径
        Dim Match As Match = Nothing
        Dim Regex As New Regex("^.*\/(?=versions\/(?<ver>[^\/]+)\/(\k<ver>)\.json$)", RegexOptions.IgnoreCase)
        For Each Entry In Archive.Entries
            Dim EntryMatch = Regex.Match("/" & Entry.FullName)
            If EntryMatch.Success Then
                Match = EntryMatch
                Exit For
            End If
        Next
        If Match Is Nothing Then Throw New Exception("文件结构不匹配，这可能不是 Minecraft 客户端压缩包？") '没有匹配
        Dim ArchiveBaseFolder As String = Match.Value.Replace("/", "\").TrimStart("\") '格式例如：包裹文件夹\.minecraft\（最短为空字符串）
        Logger.Info($"检测到压缩包的 .minecraft 根目录：{ArchiveBaseFolder}，命中的版本名：{Match.Groups(1).Value}")
        '获取解压路径
        MyMsgBox("接下来请选择一个空文件夹，它会被安装到这个文件夹里。", "安装", "继续", ForceWait:=True)
        Dim TargetFolder As String = Dialogs.SelectFolder("选择安装目标（必须是一个空文件夹）", False).FirstOrDefault
        If TargetFolder Is Nothing Then Throw New OperationCanceledException
        If TargetFolder.Contains("!") OrElse TargetFolder.Contains(";") Then Hint("Minecraft 文件夹路径中不能含有感叹号或分号！", HintType.Red) : Throw New OperationCanceledException
        If Not DirectoryUtils.IsEmpty(TargetFolder) Then Hint("请选择一个空文件夹作为安装目标！", HintType.Red) : Throw New OperationCanceledException
        '解压
        Dim Loader As New LoaderCombo(Of String)("解压压缩包", {
            New LoaderTask(Of String, Integer)("解压压缩包",
            Sub(Task As LoaderTask(Of String, Integer))
                ExtractModpackFiles(TargetFolder, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage:=0.95))
                '加入文件夹列表
                PageSelectLeft.AddFolder(TargetFolder & ArchiveBaseFolder, PathUtils.GetLastPart(TargetFolder), False)
                Thread.Sleep(400) '避免文件争用
                RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.InstanceSelect))
            End Sub)
        }) With {.OnStateChanged = AddressOf McInstallState}
        Loader.Start(TargetFolder)
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        Return Loader
    End Function

#End Region

End Module