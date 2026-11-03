Public Module ModLaunch

#Region "开始"

    Public CurrentLaunchOptions As McLaunchOptions = Nothing
    ''' <summary>
    ''' 启动选项（模型已迁 C# PCLCS.Launch.McLaunchOptions，原 :6-26 字段逐字）；
    ''' 此处转发保持宿主读写面，Instance 仍为 McInstance 类型（C# 侧为 object 承载位）。
    ''' </summary>
    Public Class McLaunchOptions
        Private ReadOnly _Core As New PCLCS.Launch.McLaunchOptions
        ''' <summary>
        ''' 强制指定在启动后进入的服务器 IP。
        ''' 默认值：Nothing。使用版本设置的值。
        ''' </summary>
        Public Property ServerIp As String
            Get
                Return _Core.ServerIp
            End Get
            Set(value As String)
                _Core.ServerIp = value
            End Set
        End Property
        ''' <summary>
        ''' 将启动脚本保存到该地址，然后取消启动。这同时会改变启动时的提示等。
        ''' 默认值：Nothing。不保存。
        ''' </summary>
        Public Property SaveBatch As String
            Get
                Return _Core.SaveBatch
            End Get
            Set(value As String)
                _Core.SaveBatch = value
            End Set
        End Property
        ''' <summary>
        ''' 强行指定启动的 MC 版本。
        ''' 默认值：Nothing。使用 McInstanceCurrent。
        ''' </summary>
        Public Property Instance As McInstance
            Get
                Return _Core.Instance
            End Get
            Set(value As McInstance)
                _Core.Instance = value
            End Set
        End Property
        ''' <summary>
        ''' 额外的启动参数。
        ''' </summary>
        Public Property ExtraGameArgs As List(Of String)
            Get
                Return _Core.ExtraGameArgs
            End Get
            Set(value As List(Of String))
                _Core.ExtraGameArgs = value
            End Set
        End Property
    End Class
    ''' <summary>
    ''' 尝试启动 Minecraft。必须在 UI 线程调用。
    ''' 返回是否实际开始了启动（如果没有，则一定弹出了错误提示）。
    ''' </summary>
    Public Function McLaunchStart(Optional Options As McLaunchOptions = Nothing) As Boolean
        CurrentLaunchOptions = If(Options, New McLaunchOptions)
        '预检查
        If Not RunInUi() Then Throw New Exception("McLaunchStart 必须在 UI 线程调用！")
        If McLaunchLoader.State = LoadState.Loading Then
            Hint("已有游戏正在启动中！", HintType.Red)
            Return False
        End If
        '强制切换需要启动的版本
        If CurrentLaunchOptions.Instance IsNot Nothing AndAlso McInstanceSelected <> CurrentLaunchOptions.Instance Then
            McLaunchLog("在启动前切换到版本 " & CurrentLaunchOptions.Instance.Name)
            '检查版本是否存在
            Try
                CurrentLaunchOptions.Instance.GetJsonPath()
            Catch
                Hint($"无法启动 {CurrentLaunchOptions.Instance.Name}：当前 Minecraft 文件夹中尚未安装该版本！", HintType.Red)
                Return False
            End Try
            '检查版本
            CurrentLaunchOptions.Instance.Load()
            If CurrentLaunchOptions.Instance.State = McInstance.McInstanceState.Error Then
                Hint($"无法启动 {CurrentLaunchOptions.Instance.Name}：{CurrentLaunchOptions.Instance.Info}", HintType.Red)
                Return False
            End If
            '切换版本
            McInstanceSelected = CurrentLaunchOptions.Instance
            FrmLaunchLeft.RefreshButtonsUI()
            FrmLaunchLeft.RefreshPage(False, False)
        End If
        If McInstanceSelected IsNot Nothing AndAlso McInstanceLock.IsLocked(McInstanceSelected) Then
            Hint($"版本 {McInstanceSelected.Name} 正在被修改，请稍后再启动！", HintType.Red)
            Return False
        End If
        FrmMain.AprilGiveup()
        '禁止进入版本选择页面（否则就可以在启动中切换 McInstanceSelected 了）
        FrmMain.PageStack = FrmMain.PageStack.Where(Function(p) p.Page <> FormMain.PageType.InstanceSelect).ToList
        '实际启动加载器
        McLaunchLoader.Start(Options, IsForceRestart:=True)
        Return True
    End Function

    ''' <summary>
    ''' 记录启动日志。
    ''' </summary>
    Public Sub McLaunchLog(Text As String, Optional Level As LogLevel = LogLevel.Info)
        Text = FilterUserName(FilterAccessToken(Text, "*"), "*")
        If ModeDebug Then
            RunInUi(
            Sub()
                FrmLaunchRight.LabLog.AppendText($"[{Date.Now:HH':'mm':'ss'.'fff}] {Text}{vbCrLf}")
            End Sub)
        End If
        Logger.Log(Text, Level)
    End Sub

    '启动状态切换
    Public McLaunchLoader As New LoaderTask(Of McLaunchOptions, Object)("Loader Launch", AddressOf McLaunchStart) With {.OnStateChanged = AddressOf McLaunchState}
    Public McLaunchLoaderReal As LoaderCombo(Of Object)
    Public McLaunchProcess As Process
    Public McLaunchWatcher As Watcher
    Private Sub McLaunchState(Loader As LoaderTask(Of McLaunchOptions, Object))
        Select Case McLaunchLoader.State
            Case LoadState.Finished, LoadState.Failed, LoadState.Waiting, LoadState.Canceled
                FrmLaunchLeft.PageChangeToLogin()
            Case LoadState.Loading
                '在预检测结束后再触发动画
                FrmLaunchRight.LabLog.Text = ""
        End Select
    End Sub
    ''' <summary>
    ''' 指定启动中断时的提示文本。若不为 Nothing 则会显示为绿色。
    ''' </summary>
    Private CanceledHint As String = Nothing

    '实际的启动方法
    Private Sub McLaunchStart(Loader As LoaderTask(Of McLaunchOptions, Object))
        '开始动画
        RunInUiWait(AddressOf FrmLaunchLeft.PageChangeToLaunching)
        '预检测（预检测的错误将直接抛出）
        Try
            McLaunchPrecheck()
            McLaunchLog("预检测已通过")
        Catch ex As Exception
            If Not ex.IsCanceled Then Hint(ex.Message, HintType.Red)
            Throw
        End Try
        Dim IsSavingBatch As Boolean = CurrentLaunchOptions?.SaveBatch IsNot Nothing

        '正式加载
        Try
            '构造主加载器
            Dim Loaders As New List(Of LoaderBase) From {
                New LoaderTask(Of Integer, Integer)("获取 Java", AddressOf McLaunchJava) With {.ProgressWeight = 4, .Block = False},
                McLoginLoader, '.ProgressWeight = 15, .Block = False
                New LoaderCombo(Of String)("补全文件", DlClientFix(McInstanceSelected, False, True)) With {.ProgressWeight = 15, .Show = False},
                New LoaderTask(Of String, Integer)("获取启动参数", AddressOf McLaunchArgumentMain) With {.ProgressWeight = 2},
                New LoaderTask(Of Integer, Integer)("解压文件", AddressOf McLaunchNatives) With {.ProgressWeight = 2},
                New LoaderTask(Of Integer, Integer)("预启动处理", AddressOf McLaunchPrerun) With {.ProgressWeight = 1},
                New LoaderTask(Of Integer, Integer)("执行自定义命令", AddressOf McLaunchCustom) With {.ProgressWeight = 1},
                New LoaderTask(Of Integer, Process)("启动进程", AddressOf McLaunchRun) With {.ProgressWeight = 2},
                New LoaderTask(Of Process, Integer)("等待游戏窗口出现", AddressOf McLaunchWait) With {.ProgressWeight = 1},
                New LoaderTask(Of Integer, Integer)("结束处理", AddressOf McLaunchEnd) With {.ProgressWeight = 1}
            }
            '内存优化
            Select Case Settings.Get(Of Integer)("VersionRamOptimize", Instance:=McInstanceSelected)
                Case 0 '全局
                    If Settings.Get(Of Boolean)("LaunchArgumentRam") Then '使用全局设置
                        CType(Loaders(2), LoaderCombo(Of String)).Block = False
                        Loaders.Insert(3, New LoaderTask(Of Integer, Integer)("内存优化", AddressOf McLaunchMemoryOptimize) With {.ProgressWeight = 30})
                    End If
                Case 1 '开启
                    CType(Loaders(2), LoaderCombo(Of String)).Block = False
                    Loaders.Insert(3, New LoaderTask(Of Integer, Integer)("内存优化", AddressOf McLaunchMemoryOptimize) With {.ProgressWeight = 30})
                Case 2 '关闭
            End Select
            Dim LaunchLoader As New LoaderCombo(Of Object)("Minecraft 启动", Loaders) With {.Show = False}
            If McLoginLoader.State = LoadState.Finished Then McLoginLoader.State = LoadState.Waiting '要求重启登录主加载器，它会自行决定是否启动副加载器
            '等待加载器执行并更新 UI
            McLaunchLoaderReal = LaunchLoader
            CanceledHint = Nothing
            LaunchLoader.Start()
            '任务栏进度条
            LoaderTaskbarAdd(LaunchLoader)
            Do While LaunchLoader.State = LoadState.Loading
                FrmLaunchLeft.Dispatcher.Invoke(AddressOf FrmLaunchLeft.LaunchingRefresh)
                Thread.Sleep(100)
            Loop
            FrmLaunchLeft.Dispatcher.Invoke(AddressOf FrmLaunchLeft.LaunchingRefresh)
            '成功与失败处理
            Select Case LaunchLoader.State
                Case LoadState.Finished
                    Hint(McInstanceSelected.Name & " 启动成功！", HintType.Green)
                Case LoadState.Canceled
                    If CanceledHint Is Nothing Then
                        Hint(If(IsSavingBatch, "已取消导出启动脚本！", "已取消启动！"), HintType.Blue)
                    Else
                        Hint(CanceledHint, HintType.Green)
                    End If
                Case LoadState.Failed
                    Throw LaunchLoader.Error
                Case Else
                    Throw New Exception("错误的状态改变：" & LaunchLoader.State.ToString())
            End Select
        Catch ex As Exception
            ex.ThrowIfCanceled()
            Dim CurrentEx = ex
NextInner:
            If CurrentEx.Message.StartsWithF("$") Then
                '若有以 $ 开头的错误信息，则以此为准显示提示
                MyMsgBox(CurrentEx.Message.TrimStart("$"), If(IsSavingBatch, "导出启动脚本失败", "启动失败"))
                Throw
            ElseIf CurrentEx.InnerException IsNot Nothing Then
                '检查下一级错误
                CurrentEx = CurrentEx.InnerException
                GoTo NextInner
            Else
                '没有特殊处理过的错误信息
                McLaunchLog("错误：" & ex.GetDisplay(True))
                Logger.Error(ex, If(IsSavingBatch, "导出启动脚本失败", "Minecraft 启动失败"), LogBehavior.Alert)
                Throw
            End If
        End Try
    End Sub

#End Region

#Region "内存优化"

    Private Sub McLaunchMemoryOptimize(Loader As LoaderTask(Of Integer, Integer))
        McLaunchLog("内存优化开始")
        Dim Finished As Boolean = False
        RunInNewThread(
        Sub()
            PageOtherTest.MemoryOptimize(False)
            Finished = True
        End Sub, "Launch Memory Optimize")
        Do While Not Finished AndAlso Not Loader.IsCanceled
            If Loader.Progress < 0.7 Then
                Loader.Progress += 0.007 '10s
            Else
                Loader.Progress += (0.95 - Loader.Progress) * 0.02 '最快 += 0.005
            End If
            Thread.Sleep(100)
        Loop
    End Sub

#End Region

#Region "预检测"

    Private Sub McLaunchPrecheck()
        If Settings.Get(Of Boolean)("SystemDebugDelay") Then Thread.Sleep(RandomInteger(100, 2000))
        '检查路径
        If McInstanceSelected.PathIndie.Contains("!") OrElse McInstanceSelected.PathIndie.Contains(";") Then Throw New Exception("游戏路径中不可包含 ! 或 ;（" & McInstanceSelected.PathIndie & "）")
        If McInstanceSelected.PathVersion.Contains("!") OrElse McInstanceSelected.PathVersion.Contains(";") Then Throw New Exception("游戏路径中不可包含 ! 或 ;（" & McInstanceSelected.PathVersion & "）")
        '检查版本
        If McInstanceSelected Is Nothing Then Throw New Exception("未选择 Minecraft 版本！")
        McInstanceSelected.Load()
        If McInstanceSelected.State = McInstance.McInstanceState.Error Then Throw New Exception("Minecraft 存在问题：" & McInstanceSelected.Info)
        '检查输入信息
        Dim CheckResult As String = ""
        RunInUiWait(Sub() CheckResult = McLoginAble(McLoginInput()))
        If CheckResult <> "" Then Throw New ArgumentException(CheckResult)
        '正版购买提示
        If CurrentLaunchOptions?.SaveBatch Is Nothing AndAlso '保存脚本时不提示
           Not Settings.Get(Of Boolean)("HintBuy") AndAlso Settings.Get(Of McLoginType)("LoginType") <> McLoginType.Ms Then
            If Globalization.CultureInfo.CurrentCulture.Name.StartsWithF("zh") OrElse Globalization.CultureInfo.CurrentUICulture.Name.StartsWithF("zh") Then '中文？
                RunInNewThread(
                Sub()
                    Select Case Settings.Get(Of Integer)("SystemLaunchCount")
                        Case 3, 8, 15, 30, 50, 70, 90, 110, 130, 180, 220, 280, 330, 380, 450, 550, 660, 750, 880, 950, 1100, 1300, 1500, 1700, 1900
                            If MyMsgBox("你已经启动了 " & Settings.Get(Of Integer)("SystemLaunchCount") & " 次 Minecraft 啦！" & vbCrLf &
                                "如果觉得 Minecraft 还不错，可以购买正版支持一下，毕竟开发游戏也真的很不容易……不要一直白嫖啦。" & vbCrLf & vbCrLf &
                                "在登录一次正版账号后，就不会再出现这个提示了！",
                                "考虑一下正版？", "支持正版游戏！", "下次一定") = 1 Then
                                OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj")
                            End If
                    End Select
                End Sub, "Buy Minecraft")
            ElseIf Settings.Get(Of McLoginType)("LoginType") = McLoginType.Legacy Then
                Select Case MyMsgBox("你必须先登录正版账号，才能进行离线登录！", "正版验证", "购买正版", "试玩", "返回",
                    Button1Action:=Sub() OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj"))
                    Case 2
                        Hint("游戏将以试玩模式启动！", HintType.Red)
                        CurrentLaunchOptions.ExtraGameArgs.Add("--demo")
                    Case 3
                        Throw New OperationCanceledException
                End Select
            End If
        End If
    End Sub

#End Region

#Region "主登录模块"

    '登录方式
    Public Enum McLoginType
        Legacy = 0
        Nide = 2
        Auth = 3
        Ms = 5
    End Enum

    '各个登录方式的对应数据
    Public MustInherit Class McLoginData
        ''' <summary>
        ''' 登录方式。
        ''' </summary>
        Public Type As McLoginType
        Public Overrides Function Equals(obj As Object) As Boolean
            Return obj IsNot Nothing AndAlso obj.GetHashCode() = GetHashCode()
        End Function
    End Class
    Public Class McLoginServer
        Inherits McLoginData

        ''' <summary>
        ''' 登录用户名。
        ''' </summary>
        Public UserName As String
        ''' <summary>
        ''' 登录密码。
        ''' </summary>
        Public Password As String
        ''' <summary>
        ''' 登录服务器基础地址。
        ''' </summary>
        Public BaseUrl As String
        ''' <summary>
        ''' 登录所使用的标识符，目前只可能为 “Auth” 或 “Nide”，用于存储缓存等。
        ''' </summary>
        Public Token As String
        ''' <summary>
        ''' 登录方式的描述字符串，如 “正版”、“统一通行证”。
        ''' </summary>
        Public Description As String
        ''' <summary>
        ''' 是否在本次登录中强制要求玩家重新选择角色，目前仅对 Authlib-Injector 生效。
        ''' </summary>
        Public ForceReselectProfile As Boolean = False

        Public Sub New(Type As McLoginType)
            Me.Type = Type
        End Sub
        Public Overrides Function GetHashCode() As Integer
            Return (UserName & Password & BaseUrl & Token & Type).GetStableHashCode() Mod Integer.MaxValue
        End Function

    End Class
    Public Class McLoginMs
        Inherits McLoginData

        ''' <summary>
        ''' 缓存的 OAuth Refresh Token。若没有则为空字符串。
        ''' </summary>
        Public OAuthRefreshToken As String = ""
        Public AccessToken As String = ""
        Public Uuid As String = ""
        Public UserName As String = ""
        Public ProfileJson As String = ""

        Public Sub New()
            Type = McLoginType.Ms
        End Sub
        Public Overrides Function GetHashCode() As Integer
            '不能使用全部信息，这俩就足以在登录输入时确保是同一个用户了
            Return (OAuthRefreshToken & UserName).GetStableHashCode() Mod Integer.MaxValue
        End Function
    End Class
    Public Class McLoginLegacy
        Inherits McLoginData
        ''' <summary>
        ''' 登录用户名。
        ''' </summary>
        Public UserName As String
        ''' <summary>
        ''' 皮肤种类。
        ''' </summary>
        Public SkinType As Integer
        ''' <summary>
        ''' 若采用正版皮肤，则为该皮肤名。
        ''' </summary>
        Public SkinName As String

        Public Sub New()
            Type = McLoginType.Legacy
        End Sub
        Public Overrides Function GetHashCode() As Integer
            Return (UserName & SkinType & SkinName & Type).GetStableHashCode() Mod Integer.MaxValue
        End Function
    End Class

    '登录返回结果
    Public Structure McLoginResult
        Public Name As String
        Public Uuid As String
        Public AccessToken As String
        Public Type As String
        Public ClientToken As String
        ''' <summary>
        ''' 进行微软登录时返回的 profile 信息。
        ''' </summary>
        Public ProfileJson As String
    End Structure

    ''' <summary>
    ''' 根据登录信息获取玩家的 MC 用户名。如果无法获取则返回 Nothing。
    ''' </summary>
    Public Function McLoginName() As String
        '根据当前登录方式优先返回
        Select Case Settings.Get(Of McLoginType)("LoginType")
            Case McLoginType.Ms
                If Settings.Get(Of String)("CacheMsV2Name") <> "" Then Return Settings.Get(Of String)("CacheMsV2Name")
            Case McLoginType.Legacy
                If Settings.Get(Of String)("LoginLegacyName") <> "" Then Return Settings.Get(Of String)("LoginLegacyName").ToString.BeforeFirst("¨")
            Case McLoginType.Nide
                If Settings.Get(Of String)("CacheNideName") <> "" Then Return Settings.Get(Of String)("CacheNideName")
            Case McLoginType.Auth
                If Settings.Get(Of String)("CacheAuthName") <> "" Then Return Settings.Get(Of String)("CacheAuthName")
        End Select
        '查找所有可能的项
        If Settings.Get(Of String)("CacheMsV2Name") <> "" Then Return Settings.Get(Of String)("CacheMsV2Name")
        If Settings.Get(Of String)("CacheNideName") <> "" Then Return Settings.Get(Of String)("CacheNideName")
        If Settings.Get(Of String)("CacheAuthName") <> "" Then Return Settings.Get(Of String)("CacheAuthName")
        If Settings.Get(Of String)("LoginLegacyName") <> "" Then Return Settings.Get(Of String)("LoginLegacyName").ToString.BeforeFirst("¨")
        Return Nothing
    End Function
    ''' <summary>
    ''' 当前是否可以进行登录。若不可以则会返回错误原因。
    ''' </summary>
    Public Function McLoginAble() As String
        Select Case Settings.Get(Of McLoginType)("LoginType")
            Case McLoginType.Ms
                If Settings.Get(Of String)("CacheMsV2OAuthRefresh") = "" Then
                    Return FrmLoginMs.IsVaild()
                Else
                    Return ""
                End If
            Case McLoginType.Legacy
                Return FrmLoginLegacy.IsVaild()
            Case McLoginType.Nide
                If Settings.Get(Of String)("CacheNideAccess") = "" Then
                    Return FrmLoginNide.IsVaild()
                Else
                    Return ""
                End If
            Case McLoginType.Auth
                If Settings.Get(Of String)("CacheAuthAccess") = "" Then
                    Return FrmLoginAuth.IsVaild()
                Else
                    Return ""
                End If
            Case Else
                Return "未知的登录方式"
        End Select
    End Function
    ''' <summary>
    ''' 登录输入是否可以进行登录。若不可以则会返回错误原因。
    ''' </summary>
    Public Function McLoginAble(LoginData As McLoginData) As String
        Select Case LoginData.Type
            Case McLoginType.Ms
                Return PageLoginMs.IsVaild(LoginData)
            Case McLoginType.Legacy
                Return PageLoginLegacy.IsVaild(LoginData)
            Case McLoginType.Nide
                Return PageLoginNide.IsVaild(LoginData)
            Case McLoginType.Auth
                Return PageLoginAuth.IsVaild(LoginData)
            Case Else
                Return "未知的登录方式"
        End Select
    End Function

    '登录主模块加载器
    Public McLoginLoader As New LoaderTask(Of McLoginData, McLoginResult)("登录", AddressOf McLoginStart, AddressOf McLoginInput, ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1, .ProgressWeight = 15, .Block = False}
    Public Function McLoginInput() As McLoginData
        Dim LoginData As McLoginData = Nothing
        Dim LoginType = Settings.Get(Of McLoginType)("LoginType")
        Try
            Select Case LoginType
                Case McLoginType.Legacy
                    LoginData = PageLoginLegacy.GetLoginData()
                Case McLoginType.Ms
                    If Settings.Get(Of String)("CacheMsV2OAuthRefresh") = "" Then
                        LoginData = PageLoginMs.GetLoginData()
                    Else
                        LoginData = PageLoginMsSkin.GetLoginData()
                    End If
                Case McLoginType.Nide
                    If Settings.Get(Of String)("CacheNideAccess") = "" Then
                        LoginData = PageLoginNide.GetLoginData()
                    Else
                        LoginData = PageLoginNideSkin.GetLoginData()
                    End If
                Case McLoginType.Auth
                    If Settings.Get(Of String)("CacheAuthAccess") = "" Then
                        LoginData = PageLoginAuth.GetLoginData()
                    Else
                        LoginData = PageLoginAuthSkin.GetLoginData()
                    End If
            End Select
            Return LoginData
        Catch ex As Exception
            Throw New Exception($"获取登录输入信息失败（{LoginType}）", ex)
        End Try
    End Function
    Private Sub McLoginStart(Data As LoaderTask(Of McLoginData, McLoginResult))
        McLaunchLog("登录加载已开始")
        '校验登录信息
        Dim CheckResult As String = McLoginAble(Data.Input)
        If Not CheckResult = "" Then Throw New ArgumentException(CheckResult)
        '获取对应加载器
        Dim Loader As LoaderBase = Nothing
        Select Case Data.Input.Type
            Case McLoginType.Ms
                Loader = McLoginMsLoader
            Case McLoginType.Legacy
                Loader = McLoginLegacyLoader
            Case McLoginType.Nide
                Loader = McLoginNideLoader
            Case McLoginType.Auth
                Loader = McLoginAuthLoader
        End Select
        '尝试加载
        Loader.WaitForExit(Data.Input, McLoginLoader, Data.IsForceRestarting)
        Data.Output = CType(Loader, Object).Output
        RunInUi(Sub() FrmLaunchLeft.RefreshPage(True, False)) '刷新自动填充列表
        McLaunchLog("登录加载已结束")
    End Sub

