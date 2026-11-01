Imports PCLCS.Net
'类型别名：本模块的 NetManager 属性（转发 C# 单例）在表达式上下文会遮蔽同名 C# 类型，
'故对 C# 类型的静态访问与类型标注统一走别名，避免限定符冗余与属性自引用
Imports DownloadManager = PCLCS.Net.NetManager
Imports System.Net.Http.Headers

Public Module ModNet

    Sub New()
        '将 VB 侧的签名实现接线到 C# 网络核心；签名逻辑留在 ModSecret（与 W1 SettingsService.IdentifyProvider 同模式）
        NetRequest.CdnSigner = AddressOf SecretCdnSign
        NetRequest.HeadersSigner = AddressOf HeadersSignAdapter
        '下载引擎（NetFile）对宿主层全局状态的接线：全局 Uuid、缓存目录与退出标志（管理器计数器已随 NetManager 迁入 C#，引擎直连入账）
        NetFile.UuidProvider = AddressOf GetUuid
        NetFile.PathTempProvider = Function() PathTemp
        NetFile.IsProgramEndingProvider = Function() IsProgramEnding
        '下载管理器（NetManager）的宿主层接线：缓存清理目录与守护线程异常上报（AlertThenCrash 路径）
        DownloadManager.TempFolderProvider = Function() PathTemp
        DownloadManager.CrashAlert = Sub(ex) Logger.Error(ex, Nothing, LogBehavior.AlertThenCrash)
    End Sub

    ''' <summary>
    ''' SecretHeadersSign 的 HttpRequestHeaders 适配：借用临时请求承载签名结果，再回拷到 C# 核心的请求头。
    ''' </summary>
    Private Sub HeadersSignAdapter(Headers As HttpRequestHeaders, Url As String, SimulateBrowserHeaders As Boolean)
        Using Req As New HttpRequestMessage()
            SecretHeadersSign(Url, Req, SimulateBrowserHeaders)
            For Each Header In Req.Headers
                Headers.TryAddWithoutValidation(Header.Key, Header.Value)
            Next
        End Using
    End Sub

#Region "网络请求"

    ''' <summary>
    ''' 发送一次网络请求并获取返回内容。
    ''' </summary>
    Public Function NetRequestByClient(Url As String, Optional Method As HttpMethod = Nothing,
            Optional Content As Object = Nothing, Optional ContentType As String = Nothing, Optional Accept As String = "*/*",
            Optional Timeout As Integer = 25000, Optional Headers As String(,) = Nothing, Optional RequireJson As Boolean = False,
            Optional Encoding As Encoding = Nothing, Optional SimulateBrowserHeaders As Boolean = False, Optional MakeLog As Boolean = True) As String
        Return NetRequest.NetRequestByClient(Url, New NetRequestOptions With {
            .Method = Method, .Content = Content, .ContentType = ContentType, .Accept = Accept, .TimeoutMs = Timeout,
            .Headers = If(Headers, {}).ToDictionary(), .RequireJson = RequireJson, .Encoding = Encoding,
            .SimulateBrowserHeaders = SimulateBrowserHeaders, .MakeLog = MakeLog, .RunInUi = AddressOf RunInUi})
    End Function
    ''' <summary>
    ''' 发送网络请求并获取返回内容。
    ''' 会进行至多 45 秒 3 次的尝试，允许最长 30s 的超时。
    ''' </summary>
    ''' <param name="BackupUrl">如果第一次尝试失败，换用的备用 URL。</param>
    Public Function NetRequestByClientRetry(Url As String, Optional Method As HttpMethod = Nothing, Optional BackupUrl As String = Nothing,
            Optional Content As Object = Nothing, Optional ContentType As String = Nothing, Optional Accept As String = "*/*",
            Optional Headers As String(,) = Nothing, Optional RequireJson As Boolean = False,
            Optional Encoding As Encoding = Nothing, Optional SimulateBrowserHeaders As Boolean = False) As String
        Return NetRequest.NetRequestByClientRetry(Url, New NetRequestOptions With {
            .Method = Method, .Content = Content, .ContentType = ContentType, .Accept = Accept,
            .Headers = If(Headers, {}).ToDictionary(), .RequireJson = RequireJson, .Encoding = Encoding,
            .SimulateBrowserHeaders = SimulateBrowserHeaders, .RunInUi = AddressOf RunInUi})
    End Function
    ''' <summary>
    ''' 生成数个线程，同时发送网络请求并获取返回内容。
    ''' 每个线程开始之前会略有延迟。
    ''' </summary>
    Public Function NetRequestByClientMultiple(Url As String, Optional Method As HttpMethod = Nothing, Optional ThreadCount As Integer = 3,
            Optional Content As Object = Nothing, Optional ContentType As String = Nothing, Optional Accept As String = "*/*",
            Optional Headers As String(,) = Nothing, Optional RequireJson As Boolean = False, Optional Timeout As Integer = 25000,
            Optional Encoding As Encoding = Nothing, Optional SimulateBrowserHeaders As Boolean = False) As String
        Return NetRequest.NetRequestByClientMultiple(Url, ThreadCount, New NetRequestOptions With {
            .Method = Method, .Content = Content, .ContentType = ContentType, .Accept = Accept, .TimeoutMs = Timeout,
            .Headers = If(Headers, {}).ToDictionary(), .RequireJson = RequireJson, .Encoding = Encoding,
            .SimulateBrowserHeaders = SimulateBrowserHeaders, .RunInUi = AddressOf RunInUi})
    End Function

    ''' <summary>
    ''' 以多线程下载网页文件的方式获取内容。
    ''' 不支持缓存协商；若需缓存协商，需要换用 NetRequestByClient。
    ''' </summary>
    Public Function NetRequestByLoader(Url As String, Optional Timeout As Integer = 45000, Optional IsJson As Boolean = False, Optional SimulateBrowserHeaders As Boolean = False) As String
        Return NetRequestByLoader({Url}, Timeout, IsJson, SimulateBrowserHeaders)
    End Function
    ''' <summary>
    ''' 以多线程下载网页文件的方式获取内容。
    ''' 不支持缓存协商；若需缓存协商，需要换用 NetRequestByClient。
    ''' </summary>
    Public Function NetRequestByLoader(Urls As IEnumerable(Of String), Optional Timeout As Integer = 45000, Optional IsJson As Boolean = False, Optional SimulateBrowserHeaders As Boolean = False) As String
        Dim Temp As String = RequestTaskTempFolder() & "download.txt"
        Dim NewTask As New LoaderDownload("源码获取 " & GetUuid() & "#", New List(Of NetFile) From {New NetFile(Urls, Temp, New FileChecker With {.IsJson = IsJson}, SimulateBrowserHeaders)})
        Try
            NewTask.WaitForExitTime(Timeout, TimeoutMessage:="连接服务器超时（第一下载源：" & Urls.First & "）")
            Dim Result = FileUtils.ReadAsString(Temp)
            FileUtils.Delete(Temp)
            Return Result
        Finally
            NewTask.Cancel()
        End Try
    End Function

#End Region

#Region "文件下载"

    ''' <summary>
    ''' 通过网络请求直接下载小文件，若文件已存在将被覆盖。
    ''' 不建议用于下载大文件。
    ''' </summary>
    Public Sub NetDownloadByClient(Url As String, LocalPath As String, Optional SimulateBrowserHeaders As Boolean = False)
        NetRequest.NetDownloadByClient(Url, LocalPath, New NetRequestOptions With {
            .TimeoutMs = 25000, '旧 SendRequest 默认 Timeout=25000，显式对齐以免落入选项默认 15000
            .SimulateBrowserHeaders = SimulateBrowserHeaders, .RunInUi = AddressOf RunInUi})
    End Sub

    ''' <summary>
    ''' 通过多线程下载引擎下载文件。
    ''' </summary>
    Public Sub NetDownloadByLoader(Url As String, LocalPath As String, Optional LoaderToSyncProgress As LoaderBase = Nothing, Optional Check As FileChecker = Nothing, Optional SimulateBrowserHeaders As Boolean = False)
        NetDownloadByLoader({Url}, LocalPath, LoaderToSyncProgress, Check, SimulateBrowserHeaders)
    End Sub
    ''' <summary>
    ''' 通过多线程下载引擎下载文件。
    ''' </summary>
    ''' <param name="Urls">文件的 Url 列表。</param>
    ''' <param name="LocalPath">下载的本地地址。</param>
    Public Sub NetDownloadByLoader(Urls As IEnumerable(Of String), LocalPath As String, Optional LoaderToSyncProgress As LoaderBase = Nothing, Optional Check As FileChecker = Nothing, Optional SimulateBrowserHeaders As Boolean = False)
        Dim NewTask As New LoaderDownload($"文件下载 {GetUuid()}#", New List(Of NetFile) From {New NetFile(Urls, LocalPath, Check, SimulateBrowserHeaders)})
        Try
            NewTask.WaitForExit(LoaderToSyncProgress:=LoaderToSyncProgress)
        Catch ex As Exception
            Throw New WebException($"多线程直接下载文件失败（第一下载源：" & Urls.First() & "）", ex)
        Finally
            NewTask.Cancel()
        End Try
    End Sub

#End Region

#Region "多线程下载引擎"

    '下载引擎全局状态（原 ModNet.vb:311-351）已迁入 C# NetSpeed，下载管理器（原 NetManagerClass，:1635-1837）已迁入 C# NetManager；
    '以下转发成员保持既有 VB 调用点（PageSpeedLeft、PageLaunchLeft、ModAnimation 等）不变

    ''' <summary>
    ''' 最大线程数。
    ''' </summary>
    Public ReadOnly Property NetTaskThreadLimit As Integer
        Get
            Return NetSpeed.ThreadLimit
        End Get
    End Property
    ''' <summary>
    ''' 速度下限。
    ''' </summary>
    Public Property NetTaskSpeedLimitLow As Long
        Get
            Return NetSpeed.SpeedLimitLow
        End Get
        Set(value As Long)
            NetSpeed.SpeedLimitLow = value
        End Set
    End Property
    ''' <summary>
    ''' 速度上限。若无限制则为 -1。
    ''' </summary>
    Public ReadOnly Property NetTaskSpeedLimitHigh As Long
        Get
            Return NetSpeed.SpeedLimitHigh
        End Get
    End Property
    ''' <summary>
    ''' 基于限速，当前可以下载的剩余量。
    ''' </summary>
    Public ReadOnly Property NetTaskSpeedLimitLeft As Long
        Get
            Return NetSpeed.SpeedLimitLeft
        End Get
    End Property
    ''' <summary>
    ''' 正在运行中的线程数。
    ''' </summary>
    Public ReadOnly Property NetTaskThreadCount As Integer
        Get
            Return NetSpeed.ThreadCount
        End Get
    End Property

    Public Sub UpdateNetTaskSpeedLimitHigh(Value As Integer)
        NetSpeed.UpdateNetTaskSpeedLimitHigh(Value)
    End Sub

    ''' <summary>
    ''' 下载一系列文件的加载器。
    ''' </summary>
    Public Class LoaderDownload
        Inherits LoaderBase
        Implements INetTaskHost '下载引擎（NetFile）留守 VB 的宿主回调面（C# 引擎经此通知完成/失败并同步失败计数）

#Region "属性"

        ''' <summary>
        ''' 需要下载的文件。
        ''' </summary>
        Public Files As ConcurrentQueue(Of NetFile)
        ''' <summary>
        ''' 剩余未完成的文件数。（用于减轻 FilesLock 的占用）
        ''' </summary>
        Private FileRemain As Integer
        Private ReadOnly FileRemainLock As New Object

        ''' <summary>
        ''' 用于显示的百分比进度。
        ''' </summary>
        Public Overrides Property Progress As Double
            Get
                If State >= LoadState.Finished Then Return 1
                If Not Files.Any() Then Return 0 '必须返回 0，否则在获取列表的时候会错觉已经下载完了
                Return _Progress
            End Get
            Set(value As Double)
                Throw New Exception("文件下载不允许指定进度")
            End Set
        End Property
        Private _Progress As Double = 0

        ''' <summary>
        ''' 任务中的文件的连续失败计数。
        ''' </summary>
        Public Property FailCount As Integer Implements INetTaskHost.FailCount
            Get
                Return _FailCount
            End Get
            Set(value As Integer)
                _FailCount = value
                If State = LoadState.Loading AndAlso value >= Math.Min(10000, Math.Max(FileRemain * 8, NetTaskThreadLimit * 8 + 3)) Then
                    Logger.Error($"由于同加载器中失败次数过多引发强制失败：连续失败了 {value} 次", LogBehavior.ToastIfDebug)
                    On Error Resume Next
                    Dim ExList As New List(Of Exception)
                    For Each File In Files
                        For Each Source In File.Sources
                            If Source.Ex IsNot Nothing Then
                                ExList.Add(Source.Ex)
                                If ExList.Count > 10 Then GoTo FinishExCatch
                            End If
                        Next
                    Next
FinishExCatch:
                    OnFail(ExList)
                End If
            End Set
        End Property
        Private _FailCount As Integer = 0

#End Region

        ''' <summary>
        ''' 刷新公开属性。由 NetManager 每 0.1 秒调用一次。
        ''' </summary>
        Public Sub RefreshStat() Implements INetTaskHost.RefreshStat
            '计算进度
            Dim NewProgress As Double = 0
            Dim TotalProgress As Double = 0
            For Each File In Files
                If File.IsCopy Then
                    NewProgress += File.Progress * 0.2
                    TotalProgress += 0.2
                Else
                    NewProgress += File.Progress
                    TotalProgress += 1
                End If
            Next
            If TotalProgress > 0 AndAlso Not Double.IsNaN(TotalProgress) Then NewProgress /= TotalProgress
            '刷新进度
            _Progress = NewProgress
        End Sub

        Public Sub New(Name As String, FileTasks As IEnumerable(Of NetFile))
            Me.Name = Name
            Files = New ConcurrentQueue(Of NetFile)(FileTasks)
        End Sub
        Public Overrides Sub Start(Optional Input As Object = Nothing, Optional IsForceRestart As Boolean = False)
            If Input IsNot Nothing Then Files = New ConcurrentQueue(Of NetFile)(CType(Input, IEnumerable(Of NetFile)))
            '去重
            Files = New ConcurrentQueue(Of NetFile)(Files.DistinctBy(Function(a) PathUtils.ForCompare(a.LocalPath).Lower()))
            '设置剩余文件数
            SyncLock FileRemainLock
                FileRemain += Files.Where(Function(f) f.State <> NetState.Finished).Count
            End SyncLock
            State = LoadState.Loading
            '开始执行
            RunInNewThread(
            Sub()
                Try
                    '输入检测
                    If Not Files.Any() Then
                        OnFinish()
                        Return
                    End If
                    For Each File As NetFile In Files
                        If File Is Nothing Then Throw New ArgumentException("存在空文件请求！")
                        For Each Source As NetSource In File.Sources
                            If Not (Source.Url.StartsWithF("https://", True) OrElse Source.Url.StartsWithF("http://", True)) Then
                                Source.Ex = New ArgumentException("输入的下载链接不正确：" & Source.Url)
                                Source.IsFailed = True
                            End If
                        Next
                        If Not File.HasAvailableSource() Then Throw New ArgumentException("输入的下载链接不正确！")
                        File.LocalPath = File.LocalPath.Replace("/", "\")
                        If Not File.LocalPath.Contains(":\") Then Throw New ArgumentException("输入的本地文件地址不正确：" & File.LocalPath)
                        If File.LocalPath.EndsWithF("\") Then Throw New ArgumentException("请输入含文件名的完整文件路径：" & File.LocalPath)
                        DirectoryUtils.Create(PathUtils.RemoveLastPart(File.LocalPath)) '创建目标文件夹
                    Next
                    '接入下载管理器，获取新开始下载的文件列表
                    Dim NewFiles = NetManager.Start(Me)


                    '====================================
                    ' 已存在文件查找
                    '====================================

                    '整理允许进行查找的文件
                    Dim FilesToCheck As New List(Of NetFile)
                    For Each File In NewFiles
                        If File.Check IsNot Nothing Then
                            FilesToCheck.Add(File)
                        Else '没有校验信息，直接开始下载
                            SyncLock LockState
                                File.State = NetState.WaitingToDownload
                                File.IsCopy = False
                            End SyncLock
                        End If
                    Next
                    If Not FilesToCheck.Any Then Return
                    '获取 MC 文件夹列表
                    Dim Folders As New List(Of String)
                    Folders.Add(Paths.AppData & ".minecraft\") '总是添加官启文件夹，因为 HMCL 会把所有文件存在这里
                    Folders.AddRange(McFolderList.Select(Function(f) f.Location))
                    Folders = Folders.Distinct.Where(Function(f) DirectoryUtils.Exists(f)).ToList
                    '平均分配到多个检查线程
                    Dim ThreadCount As Integer = (FilesToCheck.Count \ 40).Clamp(1, 8) '每个线程至少 40 个文件，最多 8 线程
                    If ThreadCount = 1 Then '只有一个线程，直接执行
                        CheckExistingFiles(FilesToCheck, Folders)
                    Else
                        Dim BaseSize = FilesToCheck.Count \ ThreadCount
                        Dim Remainder = FilesToCheck.Count Mod ThreadCount
                        Dim Index = 0
                        For i = 0 To ThreadCount - 1
                            Dim Size = BaseSize + If(i < Remainder, 1, 0)
                            Dim ThreadFiles = FilesToCheck.GetRange(Index, Size)
                            Index += Size
                            RunInNewThread(Sub() CheckExistingFiles(ThreadFiles, Folders), $"下载 文件复制 {Uuid}/{GetUuid()}")
                        Next
                    End If
                Catch ex As Exception
                    OnFail(New List(Of Exception) From {New Exception("下载初始化失败", ex)})
                End Try
            End Sub, "L/下载 " & Uuid)
        End Sub
        Private Sub CheckExistingFiles(Files As List(Of NetFile), FolderList As List(Of String))
            Try
                Logger.Trace(Function() $"文件检查开始，本线程负责 {Files.Count} 个文件，首个文件为 {Files.FirstOrDefault?.LocalName}")
                '列出 MC 文件夹中的各个版本文件夹
                Dim VersionFolders As New List(Of String)
                For Each McFolder In FolderList
                    For Each VersionFolder In DirectoryUtils.EnumerateDirectories(McFolder & "versions\")
                        VersionFolders.Add(PathUtils.AddSlashSuffix(VersionFolder))
                    Next
                Next
                '处理每个文件
                For Each File As NetFile In Files
                    Dim Target As String = CheckExistingFile(FolderList, VersionFolders, File)
                    Logger.Trace(Function() $"本地文件匹配：{File.LocalName} → {If(Target, "无匹配")}，当前状态 {File.State}")
                    If File.State >= NetState.WaitingToDownload Then Continue For '中断
                    '已找到相同文件
                    If Target IsNot Nothing Then
                        File.IsCopy = True
                        Try
                            FileUtils.Copy(Target, File.LocalPath)
                            File.Finish(False)
                            Continue For
                        Catch
                        End Try
                    End If
                    '回退到下载
                    SyncLock LockState
                        File.State = NetState.WaitingToDownload
                        File.IsCopy = False
                    End SyncLock
                Next
            Catch ex As Exception
                OnFail(New List(Of Exception) From {New Exception("下载已存在文件查找失败", ex)})
            End Try
        End Sub
        Private Function CheckExistingFile(FolderList As List(Of String), VersionFolders As List(Of String), File As NetFile) As String
            '目标文件已存在
            If File.Check.Check(File.LocalPath) Is Nothing Then Return File.LocalPath
            '在设置中禁用了复制
            If Settings.Get(Of Boolean)("SystemDebugSkipCopy") Then Return Nothing
            '没有可用的检查规则，只能开始下载
            If File.Check.Hash Is Nothing AndAlso File.Check.ActualSize < 0 Then Return Nothing
            '大致判断文件类别
            Dim TypeIndexes =
                {"\assets\", "\libraries\", "\versions\", "\mods\", "\coremods\", "\lib\", "\resourcepacks\", "\texturepacks\", "\shaderpacks\"}.
                Select(Function(FolderName) (FolderName, File.LocalPath.IndexOfF(FolderName, True))).
                Where(Function(kv) kv.Item2 >= 0).ToList
            If Not TypeIndexes.Any Then
                If PathUtils.GetExtension(File.LocalName) = "jar" Then
                    TypeIndexes.Add(("\versions\", 1)) '总是对 jar 进行版本文件检查，以包括另存为 jar 的情况
                Else
                    Return Nothing
                End If
            End If
            Dim Type = TypeIndexes.MaxBy(Function(kv) kv.Item2).FolderName.TrimStart("\"c)
            '根据类别进行查找
            Static Sizes As New ConcurrentDictionary(Of String, Long)
            Static CheckCandidate As Func(Of String, FileChecker, Boolean) =
            Function(Candidate, Checker)
                '快速进行大小校验
                Dim Length = Sizes.GetOrAdd(Candidate, Function(c) If(FileUtils.Exists(c), FileUtils.GetInfo(c).Length, -1))
                If Checker.ActualSize <> Length Then Return False
                '进一步进行 Hash 校验
                Return Checker.Check(Candidate) Is Nothing
            End Function
            Select Case Type
                Case "assets\", "libraries\"
                    'assets/libraries：查找 MC 文件夹下的相同路径
                    For Each Folder In FolderList
                        Dim Candidate = Folder & Type & File.LocalPath.AfterFirst(Type)
                        If CheckCandidate(Candidate, File.Check) Then Return Candidate
                    Next
                    Return Nothing
                Case "versions\"
                    '版本 jar 或 json：查找 MC 文件夹下的各个版本文件夹
                    For Each VersionFolder In VersionFolders
                        For Each Candidate In DirectoryUtils.EnumerateFiles(VersionFolder, searchPattern:="*." & PathUtils.GetExtension(File.LocalPath))
                            If CheckCandidate(Candidate, File.Check) Then Return Candidate
                        Next
                    Next
                Case Else
                    '社区资源
                    If File.Check.ActualSize < 0 OrElse File.Check.Hash Is Nothing Then Return Nothing '必须要求指定了文件大小和 Hash
                    For Each Folder In FolderList.Concat(VersionFolders)
                        For Each Candidate In DirectoryUtils.EnumerateFiles(Folder & Type)
                            If CheckCandidate(Candidate, File.Check) Then Return Candidate
                        Next
                    Next
            End Select
            Return Nothing
        End Function

        ''' <summary>宿主任务标识的接口代理（LoaderBase.Uuid 为字段，不能直接承载 Implements）。</summary>
        Public ReadOnly Property TaskUuid As Integer Implements INetTaskHost.Uuid
            Get
                Return Uuid
            End Get
        End Property
        ''' <summary>宿主任务文件队列的接口代理（Files 为字段，不能直接承载 Implements；管理器 Start 读取并按去重结果整队列替换）。</summary>
        Public Property FilesBridge As ConcurrentQueue(Of NetFile) Implements INetTaskHost.Files
            Get
                Return Files
            End Get
            Set(value As ConcurrentQueue(Of NetFile))
                Files = value
            End Set
        End Property
        Public Sub OnFileFinish(File As NetFile) Implements INetTaskHost.OnFileFinish
            '要求全部文件完成
            SyncLock FileRemainLock
                FileRemain -= 1
                If FileRemain > 0 Then Return
            End SyncLock
            OnFinish()
        End Sub
        Public Sub OnFinish()
            RaisePreviewFinish()
            SyncLock LockState
                If State > LoadState.Loading Then Return
                State = LoadState.Finished
            End SyncLock
        End Sub
        Public Sub OnFileFail(File As NetFile) Implements INetTaskHost.OnFileFail
            '将下载源的错误加入主错误列表
            For Each Source In File.Sources
                If Not IsNothing(Source.Ex) Then File.Ex.Add(Source.Ex)
            Next
            OnFail(File.Ex)
        End Sub
        Public Overrides Sub Failed(Ex As Exception)
            OnFail(New List(Of Exception) From {Ex})
        End Sub
        Public Sub OnFail(ExList As List(Of Exception))
            SyncLock LockState
                If State >= LoadState.Finished Then Return
                If ExList Is Nothing OrElse Not ExList.Any() Then ExList = New List(Of Exception) From {New Exception("未知错误！")}
                '寻找有效的错误信息
                Dim UsefulExs = ExList.Where(Function(e) Not e.IsCanceled AndAlso TypeOf e IsNot TimeoutException).ToList
                [Error] = If(UsefulExs.FirstOrDefault, ExList.FirstOrDefault)
                '获取实际失败的文件
                For Each File In Files
                    If File.State <> NetState.Canceled Then Continue For
                    If File.Sources.All(Function(s) s.Ex.IsCanceled OrElse TypeOf s.Ex Is TimeoutException) Then Continue For
                    Dim Detail As String = File.Sources.Select(Function(s) $"{If(s.Ex Is Nothing, "无错误信息。", s.Ex.GetDisplay(False))}（{s.Url}）").Join(vbCrLf)
                    [Error] = New Exception("文件下载失败：" & File.LocalPath & vbCrLf &
                                            "各下载源的错误如下：" & vbCrLf & Detail, [Error])
                    Exit For
                Next
                '在设置 Error 对象后再更改为失败，避免 WaitForExit 无法捕获错误
                State = LoadState.Failed
            End SyncLock
            '中断所有文件
            For Each TaskFile In Files
                TaskFile.Cancel(Me)
            Next
            '在退出同步锁后再进行日志输出
            Logger.Error(New AggregateException(ExList), "下载加载器失败", LogBehavior.None)
        End Sub
        Public Overrides Sub Cancel()
            SyncLock LockState
                If State >= LoadState.Finished Then Return
                State = LoadState.Canceled
            End SyncLock
            Logger.Info($"{Name} 已取消！")
            '中断所有文件
            For Each TaskFile In Files
                TaskFile.Cancel(Me)
            Next
        End Sub

    End Class

    ''' <summary>
    ''' 下载文件管理（原 NetManagerClass 公共字段，ModNet.vb:1635-1837 已迁入 C# 单例；公共名逐字保留，消费方零改动）。
    ''' </summary>
    Public ReadOnly Property NetManager As DownloadManager
        Get
            Return DownloadManager.Instance
        End Get
    End Property

    ''' <summary>
    ''' 是否有正在进行中、需要在任务管理页面显示的下载任务？
    ''' </summary>
    Public Function HasDownloadingTask(Optional IgnoreCustomDownload As Boolean = False) As Boolean
        Return LoaderTaskbar.Any(
        Function(Task) Task.Show AndAlso Task.State = LoadState.Loading AndAlso
                       (Not IgnoreCustomDownload OrElse Not Task.Name.ToString.Contains("自定义下载")))
    End Function

#End Region

#Region "端口"

    ''' <summary>
    ''' 随机获取单个可用的端口。
    ''' </summary>
    Public Function FindFreePort() As Integer
        Return NetRequest.FindFreePort()
    End Function

    ''' <summary>
    ''' 获取当前已被占用的端口列表。
    ''' </summary>
    Public Function GetUsedPorts() As List(Of Integer)
        Return NetRequest.GetUsedPorts()
    End Function

    ''' <summary>
    ''' 寻找数个连续编号的可用端口。
    ''' </summary>
    Public Function FindFreePorts(ConsecutiveCount As Integer, ParamArray ExtraBlackLists As Integer()) As List(Of Integer)
        Return NetRequest.FindFreePorts(ConsecutiveCount, ExtraBlackLists)
    End Function

#End Region

    ''' <summary>
    ''' 测试 Ping。失败则返回 -1。
    ''' </summary>
    Public Function Ping(Ip As String, Optional Timeout As Integer = 10000, Optional MakeLog As Boolean = True) As Integer
        'C# 核心固定记日志；VB 的 MakeLog 参数当前无调用方传入 False，故不透传
        Return NetRequest.Ping(Ip, Timeout)
    End Function

    ''' <summary>
    ''' 设置是否应当在正版登录时校验 SSL 证书。
    ''' </summary>
    Public Sub ShouldValidateSslCertificateOnLogin(Enabled As Boolean)
        NetRequest.ShouldValidateSslCertificateOnLogin(Enabled)
    End Sub

End Module
