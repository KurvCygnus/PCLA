Imports System.Text.Json.Nodes
Imports PCLCS.Net

'说明：McInstance 的 JSON 管线已切换到 PCLCS.Instances.InstanceCore；McInstance.JsonObject 暴露的是
'由 LegacyJsonExtensions.ToJToken(_core.VersionJson) 生成的 Newtonsoft JObject 快照——W3 迁移完成前，
'外部对快照的修改不会写回核心（现有 VB 读取方均为只读，已核实）。
Public Module ModMinecraft

#Region "文件夹"

    ''' <summary>
    ''' 当前的 Minecraft 文件夹路径，以 \ 结尾。
    ''' </summary>
    Public Property McFolderSelected As String
        Get
            If _McFolderSelected Is Nothing Then SetMcFolder(Settings.Get(Of String)("LaunchFolderSelect")) '不能使用 McFolderSelected 触发 Set，这会被视为为 Get 设置返回值
            Return _McFolderSelected
        End Get
        Set(Value As String)
            SetMcFolder(Value)
        End Set
    End Property
    Private Sub SetMcFolder(Value As String) '等同 McFolderSelected.Set
        If PathUtils.IsSame(_McFolderSelected, Value.Replace("$", Paths.Base)) Then Return
        _McFolderSelected = Value.Replace("$", Paths.Base)
        _McFolderSelected = PathUtils.AddSlashSuffix(PathUtils.ForCompare(_McFolderSelected))
        Settings.Set("LaunchFolderSelect", Value.Replace(Paths.Base, "$"))
        Logger.Info($"当前选择的 Minecraft 文件夹：{_McFolderSelected}")
    End Sub
    Private _McFolderSelected As String
    ''' <summary>
    ''' 当前的 Minecraft 文件夹列表。
    ''' </summary>
    Public McFolderList As New List(Of McFolder)

    Public Class McFolder
        Public Name As String
        ''' <summary>
        ''' 文件夹路径。
        ''' 以 \ 结尾，例如 "D:\Game\MC\.minecraft\"。
        ''' </summary>
        Public Location As String
        Public Type As Types
        Public Enum Types
            Vanilla
            RenamedVanilla
            Custom
        End Enum

        Public Overrides Function Equals(obj As Object) As Boolean
            If Not (TypeOf obj Is McFolder) Then Return False
            Dim Folder = DirectCast(obj, McFolder)
            Return Name = Folder.Name AndAlso PathUtils.IsSame(Location, Folder.Location) AndAlso Type = Folder.Type
        End Function
        Public Overrides Function ToString() As String
            Return Location
        End Function
    End Class

    ''' <summary>
    ''' 加载 Minecraft 文件夹列表。
    ''' </summary>
    Public McFolderListLoader As New LoaderTask(Of Integer, Integer)("Minecraft Folder List", AddressOf McFolderListLoadSub, Priority:=ThreadPriority.AboveNormal)
    Private Sub McFolderListLoadSub()
        Try
            '初始化
            Dim CacheMcFolderList = New List(Of McFolder)

#Region "读取默认（Original）文件夹，即当前、官启文件夹，可能没有结果"

            '扫描当前文件夹
            Try
                If DirectoryUtils.Exists(Paths.Base & "versions\") Then CacheMcFolderList.Add(New McFolder With {.Name = "当前文件夹", .Location = Paths.Base, .Type = McFolder.Types.Vanilla})
                For Each Folder As DirectoryInfo In DirectoryUtils.GetInfo(Paths.Base).GetDirectories
                    If DirectoryUtils.Exists(Folder.FullName & "versions\") OrElse Folder.Name = ".minecraft" Then
                        CacheMcFolderList.Add(New McFolder With {.Name = "当前文件夹", .Location = PathUtils.AddSlashSuffix(PathUtils.ForCompare(Folder.FullName)), .Type = McFolder.Types.Vanilla})
                    End If
                Next
            Catch ex As Exception
                Logger.Warn(ex, "扫描 PCL 所在文件夹中是否有 MC 文件夹失败")
            End Try

            '扫描官启文件夹
            Dim MojangPath As String = Paths.AppData & ".minecraft\"
            If (Not CacheMcFolderList.Any OrElse Not PathUtils.IsSame(MojangPath, CacheMcFolderList(0).Location)) AndAlso '当前文件夹不是官启文件夹
                DirectoryUtils.Exists(MojangPath & "versions\") Then '具有权限且存在 versions 文件夹
                CacheMcFolderList.Add(New McFolder With {.Name = "官方启动器文件夹", .Location = MojangPath, .Type = McFolder.Types.Vanilla})
            End If

#End Region

#Region "读取自定义（Custom）文件夹，可能没有结果"

            '格式：TMZ 12>C://xxx/xx/|Test>D://xxx/xx/|名称>路径
            For Each Folder As String In Settings.Get(Of String)("LaunchFolders").ToString.Split("|"c, True)
                If Folder = "" Then Continue For
                If Not Folder.Contains(">") OrElse Not Folder.EndsWithF("\") Then
                    Hint("无效的 Minecraft 文件夹：" & Folder, HintType.Red)
                    Continue For
                End If
                Dim FolderName As String = Folder.BeforeFirst(">"), FolderPath As String = Folder.AfterFirst(">")
                Try
                    CheckPermissionWithException(FolderPath)
                    '若已有该文件夹，则直接重命名；没有则添加
                    Dim Renamed As Boolean = False
                    For Each OriginalFolder As McFolder In CacheMcFolderList
                        If PathUtils.IsSame(OriginalFolder.Location, FolderPath) Then
                            OriginalFolder.Name = FolderName
                            OriginalFolder.Type = McFolder.Types.RenamedVanilla
                            Renamed = True
                        End If
                    Next
                    If Not Renamed Then CacheMcFolderList.Add(New McFolder With {.Name = FolderName, .Location = PathUtils.AddSlashSuffix(PathUtils.ForCompare(FolderPath)), .Type = McFolder.Types.Custom})
                Catch ex As Exception
                    MyMsgBox("失效的 Minecraft 文件夹：" & vbCrLf & FolderPath & vbCrLf & vbCrLf & ex.GetDisplay(False), "Minecraft 文件夹失效", IsWarn:=True)
                    Logger.Warn(ex, $"无法访问 Minecraft 文件夹 {FolderPath}")
                End Try
            Next

            '将自定义文件夹情况同步到设置
            Dim NewSetup As New List(Of String)
            For Each Folder As McFolder In CacheMcFolderList
                If Not Folder.Type = McFolder.Types.Vanilla Then NewSetup.Add(Folder.Name & ">" & Folder.Location)
            Next
            If Not NewSetup.Any() Then NewSetup.Add("") '防止 0 元素 Join 返回 Nothing
            Settings.Set("LaunchFolders", NewSetup.Join("|"c))

#End Region

            '若没有可用文件夹，则创建 .minecraft
            If Not CacheMcFolderList.Any() Then
                DirectoryUtils.Create(Paths.Base & ".minecraft\versions\")
                CacheMcFolderList.Add(New McFolder With {.Name = "当前文件夹", .Location = Paths.Base & ".minecraft\", .Type = McFolder.Types.Vanilla})
            End If

            For Each Folder As McFolder In CacheMcFolderList
                McFolderLauncherProfilesJsonCreate(Folder.Location)
            Next
            If Settings.Get(Of Boolean)("SystemDebugDelay") Then Thread.Sleep(RandomInteger(200, 2000))

            '回设
            McFolderList = CacheMcFolderList

        Catch ex As Exception
            Logger.Error(ex, "加载 Minecraft 文件夹列表失败")
        End Try
    End Sub

    ''' <summary>
    ''' 为 Minecraft 文件夹创建 launcher_profiles.json 文件。
    ''' </summary>
    Public Sub McFolderLauncherProfilesJsonCreate(Folder As String)
        Try
            If FileUtils.Exists(Folder & "launcher_profiles.json") Then Return
            Dim ResultJson As String =