#End Region
#Region "分方式登录模块"

    '各个登录方式的主对象与输入构造
    Public McLoginMsLoader As New LoaderTask(Of McLoginMs, McLoginResult)("Loader Login Ms", AddressOf McLoginMsStart) With {.ReloadTimeout = 1}
    Public McLoginLegacyLoader As New LoaderTask(Of McLoginLegacy, McLoginResult)("Loader Login Legacy", AddressOf McLoginLegacyStart)
    Public McLoginNideLoader As New LoaderTask(Of McLoginServer, McLoginResult)("Loader Login Nide", AddressOf McLoginServerStart) With {.ReloadTimeout = 1000 * 60 * 10}
    Public McLoginAuthLoader As New LoaderTask(Of McLoginServer, McLoginResult)("Loader Login Auth", AddressOf McLoginServerStart) With {.ReloadTimeout = 1000 * 60 * 10}

    '主加载函数，返回所有需要的登录信息
    Private Sub McLoginMsStart(Data As LoaderTask(Of McLoginMs, McLoginResult))
        Dim Input As McLoginMs = Data.Input
        Dim LogUsername As String = Input.UserName
        McLaunchLog("登录方式：正版（" & If(LogUsername = "", "尚未登录", LogUsername) & "）")
        Data.Progress = 0.05
        '检查是否已经登录完成
        Dim ExpiresAt = Settings.Get(Of Long)("CacheMsV2Expires")
        If Not Data.IsForceRestarting AndAlso '不要求强行重启
           ExpiresAt > 0 AndAlso ExpiresAt > ToUnixTimestampUtc(Date.UtcNow) AndAlso 'AccessToken 尚未过期
           Input.UserName = Settings.Get(Of String)("CacheMsV2Name") Then
            Data.Output = New McLoginResult With {
                .Name = Input.UserName, .Type = "Microsoft",
                .AccessToken = Settings.Get(Of String)("CacheMsV2Access"),
                .Uuid = Settings.Get(Of String)("CacheMsV2Uuid"),
                .ClientToken = Settings.Get(Of String)("CacheMsV2Uuid"),
                .ProfileJson = Settings.Get(Of String)("CacheMsV2ProfileJson")
            }
            McLaunchLog("无需登录，AccessToken 尚未过期")
            GoTo SkipLogin
        End If
        '尝试登录
        Dim OAuthTokens As (OAuthAccessToken As String, OAuthRefreshToken As String)
        If Input.OAuthRefreshToken = "" Then
            '无 RefreshToken
