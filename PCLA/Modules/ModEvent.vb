

#Region "附加属性"

Imports PCL.FormMain
Imports PCLCS.Common

''' <summary>
''' 用于在 XAML 中初始化列表对象。
''' 附加属性无法在 XAML 中为每个对象初始化独立的列表对象，因此需要一个包装类，然后在 XAML 中显式初始化。
''' </summary>
<Markup.ContentProperty("Events")>
Public Class CustomEventCollection
    Implements IEnumerable(Of CustomEvent)
    Dim _Events As New List(Of CustomEvent)
    Public ReadOnly Property Events As List(Of CustomEvent)
        Get
            Return _Events
        End Get
    End Property
    Public Function GetEnumerator() As IEnumerator(Of CustomEvent) Implements IEnumerable(Of CustomEvent).GetEnumerator
        Return DirectCast(Events, IEnumerable(Of CustomEvent)).GetEnumerator()
    End Function
    Private Function IEnumerable_GetEnumerator() As IEnumerator Implements IEnumerable.GetEnumerator
        Return DirectCast(Events, IEnumerable).GetEnumerator()
    End Function
End Class

''' <summary>
''' 提供自定义事件的附加属性。
''' </summary>
Public Class CustomEventService

    'Events
    Public Shared ReadOnly EventsProperty As DependencyProperty =
            DependencyProperty.RegisterAttached("Events", GetType(CustomEventCollection), GetType(CustomEventService), New PropertyMetadata(Nothing))
    <AttachedPropertyBrowsableForType(GetType(DependencyObject))>
    Public Shared Sub SetEvents(d As DependencyObject, value As CustomEventCollection)
        d.SetValue(EventsProperty, value)
    End Sub
    <AttachedPropertyBrowsableForType(GetType(DependencyObject))>
    Public Shared Function GetEvents(d As DependencyObject) As CustomEventCollection
        If d.GetValue(EventsProperty) Is Nothing Then d.SetValue(EventsProperty, New CustomEventCollection)
        Return d.GetValue(EventsProperty)
    End Function

    'EventType
    Public Shared ReadOnly EventTypeProperty As DependencyProperty =
            DependencyProperty.RegisterAttached("EventType", GetType(CustomEvent.EventType), GetType(CustomEventService), New PropertyMetadata(Nothing))
    <AttachedPropertyBrowsableForType(GetType(DependencyObject))>
    Public Shared Sub SetEventType(d As DependencyObject, value As CustomEvent.EventType)
        d.SetValue(EventTypeProperty, value)
    End Sub
    <AttachedPropertyBrowsableForType(GetType(DependencyObject))>
    Public Shared Function GetEventType(d As DependencyObject) As CustomEvent.EventType
        Return d.GetValue(EventTypeProperty)
    End Function

    'EventData
    Public Shared ReadOnly EventDataProperty As DependencyProperty =
            DependencyProperty.RegisterAttached("EventData", GetType(String), GetType(CustomEventService), New PropertyMetadata(Nothing))
    <AttachedPropertyBrowsableForType(GetType(DependencyObject))>
    Public Shared Sub SetEventData(d As DependencyObject, value As String)
        d.SetValue(EventDataProperty, value)
    End Sub
    <AttachedPropertyBrowsableForType(GetType(DependencyObject))>
    Public Shared Function GetEventData(d As DependencyObject) As String
        Return d.GetValue(EventDataProperty)
    End Function

End Class

Partial Public Module ModMain

    ''' <summary>
    ''' 触发该控件上的自定义事件。
    ''' 事件会在新线程中执行。
    ''' </summary>
    <Runtime.CompilerServices.Extension>
    Public Sub RaiseCustomEvent(Control As DependencyObject)
        '收集事件列表
        Dim Events = CustomEventService.GetEvents(Control).ToList
        Dim EventType = CustomEventService.GetEventType(Control)
        If EventType <> CustomEvent.EventType.None Then Events.Add(New CustomEvent(EventType, CustomEventService.GetEventData(Control)))
        '执行事件
        If Not Events.Any Then Return
        RunInNewThread(
        Sub()
            For Each e In Events
                e.Raise()
            Next
        End Sub, "执行自定义事件 " & GetUuid())
    End Sub

End Module

#End Region

''' <summary>
''' 自定义事件。
''' </summary>
Public Class CustomEvent
    Inherits DependencyObject