"{
    ""profiles"":  {
        ""PCL"": {
            ""icon"": ""Grass"",
            ""name"": ""PCL"",
            ""lastVersionId"": ""latest-release"",
            ""type"": ""latest-release"",
            ""lastUsed"": """ & Date.Now.ToString("yyyy'-'MM'-'dd") & "T" & Date.Now.ToString("HH':'mm':'ss") & ".0000Z""
        }
    },
    ""selectedProfile"": ""PCL"",
    ""clientToken"": ""23323323323323323323323323323333""
}"
            '只替换本方法刚生成的三个 profile 字段；已有文件在方法开头已直接返回。
            ResultJson = ResultJson.Replace("""PCL""", """" & PCLCS.Startup.LauncherIdentity.AssemblyName & """")
            FileUtils.Write(Folder & "launcher_profiles.json", ResultJson, encoding:=Encoding.GetEncoding("GB18030"))
            Logger.Info($"已创建 launcher_profiles.json：{Folder}")
        Catch ex As Exception
            Logger.Error(ex, $"创建 launcher_profiles.json 失败（{Folder}）")
        End Try
    End Sub

#End Region

#Region "版本处理"

    Private _McInstanceSelected As McInstance
    ''' <summary>
    ''' 当前的 Minecraft 版本。
    ''' </summary>
    Public Property McInstanceSelected As McInstance
        Get
            Return _McInstanceSelected
        End Get
        Set(value As McInstance)
            Static _McInstanceSelectedLast As Object = 0 '为 0 以保证与 Nothing 不相同，使得 UI 显示可以正常初始化
            If ReferenceEquals(_McInstanceSelectedLast, value) Then Return
            _McInstanceSelected = value '由于有可能是 Nothing，导致无法初始化，才得这样弄一圈
            _McInstanceSelectedLast = value
            '更新 MC 文件夹所选取的 Minecraft 版本
            Logger.Info($"当前选择的 Minecraft 版本：{If(value?.PathVersion, "无")}")
            WriteIni(McFolderSelected & "PCL.ini", "Version", If(value?.Name, ""))
            If value Is Nothing Then Return
            '重置缓存的下载文件夹
            PageDownloadResourceDetail.CachedFolder.Clear()
            '统一通行证重判
            If AniControlEnabled = 0 AndAlso
               Settings.Get(Of String)("VersionServerNide", Instance:=value) <> Settings.Get(Of String)("CacheNideServer") AndAlso
               Settings.Get(Of Integer)("VersionServerLogin", Instance:=value) = 3 Then
                Settings.Set("CacheNideAccess", "")
                Logger.Info("服务器改变，要求重新登录统一通行证")
            End If
            If Settings.Get(Of Integer)("VersionServerLogin", Instance:=value) = 3 Then
                Settings.Set("CacheNideServer", Settings.Get(Of String)("VersionServerNide", Instance:=value))
            End If
            'Authlib-Injector 重判
            If AniControlEnabled = 0 AndAlso
               Settings.Get(Of String)("VersionServerAuthServer", Instance:=value) <> Settings.Get(Of String)("CacheAuthServerServer") AndAlso
               Settings.Get(Of Integer)("VersionServerLogin", Instance:=value) = 4 Then
                Settings.Set("CacheAuthAccess", "")
                Logger.Info("服务器改变，要求重新登录 Authlib-Injector")
            End If
            If Settings.Get(Of Integer)("VersionServerLogin", Instance:=value) = 4 Then
                Settings.Set("CacheAuthServerServer", Settings.Get(Of String)("VersionServerAuthServer", Instance:=value))
                Settings.Set("CacheAuthServerName", Settings.Get(Of String)("VersionServerAuthName", Instance:=value))
                Settings.Set("CacheAuthServerRegister", Settings.Get(Of String)("VersionServerAuthRegister", Instance:=value))
            End If
        End Set
    End Property

    Public Class McInstance

        Public Enum McInstanceState
            [Error]
            Original
            Snapshot
            Fool
            OptiFine
            Old
            Forge
            NeoForge
            LiteLoader
            Fabric
        End Enum

        ''' <summary>
        ''' 实例数据面核心（JSON 管线、检查与隔离判定，PCLCS.Instances.InstanceCore）；
        ''' UI 绑定字段（Info/State/Logo/IsStar/DisplayType）与 ini 编排保留在本类。
        ''' </summary>
        Private ReadOnly _core As Instances.InstanceCore

        ''' <summary>
        ''' 核心引用，供版本列表编排建立 core→wrapper 回映并参与 C# 分类（Instances.InstanceScanner.ClassifyAndSort）。
        ''' </summary>
        Friend ReadOnly Property Core As Instances.InstanceCore
            Get
                Return _core
            End Get
        End Property

        ''' <summary>
        ''' 版本文件夹路径，以 \ 结尾。
        ''' </summary>
        Public ReadOnly Property PathVersion As String
            Get
                Return _core.PathVersion
            End Get
        End Property
        ''' <summary>
        ''' 应用版本隔离后，该版本所对应的 Minecraft 根文件夹，以 \ 结尾。
        ''' </summary>
        Public ReadOnly Property PathIndie As String
            Get
                '与旧实现的判定次序一致：未判定且未加载时先走完整 Load（补齐 Setup.ini 缓存与 UI 字段），
                '并经 VB façade 触发实例旧设置的懒式导入，再交由核心判定与写回
                If Not Settings.HasSaved("VersionArgumentIndieV2", Instance:=Me) Then
                    If Not IsLoaded Then Load()
                End If
                Return _core.PathIndie
            End Get
        End Property

        ''' <summary>
        ''' 该版本的版本文件夹名称。
        ''' </summary>
        Public ReadOnly Property Name As String
            Get
                If _Name Is Nothing AndAlso Not PathVersion = "" Then _Name = PathUtils.GetLastPart(PathVersion)
                Return _Name
            End Get
        End Property
        Private _Name As String = Nothing

        ''' <summary>
        ''' 显示的描述文本。
        ''' </summary>
        Public Info As String = "该版本未被加载，请向作者反馈此问题"

        ''' <summary>
        ''' 该版本的列表检查原始结果，不受自定义影响。
        ''' </summary>
        Public State As McInstanceState = McInstanceState.Error
        ''' <summary>
        ''' 显示的版本图标。
        ''' </summary>
        Public Logo As String
        ''' <summary>
        ''' 是否为收藏的版本。
        ''' </summary>
        Public IsStar As Boolean = False
        ''' <summary>
        ''' 强制版本分类，0 为未启用，1 为隐藏，2 及以上为其他普通分类。
        ''' </summary>
        Public DisplayType As McInstanceCardType = McInstanceCardType.Auto
        ''' <summary>
        ''' 该版本是否可以安装 Mod。
        ''' </summary>
        Public ReadOnly Property Modable As Boolean
            Get
                If Not IsLoaded Then Load()
                Return Version.ModLoaders.Flags.Any OrElse DisplayType = McInstanceCardType.API '#223
            End Get
        End Property
        ''' <summary>
        ''' 版本信息。可被外部赋值覆盖（版本列表缓存/manifest 恢复）；覆盖后 C# 核心无法感知，
        ''' 描述计算将走本类的旧逻辑（见 VersionDisplayName）。
        ''' </summary>
        Public Property Version As McVersion
            Get
                If _Version IsNot Nothing Then Return _Version
                Dim InfoBefore As String = _core.Info
                _Version = _core.Version '版本识别与“无法识别”的描述回写在 C# 侧完成
                If _core.Info <> InfoBefore Then Info = _core.Info '仅同步核心识别失败时的描述回写
                Return _Version
            End Get
            Set(value As McVersion)
                _Version = value
                _VersionOverridden = True
            End Set
        End Property
        Private _Version As McVersion = Nothing
        ''' <summary>Version 是否被外部赋值覆盖（列表缓存/manifest 恢复，ModMinecraft.vb:1101）。</summary>
        Private _VersionOverridden As Boolean = False

        ''' <summary>
        ''' 版本的发布时间。
        ''' </summary>
        Public ReadOnly Property ReleaseTime As Date
            Get
                Return Version.ReleaseTime
            End Get
        End Property

        ''' <summary>
        ''' 获取该版本的 JSON 路径。
        ''' </summary>
        Public Function GetJsonPath() As String
            Return Instances.InstancePaths.GetJsonPath(PathVersion, Name) '同名探测与异常契约在 C# 侧保持一致
        End Function
        ''' <summary>
        ''' 该版本的 Json 文本。会抛出异常。
        ''' </summary>
        Public Property JsonText As String
            Get
                '快速检查 JSON 是否以 { 开头、} 结尾；忽略空白字符
                Dim FastJsonCheck =
                Function(Json As String) As Boolean
                    Dim TrimedJson As String = Json.Trim()
                    Return TrimedJson.StartsWithF("{") AndAlso TrimedJson.EndsWithF("}")
                End Function
                If _JsonText Is Nothing Then
                    Dim JsonPath As String = GetJsonPath()
                    _JsonText = If(FileUtils.TryReadAsString(JsonPath), "")
                    '如果 ReadFile 失败会返回空字符串；这可能是由于文件被临时占用，故延时后重试
                    If Not FastJsonCheck(_JsonText) Then
                        If RunInUi() Then
                            Logger.Warn("版本 JSON 文件为空或有误，由于代码在主线程运行，将不再进行重试")
                            _JsonText.DeserializeJson() '触发异常
                        Else
                            Logger.Warn($"版本 JSON 文件为空或有误，将在 2s 后重试读取（{JsonPath}）")
                            Thread.Sleep(2000)
                            _JsonText = FileUtils.ReadAsString(JsonPath)
                            If Not FastJsonCheck(_JsonText) Then _JsonText.DeserializeJson() '触发异常
                        End If
                    End If
                End If
                Return _JsonText
            End Get
            Set(value As String)
                _JsonText = value
            End Set
        End Property
        Private _JsonText As String = Nothing
        ''' <summary>
        ''' 该版本的 Json 对象。
        ''' 若 Json 存在问题，在获取该属性时即会抛出异常。
        ''' </summary>
        Public Property JsonObject As JObject
            Get
                '快照：W3 迁移完成前，外部对快照的修改不会写回核心（现有读取方均为只读，见文件头说明）
                If _JsonObject Is Nothing Then _JsonObject = CType(_core.VersionJson.ToJToken(), JObject)
                Return _JsonObject
            End Get
            Set(value As JObject)
                _JsonObject = value
            End Set
        End Property
        Private _JsonObject As JObject = Nothing
        ''' <summary>
        ''' 是否为旧版 Json 格式。
        ''' </summary>
        Public ReadOnly Property IsOldJson As Boolean
            Get
                Return _core.IsOldJson
            End Get
        End Property
        ''' <summary>
        ''' Json 是否为 HMCL 格式。
        ''' </summary>
        Public ReadOnly Property IsHmclFormatJson As Boolean
            Get
                Return _core.IsHmclFormatJson 'HMCL patches 合并在核心构建 VersionJson 时完成
            End Get
        End Property

        ''' <summary>
        ''' 版本 jar 中的 version.json 文件对象。
        ''' 若没有则返回 Nothing。
        ''' </summary>
        Public ReadOnly Property JsonVersion As JObject
            Get
                Static JsonVersionInited As Boolean = False
                If Not JsonVersionInited Then
                    JsonVersionInited = True
                    Try
                        If Not FileUtils.Exists($"{PathVersion}{Name}.jar") Then Exit Try
                        Using JarArchive = FileUtils.OpenZip(PathVersion & Name & ".jar")
                            Dim VersionJson As ZipArchiveEntry = JarArchive.GetEntry("version.json")
                            If VersionJson Is Nothing Then Exit Try
                            Using VersionJsonStream As New StreamReader(VersionJson.Open)
                                _JsonVersion = VersionJsonStream.ReadToEnd().DeserializeJson()
                            End Using
                        End Using
                    Catch ex As Exception
                        Logger.Warn(ex, $"从版本 jar 中读取 version.json 失败（{PathVersion}{Name}.jar）")
                    End Try
                End If
                Return _JsonVersion
            End Get
        End Property
        Private _JsonVersion As JObject = Nothing

        ''' <summary>
        ''' 该版本的依赖版本。若无依赖版本则为空字符串。
        ''' </summary>
        Public ReadOnly Property InheritName As String
            Get
                Return _core.InheritName '继承合并与 LiteLoader 特判在核心完成
            End Get
        End Property

        Public Config As JsonConfigProvider
        Public Sub ResetSettingsCache()
            IniClearCache(PathIndie & "options.txt")
            IniClearCache(PathVersion & "PCL\Setup.ini")
            Config.DiscardCache()
            For Each Entry In Settings.Entries.Values
                If Entry.Source = Settings.Sources.Instance Then Entry.ValueCache.Remove(PathVersion)
            Next
        End Sub

        ''' <summary>
        ''' 从版本名，或版本文件夹的完整路径初始化。
        ''' </summary>
        Public Sub New(NameOrFullPath As String)
            Dim VersionFolder As String = Instances.InstancePaths.ResolveVersionFolder(NameOrFullPath, McFolderSelected)
            _core = New Instances.InstanceCore(VersionFolder, McFolderSelected, VersionFolder, AddressOf RunInUi)
            Config = New JsonConfigProvider(Path.Combine(PathVersion, "PCL\config.json"))
        End Sub

        ''' <summary>
        ''' 检查 Minecraft 版本，若检查通过 State 则为 Original 且返回 True。
        ''' </summary>
        Public Function Check() As Boolean
            Dim Result = _core.Check()
            State = MapState(_core.State)
            Info = _core.Info
            _JsonObject = Nothing '快照按核心最新状态重建（含旧 Check 中 JSON 失败时的缓存失效）
            Return Result
        End Function
        ''' <summary>
        ''' 加载 Minecraft 版本的详细信息。不使用其缓存，且会更新缓存。
        ''' </summary>
        Public Function Load() As McInstance
            Try
                '检查、分类、描述基准值与 ini 缓存回调（State/Info/ReleaseTime/VersionXxx）由核心完成
                _core.Load(AddressOf PersistInstanceCache)
                State = MapState(_core.State)
                Info = _core.Info
                _JsonObject = Nothing
                '确定版本描述（旧 :696-697；CustomInfo 覆盖在本类处理，其余由核心计算）
                RefreshInfo()
                '确定版本图标（旧 :671-695）
                Logo = ReadIni(PathVersion & "PCL\Setup.ini", "Logo", "")
                If Logo = "" OrElse Not CType(ReadIni(PathVersion & "PCL\Setup.ini", "LogoCustom", False), Boolean) Then
                    Select Case State
                        Case McInstanceState.Original
                            Logo = PathImage & "Blocks/Grass.png"
                        Case McInstanceState.Snapshot
                            Logo = PathImage & "Blocks/CommandBlock.png"
                        Case McInstanceState.Old
                            Logo = PathImage & "Blocks/CobbleStone.png"
                        Case McInstanceState.Forge
                            Logo = PathImage & "Blocks/Anvil.png"
                        Case McInstanceState.NeoForge
                            Logo = PathImage & "Blocks/NeoForge.png"
                        Case McInstanceState.Fabric
                            Logo = PathImage & "Blocks/Fabric.png"
                        Case McInstanceState.OptiFine
                            Logo = PathImage & "Blocks/GrassPath.png"
                        Case McInstanceState.LiteLoader
                            Logo = PathImage & "Blocks/Egg.png"
                        Case McInstanceState.Fool
                            Logo = PathImage & "Blocks/GoldBlock.png"
                        Case Else
                            Logo = PathImage & "Blocks/RedstoneBlock.png"
                    End Select
                End If
                '确定版本收藏状态与显示种类（旧 :699-701）
                IsStar = ReadIni(PathVersion & "PCL\Setup.ini", "IsStar", False)
                DisplayType = ReadIni(PathVersion & "PCL\Setup.ini", "DisplayType", McInstanceCardType.Auto)
                'ini 读取完成后同步给核心（InstanceCore.Load 的约定：IsStar/DisplayType 由 wrapper 赋值），
                '供列表分类（InstanceScanner.ClassifyAndSort）与核心 Modable 判定读取
                _core.IsStar = IsStar
                _core.DisplayType = CInt(DisplayType)
                '写入缓存（旧 :703-707）：State 与 ReleaseTime/VersionXxx 项已经由 PersistInstanceCache 写入，
                'Info 需按 CustomInfo 覆盖后的值补写（值未变时 WriteIni 自动跳过），Logo 项留在本类
                If DirectoryUtils.Exists(PathVersion) Then
                    WriteIni(PathVersion & "PCL\Setup.ini", "Info", Info)
                    WriteIni(PathVersion & "PCL\Setup.ini", "Logo", Logo)
                End If
            Catch ex As Exception
                Info = ex.GetDisplay(False)
                Logo = PathImage & "Blocks/RedstoneBlock.png"
                State = McInstanceState.Error
                _core.State = MapState(McInstanceState.Error)
                _core.Info = Info
                Logger.Warn(ex, $"加载版本失败（{Name}）")
            Finally
                IsLoaded = True
            End Try
            Return Me
        End Function
        Public IsLoaded As Boolean = False
        ''' <summary>
        ''' 核心 Load 的 ini 缓存回调：等价旧 :703-717 的 Setup.ini 写入（Logo 项由本类另行写入）。
        ''' 注意：旧实现写 State 是 VB 枚举→String 隐式转换的产物——数值形式（如 1=Original）而非名称，
        ''' 这里须保持数值，以兼容 :1064 的读取与 PCL2 等外部工具对 ini 的解析。
        ''' </summary>
        Private Sub PersistInstanceCache(Key As String, Value As String)
            If Key = "State" Then
                WriteIni(PathVersion & "PCL\Setup.ini", Key, CInt(DirectCast([Enum].Parse(GetType(McInstanceState), Value), McInstanceState)).ToString())
            Else
                WriteIni(PathVersion & "PCL\Setup.ini", Key, Value)
            End If
        End Sub
        ''' <summary>
        ''' 根据当前组件信息刷新版本描述。
        ''' </summary>
        Public Sub RefreshInfo()
            Dim CustomInfo As String = ReadIni(PathVersion & "PCL\Setup.ini", "CustomInfo")
            If CustomInfo <> "" Then
                Info = CustomInfo
            ElseIf _VersionOverridden Then
                'Version 被外部覆盖（列表缓存/manifest 恢复）后核心无法感知，按旧 :732-745 逻辑计算
                Info = VersionDisplayName()
                If State = McInstanceState.Fool Then
                    Info = Instances.FoolNames.Get(Version.VanillaName)
                ElseIf State <> McInstanceState.Error Then
                    If Settings.Get(Of Integer)("VersionServerLogin", Instance:=Me) = 3 Then Info += ", 统一通行证验证"
                    If Settings.Get(Of Integer)("VersionServerLogin", Instance:=Me) = 4 Then Info += ", Authlib 验证"
                End If
            Else
                _core.State = MapState(State) '核心在 Error 态的描述会回读自身 Info，先同步 VB 侧存储
                _core.Info = Info
                _core.RefreshInfo(AddressOf VersionServerLogin)
                Info = _core.Info
            End If
        End Sub
        ''' <summary>供核心 RefreshInfo 回调的登录方式读取（经 VB façade，含实例旧设置懒式导入）。</summary>
        Private Function VersionServerLogin() As Integer
            Return Settings.Get(Of Integer)("VersionServerLogin", Instance:=Me)
        End Function
        ''' <summary>
        ''' 获取对版本信息的简短描述。
        ''' 例如 “快照 16w01a”、“原版 1.12.2, Forge 1.2.3”、“愚人节版本 2.0”。
        ''' </summary>
        Public Function VersionDisplayName() As String
            If _VersionOverridden Then Return VersionDisplayNameOverridden() '见 RefreshInfo：外部覆盖 Version 时核心无法感知
            Return _core.VersionDisplayName()
        End Function
        ''' <summary>旧 :750-785 的描述计算，仅用于 Version 被外部覆盖的场景（W3 随列表编排一并迁移）。</summary>
        Private Function VersionDisplayNameOverridden() As String
            'Mod Loader 信息
            Dim ModLoaderInfo As String = ""
            If Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then ModLoaderInfo += ", Forge" & If(Version.ModLoaderVersions(ModLoaders.Forge) = "未知版本", "", " " & Version.ModLoaderVersions(ModLoaders.Forge))
            If Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) Then ModLoaderInfo += ", NeoForge" & If(Version.ModLoaderVersions(ModLoaders.NeoForge) = "未知版本", "", " " & Version.ModLoaderVersions(ModLoaders.NeoForge))
            If Version.ModLoaders.HasFlagF(ModLoaders.Fabric) Then ModLoaderInfo += ", Fabric" & If(Version.ModLoaderVersions(ModLoaders.Fabric) = "未知版本", "", " " & Version.ModLoaderVersions(ModLoaders.Fabric))
            If Version.HasOptiFine Then ModLoaderInfo += ", OptiFine" & If(Version.OptiFine = "未知版本", "", " " & Version.OptiFine.Replace("-", " ").Replace("_", " "))
            If Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) Then ModLoaderInfo += ", LiteLoader"
            '基础信息
            Dim Info As String
            Select Case State
                Case McInstanceState.Snapshot, McInstanceState.Original, McInstanceState.Forge, McInstanceState.NeoForge, McInstanceState.Fabric, McInstanceState.OptiFine, McInstanceState.LiteLoader
                    If Version.VanillaName.ContainsIgnoreCase("pre") Then
                        Info = "预发布版 " & Version.VanillaName
                    ElseIf Version.VanillaName.ContainsIgnoreCase("rc") Then
                        Info = "发布候选 " & Version.VanillaName
                    ElseIf Version.VanillaName.ContainsIgnoreCase("experimental") Then
                        Info = "实验性快照" & Version.VanillaName
                    ElseIf Version.VanillaName = "pending" Then
                        Info = "实验性快照"
                    ElseIf Version.IsSnapshot Then
                        Info = If(Version.Reliable, "快照版 " & Version.VanillaName.Replace("-snapshot", ""), "快照版")
                    Else
                        Info = If(Version.Reliable, "正式版 " & Version.VanillaName, "正式版")
                    End If
                Case McInstanceState.Old
                    Info = "远古版本"
                Case McInstanceState.Fool
                    Info = "愚人节版本 " & Version.VanillaName
                Case McInstanceState.Error
                    Return Me.Info '已有错误信息
                Case Else
                    Return "发生了未知错误，请向作者反馈此问题"
            End Select
            Return (Info & ModLoaderInfo).Replace("_", "-")
        End Function

        Public Function ToListItem(Optional ContentHandler As Action(Of MyListItem, EventArgs) = Nothing) As MyVirtualizingElement(Of MyListItem)
            Return New MyVirtualizingElement(Of MyListItem)(
            Function()
                Dim NewItem As New MyListItem With {
                    .Info = Info, .Height = 42, .Tag = Me, .SnapsToDevicePixels = True, .Type = MyListItem.CheckType.Clickable,
                    .ContentHandler = ContentHandler
                }
                '标题
                NewItem.Inlines.Clear()
                NewItem.Inlines.Add(New Run(Name))
                If ReadIni(PathVersion & "PCL\Setup.ini", "CustomInfo") <> "" Then '如果版本设置了自定义描述，在标题后面以淡灰色显示其版本号
                    NewItem.Inlines.Add(New Run("  |  " & VersionDisplayName()) With {.Foreground = New MyColor(215, 215, 215), .FontSize = 12})
                End If
                'Logo
                Try
                    If Logo.EndsWithF("PCL\Logo.png") Then
                        NewItem.Logo = PathVersion & "PCL\Logo.png" '修复老版本中，存储的自定义 Logo 使用完整路径，导致移动后无法加载的 Bug
                    Else
                        NewItem.Logo = Logo
                    End If
                Catch ex As Exception
                    Logger.Error(ex, "加载版本图标失败", LogBehavior.Toast)
                    NewItem.Logo = "pack://application:,,,/images/Blocks/RedstoneBlock.png"
                End Try
                Return NewItem
            End Function) With {.Height = 42}
        End Function

        ''' <summary>
        ''' 重命名 MC 版本。
        ''' </summary>
        Public Function Rename(NewName As String) As McInstance
            Dim OldName = Name
            Dim OldPath = PathVersion
            Dim NewPath = PathUtils.AddSlashSuffix(Path.Combine(PathUtils.RemoveLastPart(OldPath), NewName))
            '清理缓存（旧 :831-833）
            Config.Save()
            ResetSettingsCache()
            '重命名主文件夹、jar 与 natives 文件夹，并重写版本 Json（旧 :822-830, :834-839, :846-853）
            _core.Rename(NewName)
            '替换版本设置文件中的路径（旧 :840-843，须在移动完成后执行）
            If FileUtils.Exists(NewPath & "PCL\Setup.ini") Then
                FileUtils.Write(NewPath & "PCL\Setup.ini", FileUtils.ReadAsString(NewPath & "PCL\Setup.ini").Replace(OldPath, NewPath))
            End If
            '更改已选中的版本（旧 :844-845）
            If ReadIni(McFolderSelected & "PCL.ini", "Version") = OldName Then WriteIni(McFolderSelected & "PCL.ini", "Version", NewName)
            Return New McInstance(NewPath).Load()
        End Function

        '运算符支持
        Public Overrides Function Equals(obj As Object) As Boolean
            Dim version = TryCast(obj, McInstance)
            Return version IsNot Nothing AndAlso PathUtils.IsSame(PathVersion, version.PathVersion)
        End Function
        Public Shared Operator =(a As McInstance, b As McInstance) As Boolean
            If a Is Nothing AndAlso b Is Nothing Then Return True
            If a Is Nothing OrElse b Is Nothing Then Return False
            Return PathUtils.IsSame(a.PathVersion, b.PathVersion)
        End Operator
        Public Shared Operator <>(a As McInstance, b As McInstance) As Boolean
            Return Not (a = b)
        End Operator

        ''' <summary>枚举映射：VB McInstanceState 与 C# InstanceState 的成员序一一对应（InstanceCore.cs:10-23）。</summary>
        Private Shared Function MapState(State As Instances.InstanceState) As McInstanceState
            Return CType(CInt(State), McInstanceState)
        End Function
        ''' <summary>枚举映射：VB McInstanceState 与 C# InstanceState 的成员序一一对应（InstanceCore.cs:10-23）。</summary>
        Private Shared Function MapState(State As McInstanceState) As Instances.InstanceState
            Return CType(CInt(State), Instances.InstanceState)
        End Function

    End Class

    ''' <summary>
    ''' MC 版本独占锁。
    ''' </summary>
    Public NotInheritable Class McInstanceLock
        Private Shared ReadOnly LockedPaths As New ConcurrentDictionary(Of String, IWorker)(StringComparer.OrdinalIgnoreCase)
        Friend Shared ReadOnly Property IsAnyLocked As Boolean
            Get
                Return Not LockedPaths.IsEmpty
            End Get
        End Property
        ''' <summary>
        ''' 在 <paramref name="Worker"/> 结束前，锁定指定的 MC 版本。当 <paramref name="Worker"/> 结束时，会自动将锁定释放。
        ''' <para/> 仅在该版本已被锁定时返回 false，此时不会改变锁定状态。
        ''' </summary>
        Public Shared Function TryAcquire(Worker As IWorker, InstancePath As String) As Boolean
            InstancePath = PathUtils.ForCompare(InstancePath)
            If Not LockedPaths.TryAdd(InstancePath, Worker) Then Return False
            AddHandler Worker.Stopped, Sub() LockedPaths.TryRemove(InstancePath, Nothing)
            Return True
        End Function
        ''' <summary>
        ''' 检查 MC 版本是否正被锁定。
        ''' </summary>
        Public Shared Function IsLocked(Instance As McInstance) As Boolean
            If Instance Is Nothing Then Return False
            Return LockedPaths.ContainsKey(PathUtils.ForCompare(Instance.PathVersion))
        End Function
    End Class

    ''' <summary>
    ''' 根据版本名获取对应的愚人节版本描述。非愚人节版本会返回空字符串。
    ''' </summary>
    Public Function GetMcFoolName(Name As String) As String
        Return Instances.FoolNames.Get(Name) '愚人节版本名表已迁至 C#
    End Function

    ''' <summary>
    ''' 当前按卡片分类的所有版本列表。
    ''' </summary>
    Public McInstanceList As New Dictionary(Of McInstanceCardType, List(Of McInstance))

#End Region

#Region "版本列表加载"

    ''' <summary>
    ''' 是否要求本次加载强制刷新版本列表。
    ''' </summary>
    Public McInstanceListForceRefresh As Boolean = False
    ''' <summary>
    ''' 是否为本次打开 PCL 后第一次加载版本列表。
    ''' 这会清理所有 .pclignore 文件，而非跳过这些对应版本。
    ''' </summary>
    Private IsFirstMcInstanceListLoad As Boolean = True

    ''' <summary>
    ''' 加载 Minecraft 文件夹的版本列表。
    ''' </summary>
    Public McInstanceListLoader As New LoaderTask(Of String, Integer)("Minecraft Instance List", AddressOf InitMcInstanceList) With {.ReloadTimeout = 1}
    Private Sub InitMcInstanceList(Loader As LoaderTask(Of String, Integer))
        '开始加载
        Dim PathMc As String = Loader.Input
        Try
            '初始化
            McInstanceList = New Dictionary(Of McInstanceCardType, List(Of McInstance))

            '检测缓存是否需要更新
            Dim FolderList As New List(Of String)
            If DirectoryUtils.Exists(PathMc & "versions") Then '不要使用 CheckPermission，会导致写入时间改变，从而使得文件夹被强制刷新
                Try
                    For Each Folder As DirectoryInfo In DirectoryUtils.GetInfo(PathMc & "versions").GetDirectories
                        FolderList.Add(Folder.Name)
                    Next
                Catch ex As Exception
                    Throw New Exception("无法读取版本文件夹，可能是由于没有权限（" & PathMc & "versions）", ex)
                End Try
            End If
            '不可用
            If Not FolderList.Any() Then
                WriteIni(PathMc & "PCL.ini", "InstanceCache", "") '清空缓存
                GoTo OnLoaded
            End If
            '有可用版本；根据文件夹名列表生成辨识码（旧 :976 的哈希已迁至 C# InstanceScanner.CacheCheckCode）
            Dim FolderListCheck As String = Instances.InstanceScanner.CacheCheckCode(PathMc, FolderList)
            If Not McInstanceListForceRefresh AndAlso ReadIni(PathMc & "PCL.ini", "InstanceCache") = FolderListCheck Then
                '可以使用缓存
                Dim Result = InitMcInstanceListWithCache(PathMc)
                If Result Is Nothing Then
                    GoTo Reload
                Else
                    McInstanceList = Result
                End If
            Else
                '文件夹列表不符
Reload:
                McInstanceListForceRefresh = False
                Logger.Info("文件夹列表变更，重载所有版本")
                WriteIni(PathMc & "PCL.ini", "InstanceCache", FolderListCheck)
                McInstanceList = InitMcInstanceListWithoutCache(PathMc)
            End If
            IsFirstMcInstanceListLoad = False

            '改变当前选择的版本
OnLoaded:
            If Loader.IsCanceled Then Return
            If McInstanceList.Any(Function(v) v.Key <> McInstanceCardType.Error) Then
                '尝试读取已储存的选择
                Dim SavedSelection As String = ReadIni(PathMc & "PCL.ini", "Version")
                If SavedSelection <> "" Then
                    For Each Card As KeyValuePair(Of McInstanceCardType, List(Of McInstance)) In McInstanceList
                        For Each Instance As McInstance In Card.Value
                            If Instance.Name = SavedSelection AndAlso Not Instance.State = McInstance.McInstanceState.Error Then
                                '使用已储存的选择
                                McInstanceSelected = Instance
                                Logger.Info($"选择该文件夹储存的 Minecraft 版本：{McInstanceSelected.PathVersion}")
                                Return
                            End If
                        Next
                    Next
                End If
                If Not McInstanceList.First.Value(0).State = McInstance.McInstanceState.Error Then
                    '自动选择第一项
                    McInstanceSelected = McInstanceList.First.Value(0)
                    Logger.Info($"自动选择 Minecraft 版本：{McInstanceSelected.PathVersion}")
                End If
            Else
                McInstanceSelected = Nothing
                Logger.Info("未找到可用 Minecraft 版本")
            End If
            If Settings.Get(Of Boolean)("SystemDebugDelay") Then Thread.Sleep(RandomInteger(200, 3000))
        Catch ex As Exception
            If Loader.IsCanceled OrElse ex.IsCanceled Then Return '#5617
            WriteIni(PathMc & "PCL.ini", "InstanceCache", "") '要求下次重新加载
            Logger.Error(ex, "加载 .minecraft 版本列表失败")
        End Try
    End Sub

    '获取版本列表
    Private Function InitMcInstanceListWithCache(Folder As String) As Dictionary(Of McInstanceCardType, List(Of McInstance))
        Dim Results As New Dictionary(Of McInstanceCardType, List(Of McInstance))
        Try
            Dim CardCount As Integer = ReadIni(Folder & "PCL.ini", "CardCount", -1)
            If CardCount = -1 Then Return Nothing
            For i = 0 To CardCount - 1
                Dim CardType As McInstanceCardType = ReadIni(Folder & "PCL.ini", "CardKey" & (i + 1), ":")
                Dim InstanceList As New List(Of McInstance)

                '循环读取版本
                For Each VersionName As String In ReadIni(Folder & "PCL.ini", "CardValue" & (i + 1), ":").Split(":")
                    If VersionName = "" Then Continue For
                    Dim FolderVersions As String = $"{Folder}versions\{VersionName}\"
                    If FileUtils.Exists(FolderVersions & ".pclignore") Then
                        If IsFirstMcInstanceListLoad Then
                            Logger.Info($"清理残留的忽略项目：{FolderVersions}") '#2781
                            FileUtils.Delete(FolderVersions & ".pclignore")
                        Else
                            Logger.Info($"跳过要求忽略的项目：{FolderVersions}")
                            Continue For
                        End If
                    End If
                    Try

                        '读取单个版本
                        Dim Instance As New McInstance(FolderVersions)
                        InstanceList.Add(Instance)
                        Instance.Info = ReadIni(Instance.PathVersion & "PCL\Setup.ini", "CustomInfo", "")
                        If Instance.Info = "" Then Instance.Info = ReadIni(Instance.PathVersion & "PCL\Setup.ini", "Info", Instance.Info)
                        Instance.Logo = ReadIni(Instance.PathVersion & "PCL\Setup.ini", "Logo", Instance.Logo)
                        Instance.State = ReadIni(Instance.PathVersion & "PCL\Setup.ini", "State", Instance.State)
                        Instance.IsStar = ReadIni(Instance.PathVersion & "PCL\Setup.ini", "IsStar", False)
                        Instance.DisplayType = ReadIni(Folder & "PCL\Setup.ini", "DisplayType", McInstanceCardType.Auto)
                        Dim Manifest As McInstallManifest = If(Instance.State = McInstance.McInstanceState.Error, Nothing, Configs.McInstallManifest.Get(Instance.Config))
                        '检查 manifest 是否有过期文件，并进行更新
                        If Manifest IsNot Nothing AndAlso Manifest.SchemaVersion <= Versions.McInstallManifestVersion AndAlso Manifest.ManagedFiles.Any(Function(File) File.Component IsNot Nothing) Then
                            Dim LatestManifest = Manifest.RefreshModComponents(Instance.PathIndie)
                            If LatestManifest IsNot Manifest Then
                                Manifest = LatestManifest
                                Configs.McInstallManifest.Set(Manifest, Instance.Config)
                                Instance.Config.Save()
                            End If
                        End If
                        Dim CachedVanillaName = ReadIni(Instance.PathVersion & "PCL\Setup.ini", "VersionVanillaName", "Unknown")
                        If Instance.State <> McInstance.McInstanceState.Error AndAlso (Manifest IsNot Nothing OrElse CachedVanillaName <> "Unknown") Then
                            Dim Version As McVersion
                            If Manifest IsNot Nothing Then
                                '从 manifest 中读取版本信息
                                Version = New McVersion(Manifest.Components) With {
                                    .OptiFineAsMod = Manifest.ManagedFiles.Any(Function(File) File.Component = "OptiFine")}
                            Else
                                '从 Setup.ini 中读取版本信息
                                Dim ReadCachedInfo =
                                Function(Key As String) As String
                                    Dim Value = ReadIni(Instance.PathVersion & "PCL\Setup.ini", Key, Nothing)
                                    Return If(Value = "", Nothing, Value) '对老版本 Setup.ini 的空值兼容
                                End Function
                                Version = New McVersion(CachedVanillaName) With {.OptiFine = ReadCachedInfo("VersionOptiFine")}
                                For Each Loader In {ModLoaders.Fabric, ModLoaders.Forge, ModLoaders.NeoForge}
                                    Dim CachedVersion = ReadCachedInfo($"Version{Loader}")
                                    If CachedVersion IsNot Nothing Then Version.ModLoaderVersions(Loader) = CachedVersion
                                Next
                                If ReadIni(Instance.PathVersion & "PCL\Setup.ini", "VersionLiteLoader", False) Then Version.ModLoaderVersions(ModLoaders.LiteLoader) = "未知版本"
                            End If
                            Version.ReleaseTime = ReadIni(Instance.PathVersion & "PCL\Setup.ini", "ReleaseTime", New Date(1970, 1, 1, 15, 0, 0))
                            Version.IsFool = Version.IsFool OrElse Instance.State = McInstance.McInstanceState.Fool
                            Version.IsSnapshot = Version.IsSnapshot OrElse Instance.State = McInstance.McInstanceState.Snapshot OrElse Instance.Name.ContainsIgnoreCase("combat")
                            Instance.Version = Version
                            If Manifest IsNot Nothing Then Instance.RefreshInfo()
                        Else
                            Instance.Load()
                        End If

                        '重新检查错误版本
                        If Instance.State = McInstance.McInstanceState.Error Then
                            '重新尝试获取版本信息
                            Dim OldDesc As String = Instance.Info
                            Instance.State = McInstance.McInstanceState.Original
                            Instance.Check()
                            '校验错误原因是否改变
                            Dim CustomInfo As String = ReadIni(Instance.PathVersion & "PCL\Setup.ini", "CustomInfo")
                            If Instance.State = McInstance.McInstanceState.Original OrElse (CustomInfo = "" AndAlso Not OldDesc = Instance.Info) Then
                                Logger.Info($"版本 {Instance.Name} 的错误状态已变更，新的状态为：{Instance.Info}")
                                Return Nothing
                            End If
                        End If

                        '校验未加载的版本
                        If Instance.Logo = "" Then
                            Logger.Info($"版本 {Instance.Name} 未被加载")
                            Return Nothing
                        End If

                    Catch ex As Exception
                        Logger.Warn(ex, $"读取版本加载缓存失败（{VersionName}）")
                        Return Nothing
                    End Try
                Next

                If InstanceList.Any Then Results.Add(CardType, InstanceList)
            Next
            Return Results
        Catch ex As Exception
            Logger.Warn(ex, "读取版本缓存失败")
            Return Nothing
        End Try
    End Function
    Private Function InitMcInstanceListWithoutCache(Folder As String) As Dictionary(Of McInstanceCardType, List(Of McInstance))
        '扫描版本文件夹并加载（旧 :1141-1168 的文件系统规则已迁至 C# InstanceScanner.ScanVersionFolders）；
        '建立 core→wrapper 回映，供分类结果组装 wrapper 列表
        Dim CoreMap As New Dictionary(Of Instances.InstanceCore, McInstance)
        Dim Cores As New List(Of Instances.InstanceCore)
        For Each VersionFolder As String In Instances.InstanceScanner.ScanVersionFolders(Folder, IsFirstMcInstanceListLoad)
            Dim Instance As New McInstance(VersionFolder)
            Instance.Load()
            Cores.Add(Instance.Core)
            CoreMap.Add(Instance.Core, Instance)
        Next

        Dim Results As New Dictionary(Of McInstanceCardType, List(Of McInstance))

        '将版本分类到各个卡片并排序（旧 :1171-1324 已迁至 C# InstanceScanner.ClassifyAndSort）；
        '错误边界保留在此：分类失败时清空结果并记录，随后与旧行为一致地继续写卡片缓存并返回
        Try
            For Each Card In Instances.InstanceScanner.ClassifyAndSort(Cores)
                Dim CardInstances As New List(Of McInstance)
                For Each Core As Instances.InstanceCore In Card.items
                    CardInstances.Add(CoreMap(Core))
                Next
                Results.Add(DirectCast(Card.cardType, McInstanceCardType), CardInstances)
            Next
        Catch ex As Exception
            Results.Clear()
            Logger.Error(ex, "分类版本列表失败")
        End Try

#Region "保存卡片缓存"
        WriteIni(Folder & "PCL.ini", "CardCount", Results.Count)
        For i = 0 To Results.Count - 1
            WriteIni(Folder & "PCL.ini", "CardKey" & (i + 1), Results.Keys(i))
            Dim Value As String = ""
            For Each Instance As McInstance In Results.Values(i)
                Value += Instance.Name & ":"
            Next
            WriteIni(Folder & "PCL.ini", "CardValue" & (i + 1), Value)
        Next
#End Region

        Return Results
    End Function
    Public Enum McInstanceCardType
        Star = -1
        Auto = 0 '仅用于强制版本分类的自动
        Hidden = 1
        API = 2
        OriginalLike = 3
        Rubbish = 4
        Fool = 5
        [Error] = 6
    End Enum

#End Region

#Region "皮肤"

    Public Structure McSkinInfo
        Public IsSlim As Boolean
        Public LocalFile As String
        Public IsVaild As Boolean
    End Structure
    ''' <summary>
    ''' 要求玩家选择一个皮肤文件，并进行相关校验。
    ''' </summary>
    Public Function McSkinSelect() As McSkinInfo
        Dim FileName As String = Dialogs.SelectFile("选择皮肤文件", False, filter:={({"png", "jpg", "jpeg", "webp"}, "皮肤文件")}).FirstOrDefault()

        '验证有效性
        If String.IsNullOrEmpty(FileName) Then Return New McSkinInfo With {.IsVaild = False}
        Try
            Dim Image As New MyBitmap(FileName)
            If Image.Pic.Width <> 64 OrElse Not (Image.Pic.Height = 32 OrElse Image.Pic.Height = 64) Then
                Hint("皮肤图片大小应为 64x32 像素或 64x64 像素！", HintType.Red)
                Return New McSkinInfo With {.IsVaild = False}
            End If
            Dim FileInfo = FileUtils.GetInfo(FileName)
            If FileInfo.Length > 24 * 1024 Then
                Hint("皮肤文件大小需小于 24 KB，而所选文件大小为 " & Math.Round(FileInfo.Length / 1024, 2) & " KB", HintType.Red)
                Return New McSkinInfo With {.IsVaild = False}
            End If
        Catch ex As Exception
            Logger.Error(ex, "皮肤文件存在错误", LogBehavior.Toast)
            Return New McSkinInfo With {.IsVaild = False}
        End Try

        '获取皮肤种类
        Dim IsSlim As Integer = MyMsgBox("此皮肤为 Steve 模型（粗手臂）还是 Alex 模型（细手臂）？", "选择皮肤种类", "Steve 模型", "Alex 模型", "我不知道", HighLight:=False)
        If IsSlim = 3 Then
            Hint("请在皮肤下载页面确认皮肤种类后再使用此皮肤！")
            Return New McSkinInfo With {.IsVaild = False}
        End If

        Return New McSkinInfo With {.IsVaild = True, .IsSlim = IsSlim = 2, .LocalFile = FileName}
    End Function

    ''' <summary>
    ''' 获取 Uuid 对应的皮肤文件地址，失败将抛出异常。
    ''' 判定链已迁至 PCLCS.Instances.SkinProfile.GetAddress（旧 :1409-1455）；
    ''' ini 缓存、网络请求与所选实例状态留在 VB，经回调参数注入。
    ''' </summary>
    Public Function McSkinGetAddress(UUID As String, Type As String) As String
        Return Instances.SkinProfile.GetAddress(UUID, Type, AddressOf SkinCacheRead, AddressOf SkinCacheWrite, AddressOf SkinFetchProfile, AddressOf SkinSelectedInstanceKey)
    End Function

    ''' <summary>皮肤地址 ini 缓存读取（旧 McSkinGetAddress :1413）。</summary>
    Private Function SkinCacheRead(Uuid As String, Type As String) As String
        Return ReadIni(PathTemp & "Cache\Skin\Index" & Type & ".ini", Uuid)
    End Function

    ''' <summary>皮肤地址 ini 缓存写入（旧 McSkinGetAddress :1452）。</summary>
    Private Sub SkinCacheWrite(Uuid As String, Type As String, SkinValue As String)
        WriteIni(PathTemp & "Cache\Skin\Index" & Type & ".ini", Uuid, SkinValue)
    End Sub

    ''' <summary>皮肤返回值网络请求（旧 McSkinGetAddress :1427）。</summary>
    Private Function SkinFetchProfile(Url As String) As String
        Return NetRequestByClientRetry(Url, RequireJson:=True)
    End Function

    ''' <summary>当前所选实例的版本路径，未选择时为 Nothing（旧 McSkinGetAddress :1421/:1423 的 McInstanceSelected）。</summary>
    Private Function SkinSelectedInstanceKey() As String
        Return If(McInstanceSelected Is Nothing, Nothing, McInstanceSelected.PathVersion)
    End Function

    Private ReadOnly McSkinDownloadLock As New Object
    ''' <summary>
    ''' 从 Url 下载皮肤，可能会使用缓存。返回本地文件路径，失败将抛出异常。
    ''' </summary>
    Public Function McSkinDownload(Address As String) As String
        Dim SkinName As String = PathUtils.GetLastPart(Address)
        Dim FileAddress As String = PathTemp & "Cache\Skin\" & Address.GetStableHashCode() & ".png"
        SyncLock McSkinDownloadLock
            If Not FileUtils.Exists(FileAddress) Then
                NetDownloadByClient(Address, FileAddress & ".PCLDownloading")
                FileUtils.Move(FileAddress & ".PCLDownloading", FileAddress)
                Logger.Info($"皮肤下载成功：{FileAddress}")
            End If
            Return FileAddress
        End SyncLock
    End Function

    ''' <summary>
    ''' 获取 Uuid 对应的皮肤，返回“Steve”或“Alex”。
    ''' 判定已迁至 PCLCS.Instances.SkinProfile.GetModel（旧 :1477-1485）。
    ''' </summary>
    Public Function McSkinSex(Uuid As String) As String
        Return Instances.SkinProfile.GetModel(Uuid)
    End Function

#End Region

#Region "支持库文件（Library）"

    Public Class McLibEntry
        ''' <summary>
        ''' 文件的完整本地路径。
        ''' </summary>
        Public LocalPath As String
        ''' <summary>
        ''' 文件大小。若无有效数据即为 0。
        ''' </summary>
        Public Size As Long = 0
        ''' <summary>
        ''' 是否为 Natives 文件。
        ''' </summary>
        Public IsNatives As Boolean = False
        ''' <summary>
        ''' 文件的 SHA1。
        ''' </summary>
        Public SHA1 As String = Nothing
        ''' <summary>
        ''' 由 Json 提供的 URL，若没有则为 Nothing。
        ''' </summary>
        Public Property Url As String
            Get
                Return _Url
            End Get
            Set(value As String)
                '孤儿 Forge 作者喜欢把没有 URL 的写个空字符串
                _Url = If(String.IsNullOrWhiteSpace(value), Nothing, value)
            End Set
        End Property
        Private _Url As String
        ''' <summary>
        ''' 原 Json 中 Name 项除去版本号部分的较前部分。可能为 Nothing。
        ''' </summary>
        Public ReadOnly Property Name As String
            Get
                If OriginalName Is Nothing Then Return Nothing
                Dim Splited As New List(Of String)(OriginalName.Split(":"))
                Splited.RemoveAt(2) 'Java 的此格式下版本号固定为第三段，第四段可能包含架构、分包等其他信息
                Return Splited.Join(":"c)
            End Get
        End Property
        ''' <summary>
        ''' 原 Json 中的 Name 项。
        ''' </summary>
        Public OriginalName As String

        Public Overrides Function ToString() As String
            Return If(IsNatives, "[Native] ", "") & StringUtils.FormatByteSize(Size) & " | " & LocalPath
        End Function
    End Class

    ''' <summary>
    ''' 检查是否符合 Json 中的 Rules。
    ''' </summary>
    ''' <param name="RuleToken">Json 中的 "rules" 项目。</param>
    Public Function McJsonRuleCheck(RuleToken As JToken) As Boolean
        Return Instances.Libraries.RuleCheck(RuleToken.ToJsonNode())
    End Function

    ''' <summary>
    ''' 将 C# LibEntry 数据面转换回 VB McLibEntry（转发层；字段语义见 PCLCS\Instances\Libraries.cs）。
    ''' </summary>
    Private Function ToLibEntry(Entry As Instances.LibEntry) As McLibEntry
        Return New McLibEntry With {
            .LocalPath = Entry.Path,
            .Size = CLng(Val(Entry.Size)),
            .IsNatives = Entry.IsNative,
            .SHA1 = Entry.Checksum,
            .Url = Entry.Url,
            .OriginalName = Entry.Name}
    End Function

    ''' <summary>
    ''' 递归获取 Minecraft 某一版本的完整支持库列表。
    ''' </summary>
    Public Function McLibListGet(Instance As McInstance, IncludeMainJar As Boolean) As List(Of McLibEntry)
        Return Instances.Libraries.ListGet(Instance.Core, IncludeMainJar).Select(AddressOf ToLibEntry).ToList()
    End Function
    ''' <summary>
    ''' 获取 Minecraft 某一版本忽视继承的支持库列表，即结果中没有继承项。
    ''' </summary>
    Public Function McLibListGetWithJson(JsonObject As JObject, Optional KeepSameNameDifferentVersionResult As Boolean = False, Optional CustomMcFolder As String = Nothing) As List(Of McLibEntry)
        Return Instances.Libraries.ListGetJson(
            JsonObject.ToJsonNode().AsObject(),
            KeepSameNameDifferentVersionResult,
            If(CustomMcFolder, McFolderSelected)).Select(AddressOf ToLibEntry).ToList()
    End Function

    ''' <summary>
    ''' 获取版本所需支持库文件的 NetFile。
    ''' </summary>
    Public Function McLibNetFilesFromInstance(Instance As McInstance) As List(Of NetFile)
        If Not Instance.IsLoaded Then Instance.Load()
        Dim Result As New List(Of NetFile)

        '更新此方法时需要同步更新 Forge 新版自动安装方法！

        '主 Jar 文件
        Try
            Dim MainJar As NetFile = DlClientJarGet(Instance, True)
            If MainJar IsNot Nothing Then Result.Add(MainJar)
        Catch ex As Exception
            Logger.Warn(ex, "版本缺失主 jar 文件所必须的信息")
        End Try

        'Library 文件
        Result.AddRange(McLibNetFilesFromTokens(McLibListGet(Instance, False)))

        '统一通行证文件
        If Settings.Get(Of Integer)("VersionServerLogin", Instance:=Instance) = 3 Then
            Dim TargetFile As String = PathPure.Value & "nide8auth.jar"
            Try
                '测试链接：https://auth.mc-user.com:233/00000000000000000000000000000000/
                Logger.Info("开始获取统一通行证下载信息")
                Dim DownloadInfo As JObject = NetRequestByClientRetry("https://auth.mc-user.com:233/" & Settings.Get(Of String)("VersionServerNide", Instance:=Instance), RequireJson:=True).DeserializeJson()
                Result.Add(New NetFile(
                    Urls:={"https://login.mc-user.com:233/index/jar"},
                    LocalPath:=TargetFile,
                    Checker:=New FileChecker With {.Hash = DownloadInfo("jarHash").ToString}))
            Catch ex As Exception
                If FileUtils.Exists(TargetFile) Then
                    Logger.Warn(ex, "获取统一通行证下载信息失败")
                Else
                    Throw New Exception("获取统一通行证下载信息失败", ex)
                End If
            End Try
        End If

        'Authlib-Injector 文件
        If Settings.Get(Of Integer)("VersionServerLogin", Instance:=Instance) = 4 Then
            Dim TargetFile = PathPure.Value & "authlib-injector.jar"
            Try
                Logger.Info("开始获取 Authlib-Injector 下载信息")
                Dim DownloadInfo As JObject = NetRequestByClientRetry(
                        "https://authlib-injector.yushi.moe/artifact/latest.json",
                        BackupUrl:="https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json", RequireJson:=True).DeserializeJson()
                Dim DownloadAddress As String = DownloadInfo("download_url").ToString.
                        Replace("bmclapi2.bangbang93.com/mirrors/authlib-injector", "authlib-injector.yushi.moe")
                Result.Add(New NetFile({
                    DownloadAddress,
                    DownloadAddress.Replace("authlib-injector.yushi.moe", "bmclapi2.bangbang93.com/mirrors/authlib-injector")
                }, TargetFile, New FileChecker With {.Hash = DownloadInfo("checksums")("sha256").ToString}))
            Catch ex As Exception
                If FileUtils.Exists(TargetFile) Then
                    Logger.Warn(ex, "获取 Authlib-Injector 下载信息失败")
                Else
                    Throw New Exception("获取 Authlib-Injector 下载信息失败", ex)
                End If
            End Try
        End If

        '跳过校验
        If ShouldIgnoreFileCheck(Instance) Then
            Logger.Warn("设置要求尽量忽略文件检查，这可能会保留有误的文件")
            Result = Result.Where(
            Function(f)
                If FileUtils.Exists(f.LocalPath) Then
                    Logger.Warn($"跳过下载的支持库文件：{f.LocalPath}")
                    Return False
                Else
                    Return True
                End If
            End Function).ToList
        End If

        Return Result
    End Function
    ''' <summary>
    ''' 将 McLibToken 列表转换为 NetFile。
    ''' </summary>
    Public Function McLibNetFilesFromTokens(Libs As List(Of McLibEntry), Optional CustomMcFolder As String = Nothing) As List(Of NetFile)
        CustomMcFolder = If(CustomMcFolder, McFolderSelected)
        Dim Result As New List(Of NetFile)
        '获取
        For Each Token As McLibEntry In Libs
            '检查器
            Dim Checker As FileChecker
            If Token.Name.ContainsIgnoreCase("labymod") Then
                Checker = New FileChecker '不检查 LabyMod 的文件，它们提供的文件校验信息是错的（#3225）
            Else
                Checker = New FileChecker With {.ActualSize = If(Token.Size = 0, -1, Token.Size), .Hash = Token.SHA1}
            End If
            'URL
            Dim Urls As New List(Of String)
            If Token.Url Is Nothing AndAlso Token.Name = "net.minecraftforge:forge:universal" Then '特判修复 Forge 部分 universal 文件缺失 URL（#5455）
                Token.Url = "https://maven.minecraftforge.net" & Token.LocalPath.Replace(CustomMcFolder & "libraries", "").Replace("\", "/")
            End If
            If Token.Url IsNot Nothing Then
                '获取 URL 的真实地址
                Urls.Add(Token.Url)
                If Token.Url.Contains("launcher.mojang.com/v1/objects") OrElse Token.Url.Contains("client.txt") OrElse
                   Token.Url.Contains(".tsrg") Then
                    Urls.AddRange(DlSourceLauncherOrMetaGet(Token.Url)) 'Mappings（#4425）
                End If
                If Token.Url.Contains("maven") Then
                    Dim BmclapiUrl As String =
                        Token.Url.Replace(Token.Url.Substring(0, Token.Url.IndexOfF("maven")), "https://bmclapi2.bangbang93.com/").Replace("maven.fabricmc.net", "maven").Replace("maven.minecraftforge.net", "maven").Replace("maven.neoforged.net/releases", "maven")
                    If DlSourcePreferMojang Then
                        Urls.Add(BmclapiUrl) '官方源优先
                    Else
                        Urls.Insert(0, BmclapiUrl) '镜像源优先
                    End If
                End If
            End If
            If Token.LocalPath.Contains("transformer-discovery-service") Then
                'Transformer 文件释放
                If Not FileUtils.Exists(Token.LocalPath) Then ExtractResources(Token.LocalPath, "Transformer")
                Logger.Warn("已自动释放 Transformer Discovery Service")
                Continue For
            ElseIf Token.LocalPath.Contains("optifine\OptiFine") Then
                'OptiFine 主 Jar
                Dim OptiFineBase As String = Token.LocalPath.Replace(CustomMcFolder & "libraries\optifine\OptiFine\", "").Split("_")(0) & "/" & PathUtils.GetLastPart(Token.LocalPath).Replace("-", "_")
                OptiFineBase = "/maven/com/optifine/" & OptiFineBase
                If OptiFineBase.Contains("_pre") Then OptiFineBase = OptiFineBase.Replace("com/optifine/", "com/optifine/preview_")
                Urls.Add("https://bmclapi2.bangbang93.com" & OptiFineBase)
            ElseIf Urls.Count <= 2 Then
                '普通文件
                Urls.AddRange(DlSourceLibraryGet("https://libraries.minecraft.net" & Token.LocalPath.Replace(CustomMcFolder & "libraries", "").Replace("\", "/")))
            End If
            Result.Add(New NetFile(Urls.Distinct, Token.LocalPath, Checker))
        Next
        '去重并返回
        Return Result.DistinctBy(Function(a) a.LocalPath).ToList
    End Function
    ''' <summary>
    ''' 获取对应的支持库文件地址。
    ''' </summary>
    ''' <param name="Original">原始地址，如 com.mumfrey:liteloader:1.12.2-SNAPSHOT。</param>
    ''' <param name="WithHead">是否包含 Lib 文件夹头部，若不包含，则会类似以 com\xxx\ 开头。</param>
    Public Function McLibGet(Original As String, Optional WithHead As Boolean = True, Optional IgnoreLiteLoader As Boolean = False, Optional CustomMcFolder As String = Nothing) As String
        Return Instances.Libraries.GetLocalPath(Original, WithHead, IgnoreLiteLoader, If(CustomMcFolder, McFolderSelected))
    End Function

    ''' <summary>
    ''' 检查设置，是否应当忽略文件检查？
    ''' </summary>
    Public Function ShouldIgnoreFileCheck(Instance As McInstance)
        Return Instances.Libraries.ShouldIgnoreFileCheck(Instance.Core)
    End Function

#End Region

#Region "资源文件（Assets）"

    '获取索引
    ''' <summary>
    ''' 获取某版本资源文件索引的对应 Json 项，详见版本 Json 中的 assetIndex 项。失败会抛出异常。
    ''' </summary>
    Public Function McAssetsGetIndex(Instance As McInstance, Optional ReturnLegacyOnError As Boolean = False, Optional CheckURLEmpty As Boolean = False) As JToken
        Return Instances.AssetsIndex.GetIndex(Instance.Core, ReturnLegacyOnError, CheckURLEmpty)?.ToJToken()
    End Function
    ''' <summary>
    ''' 获取某版本资源文件索引名，优先使用 assetIndex，其次使用 assets。失败会返回 legacy。
    ''' </summary>
    Public Function McAssetsGetIndexName(Instance As McInstance) As String
        Return Instances.AssetsIndex.GetIndexName(Instance.Core)
    End Function

    ''' <summary>
    ''' 将 C# 资源文件描述（path/hash/size，PCLCS\Instances\AssetsIndex.cs FixList）映射为 NetFile，
    ''' 字段映射照抄原 McAssetsFixList（旧 :1664-1668 与 :1685）。
    ''' </summary>
    Private Function McAssetsNetFile(Token As JsonObject) As NetFile
        Dim Hash As String = Token("hash")?.ToString()
        Dim Size As Long = CLng(Val(Token("size")?.ToString()))
        Return New NetFile(
            DlSourceAssetsGet($"https://resources.download.minecraft.net/{Left(If(Hash, ""), 2)}/{Hash}"),
            LocalPath:=Token("path")?.ToString(),
            Checker:=New FileChecker With {.ActualSize = If(Size = 0, -1, Size), .Hash = Hash})
    End Function

    ''' <summary>
    ''' 获取版本资源文件所对应的 NetFile。
    ''' </summary>
    Public Function McAssetsFixList(Instance As McInstance, CheckHash As Boolean, Optional ByRef ProgressFeed As LoaderBase = Nothing) As List(Of NetFile)
        '如果需要检查 Hash，则留到下载时处理，以借助多线程加快检查速度
        If CheckHash Then
            Return Instances.AssetsIndex.FixList(Instance.Core, True).Select(AddressOf McAssetsNetFile).ToList
        End If
        '如果不检查 Hash，则立即处理（文件存在性检查在 C# 侧完成，进度按缺失项计）
        Dim Result As New List(Of NetFile)
        Try
            Dim AssetsList = Instances.AssetsIndex.FixList(Instance.Core, False)
            If ProgressFeed IsNot Nothing Then ProgressFeed.Progress = 0.04
            For i = 0 To AssetsList.Count - 1
                If ProgressFeed IsNot Nothing Then ProgressFeed.Progress = 0.05 + 0.94 * i / AssetsList.Count
                Result.Add(McAssetsNetFile(AssetsList(i)))
            Next
        Catch ex As Exception
            Logger.Warn(ex, "获取版本缺失的资源文件下载列表失败")
        End Try
        If ProgressFeed IsNot Nothing Then ProgressFeed.Progress = 0.99
        Return Result
    End Function

#End Region

    ''' <summary>
    ''' 发送 Minecraft 更新提示。
    ''' </summary>
    Public Sub McDownloadClientUpdateHint(VersionName As String, Json As JObject)
        Try

            '获取对应版本
            Dim Version As JToken = Nothing
            For Each Token In Json("versions")
                If Token("id") IsNot Nothing AndAlso Token("id").ToString = VersionName Then
                    Version = Token
                    Exit For
                End If
            Next
            '进行提示
            If Version Is Nothing Then Return
            Dim Time As Date = Version("releaseTime")
            Dim MsgBoxText As String = $"新版本：{VersionName}{vbCrLf}" &
                If((Date.Now - Time).TotalDays > 1, "更新时间：" & Time.ToString, "更新于：" & StringUtils.FormatTimeSpan(Time - Date.Now, False))
            Dim MsgResult = MyMsgBox(MsgBoxText, "Minecraft 更新提示", "确定", "下载", If((Date.Now - Time).TotalHours > 3, "更新日志", ""),
                Button3Action:=Sub() McUpdateLogShow(Version))
            '弹窗结果
            If MsgResult = 2 Then
                '下载
                RunInUi(
                Sub()
                    PageDownloadInstall.VersionWaitingSelect = VersionName
                    FrmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall)
                End Sub)
            End If

        Catch ex As Exception
            Logger.Error(ex, $"Minecraft 更新提示发送失败（{If(VersionName, "Nothing")}）")
        End Try
    End Sub

    ''' <summary>
    ''' 打码字符串中的 AccessToken。
    ''' </summary>
    Public Function FilterAccessToken(Raw As String, FilterChar As Char) As String
        '打码 "accessToken " 后的内容
        If Raw.Contains("accessToken ") Then
            For Each Token In Raw.RegexSearch("(?<=accessToken ([^ ]{5}))[^ ]+(?=[^ ]{5})")
                Raw = Raw.Replace(Token, New String(FilterChar, Token.Count))
            Next
        End If
        '打码当前登录的结果
        Dim AccessToken As String = McLoginLoader.Output.AccessToken
        If AccessToken IsNot Nothing AndAlso AccessToken.Length >= 10 AndAlso Raw.ContainsIgnoreCase(AccessToken) AndAlso
            McLoginLoader.Output.Uuid <> McLoginLoader.Output.AccessToken Then 'UUID 和 AccessToken 一样则不打码
            Raw = Raw.Replace(AccessToken, Left(AccessToken, 5) & New String(FilterChar, AccessToken.Length - 10) & Right(AccessToken, 5))
        End If
        Return Raw
    End Function
    ''' <summary>
    ''' 打码字符串中的 Windows 用户名。
    ''' </summary>
    Public Function FilterUserName(Raw As String, FilterChar As Char) As String
        If Raw.Contains(":\Users\") Then
            Raw = RegularExpressions.Regex.Replace(Raw, "(?<=:\\Users\\)[^\\]+", Function(Token) New String(FilterChar, Token.Length))
        End If
        If Raw.Contains(":/Users/") Then
            Raw = RegularExpressions.Regex.Replace(Raw, "(?<=:/Users/)[^/]+", Function(Token) New String(FilterChar, Token.Length))
        End If
        Return Raw
    End Function

End Module