Relogin:
            OAuthTokens = MsLoginStep1New(Data)
        Else
            '有 RefreshToken
            OAuthTokens = MsLoginStep1Refresh(Input.OAuthRefreshToken)
            If OAuthTokens.OAuthAccessToken = "Relogin" Then GoTo Relogin '要求重新打开登录网页认证
        End If
        If Data.IsCanceled Then Throw New OperationCanceledException
        Data.Progress = 0.25
        If Data.IsCanceled Then Throw New OperationCanceledException
        Dim OAuthAccessToken As String = OAuthTokens.OAuthAccessToken
        Dim OAuthRefreshToken As String = OAuthTokens.OAuthRefreshToken
        Dim XBLToken As String = MsLoginStep2(OAuthAccessToken)
        Data.Progress = 0.4
        If Data.IsCanceled Then Throw New OperationCanceledException
        Dim Tokens = MsLoginStep3(XBLToken)
        Data.Progress = 0.55
        If Data.IsCanceled Then Throw New OperationCanceledException
        Dim LoginResult = MsLoginStep4(Tokens)
        ExpiresAt = LoginResult.ExpiresAt
        Data.Progress = 0.7
        If Data.IsCanceled Then Throw New OperationCanceledException
        MsLoginStep5(LoginResult.AccessToken)
        Data.Progress = 0.85
        If Data.IsCanceled Then Throw New OperationCanceledException
        Dim Result = MsLoginStep6(LoginResult.AccessToken)
        Data.Progress = 0.98
        '输出登录结果
        Settings.Set("CacheMsV2OAuthRefresh", OAuthRefreshToken)
        Settings.Set("CacheMsV2Access", LoginResult.AccessToken)
        Settings.Set("CacheMsV2Uuid", Result.UUID)
        Settings.Set("CacheMsV2Name", Result.UserName)
        Settings.Set("CacheMsV2ProfileJson", Result.ProfileJson)
        Settings.Set("CacheMsV2Expires", LoginResult.ExpiresAt)
        Dim MsJson As JObject = Settings.Get(Of String)("LoginMsJson").DeserializeJson()
        MsJson.Remove(Input.UserName) '如果更改了玩家名……
        MsJson(Result.UserName) = OAuthRefreshToken
        Settings.Set("LoginMsJson", MsJson.ToString(Newtonsoft.Json.Formatting.None))
        Data.Output = New McLoginResult With {.AccessToken = LoginResult.AccessToken, .Name = Result.UserName, .Uuid = Result.UUID, .Type = "Microsoft", .ClientToken = Result.UUID, .ProfileJson = Result.ProfileJson}
        '结束
SkipLogin:
        McLaunchLog($"微软登录结束，AccessToken 将在 {DateTimeOffset.FromUnixTimeSeconds(ExpiresAt).LocalDateTime} 过期")
        Settings.Set("HintBuy", True) '关闭正版购买提示
    End Sub
    Private Sub McLoginServerStart(Data As LoaderTask(Of McLoginServer, McLoginResult))
        Dim Input As McLoginServer = Data.Input
        Dim NeedRefresh As Boolean = False, WasRefreshed As Boolean = False
        McLaunchLog("登录方式：" & Input.Description)
        Data.Progress = 0.05
        '尝试登录
        If (Not Data.Input.ForceReselectProfile) AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Username") = Data.Input.UserName AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Pass") = Data.Input.Password AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Access") <> "" AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Client") <> "" AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Uuid") <> "" AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Name") <> "" Then
            '尝试验证登录
            Try
                If Data.IsCanceled Then Throw New OperationCanceledException
                McLoginRequestValidate(Data)
                GoTo LoginFinish
            Catch ex As Exception
                Logger.Warn(ex, "验证登录失败")
                If ex.IsBadNetwork Then Throw New Exception("$登录失败：你的网络环境不佳，导致难以连接到海外服务器。" & vbCrLf & "请稍后再试，或使用加速器、VPN 改善网络环境。")
            End Try
            Data.Progress = 0.25
            '尝试刷新登录