#Region "属性与触发"

    Public Property Type As EventType
        Get
            Dim Value As EventType = EventType.None
            RunInUiWait(Sub() Value = GetValue(TypeProperty))
            Return Value
        End Get
        Set(value As EventType)
            SetValue(TypeProperty, value)
        End Set
    End Property
    Public Shared ReadOnly TypeProperty As DependencyProperty =
        DependencyProperty.Register("Type", GetType(EventType), GetType(CustomEvent), New PropertyMetadata(EventType.None))

    Public Property Data As String
        Get
            Dim Value As String = Nothing
            RunInUiWait(Sub() Value = GetValue(DataProperty))
            Return Value
        End Get
        Set(value As String)
            SetValue(DataProperty, value)
        End Set
    End Property
    Public Shared ReadOnly DataProperty As DependencyProperty =
        DependencyProperty.Register("Data", GetType(String), GetType(CustomEvent), New PropertyMetadata(Nothing))

    Public Sub New()
    End Sub
    Public Sub New(Type As EventType, Data As String)
        Me.Type = Type
        Me.Data = Data
    End Sub

    ''' <summary>
    ''' 在当前线程中触发该自定义事件。
    ''' </summary>
    Public Sub Raise()
        Raise(Type, Data)
    End Sub

#End Region

    Public Enum EventType
        None = 0
        打开网页
        打开文件
        打开帮助
        执行命令
        启动游戏
        复制文本
        刷新主页
        刷新页面
        刷新帮助
        今日人品
        内存优化
        清理垃圾
        弹出窗口
        弹出提示
        切换页面
        导入整合包
        安装整合包
        下载文件
        修改设置
        写入设置
        修改变量
        写入变量
        加入房间
        检查更新
    End Enum

    ''' <summary>
    ''' 在当前线程中触发一个自定义事件。
    ''' </summary>
    Public Shared Sub Raise(Type As EventType, Arg As String)
        If Type = EventType.None Then Return
        Logger.Info($"执行自定义事件：{Type}, {Arg}")
        If Arg Is Nothing Then Arg = ""
        Dim Args As String() = Arg.Split("|")
        Dim Arg0 As String = Arg.BeforeFirst("|")
        Dim Arg1 As String = Arg.AfterFirst("|")
        Try
            Select Case Type
                Case EventType.打开网页
                    Arg = Arg.Replace("\", "/")
                    If Not Arg.Contains("://") OrElse Arg.StartsWithF("file", True) Then '为了支持更多协议（#2200）
                        MyMsgBox("EventData 必须为一个网址。" & vbCrLf & "如果想要启动程序，请将 EventType 改为 打开文件。", "事件执行失败")
                        Return
                    End If
                    Hint("正在开启，请稍候：" & Arg)
                    RunInThread(Sub() OpenWebsite(Arg))

                Case EventType.打开文件, EventType.打开帮助, EventType.执行命令
                    '打开类 IO 核已迁 C#（EventActions.OpenFileOrExecute，旧 :197-220 RunInThread 体内逐行）：
                    '确认路径、StartProcess、异常吞咽在核内；帮助页入口与安全确认弹窗经回调留宿主层
                    Dim Kind = ToOpenKind(Type)
                    RunInThread(
                    Sub()
                        EventActions.OpenFileOrExecute(Arg, Kind,
                            AddressOf PageOtherHelp.EnterHelpPage, AddressOf EventSafetyConfirm,
                            Function(Url, LocalPath)
                                NetDownloadByClient(Url, LocalPath)
                                Return True
                            End Function, AddressOf HelpTryExtract, AddressOf RunInUi,
                            PathTemp, Paths.BaseThenName, OsDrive)
                    End Sub)

                Case EventType.启动游戏
                    If Arg0 = "\current" Then
                        If McInstanceSelected Is Nothing Then
                            Hint("请先选择一个 Minecraft 版本！", HintType.Red)
                            Return
                        Else
                            Arg0 = McInstanceSelected.Name
                        End If
                    End If
                    RunInUi(
                    Sub()
                        If McLaunchStart(New McLaunchOptions With
                                {.ServerIp = If(Args.Length >= 2, Args(1), Nothing), .Instance = New McInstance(Arg0)}) Then
                            Hint($"正在启动 {Arg0}……")
                        End If
                    End Sub)

                Case EventType.复制文本
                    ClipboardSet(Arg)

                Case EventType.刷新主页
                    RunInUi(
                    Sub()
                        FrmLaunchRight.ForceRefresh()
                        If String.IsNullOrEmpty(Arg) Then Hint("已刷新！", HintType.Green)
                    End Sub)

                Case EventType.刷新页面
                    If TypeOf FrmMain.PageRight Is IRefreshable Then
                        RunInUiWait(Sub() CType(FrmMain.PageRight, IRefreshable).Refresh())
                        If String.IsNullOrEmpty(Arg) Then Hint("已刷新！", HintType.Green)
                    Else
                        Hint("当前页面不支持刷新操作！", HintType.Red)
                    End If

                Case EventType.刷新帮助
                    RunInUiWait(Sub() PageOtherLeft.RefreshHelp())
                    If String.IsNullOrEmpty(Arg) Then Hint("已刷新！", HintType.Green)

                Case EventType.今日人品
                    PageOtherTest.Jrrp()

                Case EventType.内存优化
                    RunInThread(Sub() PageOtherTest.MemoryOptimize(True))

                Case EventType.清理垃圾
                    RunInThread(Sub() PageOtherTest.RubbishClear())

                Case EventType.弹出窗口
                    If Args.Length = 1 Then Throw New Exception($"EventType {Type} 需要至少 2 个以 | 分割的参数，例如 弹窗标题|弹窗内容")
                    MyMsgBox(Arg1.BeforeLast("|").Replace("\n", vbCrLf), Arg0.Replace("\n", vbCrLf), If(Args.Length > 2, Arg1.AfterLast("|"), "确定"))

                Case EventType.弹出提示
                    Hint(Arg0.Replace("\n", vbCrLf), If(Args.Length = 1, HintType.Blue, EnumUtils.FromString(Of HintType)(Arg1)))

                Case EventType.切换页面
                    RunInUi(
                    Sub()
                        Dim TargetPage = EnumUtils.FromString(Of PageType)(Arg0)
                        If TargetPage = PageType.InstanceSetup Then
                            If McInstanceSelected Is Nothing Then
                                Hint("请先选择一个 Minecraft 版本！", HintType.Red)
                                Return
                            End If
                            If McInstanceLock.IsLocked(McInstanceSelected) Then
                                Hint("该版本正在被修改，暂时无法进入版本设置！", HintType.Red)
                                Return
                            End If
                            PageInstanceLeft.Instance = McInstanceSelected
                        End If
                        FrmMain.PageChange(TargetPage,
                            If(Args.Length = 1, PageSubType.Default, EnumUtils.FromString(Of PageSubType)(Arg1)))
                    End Sub)

                Case EventType.导入整合包, EventType.安装整合包
                    RunInUi(Sub() ModpackInstall())

                Case EventType.下载文件
                    Arg0 = Arg0.Replace("\", "/")
                    If Not (Arg0.StartsWithF("http://", True) OrElse Arg0.StartsWithF("https://", True)) Then
                        MyMsgBox("EventData 必须为以 http:// 或 https:// 开头的网址。" & vbCrLf & "PCL 不支持其他乱七八糟的下载协议。", "事件执行失败")
                        Return
                    End If
                    If Not EventSafetyConfirm("即将从该网址下载文件：" & vbCrLf & Arg0) Then Return
                    RunInUi(
                    Sub()
                        Try
                            Select Case Args.Length
                                Case 1
                                    PageOtherTest.StartCustomDownload(Arg0, PathUtils.GetLastPart(Arg0))
                                Case 2
                                    PageOtherTest.StartCustomDownload(Arg0, Arg1)
                                Case Else
                                    PageOtherTest.StartCustomDownload(Arg0, Args(1), Args(2))
                            End Select
                        Catch
                            PageOtherTest.StartCustomDownload(Arg0, "未知")
                        End Try
                    End Sub)

                Case EventType.修改设置, EventType.写入设置
                    If Args.Length = 1 Then Throw New Exception($"EventType {Type} 需要至少 2 个以 | 分割的参数，例如 UiLauncherTransparent|400")
                    Settings.SetSafe(Arg0, Args(1), Instance:=McInstanceSelected)
                    If Args.Length = 2 Then Hint($"已写入设置：{Arg0} → {Args(1)}", HintType.Green)

                Case EventType.修改变量, EventType.写入变量
                    If Args.Length = 1 Then Throw New Exception($"EventType {Type} 需要至少 2 个以 | 分割的参数，例如 VariableName|SomeValue")
                    '注册表写入核已迁 C#（EventActions.WriteVariable，旧 :329-334 的 RegistryUtils 面逐字；失败告警在核内），
                    '成功提示的判长与文案留宿主层（Args.Length = 2 条件传入回调）
                    EventActions.WriteVariable(Arg0, Args(1),
                        If(Args.Length = 2, Sub() Hint($"已写入变量：{Arg0} → {Args(1)}", HintType.Green), Nothing))

                Case EventType.加入房间
                    RunInUi(
                    Sub()
                        If String.IsNullOrWhiteSpace(Arg) Then
                            PageLinkMain.Join()
                        Else
                            If Not EventSafetyConfirm("即将加入第三方联机房间。") Then Return
                            PageLinkMain.Join(Arg)
                        End If
                    End Sub)

                Case EventType.检查更新
                    UpdateCheckByButton()

                Case Else
                    MyMsgBox("未知的事件类型：" & Type & vbCrLf & "请检查事件类型填写是否正确，或者 PCL 是否为最新版本。", "事件执行失败")
            End Select
        Catch ex As Exception
            Logger.Error(ex, $"事件执行失败（{Type}, {Arg}）", LogBehavior.Alert)
        End Try
    End Sub

    ''' <summary>
    ''' 返回自定义事件的绝对 Url。实际返回 {绝对 Url, WorkingDir}。
    ''' 失败会抛出异常。
    ''' 决策核已迁 C#（EventActions.GetAbsoluteUrls，旧 :362-428 逐行：json/xaml 下载 ×2、TaskTemp 申请、
    ''' HelpTryExtract 判定与六分支路径解析）；下载客户端（NetDownloadByClient）、HelpTryExtract 与 RunInUi 经回调接线，
    ''' PathTemp/Paths.BaseThenName/OsDrive 模块状态经参数注入（GetNativesFolder 同模式）。
    ''' </summary>
    Public Shared Function GetAbsoluteUrls(RelativeUrl As String, Type As EventType) As String()
        Return EventActions.GetAbsoluteUrls(RelativeUrl, ToOpenKind(Type),
            Function(Url, LocalPath)
                NetDownloadByClient(Url, LocalPath)
                Return True
            End Function,
            AddressOf HelpTryExtract, AddressOf RunInUi, Paths.BaseThenName, PathTemp, OsDrive).ToArray()
    End Function

    ''' <summary>
    ''' 旧 CustomEvent.EventType → C# EventOpenKind 的按名映射（C# 核仅读取 打开文件/打开帮助/执行命令 三判定，见 EventOpenKind）。
    ''' </summary>
    Private Shared Function ToOpenKind(Type As EventType) As EventOpenKind
        Select Case Type
            Case EventType.打开文件 : Return EventOpenKind.打开文件
            Case EventType.打开帮助 : Return EventOpenKind.打开帮助
            Case EventType.执行命令 : Return EventOpenKind.执行命令
            Case Else : Return EventOpenKind.None
        End Select
    End Function

    ''' <summary>
    ''' 弹出安全确认弹窗。返回是否继续执行。
    ''' </summary>
    Private Shared Function EventSafetyConfirm(Message As String) As Boolean
        If Settings.Get(Of Boolean)("HintCustomCommand") Then Return True
        Select Case MyMsgBox(Message & vbCrLf & "请在确认没有安全隐患后再继续。", "执行确认", "继续", "继续且今后不再要求确认", "取消")
            Case 1
                Return True
            Case 2
                Settings.Set("HintCustomCommand", True)
                Return True
            Case Else
                Return False
        End Select
    End Function

End Class