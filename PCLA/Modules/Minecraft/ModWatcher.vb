Public Module ModWatcher

    Sub New()
        '看门狗核已迁 C#（PCLCS.Watch.Watcher，旧 :58-392 逐行：构造收敛、状态机、日志缓冲与阶段进度、
        '崩溃关键字表、退出码判定、窗口枚举与最大化、Kill）。宿主层副作用在此接线（ModNet.Sub New 同模式）：
        '状态汇总翻转通知（→ MinecraftStart/Stop）、启动日志（McLaunchLog，脱敏留 VB）、崩溃提示（文案留 VB）与反馈上报
        PCLCS.Watch.Watcher.StateChanged = AddressOf OnWatcherStateChanged
        PCLCS.Watch.Watcher.LaunchLog = AddressOf McLaunchLog
        PCLCS.Watch.Watcher.CrashHint = Sub() Hint("检测到 Minecraft 出现错误，错误分析已开始……", HintType.Red)
        PCLCS.Watch.Watcher.Feedback = AddressOf FeedbackInfo
    End Sub

    '对全体的监视（全局态已迁 C#：PCLCS.Watch.Watcher.McWatcherList/HasRunningMinecraft，裁定 5；以下为薄转发，公共名保留）

    Public ReadOnly Property McWatcherList As List(Of PCLCS.Watch.Watcher)
        Get
            Return PCLCS.Watch.Watcher.McWatcherList
        End Get
    End Property
    Public Property HasRunningMinecraft As Boolean
        Get
            Return PCLCS.Watch.Watcher.HasRunningMinecraft
        End Get
        Set(value As Boolean)
            PCLCS.Watch.Watcher.HasRunningMinecraft = value
        End Set
    End Property

    ''' <summary>
    ''' 状态汇总翻转通知（原 WatcherStateChanged :21-24 的两分支；汇总计算与翻转守卫已迁 C#）。
    ''' </summary>
    Private Sub OnWatcherStateChanged(IsRunning As Boolean, TriggerLauncherShutdown As Boolean)
        If IsRunning Then
            MinecraftStart()
        Else
            MinecraftStop(TriggerLauncherShutdown)
        End If
    End Sub
    Private Sub MinecraftStart()
        McLaunchLog("[全局] 出现运行中的 Minecraft")
        HasRunningMinecraft = True
        FrmMain.BtnExtraShutdown.ShowRefresh()
    End Sub
    Private Sub MinecraftStop(TriggerLauncherShutdown As Boolean)
        McLaunchLog("[全局] 已无运行中的 Minecraft")
        HasRunningMinecraft = False
        FrmMain.BtnExtraShutdown.ShowRefresh()
        '音乐播放
        If Settings.Get(Of Boolean)("UiMusicStop") Then
            RunInUi(Sub() If MusicResume() Then Logger.Info("已根据设置，在结束后开始音乐播放"))
        ElseIf Settings.Get(Of Boolean)("UiMusicStart") Then
            RunInUi(Sub() If MusicPause() Then Logger.Info("已根据设置，在结束后暂停音乐播放"))
        End If
        '启动器可见性
        Select Case Settings.Get(Of Integer)("LaunchArgumentVisible")
            Case 2
                '直接关闭
                If TriggerLauncherShutdown Then
                    RunInUi(Sub() FrmMain.EndProgram(False))
                Else
                    RunInUi(Sub() FrmMain.Hidden = False)
                End If
            Case 3
                '恢复
                RunInUi(Sub() FrmMain.Hidden = False)
        End Select
    End Sub

    ''' <summary>
    ''' 看门狗宿主面实现（旧构造参数 Loader/Instance 的读取收敛；旧 :66-70/:96/:171/:253）：
    ''' 加载器承载游戏进程与进度/取消，实例承载版本发布时间。
    ''' </summary>
    Friend Class WatcherHost
        Implements PCLCS.Watch.IWatcherHost

        Private ReadOnly Loader As LoaderTask(Of Process, Integer)
        Private ReadOnly Instance As McInstance

        Public Sub New(Loader As LoaderTask(Of Process, Integer), Instance As McInstance)
            Me.Loader = Loader
            Me.Instance = Instance
        End Sub

        Public ReadOnly Property GameProcess As Process Implements PCLCS.Watch.IWatcherHost.GameProcess
            Get
                Return Loader.Input
            End Get
        End Property
        Public Property Progress As Double Implements PCLCS.Watch.IWatcherHost.Progress
            Get
                Return Loader.Progress
            End Get
            Set(value As Double)
                Loader.Progress = value
            End Set
        End Property
        Public ReadOnly Property IsCanceled As Boolean Implements PCLCS.Watch.IWatcherHost.IsCanceled
            Get
                Return Loader.State = LoadState.Canceled
            End Get
        End Property
        Public ReadOnly Property ReleaseTime As Date Implements PCLCS.Watch.IWatcherHost.ReleaseTime
            Get
                Return Instance.ReleaseTime
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 崩溃分析委托目标（原 Watcher.Crashed :363-377 的分析段；CrashAnalyzer 为 W4 域留 VB）。
    ''' 2 秒延迟与「崩溃分析开始」日志在 C# 核内，此处仅编排分析器。
    ''' </summary>
    Friend Sub RunCrashAnalyzer(Watched As PCLCS.Watch.Watcher, Instance As McInstance)
        Dim Analyzer As New CrashAnalyzer(Instance)
        Analyzer.Collect(Instance.PathIndie, Watched.LatestLog.ToList)
        Analyzer.Prepare()
        Analyzer.Analyze()
        Analyzer.Output(False, New List(Of String) From
            {Instance.PathVersion & Instance.Name & ".json", Paths.BaseThenName & "Log1.txt", Paths.BaseThenName & "LatestLaunch.bat"})
    End Sub

End Module