Refresh:
            Try
                If Data.IsCanceled Then Throw New OperationCanceledException
                McLoginRequestRefresh(Data, NeedRefresh)
                GoTo LoginFinish
            Catch ex As Exception
                McLaunchLog("刷新登录失败：" & ex.GetDisplay(True))
                If WasRefreshed Then Throw New Exception("二轮刷新登录失败", ex)
            End Try
            Data.Progress = If(NeedRefresh, 0.85, 0.45)
        End If
        '尝试普通登录
        Try
            If Data.IsCanceled Then Throw New OperationCanceledException
            NeedRefresh = McLoginRequestLogin(Data)
        Catch ex As Exception
            McLaunchLog("登录失败：" & ex.GetDisplay(True))
            Throw
        End Try
        If NeedRefresh Then
            McLaunchLog("重新进行刷新登录")
            WasRefreshed = True
            Data.Progress = 0.65
            GoTo Refresh
        End If
LoginFinish:
        Data.Progress = 0.95
        '保存启动记录
        Dim Dict As New Dictionary(Of String, String)
        Dim Emails As New List(Of String)
        Dim Passwords As New List(Of String)
        Try
            If Not Settings.Get(Of String)("Login" & Input.Token & "Email") = "" Then Emails.AddRange(Settings.Get(Of String)("Login" & Input.Token & "Email").Split("¨"))
            If Not Settings.Get(Of String)("Login" & Input.Token & "Pass") = "" Then Passwords.AddRange(Settings.Get(Of String)("Login" & Input.Token & "Pass").Split("¨"))
            For i = 0 To Emails.Count - 1
                Dict.Add(Emails(i), Passwords(i))
            Next
            Dict.Remove(Input.UserName)
            Emails = New List(Of String)(Dict.Keys)
            Emails.Insert(0, Input.UserName)
            Passwords = New List(Of String)(Dict.Values)
            Passwords.Insert(0, Input.Password)
            Settings.Set("Login" & Input.Token & "Email", Emails.Join("¨"c))
            Settings.Set("Login" & Input.Token & "Pass", Passwords.Join("¨"c))
        Catch ex As Exception
            Logger.Error(ex, "保存启动记录失败", LogBehavior.Toast)
            Settings.Set("Login" & Input.Token & "Email", "")
            Settings.Set("Login" & Input.Token & "Pass", "")
        End Try
    End Sub
    Private Sub McLoginLegacyStart(Data As LoaderTask(Of McLoginLegacy, McLoginResult))
        Dim Input As McLoginLegacy = Data.Input
        McLaunchLog("登录方式：离线（" & Input.UserName & "）")
        Data.Progress = 0.1
        With Data.Output
            .Name = Input.UserName
            .Uuid = McLoginLegacyUuidWithCustomSkin(Input.UserName, Input.SkinType, Input.SkinName)
            .Type = "Legacy"
        End With
        '将结果扩展到所有项目中
        Data.Output.AccessToken = Data.Output.Uuid
        Data.Output.ClientToken = Data.Output.Uuid
        '保存启动记录
        Dim Names As New List(Of String)
        If Not Settings.Get(Of String)("LoginLegacyName") = "" Then Names.AddRange(Settings.Get(Of String)("LoginLegacyName").ToString.Split("¨"))
        Names.Remove(Input.UserName)
        Names.Insert(0, Input.UserName)
        Settings.Set("LoginLegacyName", Names.Join("¨"c))
    End Sub

    'Server 登录：三种验证方式的请求
    ''' <summary>VB 登录输入 → C# 请求核模型（PCLCS\Launch\LoginModels.cs 逐字段对拷）。</summary>
    Private Function TranslateMcLoginServer(Input As McLoginServer) As PCLCS.Launch.McLoginServer
        Return New PCLCS.Launch.McLoginServer(CType(Input.Type, PCLCS.Launch.McLoginType)) With {
            .UserName = Input.UserName, .Password = Input.Password, .BaseUrl = Input.BaseUrl,
            .Token = Input.Token, .Description = Input.Description, .ForceReselectProfile = Input.ForceReselectProfile}
    End Function
    Private Sub McLoginRequestValidate(ByRef Data As LoaderTask(Of McLoginServer, McLoginResult))
        '验证登录请求核已迁 C#（PCLCS.Launch.LoginCores.RequestValidate，旧 :710-733 逐行：缓存预读、请求体、Accept-Language、不改缓存）
        Dim Output As PCLCS.Launch.McLoginResult = PCLCS.Launch.LoginCores.RequestValidate(TranslateMcLoginServer(Data.Input), Sub(LogText) McLaunchLog(LogText))
        Data.Output.AccessToken = Output.AccessToken
        Data.Output.ClientToken = Output.ClientToken
        Data.Output.Uuid = Output.Uuid
        Data.Output.Name = Output.Name
        Data.Output.Type = Output.Type
    End Sub
    Private Sub McLoginRequestRefresh(ByRef Data As LoaderTask(Of McLoginServer, McLoginResult), RequestUser As Boolean)
        '刷新登录请求核已迁 C#（PCLCS.Launch.LoginCores.RequestRefresh，旧 :734-763 逐行：请求体、缓存回写）；
        'RequestUser 在旧实现中未被请求体消费（requestUser 恒为 True），故不透传
        Dim Output As PCLCS.Launch.McLoginResult = PCLCS.Launch.LoginCores.RequestRefresh(TranslateMcLoginServer(Data.Input), Sub(LogText) McLaunchLog(LogText))
        Data.Output.AccessToken = Output.AccessToken
        Data.Output.ClientToken = Output.ClientToken
        Data.Output.Uuid = Output.Uuid
        Data.Output.Name = Output.Name
        Data.Output.Type = Output.Type
    End Sub
    Private Function McLoginRequestLogin(ByRef Data As LoaderTask(Of McLoginServer, McLoginResult)) As Boolean
        '登录请求核已迁 C#（PCLCS.Launch.LoginCores.RequestLogin，旧 :764-883 逐行：请求体、三态角色收敛、缓存回写与错误分诊表逐字）；
        '角色选择弹窗（旧 MyMsgBoxSelect）、红字提示（旧 Hint）与退出登录（旧 PageLogin*Skin.ExitLogin）经回调回接 UI 面
        Dim Output As PCLCS.Launch.McLoginResult = Nothing
        Dim NeedRefresh As Boolean = PCLCS.Launch.LoginCores.RequestLogin(
            TranslateMcLoginServer(Data.Input),
            Function(Names As IReadOnlyList(Of String))
                Dim SelectedIndex As Integer = 0
                RunInUiWait(
                Sub()
                    Dim SelectionControl As New List(Of IMyRadio)
                    For Each Name As String In Names
                        SelectionControl.Add(New MyRadioBox With {.Text = Name})
                    Next
                    SelectedIndex = MyMsgBoxSelect(SelectionControl, "选择使用的角色")
                End Sub)
                Return SelectedIndex
            End Function,
            Output,
            Sub(LogText) McLaunchLog(LogText),
            Sub(HintText) Hint(HintText, HintType.Red),
            Sub(LoginType As PCLCS.Launch.McLoginType)
                Select Case CType(LoginType, McLoginType)
                    Case McLoginType.Auth
                        RunInUi(AddressOf PageLoginAuthSkin.ExitLogin)
                    Case McLoginType.Nide
                        RunInUi(AddressOf PageLoginNideSkin.ExitLogin)
                End Select
            End Sub)
        Data.Output.AccessToken = Output.AccessToken
        Data.Output.ClientToken = Output.ClientToken
        Data.Output.Uuid = Output.Uuid
        Data.Output.Name = Output.Name
        Data.Output.Type = Output.Type
        Return NeedRefresh
    End Function

    '微软登录步骤 1，原始登录：获取 DeviceCode 并开启登录网页
    Private Function MsLoginStep1New(Data As LoaderTask(Of McLoginMs, McLoginResult)) As (OAuthAccessToken As String, OAuthRefreshToken As String)
        '设备码流核已迁 C#（PCLCS.Launch.MsLoginSteps.BeginDeviceCodeFlow，旧 :777-802 逐行：
        'devicecode 获取、令牌 / RestartException / 异常三态收敛与 GoTo Retry 环；参考 learn.microsoft.com 的 v2-oauth2-device-code）；
        'WaitingMyMsgBox 轮询弹窗与密码登录引导弹窗留宿主，经轮询回调注入（Succeed/Retry/Fail 三态，弹窗异常由 C# 原样抛回）
        Dim Flow = PCLCS.Launch.MsLoginSteps.BeginDeviceCodeFlow(
            Function(PrepareJson As String)
                '弹窗（旧 :785-789）
                Dim Converter As New MyMsgBoxConverter With {.Content = PrepareJson.DeserializeJson(), .ForceWait = True, .Type = MyMsgBoxType.Login}
                WaitingMyMsgBox.Enqueue(Converter)
                While Converter.Result Is Nothing
                    Thread.Sleep(100)
                End While
                If TypeOf Converter.Result Is RestartException Then
                    '密码登录引导（旧 :790-797）：重新登录 → C# 重开设备码流；设置密码经 Button2Action 打开网页；取消 → 中止
                    If MyMsgBox($"请在登录时选择 {vbLQ}其他登录方法{vbRQ}，然后选择 {vbLQ}使用我的密码{vbRQ}。{vbCrLf}如果没有该选项，请选择 {vbLQ}设置密码{vbRQ}，设置完毕后再登录。",
                        "需要使用密码登录", "重新登录", "设置密码", "取消",
                        Button2Action:=Sub() OpenWebsite("https://account.live.com/password/Change")) = 1 Then
                        Return PCLCS.Launch.MsPollResult.Retry()
                    Else
                        Return PCLCS.Launch.MsPollResult.Fail(New OperationCanceledException)
                    End If
                ElseIf TypeOf Converter.Result Is Exception Then
                    Return PCLCS.Launch.MsPollResult.Fail(CType(Converter.Result, Exception))
                Else
                    Return PCLCS.Launch.MsPollResult.Succeed(Converter.Result(0), Converter.Result(1)) '旧 :801
                End If
            End Function,
            Sub(LogText) McLaunchLog(LogText))
        Return (Flow.OAuthAccessToken, Flow.OAuthRefreshToken)
    End Function
    '微软登录步骤 1，刷新登录：从 OAuth Code 或 OAuth RefreshToken 获取 {OAuth AccessToken, OAuth RefreshToken}
    Private Function MsLoginStep1Refresh(Code As String) As (OAuthAccessToken As String, OAuthRefreshToken As String)
        '刷新令牌核已迁 C#（PCLCS.Launch.MsLoginSteps.RefreshTokens，旧 :805-837 逐行：
        '请求体、首赢家双线程竞速与 Relogin/安全/封禁分诊表逐字）；
        'Relogin 信号原样占用 AccessToken（McLoginMsStart :576 判定不变），安全/封禁弹窗面留宿主（按决策枚举映射，弹后取消）
        Try
            Dim Tokens = PCLCS.Launch.MsLoginSteps.RefreshTokens(Code, Sub(LogText) McLaunchLog(LogText))
            Return (Tokens.AccessToken, Tokens.RefreshToken)
        Catch ex As PCLCS.Launch.MsLoginDecisionException
            '旧 :822-827 的两档警告弹窗（单按钮「我知道了」，文案随迁自 C# 异常）
            MyMsgBox(ex.Text, ex.Title, ex.Button1, IsWarn:=ex.IsWarn)
            Throw New OperationCanceledException
        End Try
    End Function
    '微软登录步骤 2：从 OAuth AccessToken 获取 XBLToken
    Private Function MsLoginStep2(AccessToken As String) As String
        'XBL 令牌核已迁 C#（PCLCS.Launch.MsLoginSteps.XblAuthenticate，旧 :839-853 逐行：RpsTicket 的 d= 前缀与请求体）
        Return PCLCS.Launch.MsLoginSteps.XblAuthenticate(AccessToken, Sub(LogText) McLaunchLog(LogText))
    End Function
    '微软登录步骤 3：从 XBLToken 获取 {XSTSToken, UHS}
    Private Function MsLoginStep3(XBLToken As String) As (XSTSToken As String, UHS As String)
        'XSTS 授权核已迁 C#（PCLCS.Launch.MsLoginSteps.XstsAuthorize，旧 :855-905 逐行：请求体与 214891623x 错误分诊表逐字）；
        'Whoa 错误码的弹窗面留宿主：按决策枚举映射 MyMsgBox（主文案经异常随迁），弹后取消语义与旧一致
        Try
            Dim Tokens = PCLCS.Launch.MsLoginSteps.XstsAuthorize(XBLToken, Sub(LogText) McLaunchLog(LogText))
            Return (Tokens.AccessToken, Tokens.Uhs)
        Catch ex As PCLCS.Launch.MsLoginDecisionException
            Select Case ex.Decision
                Case PCLCS.Launch.MsLoginDecision.XstsBanned
                    MyMsgBox(ex.Text, ex.Title, ex.Button1, IsWarn:=ex.IsWarn)
                Case PCLCS.Launch.MsLoginDecision.XstsNotRegistered
                    If MyMsgBox(ex.Text, ex.Title, ex.Button1, ex.Button2) = 1 Then
                        OpenWebsite("https://signup.live.com/signup")
                    End If
                Case PCLCS.Launch.MsLoginDecision.XstsRegionUnavailable
                    MyMsgBox(ex.Text, ex.Title, ex.Button1)
                Case PCLCS.Launch.MsLoginDecision.XstsAgeInsufficient
                    If MyMsgBox(ex.Text, ex.Title, ex.Button1, ex.Button2, ex.Button3) = 1 Then
                        OpenWebsite("https://account.live.com/editprof.aspx")
                        MyMsgBox("请在打开的网页中修改账号的出生日期（至少改为 18 岁以上）。" & vbCrLf &
                                 "在修改成功后等待一分钟，然后再回到 PCL，就可以正常登录了！", "登录提示")
                    Else
                        OpenWebsite("https://support.microsoft.com/zh-cn/account-billing/如何更改-microsoft-帐户上的出生日期-837badbc-999e-54d2-2617-d19206b9540a")
                        MyMsgBox("请根据打开的网页的说明，修改账号的出生日期（至少改为 18 岁以上）。" & vbCrLf &
                                 "在修改成功后等待一分钟，然后再回到 PCL，就可以正常登录了！", "登录提示")
                    End If
            End Select
            Throw New OperationCanceledException
        End Try
    End Function
    '微软登录步骤 4：从 {XSTSToken, UHS} 获取 {Minecraft AccessToken, 过期于 (Unix 时间戳)}
    Private Function MsLoginStep4(Tokens As (XSTSToken As String, UHS As String)) As (AccessToken As String, ExpiresAt As Integer)
        'MTS 令牌核已迁 C#（PCLCS.Launch.MsLoginSteps.MinecraftToken，旧 :907-937 逐行：
        'identityToken、429/403/503 红字与提前 1 小时过期）；
        'ACCOUNT_SUSPENDED（#8655）弹窗面留宿主（决策枚举映射，弹后取消）
        Try
            Dim Result = PCLCS.Launch.MsLoginSteps.MinecraftToken(Tokens.XSTSToken, Tokens.UHS, Sub(LogText) McLaunchLog(LogText))
            Return (Result.AccessToken, Result.ExpiresAt)
        Catch ex As PCLCS.Launch.MsLoginDecisionException
            MyMsgBox(ex.Text, ex.Title, ex.Button1, IsWarn:=ex.IsWarn)
            Throw New OperationCanceledException
        End Try
    End Function
    '微软登录步骤 5：验证微软账号是否持有 MC，这也会刷新 XGP
    Private Sub MsLoginStep5(AccessToken As String)
        'entitlements 核已迁 C#（PCLCS.Launch.MsLoginSteps.EntitlementsOk，旧 :939-957 逐行：
        'Bearer 头、双线程与 items 判定、响应异常 Warn 后原样再抛）；
        '未持有弹窗（购买 Minecraft/取消 → 商店网址）留宿主（决策枚举映射，弹后取消）
        Try
            PCLCS.Launch.MsLoginSteps.EntitlementsOk(AccessToken, Sub(LogText) McLaunchLog(LogText))
        Catch ex As PCLCS.Launch.MsLoginDecisionException
            If MyMsgBox(ex.Text, ex.Title, ex.Button1, ex.Button2) = 1 Then
                OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj")
            End If
            Throw New OperationCanceledException
        End Try
    End Sub
    '微软登录步骤 6：从 Minecraft AccessToken 获取 {UUID, UserName, ProfileJson}
    Private Function MsLoginStep6(AccessToken As String) As (UUID As String, UserName As String, ProfileJson As String)
        'profile 核已迁 C#（PCLCS.Launch.MsLoginSteps.Profile，旧 :959-989 逐行：429/404 分诊与档案解析）；
        '404 的非阻塞建档弹窗（旧 :973-979 RunInNewThread）留宿主（决策枚举映射，弹后取消）
        Try
            Dim Profile = PCLCS.Launch.MsLoginSteps.Profile(AccessToken, Sub(LogText) McLaunchLog(LogText))
            Return (Profile.Uuid, Profile.UserName, Profile.ProfileJson)
        Catch ex As PCLCS.Launch.MsLoginDecisionException
            Dim Text = ex.Text, Title = ex.Title, Button1 = ex.Button1, Button2 = ex.Button2
            RunInNewThread(
            Sub()
                If MyMsgBox(Text, Title, Button1, Button2) = 1 Then
                    OpenWebsite("https://www.minecraft.net/zh-hans/msaprofile/mygames/editprofile")
                End If
            End Sub, "Login Failed: Create Profile")
            Throw New OperationCanceledException
        End Try
    End Function

    '返回符合离线皮肤设置的 UUID
    Private Function McLoginLegacyUuidWithCustomSkin(UserName As String, SkinType As Integer, SkinName As String) As String
        '皮肤调整决策核已迁 C#（PCLCS.Launch.LoginCores.LegacyUuidWithCustomSkin，旧 :1105-1142 逐行：
        'Steve/Alex/正版/自定义四态与 #3746 的 1.20- 限制；皮肤性别经 W1 SkinProfile.GetModel，正版查询经 MojangUuid 转发）
        Return PCLCS.Launch.LoginCores.LegacyUuidWithCustomSkin(UserName, SkinType, SkinName)
    End Function
    '根据用户名返回对应 UUID，需要多线程
    Public Function McLoginMojangUuid(Name As String, ThrowOnNotFound As Boolean)
        '正版 UUID 查询与 ini 缓存核已迁 C#（PCLCS.Launch.LoginCores.MojangUuid，旧 :1144-1166 逐行；
        '缓存目录经 LoginCores.MojangUuidCacheFolder 接线于 ModNet）
        Return PCLCS.Launch.LoginCores.MojangUuid(Name, ThrowOnNotFound)
    End Function
    Public Function McLoginLegacyUuid(Name As String)
        '离线 UUID 算法已迁 C#（PCLCS.Launch.LoginCores.LegacyUuid，旧 :1167-1170 逐字纯函数；PageLaunchLeft 皮肤预览等调用方保持原签名）
        Return PCLCS.Launch.LoginCores.LegacyUuid(Name)
    End Function

#End Region

#Region "Java 处理"

    Public McLaunchJavaSelected As Java = Nothing
    Private Sub McLaunchJava(Task As LoaderTask(Of Integer, Integer))
        McLaunchJavaSelected = SelectOrDownloadJava(McInstanceSelected, True, Task.CreateCancellationToken, Task.CreateSyncProgressProvider)
        If Task.IsCanceled Then Return
        If McLaunchJavaSelected Is Nothing Then Throw New OperationCanceledException 'SelectOrDownloadJava 已经会给予适当的提示
        If ModeDebug Then
            McLaunchLog($"预期 Java 版本范围：{GetJavaRequirement(McInstanceSelected).Range}")
            McLaunchLog($"Java 版本设置类别：{Settings.Get(Of Integer)("VersionArgumentJavaV2", McInstanceSelected)}")
        End If
        McLaunchLog("选择的 Java：" & McLaunchJavaSelected.ToString)
    End Sub

#End Region

#Region "启动参数"
    '原 :1158-1159 模块状态已迁 C#（PCLCS.Launch.LaunchArguments.LaunchArgument/LaunchEncoding），转发保持读写面
    Private Property McLaunchArgument As String
        Get
            Return PCLCS.Launch.LaunchArguments.LaunchArgument
        End Get
        Set(value As String)
            PCLCS.Launch.LaunchArguments.LaunchArgument = value
        End Set
    End Property
    Private Property McLaunchEncoding As Encoding
        Get
            Return PCLCS.Launch.LaunchArguments.LaunchEncoding
        End Get
        Set(value As Encoding)
            PCLCS.Launch.LaunchArguments.LaunchEncoding = value
        End Set
    End Property

    '主方法（:1162-1189 决策核已迁 C# LaunchArguments.BuildArguments，此处按宿主读取面构造 inputs 转发）
    Private Sub McLaunchArgumentMain(Loader As LoaderTask(Of String, Integer))
        PCLCS.Launch.LaunchArguments.BuildArguments(New PCLCS.Launch.LaunchArgumentInputs With
            {.Jvm = McLaunchJvmInputs(),
             .Game = McLaunchGameInputs(),
             .Token = McLaunchTokenInputs(),
             .VersionJsonWriter = Sub(Content) FileUtils.Write(McInstanceSelected.GetJsonPath(), Content),
             .RunInUiWait = AddressOf McLaunchRunInUiWait,
             .Log = Sub(Text) McLaunchLog(Text)})
    End Sub

    'RunInUiWait（ModBase.vb:924）的 C# Func(Of Action, Boolean) 适配
    Private Function McLaunchRunInUiWait(Job As Action) As Boolean
        RunInUiWait(Job)
        Return True
    End Function

    '登录输出快照（C# 不引用宿主层 Loader，由宿主从 McLoginLoader.Output 取值构造，快照语义）
    Private Function McLaunchLoginOutput() As PCLCS.Launch.McLoginOutput
        Return New PCLCS.Launch.McLoginOutput With
            {.Type = McLoginLoader.Output.Type,
             .Name = McLoginLoader.Output.Name,
             .Uuid = McLoginLoader.Output.Uuid,
             .AccessToken = McLoginLoader.Output.AccessToken}
    End Function

    '继承链父版本解析器（旧 :1204/:1335 的 Function(name) (New McInstance(name)).JsonObject… 等价面）：
    'ResolveVersionFolder（McInstance.New :471 的路径解析）→ InstanceCore(Folder, McFolderSelected, Folder, RunInUi)
    '（McInstance.New :472，不自动 Load）→ .VersionJson 惰性读盘 + HMCL/inherit 合并（旧 .JsonObject 快照的源，
    'ModMinecraft.vb:397-399 即 _core.VersionJson 转 JObject）
    Private Function McLaunchResolveParent(Name As String) As System.Text.Json.Nodes.JsonObject
        Dim Folder = Instances.InstancePaths.ResolveVersionFolder(Name, McFolderSelected)
        Return New Instances.InstanceCore(Folder, McFolderSelected, Folder, AddressOf RunInUi).VersionJson
    End Function

    'JVM 段的宿主读取面（原 :1192-1323 直接读取的模块状态收敛于此）
    Private Function McLaunchJvmInputs() As PCLCS.Launch.JvmInputs
        Return New PCLCS.Launch.JvmInputs With
            {.Instance = McInstanceSelected.Core,
             .Environment = New PCLCS.Minecraft.Launch.RuleEnvironment(My.Computer.Info.OSVersion),
             .ResolveParent = AddressOf McLaunchResolveParent,
             .LaunchJava = McLaunchJavaSelected,
             .IsGbkEncoding = IsGBKEncoding,
             .PathPure = PathPure.Value,
             .LwjglUnsafeAgentJar = Function() LaunchUtils.ExtractPatch("Launch\LwjglUnsafeAgent.jar", PathPure.Value),
             .JavaWrapperJar = Function() LaunchUtils.ExtractPatch("Launch\JavaWrapper.jar", PathPure.Value),
             .ArgumentReplace = Function(Text As String) ArgumentReplace(Text),
             .RamGb = PageInstanceSetup.GetRam(McInstanceSelected),
             .AvailablePhysicalMemory = My.Computer.Info.AvailablePhysicalMemory,
             .Debug = ModeDebug,
             .WindowsVersion = Environment.OSVersion.Version,
             .Login = McLaunchLoginOutput(),
             .Log = Sub(Text) McLaunchLog(Text),
             .WarnLog = Sub(Text) McLaunchLog(Text, LogLevel.Warn)}
    End Function

    '游戏段的宿主读取面（原 :1325-1398 直接读取的模块状态收敛于此）
    Private Function McLaunchGameInputs() As PCLCS.Launch.GameInputs
        Return New PCLCS.Launch.GameInputs With
            {.Instance = McInstanceSelected.Core,
             .Environment = New PCLCS.Minecraft.Launch.RuleEnvironment(My.Computer.Info.OSVersion),
             .ResolveParent = AddressOf McLaunchResolveParent,
             .Version = McInstanceSelected.Version,
             .ArgumentReplace = Function(Text As String) ArgumentReplace(Text),
             .ExtraGameArgs = CurrentLaunchOptions.ExtraGameArgs,
             .ServerIp = CurrentLaunchOptions.ServerIp,
             .Hint = Sub(Text) Hint(Text, HintType.Red),
             .Log = Sub(Text) McLaunchLog(Text)}
    End Function

    '替换标记段的宿主读取面（原 :1400-1473 直接读取的模块状态收敛于此）
    Private Function McLaunchTokenInputs() As PCLCS.Launch.TokenInputs
        Return New PCLCS.Launch.TokenInputs With
            {.Instance = McInstanceSelected.Core,
             .PathPure = PathPure.Value,
             .NativesFolder = GetNativesFolder(),
             .AssetsIndexName = McAssetsGetIndexName(McInstanceSelected),
             .ArgumentReplace = Function(Text As String) ArgumentReplace(Text),
             .Login = McLaunchLoginOutput(),
             .Version = McInstanceSelected.Version,
             .LaunchJava = McLaunchJavaSelected,
             .Dpi = DPI,
             .MainWindowPixelSize = Function() (GetPixelSize(FrmMain.PanForm.ActualWidth), GetPixelSize(FrmMain.PanForm.ActualHeight)),
             .Log = Sub(Text) McLaunchLog(Text)}
    End Function

    'JVM 部分（:1192-1323 决策核已迁 C# LaunchArguments.BuildJvmArguments，保留宿主转发面）
    Private Function McLaunchArgumentsJVM() As List(Of String)
        Return PCLCS.Launch.LaunchArguments.BuildJvmArguments(McLaunchJvmInputs())
    End Function
    '游戏参数部分（:1325-1398 决策核已迁 C# LaunchArguments.BuildGameArguments；裁定 10：写盘经回调绑定 FileUtils.Write）
    Private Function McLaunchArgumentsGame() As List(Of String)
        Return PCLCS.Launch.LaunchArguments.BuildGameArguments(
            McLaunchGameInputs(),
            Sub(Content) FileUtils.Write(McInstanceSelected.GetJsonPath(), Content))
    End Function
    '获取替换标记（:1400-1473 迭代核已迁 C# LaunchArguments.BuildReplacementTable，--versionType 移除作用于传入的活列表）
    Private Function McLaunchArgumentsReplace(Arguments As List(Of String)) As IEnumerable(Of (Key As String, Value As String))
        Return PCLCS.Launch.LaunchArguments.BuildReplacementTable(Arguments, McLaunchTokenInputs(), AddressOf McLaunchRunInUiWait)
    End Function

    ''' <summary>
    ''' 分割 Java 参数字符串。
    ''' </summary>
    Private Function SplitJavaArguments(Arg As String) As IEnumerable(Of String)
        Return PCLCS.Minecraft.Launch.ArgumentLine.Split(Arg)
    End Function
    ''' <summary>
    ''' 对 Java 参数进行去重。
    ''' </summary>
    Private Function DeduplicateJavaArguments(Args As List(Of String), IsJVMArgs As Boolean) As List(Of String)
        Dim Diagnostics As New List(Of String)
        Dim Result = PCLCS.Minecraft.Launch.ArgumentLine.Deduplicate(Args, IsJVMArgs, Diagnostics)
        For Each Line In Diagnostics
            McLaunchLog(Line)
        Next
        Return Result.ToList()
    End Function

#End Region

#Region "解压 Natives"

    Private Sub McLaunchNatives(Loader As LoaderTask(Of Integer, Integer))
        '解压核已迁 C#（PCLCS.Launch.LaunchFiles.ExtractNatives，:1332-1393 逐行）；此处按宿主读取面转发
        PCLCS.Launch.LaunchFiles.ExtractNatives(
            GetNativesFolder(),
            McLibListGet(McInstanceSelected, True).Where(Function(Entry) Entry.IsNatives).Select(Function(Entry) Entry.LocalPath).ToList(),
            progress:=Nothing,
            log:=Sub(Text) McLaunchLog(Text),
            debug:=ModeDebug)
    End Sub
    ''' <summary>
    ''' 获取 Natives 文件夹路径，以 \ 结尾。
    ''' </summary>
    Public Function GetNativesFolder(Optional Instance As McInstance = Nothing) As String
        '回退链核已迁 C#（PCLCS.Launch.LaunchFiles.GetNativesFolder，:1397-1408 逐字）；此处转发保持宿主读写面
        If Instance Is Nothing Then Instance = McInstanceSelected
        Return PCLCS.Launch.LaunchFiles.GetNativesFolder(Instance.PathVersion, Instance.Name, IsGBKEncoding, OsDrive)
    End Function

#End Region

#Region "启动与前后处理"

    Private Sub McLaunchPrerun()

        '使用高性能显卡
        If Settings.Get(Of Boolean)("LaunchAdvanceGraphicCard") Then
            Try
                SetGPUPreference(McLaunchJavaSelected.JavaExePath)
                SetGPUPreference(PathExe)
            Catch ex As Exception
                If WindowsUtils.HasAdminRole() Then
                    Logger.Warn(ex, "直接调整显卡设置失败")
                Else
                    Logger.Warn(ex, "直接调整显卡设置失败，将以管理员权限重启 PCL 再次尝试")
                    Try
                        If RunAsAdmin($"--gpu ""{McLaunchJavaSelected.JavaExePath}""") = ProcessReturnValues.TaskDone Then
                            McLaunchLog("以管理员权限重启 PCL 并调整显卡设置成功")
                        Else
                            Throw New Exception("调整过程中出现异常")
                        End If
                    Catch exx As Exception
                        Logger.Error(exx, "调整显卡设置失败，Minecraft 可能会使用默认显卡运行", LogBehavior.Toast)
                    End Try
                End If
            End Try
        End If

        '更新 launcher_profiles.json
        '合并核已迁 C#（PCLCS.Launch.LaunchFiles.MergeLauncherProfiles，原 :1439-1502 决策面，失败容忍语义 1:1）；
        'Microsoft 守卫与 McFolderLauncherProfilesJsonCreate 依赖留宿主层
        If McLoginLoader.Output.Type = "Microsoft" Then
            PCLCS.Launch.LaunchFiles.MergeLauncherProfiles(
                McFolderSelected, McLoginLoader.Output.Name, McLoginLoader.Output.ClientToken,
                AddressOf McFolderLauncherProfilesJsonCreate, Sub(Text) McLaunchLog(Text))
        End If

        '更新 options.txt
        Dim SetupFileAddress As String = McInstanceSelected.PathIndie & "options.txt"
        If Not FileUtils.Exists(SetupFileAddress) Then
            'Yosbr Mod 兼容（#2385）：https://www.curseforge.com/minecraft/mc-mods/yosbr
            Dim YosbrFileAddress As String = McInstanceSelected.PathIndie & "config\yosbr\options.txt"
            If FileUtils.Exists(YosbrFileAddress) Then
                McLaunchLog("将修改 Yosbr Mod 中的 options.txt")
                SetupFileAddress = YosbrFileAddress
                WriteIni(SetupFileAddress, "lang", "none") '忽略默认语言
            End If
        End If
        Try
            '语言
            '1.0-     ：没有语言选项
            '1.1 ~ 5  ：zh_CN 时正常，zh_cn 时崩溃（最后两位字母必须大写，否则将会 NPE 崩溃）
            '1.6 ~ 10 ：zh_CN 时正常，zh_cn 时自动切换为英文
            '1.11 ~ 12：zh_cn 时正常，zh_CN 时虽然显示了中文但语言设置会错误地显示选择英文
            '1.13+    ：zh_cn 时正常，zh_CN 时自动切换为英文
            Dim CurrentLang As String = ReadIni(SetupFileAddress, "lang", "none")
            Dim RequiredLang As String = If(CurrentLang = "none" OrElse Not DirectoryUtils.Exists(McInstanceSelected.PathIndie & "saves"), '#3844，整合包可能已经自带了 options.txt
                If(Settings.Get(Of Boolean)("ToolHelpChinese"), "zh_cn", "en_us"), CurrentLang.Lower)
            If Not McInstanceSelected.Version.Valid OrElse McInstanceSelected.Version.Vanilla.Major <= 10 Then
                '将最后两位改为大写，前面的部分保留
                RequiredLang = RequiredLang.Substring(0, RequiredLang.Length - 2) & RequiredLang.Substring(RequiredLang.Length - 2).Upper
            End If
            If CurrentLang = RequiredLang Then
                McLaunchLog($"需要的语言为 {RequiredLang}，当前语言为 {CurrentLang}，无需修改")
            Else
                WriteIni(SetupFileAddress, "lang", "-") '触发缓存更改，避免删除后重新下载残留缓存
                WriteIni(SetupFileAddress, "lang", RequiredLang)
                McLaunchLog($"已将语言从 {CurrentLang} 修改为 {RequiredLang}")
            End If
            ''如果是初次设置，一并修改 forceUnicodeFont
            'If Settings.Get(Of Boolean)("ToolHelpChinese") AndAlso (CurrentLang = "none" OrElse Not DirectoryUtils.Exists(McInstanceSelected.PathIndie & "saves")) Then
            '    WriteIni(SetupFileAddress, "forceUnicodeFont", "true")
            '    McLaunchLog("已开启 forceUnicodeFont")
            'End If
            '窗口
            Select Case Settings.Get(Of Integer)("LaunchArgumentWindowType")
                Case 0 '全屏
                    WriteIni(SetupFileAddress, "fullscreen", "true")
                Case 1 '默认
                Case Else '其他
                    WriteIni(SetupFileAddress, "fullscreen", "false")
            End Select
        Catch ex As Exception
            Logger.Error(ex, "更新 options.txt 失败", LogBehavior.Toast)
        End Try

        '离线皮肤 Alex 警告
        If McInstanceSelected.Version.Vanilla.Major <= 7 AndAlso McInstanceSelected.Version.Vanilla.Major >= 2 AndAlso '1.2 ~ 1.7
               McLoginLoader.Input.Type = McLoginType.Legacy AndAlso '离线登录
               (Settings.Get(Of Integer)("LaunchSkinType") = 2 OrElse '强制 Alex
               (Settings.Get(Of Integer)("LaunchSkinType") = 4 AndAlso Settings.Get(Of Boolean)("LaunchSkinSlim"))) Then '或选用 Alex 皮肤
            Hint("此 Minecraft 版本尚不支持 Alex 皮肤，你的皮肤可能会显示为 Steve！", HintType.Red)
        End If

        '离线皮肤资源包
        Try
            Dim ZipFileAddress As String = McInstanceSelected.PathIndie & "resourcepacks\PCL2 Skin.zip"
            Dim NewTypeSetup As Boolean = McInstanceSelected.Version.Vanilla.Major >= 13 OrElse McInstanceSelected.Version.Vanilla.Major < 6
            If McLoginLoader.Input.Type = McLoginType.Legacy AndAlso Settings.Get(Of Integer)("LaunchSkinType") = 4 AndAlso FileUtils.Exists(Paths.AppDataThenName & "CustomSkin.png") Then
                Dim MetaFileAddress As String = PathTemp & "pack.mcmeta"
                Dim PackPicAddress As String = PathTemp & "pack.png"
                If McInstanceSelected.Version.Vanilla.Major < 6 Then
                    McLaunchLog("Minecraft 版本过老，尚不支持自定义离线皮肤")
                    GoTo IgnoreCustomSkin
                End If
                Dim PackVersionJToken As JToken = McInstanceSelected.JsonVersion?("pack_version")
                If TypeOf PackVersionJToken Is JObject Then PackVersionJToken = DirectCast(PackVersionJToken, JObject)("resource") '从 1.17 开始，可以从 pack_version.resource 获取资源包版本
                Dim PackVersion As Integer = Val(PackVersionJToken?.ToString)
                If PackVersion <= 0 Then '没有对应字段的老版本 MC，或兜底
                    Select Case McInstanceSelected.Version.Vanilla.Major
                        Case 6, 7, 8
                            PackVersion = 1
                        Case 9, 10
                            PackVersion = 2
                        Case 11, 12
                            PackVersion = 3
                        Case 13, 14
                            PackVersion = 4
                        Case 15
                            PackVersion = 5
                        Case 16
                            PackVersion = If(McInstanceSelected.Version.Vanilla.Build <= 1, 5, 6)
                        Case 17
                            PackVersion = 7
                        Case 18
                            PackVersion = 8
                        Case Else '旧版快照可能没有可用的资源包格式声明
                            PackVersion = 17
                    End Select
                End If
                McLaunchLog($"正在构建自定义皮肤资源包，版本号 {PackVersion}，MC 版本 {McInstanceSelected.Version.Vanilla}")
                '准备文件
                Dim Bit As New MyBitmap(PathImage & "Heads/Logo.png")
                Bit.Save(PackPicAddress)
                FileUtils.Write(MetaFileAddress, "{""pack"":{""pack_format"":" & PackVersion & ",""description"":""PCL 自定义离线皮肤资源包""}}")
                Dim Skin As New MyBitmap(Paths.AppDataThenName & "CustomSkin.png")
                If (McInstanceSelected.Version.Vanilla.Major = 6 OrElse McInstanceSelected.Version.Vanilla.Major = 7) AndAlso Skin.Pic.Height = 64 Then
                    McLaunchLog("该 Minecraft 版本不支持双层皮肤，已进行裁剪")
                    Skin = Skin.Clip(0, 0, 64, 32)
                End If
                Skin.Save(Paths.BaseThenName & "CustomSkin_Cliped.png")
                '构建压缩文件
                Dim FilesToCompress As New Dictionary(Of String, String) From {
                    {"pack.mcmeta", MetaFileAddress}, {"pack.png", PackPicAddress}}
                If McInstanceSelected.Version.Vanilla >= New Version(19, 0, 3) Then
                    '1.19.3+ 使用复杂版本的替换
                    For Each SkinName In {"alex", "ari", "efe", "kai", "makena", "noor", "steve", "sunny", "zuri"}
                        FilesToCompress.Add(
                                $"assets/minecraft/textures/entity/player/{If(Settings.Get(Of Boolean)("LaunchSkinSlim"), "slim", "wide")}/{SkinName}.png",
                                Paths.BaseThenName & "CustomSkin_Cliped.png")
                    Next
                Else
                    FilesToCompress.Add(
                            $"assets/minecraft/textures/entity/{If(Settings.Get(Of Boolean)("LaunchSkinSlim"), "alex.png", "steve.png")}",
                            Paths.BaseThenName & "CustomSkin_Cliped.png")
                End If
                FileUtils.CreateZipFromFiles(ZipFileAddress, FilesToCompress)
                FileUtils.Delete(Paths.BaseThenName & "CustomSkin_Cliped.png")
                '更改设置文件
                IniClearCache(SetupFileAddress)
                Dim EnabledResourcePack As String = ReadIni(SetupFileAddress, "resourcePacks", "[]").TrimStart("[").TrimEnd("]")
                If NewTypeSetup Then
                    If EnabledResourcePack = "" Then EnabledResourcePack = """vanilla"""
                    Dim EnabledResourcePacks As New List(Of String)(EnabledResourcePack.Split(","))
                    Dim NewResourcePacks As New List(Of String)
                    For Each Res In EnabledResourcePacks
                        If Res <> """file/PCL2 Skin.zip""" AndAlso Res <> "" Then NewResourcePacks.Add(Res)
                    Next
                    NewResourcePacks.Add("""file/PCL2 Skin.zip""")
                    Dim Result As String = "[" & NewResourcePacks.Join(","c) & "]"
                    WriteIni(SetupFileAddress, "resourcePacks", Result)
                Else
                    Dim EnabledResourcePacks As New List(Of String)(EnabledResourcePack.Split(","))
                    Dim NewResourcePacks As New List(Of String)
                    For Each Res In EnabledResourcePacks
                        If Res <> """PCL2 Skin.zip""" AndAlso Res <> "" Then NewResourcePacks.Add(Res)
                    Next
                    NewResourcePacks.Add("""PCL2 Skin.zip""")
                    Dim Result As String = "[" & NewResourcePacks.Join(","c) & "]"
                    WriteIni(SetupFileAddress, "resourcePacks", Result)
                End If
IgnoreCustomSkin:
            ElseIf FileUtils.Exists(ZipFileAddress) Then
                McLaunchLog("正在清空自定义皮肤资源包")
                '删除压缩文件
                FileUtils.Delete(ZipFileAddress)
                '更改设置文件
                IniClearCache(SetupFileAddress)
                Dim EnabledResourcePack As String = ReadIni(SetupFileAddress, "resourcePacks", "[]").TrimStart("[").TrimEnd("]")
                If NewTypeSetup Then
                    If EnabledResourcePack = "" Then EnabledResourcePack = """vanilla"""
                    Dim EnabledResourcePacks As New List(Of String)(EnabledResourcePack.Split(","))
                    EnabledResourcePacks.Remove("""file/PCL2 Skin.zip""")
                    Dim Result As String = "[" & EnabledResourcePacks.Join(","c) & "]"
                    WriteIni(SetupFileAddress, "resourcePacks", Result)
                Else
                    Dim EnabledResourcePacks As New List(Of String)(EnabledResourcePack.Split(","))
                    EnabledResourcePacks.Remove("""PCL2 Skin.zip""")
                    Dim Result As String = "[" & EnabledResourcePacks.Join(","c) & "]"
                    WriteIni(SetupFileAddress, "resourcePacks", Result)
                End If
            End If
        Catch ex As Exception
            Logger.Error(ex, "离线皮肤资源包设置失败", LogBehavior.Toast)
        End Try

    End Sub
    Private Sub McLaunchCustom(Loader As LoaderTask(Of Integer, Integer))
        '决策核已迁 C#（PCLCS.Launch.ProcessRunner.RunCustomCommands，原 :1674-1763 决策面）；此处按宿主读取面转发
        PCLCS.Launch.ProcessRunner.RunCustomCommands(New PCLCS.Launch.CustomInputs With
            {.InstanceName = McInstanceSelected.Name,
             .InstanceIndiePath = McInstanceSelected.PathIndie,
             .McFolderSelected = McFolderSelected,
             .JavaExePath = McLaunchJavaSelected.JavaExePath,
             .Arguments = McLaunchArgument,
             .Encoding = McLaunchEncoding,
             .IsGbkEncoding = IsGBKEncoding,
             .SaveBatch = CurrentLaunchOptions?.SaveBatch,
             .InstanceSettingsKey = McInstanceSelected.Core.InstanceSettingsKey,
             .ArgumentReplace = Function(Text As String) ArgumentReplace(Text),
             .FilterAccessToken = Function(Text As String, FilterChar As Char) FilterAccessToken(Text, FilterChar)},
            New PCLCS.Launch.ProcessCallbacks With
            {.IsCanceled = Function() Loader.IsCanceled,
             .Log = Sub(Text) McLaunchLog(Text),
             .SaveBatchHint = Sub() CanceledHint = "导出启动脚本成功！",
             .OpenExplorer = Sub(Path As String) OpenExplorer(Path),
             .CancelLaunch = Sub() Loader.Parent.Cancel()})
    End Sub
    Private Sub McLaunchRun(Loader As LoaderTask(Of Integer, Process))
        '决策核已迁 C#（PCLCS.Launch.ProcessRunner.RunGameProcess，原 :1764-1812 决策面）；此处按宿主读取面转发
        Dim GameProcess = PCLCS.Launch.ProcessRunner.RunGameProcess(New PCLCS.Launch.GameProcessInputs With
            {.JavaExePath = McLaunchJavaSelected.JavaExePath,
             .JavaFolder = McLaunchJavaSelected.Folder,
             .McFolderSelected = McFolderSelected,
             .WorkingDirectory = McInstanceSelected.PathIndie,
             .Arguments = McLaunchArgument,
             .Encoding = McLaunchEncoding},
            New PCLCS.Launch.ProcessCallbacks With
            {.IsCanceled = Function() Loader.IsCanceled,
             .Log = Sub(Text) McLaunchLog(Text)})
        '原 :1795-1796：取消路径（返回 Nothing）时不赋 Loader.Output / McLaunchProcess
        If GameProcess IsNot Nothing Then
            Loader.Output = GameProcess
            McLaunchProcess = GameProcess
        End If
    End Sub
    Private Sub McLaunchWait(Loader As LoaderTask(Of Process, Integer))

        '输出信息
        McLaunchLog("")
        McLaunchLog("~ 基础参数 ~")
        McLaunchLog("PCLA 版本：" & VersionDisplay & " (" & ProtocolVersion & ")")
        McLaunchLog($"游戏版本：{McInstanceSelected.VersionDisplayName}（{McInstanceSelected.Version.Vanilla}，Drop {McInstanceSelected.Version.Drop}{If(McInstanceSelected.Version.Reliable, "", "，无法完全确定")}）")
        McLaunchLog("资源版本：" & McAssetsGetIndexName(McInstanceSelected))
        McLaunchLog("版本继承：" & If(McInstanceSelected.InheritName = "", "无", McInstanceSelected.InheritName))
        McLaunchLog("分配的内存：" & PageInstanceSetup.GetRam(McInstanceSelected) & " GB（" & Math.Round(PageInstanceSetup.GetRam(McInstanceSelected) * 1024) & " MB）")
        McLaunchLog("MC 文件夹：" & McFolderSelected)
        McLaunchLog("版本文件夹：" & McInstanceSelected.PathVersion)
        McLaunchLog("版本隔离：" & (McInstanceSelected.PathIndie = McInstanceSelected.PathVersion))
        McLaunchLog("HMCL 格式：" & McInstanceSelected.IsHmclFormatJson)
        McLaunchLog("Java 信息：" & If(McLaunchJavaSelected IsNot Nothing, McLaunchJavaSelected.ToString, "无可用 Java"))
        McLaunchLog("Natives 文件夹：" & GetNativesFolder())
        McLaunchLog("")
        McLaunchLog("~ 登录参数 ~")
        McLaunchLog("玩家用户名：" & McLoginLoader.Output.Name)
        McLaunchLog("AccessToken：" & McLoginLoader.Output.AccessToken)
        McLaunchLog("ClientToken：" & McLoginLoader.Output.ClientToken)
        McLaunchLog("UUID：" & McLoginLoader.Output.Uuid)
        McLaunchLog("登录方式：" & McLoginLoader.Output.Type)
        McLaunchLog("")

        '获取窗口标题
        Dim WindowTitle As String = Settings.Get(Of String)("VersionArgumentTitle", Instance:=McInstanceSelected)
        If WindowTitle = "" Then WindowTitle = Settings.Get(Of String)("LaunchArgumentTitle")
        WindowTitle = ArgumentReplace(WindowTitle, ReplaceTime:=False)

        '初始化等待
        Dim Watcher As New Watcher(Loader, McInstanceSelected, WindowTitle)
        McLaunchWatcher = Watcher

        '等待
        Do While Watcher.State = Watcher.MinecraftState.Loading
            Thread.Sleep(100)
        Loop
        If Watcher.State = Watcher.MinecraftState.Crashed Then
            Throw New OperationCanceledException
        End If

    End Sub
    Private Sub McLaunchEnd()
        McLaunchLog("开始启动结束处理")

        '暂停或开始音乐播放
        If Settings.Get(Of Boolean)("UiMusicStop") Then
            RunInUi(Sub() If MusicPause() Then Logger.Info("已根据设置，在启动后暂停音乐播放"))
        ElseIf Settings.Get(Of Boolean)("UiMusicStart") Then
            RunInUi(Sub() If MusicResume() Then Logger.Info("已根据设置，在启动后开始音乐播放"))
        End If

        '启动器可见性
        McLaunchLog("启动器可见性：" & Settings.Get(Of Integer)("LaunchArgumentVisible"))
        Select Case Settings.Get(Of Integer)("LaunchArgumentVisible")
            Case 0
                '直接关闭
                McLaunchLog("已根据设置，在启动后关闭启动器")
                RunInUi(Sub() FrmMain.EndProgram(False))
            Case 2, 3
                '隐藏
                McLaunchLog("已根据设置，在启动后隐藏启动器")
                RunInUi(Sub() FrmMain.Hidden = True)
            Case 4
                '最小化
                McLaunchLog("已根据设置，在启动后最小化启动器")
                RunInUi(Sub() FrmMain.WindowState = WindowState.Minimized)
            Case 5
                '啥都不干
        End Select

        '启动计数
        Settings.Set("SystemLaunchCount", Settings.Get(Of Integer)("SystemLaunchCount") + 1)

    End Sub

#End Region

End Module
