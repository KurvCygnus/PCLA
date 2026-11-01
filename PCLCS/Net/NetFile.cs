using PCLCS.Settings;
using System.Diagnostics;
using System.Net.Http.Headers;

namespace PCLCS.Net;

/// <summary>
/// 宿主任务回调面：C# 引擎逐任务通知完成/失败并同步失败计数。
/// 宿主 LoaderDownload（ModNet.vb）留守在 VB 侧，宿主层程序集不进入本工程引用，故经此接口桥接；
/// VB 侧以 Implements 子句接入（LoaderDownload.Uuid 为字段，另设代理属性承载）。
/// </summary>
public interface INetTaskHost
{
    /// <summary>宿主任务标识（LoaderBase.Uuid）。</summary>
    int Uuid { get; }
    /// <summary>任务中的文件的连续失败计数（LoaderDownload.FailCount）。</summary>
    int FailCount { get; set; }
    /// <summary>单个文件失败回调（ModNet.vb:1390）。</summary>
    void OnFileFail(NetFile file);
    /// <summary>单个文件完成回调（ModNet.vb:1375）。</summary>
    void OnFileFinish(NetFile file);
}

/// <summary>
/// 下载单个文件（ModNet.vb:388-1110 逐行移植）。
/// Start 为过渡语义的自驱入口：承担 NetManager ThreadStarter 的逐文件调度（:1529-1571），
/// 待 Task 3 NetManager.Register 落地后改为注册制。
/// </summary>
public sealed class NetFile
{
    //？ 引擎对宿主层全局状态的副作用经以下接线完成（ModNet.Sub New 赋值，与 NetRequest.CdnSigner 同模式）；null 时为测试直用语义

    /// <summary>Interlocked.Add(NetManager.DownloadDone, Δ) 接线（NetManagerClass 留守 VB）。</summary>
    public static Action<long>? DownloadDoneDelta;

    /// <summary>Interlocked.Increment / Decrement(NetManager.FileRemain) 接线（NetManagerClass 留守 VB）。</summary>
    public static Action<int>? FileRemainDelta;

    /// <summary>ModBase.GetUuid 接线；null 时回退本地计数器（测试直用）。</summary>
    public static Func<int>? UuidProvider;

    /// <summary>ModBase.PathTemp 接线；null 时回退 %TEMP%（测试直用）。</summary>
    public static Func<string>? PathTempProvider;

    /// <summary>ModBase.IsProgramEnding 接线；null 时视为未结束（测试直用）。</summary>
    public static Func<bool>? IsProgramEndingProvider;

    #region 全局调度状态（ModNet.vb:137-177 随引擎迁入；VB 侧经 ModNet 转发成员访问）

    /// <summary>正在运行中的线程数。</summary>
    public static int NetTaskThreadCount;

    /// <summary>最大线程数。</summary>
    public static int NetTaskThreadLimit => SettingsService.Get<int>("ToolDownloadThread") + 1;

    /// <summary>速度下限。</summary>
    public static long NetTaskSpeedLimitLow = 256 * 1024L; //256K/s

    private static long? netTaskSpeedLimitHighCache;

    /// <summary>速度上限。若无限制则为 -1。</summary>
    public static long NetTaskSpeedLimitHigh
    {
        get
        {
            //! 逐字移植豁免（Rider 反转 if）：VB :149-165 的 If 缓存判空结构
            if (netTaskSpeedLimitHighCache == null)
            {
                var setting = SettingsService.Get<int>("ToolDownloadSpeed");
                if (setting <= 14)
                {
                    netTaskSpeedLimitHighCache = Convert.ToInt64((setting + 1) * 0.1 * 1024 * 1024L);
                } else if (setting <= 31)
                {
                    netTaskSpeedLimitHighCache = Convert.ToInt64((setting - 11) * 0.5 * 1024 * 1024L);
                } else if (setting <= 41)
                {
                    netTaskSpeedLimitHighCache = Convert.ToInt64((setting - 21) * 1024 * 1024L);
                } else
                {
                    netTaskSpeedLimitHighCache = -1;
                }
            }
            return netTaskSpeedLimitHighCache.Value;
        }
    }

    /// <summary>基于限速，当前可以下载的剩余量。</summary>
    private static long netTaskSpeedLimitLeft = -1;

    /// <summary>清空速度上限缓存（ModNet.vb:167-169；Value 参数与 VB 一致被忽略）。</summary>
    public static void UpdateNetTaskSpeedLimitHigh(int value) => netTaskSpeedLimitHighCache = null;

    /// <summary>增加限速余量（ModNet.vb:1584 StatRefresher 的 Interlocked.Add；VB 侧改为直呼本方法）。</summary>
    public static void RefillSpeedLimit()
    {
        if (NetTaskSpeedLimitHigh > 0) Interlocked.Add(ref netTaskSpeedLimitLeft, Convert.ToInt64(NetTaskSpeedLimitHigh / 10.0));
    }

    #endregion

    #region 属性

    /// <summary>所属的文件列表任务。</summary>
    public ConcurrentList<INetTaskHost> Tasks { get; } = [];

    /// <summary>所有下载源。</summary>
    public List<NetSource> Sources { get; } = [];

    /// <summary>用于在第一个线程出错时切换下载源。</summary>
    private int _firstThreadSource;

    /// <summary>
    /// 所有已经被标记为失败的，但未完整尝试过的，不允许断点续传的下载源。
    /// </summary>
    public ConcurrentList<NetSource> SourcesOnce { get; } = [];

    /// <summary>
    /// 仅当合并失败或首次下载失败时，会将所有下载源重新标记为不允许断点续传的下载源，逐个重新尝试下载。
    /// 这一策略可以兼容多个下载源中的一部分返回错误的文件的情况，以及部分在多线程下载时会抽风的源。
    /// </summary>
    private bool _retried;

    /// <summary>
    /// 获取从某个源开始，第一个可用的源（ModNet.vb:416-436 逐字）。
    /// </summary>
    private NetSource? GetSource(int id = 0)
    {
        if (id >= Sources.Count || id < 0) id = 0;
        //! 逐字移植豁免（Rider 锁序环）：VB :416-436 GetSource 的 SyncLock LockSource 内调 HasAvailableSource 同锁重入
        lock (LockSource)
        {
            if (HasAvailableSource(false))
            {
                //存在多线程可用源
                var currentSource = Sources[id];
                while (currentSource.IsFailed)
                {
                    id++;
                    if (id >= Sources.Count) id = 0;
                    currentSource = Sources[id];
                }
                return currentSource;
            }
            //! 逐字移植豁免（Rider 转 return）：VB :428-434 的 If/Else 结构镜像
            if (SourcesOnce.Any()) return SourcesOnce.First(); //仅存在单线程可用源
            return null; //没有可用源
        }
    }

    /// <summary>
    /// 是否存在可用源（ModNet.vb:440-446 逐字）。
    /// </summary>
    public bool HasAvailableSource(bool allowOnceSource = true)
    {
        lock (LockSource)
        {
            if (Sources.Any(static s => !s.IsFailed)) return true; //存在多线程可用源
            if (allowOnceSource && SourcesOnce.Any()) return true; //存在单线程可用源
        }
        return false;
    }

    /// <summary>存储在本地的带文件名的地址。（LoaderDownload.Start 会改写分隔符，故开放写入）</summary>
    public string LocalPath { get; set; } = "";

    /// <summary>存储在本地的文件名。</summary>
    public string LocalName { get; } = "";

    /// <summary>当前的下载状态。（LoaderDownload.Start 会置 WaitingToDownload，故开放写入）</summary>
    public NetState State { get; set; } = NetState.WaitingToCheck;

    /// <summary>导致下载失败的原因。</summary>
    public List<Exception> Ex { get; } = [];

    /// <summary>导致下载失败的原因（brief 接口名，与 Ex 同一列表）。</summary>
    public List<Exception> Exs => Ex;

    /// <summary>作为文件组成部分的线程链表。如果没有线程，可以为 null。</summary>
    public NetThread? Threads { get; internal set; }

    /// <summary>文件的总大小。若为 -2 则为未获取，若为 -1 则为无法获取准确大小。</summary>
    public long FileSize { get; internal set; } = -2;

    /// <summary>该文件是否无法获取准确大小。</summary>
    public bool IsUnknownSize { get; internal set; }

    /// <summary>该文件是否不需要分割。</summary>
    public bool IsNoSplit => IsUnknownSize || FileSize < 1024 * 1024; //小于 1MB 的文件不分割

    /// <summary>为不需要分割的小文件在内存中提供临时存储。</summary>
    private MemoryStream? _cache;

    /// <summary>文件的已下载大小。（VB 为公共字段：Reset 清零与线程 Interlocked 记数并存，保留字段语义）</summary>
    public long DownloadDone;

    /// <summary>文件的校验规则。</summary>
    public FileChecker? Check { get; set; }

    /// <summary>是否模拟浏览器的 UserAgent 和 Referer。</summary>
    public bool SimulateBrowserHeaders { get; set; }

    /// <summary>上次记速时的时间。</summary>
    private long _speedLastTime = GetTimeMs();

    /// <summary>上次记速时的已下载大小。</summary>
    private long _speedLastDone;

    /// <summary>当前的下载速度，单位为 Byte / 秒。</summary>
    public long Speed
    {
        get
        {
            //! 逐字移植豁免（Rider 反转 if）：VB :519-527 的记速结构
            if (GetTimeMs() - _speedLastTime > 200)
            {
                var deltaTime = GetTimeMs() - _speedLastTime;
                _speed = Convert.ToInt64((DownloadDone - _speedLastDone) / (deltaTime / 1000.0));
                _speedLastDone = DownloadDone;
                _speedLastTime += deltaTime;
            }
            return _speed;
        }
    }
    private long _speed;

    /// <summary>该文件是否由本地文件直接复制完成。</summary>
    public bool IsCopy { get; set; }

    /// <summary>本文件的显示进度。</summary>
    public double Progress
    {
        get
        {
            switch (State)
            {
            case NetState.WaitingToCheck: return 0;
            case NetState.WaitingToDownload: return 0.01;
            case NetState.Connecting: return 0.02;
            case NetState.Reading: return 0.04;
            case NetState.Downloading:
            {
                //正在下载中，对应 5% ~ 98%
                var originalProgress = IsUnknownSize ? 0.5 : DownloadDone / (double) Math.Max(FileSize, 1);
                originalProgress = 1 - Math.Pow(1 - originalProgress, 0.9);
                return originalProgress * 0.93 + 0.05;
            }
            case NetState.Merging: return 0.99;
            case NetState.Finished:
            case NetState.Canceled: return 1;
            default: throw new ArgumentOutOfRangeException($"文件状态未知：{State}");
            }
        }
    }

    /// <summary>各个线程建立连接成功的总次数。</summary>
    private int _connectCount;

    /// <summary>各个线程建立连接成功的总时间。</summary>
    private long _connectTime;

    /// <summary>各个线程建立连接成功的平均时间，单位为毫秒，-1 代表尚未有成功连接。</summary>
    private int ConnectAverage
    {
        get
        {
            var currentConnectCount = _connectCount; //防止竞态条件
            return currentConnectCount == 0 ? -1 : Convert.ToInt32(_connectTime / (double) currentConnectCount);
        }
    }

    private const long FilePieceLimit = 256 * 1024;

    public readonly object LockState = new();
    public readonly object LockChain = new();
    public readonly object LockSource = new();

    public int Uuid { get; } = GetUuid();
    private static int uuidFallback;

    /// <summary>获取全程序唯一编号（ModBase.GetUuid 经 UuidProvider 接线；null 时回退本地计数器）。</summary>
    private static int GetUuid() => UuidProvider?.Invoke() ?? Interlocked.Increment(ref uuidFallback);

    public override bool Equals(object? obj) => obj is NetFile file && Uuid == file.Uuid;

    //! VB 未重写 GetHashCode；C# 重写 Equals 须配套以避免 CS0659
    public override int GetHashCode() => Uuid;

    #endregion

    /// <summary>
    /// 新建一个需要下载的文件（ModNet.vb:598-611 逐字；每源 URL 经 CdnSigner）。
    /// </summary>
    /// <param name="urls">下载源 Url 列表。</param>
    /// <param name="localPath">包含文件名的本地地址。</param>
    /// <param name="checker">文件的校验规则。</param>
    /// <param name="simulateBrowserHeaders">是否模拟浏览器的 UserAgent 和 Referer。</param>
    public NetFile(IEnumerable<string> urls, string localPath, FileChecker? checker = null, bool simulateBrowserHeaders = false)
    {
        var sources = new List<NetSource>();
        var count = 0;
        urls = urls.Distinct().ToArray();
        foreach (var source in urls)
        {
            var cleaned = source.ReplaceLineEndings("").Trim();
            sources.Add(new NetSource { FailCount = 0, Url = NetRequest.CdnSigner?.Invoke(cleaned) ?? cleaned, Id = count, IsFailed = false, Ex = null });
            count++;
        }
        Sources = sources;
        LocalPath = localPath;
        Check = checker;
        SimulateBrowserHeaders = simulateBrowserHeaders;
        LocalName = PathUtils.GetLastPart(localPath);
    }

    /// <summary>
    /// 尝试开始一个新的下载线程（ModNet.vb:617-707 逐字）。
    /// 如果失败，返回 null。
    /// </summary>
    public NetThread? TryBeginThread()
    {
        try
        {
            long startPosition;
            NetSource? startSource;
            Thread thread;
            NetThread threadInfo;
            //条件检测
            //! 逐字移植豁免（Rider 锁序环）：VB :623 SyncLock LockSource 内调 HasAvailableSource 同锁重入
            lock (LockSource)
            {
                if (NetTaskThreadCount >= NetTaskThreadLimit || !HasAvailableSource() ||
                    (IsNoSplit && Threads != null && Threads.State != NetState.Canceled)) return null;
                if (State >= NetState.Merging || State == NetState.WaitingToCheck) return null;
                lock (LockState)
                {
                    if (State < NetState.Connecting) State = NetState.Connecting;
                }
                //初始化参数
                //获取线程起点与下载源
                //不分割 / 只剩下单线程可用点（VB 经 GoTo StartSingleThreadDownload 汇入同一段：Reset + 置 Reading；
                //C# 标签限定在声明块内，故以等价 if / else-if 承载——IsNoSplit 为真时不评估第二条件，与 VB 跳转一致）
                if (IsNoSplit)
                {
                    Reset();
                    State = NetState.Reading;
                } else if (!HasAvailableSource(false))
                {
                    var onceSource = SourcesOnce.First();
                    if (onceSource.SingleThread != null && onceSource.SingleThread.State != NetState.Canceled) return null;
                    Reset();
                    State = NetState.Reading;
                }
            }
            //! 逐字移植豁免（Rider 锁序环）：VB :642 SyncLock LockChain，链上 DownloadEnd 亦持 LockChain（同锁重入）
            lock (LockChain)
            {
                //首个开始点
                if (Threads == null)
                {
                    startPosition = 0;
                    startSource = GetSource(_firstThreadSource);
                    _firstThreadSource = startSource!.Id + 1; //VB 同处直接解引用，无可用源时由外层 catch 兜底 Fail
                    goto StartThread;
                }
                //寻找失败点
                foreach (var canceledThread in Threads)
                {
                    //! 逐字移植豁免（Rider 反转 if）：VB :652 寻找失败点结构
                    if (canceledThread.State == NetState.Canceled && canceledThread.DownloadUndone > 0)
                    {
                        startPosition = canceledThread.DownloadStart + canceledThread.DownloadDone;
                        startSource = GetSource(canceledThread.Source.Id + 1);
                        goto StartThread;
                    }
                }
                //是否禁用多线程，以及规定碎片大小
                var source = GetSource();
                if (source == null) return null;
                var targetUrl = source.Url;
                if (targetUrl.Contains("pcl2-server") || targetUrl.Contains("meloong.com") || targetUrl.Contains("bmclapi") || targetUrl.Contains("github.com") ||
                    targetUrl.Contains("optifine.net") || targetUrl.Contains("momot.rs")) return null;
                //寻找最大碎片
                //FUTURE: 下载引擎重做，计算下载源平均链接时间和线程下载速度，按最高时间节省来开启多线程
                var filePieceMax = Threads;
                foreach (var pieceThread in Threads)
                {
                    if (pieceThread.DownloadUndone > filePieceMax.DownloadUndone) filePieceMax = pieceThread;
                }
                if (filePieceMax == null || filePieceMax.DownloadUndone < FilePieceLimit) return null;
                startPosition = Convert.ToInt64(filePieceMax.DownloadEnd - filePieceMax.DownloadUndone * 0.4);
                startSource = GetSource();

//开始线程
StartThread:
                if ((startPosition > FileSize && FileSize >= 0 && !IsUnknownSize) || startPosition < 0 || startSource == null) return null;
                //构建线程
                var threadUuid = GetUuid();
                if (Tasks.Count == 0) return null; //由于中断，已没有可用任务
                thread = new Thread(ThreadMain) { Name = $"下载 {Tasks[0].Uuid}/{Uuid}/{threadUuid}#", Priority = ThreadPriority.BelowNormal };
                threadInfo = new NetThread { Uuid = threadUuid, DownloadStart = startPosition, Thread = thread, Source = startSource, Task = this, State = NetState.WaitingToDownload };
                //链表处理
                if (threadInfo.IsFirstThread || Threads == null)
                {
                    Threads = threadInfo;
                } else
                {
                    var currentChain = Threads;
                    while (currentChain.DownloadEnd <= startPosition) currentChain = currentChain.NextThread!; //VB 同处直接推进，由链表不变量保证可达
                    threadInfo.NextThread = currentChain.NextThread;
                    currentChain.NextThread = threadInfo;
                }
            }
            //开始线程
            Interlocked.Increment(ref NetTaskThreadCount);
            lock (LockSource)
            {
                if (!HasAvailableSource(false)) SourcesOnce.First().SingleThread = threadInfo;
            }
            thread.Start(threadInfo);
            return threadInfo;
        } catch (Exception ex)
        {
            Fail(new Exception($"尝试开始下载线程失败（{LocalName}）", ex));
            return null;
        }
    }

    /// <summary>
    /// 每个下载线程执行的代码（ModNet.vb:711-878 逐字；裸 Thread 入口保持 object 参数模型，VB 签名为 Sub Thread(Th As NetThread)）。
    /// </summary>
    private void ThreadMain(object? thObj)
    {
        var th = (NetThread) thObj!;
        Logger.Log($"{LocalName}：开始，起始点 {th.DownloadStart}，{th.Source.Url}", th.DownloadStart == 0 ? LogLevel.Info : LogLevel.Trace);
        Stream? resultStream = null;
        HttpRequestMessage? httpRequest = null;
        HttpResponseMessage? response = null;
        Stream? responseStream = null;
        CancellationTokenSource? cancelToken = null;
        //部分下载源真的特别慢，并且只需要一个请求，例如 Ping 为 20s，如果增长太慢，就会造成类似 2.5s 5s 7.5s 10s 12.5s... 的极大延迟
        //延迟过长会导致某些特别慢的链接迟迟不被掐死
        var timeout = Math.Min(Math.Max(ConnectAverage, 15000) * (1 + th.Source.FailCount), 30000);
        th.State = NetState.Connecting;
        try
        {
            var httpDataCount = 0;
            long contentLength = 0; //VB 于 :727 声明并初始化；C# 的 goto SourceBreak 会跳过后续声明点，故提前到 try 起始
            if (SourcesOnce.Contains(th.Source) && th != th.Source.SingleThread) goto SourceBreak;
            httpRequest = new HttpRequestMessage(HttpMethod.Get, th.Source.Url);
            NetRequest.HeadersSigner?.Invoke(httpRequest.Headers, th.Source.Url, SimulateBrowserHeaders); //ModNet.vb:724 SecretHeadersSign 经签名接线
            cancelToken = new CancellationTokenSource(timeout);
            if (!th.IsFirstThread) httpRequest.Headers.Range = new RangeHeaderValue(th.DownloadStart, null);
            response = GetResultWithTimeout(ThreadClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancelToken.Token), cancelToken, timeout);
            if (!response.IsSuccessStatusCode) //状态码检查
            {
                throw new HttpRequestCodeException($"错误码 {response.StatusCode} ({(int) response.StatusCode})，{th.Source.Url}", response.StatusCode, null);
            }
            if (State == NetState.Canceled) goto SourceBreak; //快速中断
            //! 逐字移植豁免（Rider 捕获变量释放序）：VB :733 的重定向延迟日志，response 于 finally 释放
            if (response.RequestMessage!.RequestUri!.ToString() != th.Source.Url) Logger.Trace(() => $"{LocalName}：重定向至 {response.RequestMessage.RequestUri}");
            //文件大小校验（ModNet.vb:734-781；VB 的 GoTo NotSupportRange 以 notSupportRange 标志汇入同段：
            //contentLength<0 分支结束后 VB 即跳出整条链，故 Range 一致性检查仅在其未进入或显式汇入时执行）
            contentLength = response.Content.Headers.ContentLength.GetValueOrDefault(-1);
            var notSupportRange = false;
            if (contentLength < 0)
            {
                if (FileSize > 1)
                {
                    if (th.DownloadStart == 0)
                    {
                        Logger.Info($"{LocalName}：文件大小未知，但已从其他下载源获取，不作处理");
                    } else
                    {
                        Logger.Info($"{LocalName}：ContentLength 返回了 {contentLength}，无法确定是否支持分段下载，视作不支持");
                        notSupportRange = true; //GoTo NotSupportRange
                    }
                } else
                {
                    FileSize = -1;
                    IsUnknownSize = true;
                    Logger.Info($"{LocalName}：文件大小未知");
                }
            } else if (th.IsFirstThread)
            {
                if (Check != null)
                {
                    if (contentLength < Check.MinSize && Check.MinSize > 0)
                    {
                        throw new Exception($"文件大小不足，获取结果为 {contentLength}，要求至少为 {Check.MinSize}。");
                    }
                    if (contentLength != Check.ActualSize && Check.ActualSize > 0)
                    {
                        throw new Exception($"文件大小不一致，获取结果为 {contentLength}，要求必须为 {Check.ActualSize}。");
                    }
                }
                FileSize = contentLength;
                IsUnknownSize = false;
                Logger.Info($"{LocalName}：文件大小 {contentLength}（{StringUtils.FormatByteSize(contentLength)}）");
                //若文件大小大于 50 M，进行剩余磁盘空间校验
                if (contentLength > 50 * 1024 * 1024)
                {
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        var driveName = drive.Name.First().ToString();
                        var requiredSpace = (PathTemp.StartsWithF(driveName) ? contentLength * 1.1 : 0) +
                                            (LocalPath.StartsWithF(driveName) ? contentLength + 5 * 1024 * 1024 : 0);
                        if (requiredSpace > 0 && drive.TotalFreeSpace < requiredSpace)
                        {
                            throw new Exception(driveName + " 盘空间不足，无法进行下载。\r\n需要至少 " + StringUtils.FormatByteSize(Convert.ToInt64(requiredSpace)) + " 空间，但当前仅剩余 " + StringUtils.FormatByteSize(drive.TotalFreeSpace) + "。" +
                                                (PathTemp.StartsWithF(driveName) ? "\r\n\r\n下载时需要与文件同等大小的空间存放缓存，你可以在设置中调整缓存文件夹的位置。" : ""));
                        }
                    }
                }
            } else if (FileSize < 0)
            {
                throw new Exception("非首线程运行时，尚未获取文件大小");
            }

            //NotSupportRange 汇合段（ModNet.vb:774-781）
            if (notSupportRange || (contentLength >= 0 && th.DownloadStart > 0 && contentLength >= FileSize))
            {
                lock (LockSource)
                {
                    if (SourcesOnce.Contains(th.Source)) goto SourceBreak;
                }
                throw new RangeNotSupportedException($"该下载源不支持分段下载：Range 起始于 {th.DownloadStart}，预期 ContentLength 为 {FileSize - th.DownloadStart}，返回 ContentLength 为 {contentLength}，总文件大小 {FileSize}");
            }
            if (contentLength >= 0 && FileSize - th.DownloadStart != contentLength)
            {
                throw new RangeNotSupportedException($"获取到的分段大小不一致：Range 起始于 {th.DownloadStart}，预期 ContentLength 为 {FileSize - th.DownloadStart}，返回 ContentLength 为 {contentLength}，总文件大小 {FileSize}");
            }
            //Log($"[Download] {LocalName} {Info.Uuid}#：通过大小检查，文件大小 {FileSize}，起始点 {Info.DownloadStart}，ContentLength {ContentLength}")
            th.State = NetState.Reading;
            lock (LockState)
            {
                if (State < NetState.Reading) State = NetState.Reading;
            }
            //创建缓存文件
            if (IsNoSplit)
            {
                th.Temp = null;
                _cache = new MemoryStream();
                resultStream = _cache;
            } else
            {
                th.Temp = $"{PathTemp}Download\\{Uuid}_{th.Uuid}_{RandomInteger(0, 999999)}.tmp";
                resultStream = FileUtils.CreateAsStream(th.Temp);
            }
            //开始下载
            cancelToken.Dispose();
            cancelToken = new CancellationTokenSource(); //重置超时，不再让 CancellationToken 在固定时间后触发（#8488）
            responseStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            if (SettingsService.Get<bool>("SystemDebugDelay")) Thread.Sleep(RandomInteger(50, 3000));
            var responseBytes = new byte[16384];
            httpDataCount = GetResultWithTimeout(responseStream.ReadAsync(responseBytes, 0, 16384, cancelToken.Token), cancelToken, timeout);
            while ((IsUnknownSize || th.DownloadUndone > 0) && //判断是否下载完成
                   httpDataCount > 0 && !IsProgramEnding && State < NetState.Merging && (!th.Source.IsFailed || th.Source.SingleThread == th))
            {
                //限速
                while (NetTaskSpeedLimitHigh > 0 && Interlocked.Read(ref netTaskSpeedLimitLeft) <= 0)
                {
                    Thread.Sleep(16);
                }
                var realDataCount = IsUnknownSize ? httpDataCount : (int) Math.Min(httpDataCount, th.DownloadUndone);
                if (NetTaskSpeedLimitHigh > 0) Interlocked.Add(ref netTaskSpeedLimitLeft, -realDataCount);
                var deltaTime = GetTimeMs() - th.LastReceiveTime;
                if (deltaTime > 100000000) deltaTime = 1; //避免时间刻反转导致出现极大值
                if (realDataCount > 0)
                {
                    //有数据
                    if (th.DownloadDone == 0)
                    {
                        //第一次接受到数据
                        th.State = NetState.Downloading;
                        lock (LockState)
                        {
                            if (State < NetState.Downloading) State = NetState.Downloading;
                        }
                        Interlocked.Increment(ref _connectCount);
                        Interlocked.Add(ref _connectTime, GetTimeMs() - th.InitTime);
                    }
                    th.Source.FailCount = 0;
                    foreach (var task in Tasks)
                    {
                        task.FailCount = 0;
                    }
                    DownloadDoneDelta?.Invoke(realDataCount);
                    Interlocked.Add(ref DownloadDone, realDataCount);
                    th.DownloadDone += realDataCount;
                    resultStream!.Write(responseBytes, 0, realDataCount);
                    //已完成
                    if (th.DownloadUndone == 0 && !IsUnknownSize) break;
                    //检查速度是否过慢
                    if (th.LastReceiveTime > 0 && deltaTime > 5000 && deltaTime > realDataCount && //数据包间隔大于 5s，且速度小于 1K/s
                        th.Source.SingleThread == null) //且并非单线程下载
                    {
                        throw new TimeoutException($"由于速度过慢断开链接，下载 {realDataCount} B，消耗 {deltaTime} ms。");
                    }
                    th.LastReceiveTime = GetTimeMs();
                } else if (th.LastReceiveTime > 0 && deltaTime > timeout)
                {
                    //无数据，且已超时
                    throw new TimeoutException("操作超时，无数据。");
                }
                httpDataCount = GetResultWithTimeout(responseStream.ReadAsync(responseBytes, 0, 16384, cancelToken.Token), cancelToken, timeout);
            }
SourceBreak:
            if (State == NetState.Canceled || (th.Source.IsFailed && th.Source.SingleThread != th) || (th.DownloadUndone > 0 && !IsUnknownSize))
            {
                //被外部中断
                th.State = NetState.Canceled;
                Logger.Info($"{LocalName}：中断");
            } else if (httpDataCount == 0 && th.DownloadUndone > 0 && !IsUnknownSize)
            {
                //服务器无返回数据
                throw new Exception($"返回的 ContentLength 过多：ContentLength 为 {contentLength}，但获取到的总数据量仅为 {th.DownloadDone}（全文件总数据量 {DownloadDone}）");
            } else
            {
                //本线程完成
                th.State = NetState.Finished;
                Logger.Trace(() => $"{LocalName}：完成，已下载 {th.DownloadDone}（{th.DownloadStart}~{th.DownloadEnd}）");
            }
        } catch (Exception ex)
        {
            if (ex.IsBadNetwork() || ex is TaskCanceledException)
            {
                Logger.Info(new TimeoutException($"{LocalName}：超时（{timeout}ms），{ex.Message}"));
            } else
            {
                Logger.Info(ex, $"{LocalName}：出错");
            }
            SourceFail(th, ex, false);
        } finally
        {
            //释放资源
            httpRequest?.Dispose();
            response?.Dispose();
            responseStream?.Dispose();
            cancelToken?.Dispose();
            if (!IsNoSplit) resultStream?.Dispose();
            //改变计数
            Interlocked.Decrement(ref NetTaskThreadCount);
            //合并
            if (((FileSize >= 0 && DownloadDone >= FileSize) || (FileSize == -1 && DownloadDone > 0)) &&
                State < NetState.Merging && th.State != NetState.Canceled) Merge(th);
        }
    }

    private void SourceFail(NetThread th, Exception ex, bool isMergeFailure)
    {
        //状态变更
        Interlocked.Increment(ref th.Source.FailCount);
        foreach (var task in Tasks)
        {
            //VB 对属性 ByRef 传递为拷贝入/出（非原子），此处保持同语义
            var failCount = task.FailCount;
            task.FailCount = failCount + 1;
        }
        th.State = NetState.Canceled;
        th.Source.Ex = ex;
        //根据情况判断，是否在多线程下禁用下载源（连续错误过多，或不支持断点续传）
        var isRangeNotSupported = ex is RangeNotSupportedException || ex.Message.Contains("(416)");
        if (isMergeFailure || isRangeNotSupported ||
            ex.Message.Contains("(502)") || ex.Message.Contains("(404)") ||
            ex.Message.Contains("未能解析") || ex.Message.Contains("无返回数据") || ex.Message.Contains("空间不足") ||
            ((ex.Message.Contains("(403)") || ex.Message.Contains("(429)")) && !th.Source.Url.Contains("bmclapi")) || //BMCLAPI 的部分源在高频率请求下会返回 403/429，所以不应因此禁用下载源
            (th.Source.FailCount >= NetTaskThreadLimit.Clamp(5, 30) && DownloadDone < 1) || th.Source.FailCount > NetTaskThreadLimit + 2)
        {
            //当一个下载源有多个线程在下载时，只选择其中一个线程进行后续处理
            var isThisFail = false;
            lock (LockSource)
            {
                if (!th.Source.IsFailed || th.Source.SingleThread == th)
                {
                    isThisFail = true;
                    th.Source.IsFailed = true;
                }
            }
            //……后续处理
            if (isThisFail)
            {
                Logger.Info($"{LocalName}：下载源被禁用（{th.Source.Id}，Range 问题：{isRangeNotSupported}）：{th.Source.Url}");
                Logger.Warn(ex, $"{(SourcesOnce.FirstOrDefault()?.SingleThread == null ? "" : "单线程")}下载源 {th.Source.Id} 已被禁用");
                lock (LockSource)
                {
                    SourcesOnce.Remove(th.Source);
                }
                if (ex.Message.Contains("空间不足"))
                {
                    //硬盘空间不足：强制失败
                    Fail(ex);
                } else if (HasAvailableSource() && !isMergeFailure)
                {
                    //当前源失败，但还有下载源：正常地继续执行
                } else if (!_retried)
                {
                    //合并失败或首次下载失败，未重试：将所有下载源重新标记为不允许断点续传的下载源，逐个重新尝试下载
                    //若所有源均不支持 Range，也会走到这里重试
                    if (!isRangeNotSupported) Logger.Warn($"{LocalName}：文件下载失败，正在自动重试……");
                    _retried = true;
                    lock (LockSource)
                    {
                        SourcesOnce.Clear();
                        foreach (var source in Sources)
                        {
                            SourcesOnce.Add(source);
                            source.IsFailed = true;
                        }
                    }
                    Reset();
                    lock (LockState)
                    {
                        State = NetState.WaitingToDownload;
                    }
                } else if (HasAvailableSource() && isMergeFailure)
                {
                    //合并失败且单个源失败：继续下一个源
                    Reset();
                    lock (LockState)
                    {
                        State = NetState.WaitingToDownload;
                    }
                } else
                {
                    //失败
                    Logger.Info($"{LocalName}：已无可用下载源，下载失败");
                    Exception? exampleEx = null;
                    lock (LockSource)
                    {
                        foreach (var source in Sources)
                        {
                            //! 逐字移植豁免（Rider 反转 if）：VB :943 无源失败时的逐源记录结构
                            Logger.Info($"已禁用的下载源：{source.Url}");
                            if (source.Ex != null)
                            {
                                exampleEx = source.Ex;
                                Logger.Warn(source.Ex, "下载源禁用原因");
                            }
                        }
                    }
                    Fail(exampleEx);
                }
            }
        }
        //清理当前已下载的内容
        if (FileSize == -2) Reset();
    }

    private void Reset()
    {
        FileSize = -2;
        _cache?.Dispose();
        _cache = null;
        //! 逐字移植豁免（Rider 锁序环）：VB :959 Reset 的 SyncLock LockChain，与 NetThread.DownloadEnd 同锁重入
        lock (LockChain)
        {
            Threads = null;
        }
        DownloadDoneDelta?.Invoke(-DownloadDone);
        DownloadDone = 0;
        _speedLastDone = 0;
    }

    //最终收束事件

    /// <summary>
    /// 下载完成。合并文件（ModNet.vb:972-1056 逐字；VB 的 GoTo Retry 以等价重试循环承载，Finally 仅在退出时执行一次的语义不变）。
    /// </summary>
    private void Merge(NetThread th)
    {
        //状态判断
        lock (LockState)
        {
            if (State < NetState.Merging)
            {
                State = NetState.Merging;
            } else
            {
                return;
            }
        }
        var retryCount = 0;
        try
        {
            while (true)
            {
                try
                {
                    //创建文件夹
                    DirectoryUtils.Create(PathUtils.RemoveLastPart(LocalPath));
                    FileUtils.Delete(LocalPath);
                    lock (LockChain)
                    {
                        //合并文件
                        if (IsNoSplit)
                        {
                            //仅有一个线程，从内存中输出
                            Logger.Trace(() => $"{LocalName}：下载结束，从内存输出文件");
                            FileUtils.Write(LocalPath, _cache!);
                        } else if (Threads!.DownloadDone == DownloadDone && Threads.Temp != null)
                        {
                            //仅有一个文件，直接复制
                            Logger.Trace(() => $"{LocalName}：下载结束，仅有一个文件，无需合并");
                            FileUtils.Copy(Threads.Temp, LocalPath);
                        } else
                        {
                            //有多个线程，合并
                            Logger.Trace(() => $"{LocalName}：下载结束，开始合并文件");
                            using var mergeFile = FileUtils.CreateAsStream(LocalPath);
                            using var addWriter = new BinaryWriter(mergeFile);
                            foreach (var thread in Threads)
                            {
                                if (thread.DownloadDone == 0 || thread.Temp == null) continue;
                                using var fs = FileUtils.ReadAsStream(thread.Temp);
                                using var tempReader = new BinaryReader(fs);
                                addWriter.Write(tempReader.ReadBytes((int) thread.DownloadDone));
                            }
                        }
                        //检查文件
                        var checkResult = Check?.Check(LocalPath);
                        if (checkResult == null && !IsUnknownSize && Check != null)
                        {
                            if (Check.ActualSize == -1)
                            {
                                checkResult = new FileChecker { ActualSize = FileSize }.Check(LocalPath); //不修改原始的 Checker，以免污染原始实例
                            } else if (Check.ActualSize != FileSize)
                            {
                                checkResult = $"文件大小不一致：任务校验要求为 {Check.ActualSize}，请求结果为 {FileSize}";
                            }
                        }
                        if (checkResult != null)
                        {
                            Logger.Info($"{LocalName} 文件校验失败，下载线程细节：");
                            foreach (var t in Threads!)
                            {
                                Logger.Info($"- {t.Uuid}#，状态 {t.State}，范围 {t.DownloadStart}~{t.DownloadEnd}，已下载 {t.DownloadDone}，未下载 {t.DownloadUndone}");
                            }
                            throw new Exception(checkResult);
                        }
                        //后处理
                        if (!IsNoSplit)
                        {
                            foreach (var thread in Threads!)
                            {
                                if (thread.Temp != null) FileUtils.Delete(thread.Temp);
                            }
                        }
                        Finish();
                    }
                    break;
                } catch (Exception ex)
                {
                    retryCount++;
                    if (State > NetState.Merging) return; //通常是因为在合并时被中断了
                    Logger.Warn(ex, $"合并文件出错，第 {retryCount} 次尝试（{LocalName}）");
                    //重新尝试合并
                    if (retryCount < 3)
                    {
                        Thread.Sleep(500 * retryCount);
                        continue; //GoTo Retry
                    }
                    //失败，禁用当前下载源并重启下载
                    try
                    {
                        FileUtils.Delete(LocalPath);
                    } catch (Exception)
                    {
                        Logger.Warn(ex, $"删除合并失败的文件出错（{LocalPath}）"); //VB 的 exx 亦未消费，直接记录外层 ex
                    }
                    SourceFail(th, ex, true);
                    break;
                }
            }
        } finally
        {
            _cache?.Dispose();
        }
    }

    /// <summary>
    /// 下载失败（ModNet.vb:1060-1072 逐字）。
    /// </summary>
    private void Fail(Exception? raiseEx = null)
    {
        lock (LockState)
        {
            if (State >= NetState.Finished) return;
            if (raiseEx != null) Ex.Add(raiseEx);
            //凉凉
            Logger.Info($"{LocalName}：已失败，当前状态 {State}");
            State = NetState.Canceled;
        }
        CancelInternal();
        foreach (var task in Tasks)
        {
            task.OnFileFail(this);
        }
    }

    /// <summary>
    /// 取消下载（ModNet.vb:1076-1087 逐字；宿主 LoaderDownload 以 File.Cancel(Me) 调用）。
    /// </summary>
    public void Cancel(INetTaskHost canceledTask)
    {
        //从特定任务中移除，如果它还属于其他任务，则继续下载
        Tasks.Remove(canceledTask);
        if (Tasks.Any()) return;
        //确认中断
        lock (LockState)
        {
            if (State >= NetState.Finished) return;
            Logger.Info($"{LocalName}：已中断，当前状态 {State}");
            State = NetState.Canceled;
        }
        CancelInternal();
    }

    private void CancelInternal()
    {
        //VB On Error Resume Next：各语句独立吞错后继续
        try
        {
            Reset();
        } catch
        {
            //吞错继续
        }
        try
        {
            FileRemainDelta?.Invoke(-1);
        } catch
        {
            //吞错继续
        }
    }

    //状态改变接口

    /// <summary>
    /// 将该文件设置为已下载完成（ModNet.vb:1098-1108 逐字）。
    /// </summary>
    public void Finish(bool printLog = true)
    {
        lock (LockState)
        {
            if (State >= NetState.Finished) return;
            State = NetState.Finished;
        }
        FileRemainDelta?.Invoke(-1);
        if (printLog) Logger.Info($"{LocalName}：已完成");
        foreach (var task in Tasks)
        {
            task.OnFileFinish(this);
        }
    }

    #region 过渡自驱调度（Task 3 由 NetManager.Register 接管后移除）

    /// <summary>
    /// 开始下载（过渡语义，ModNet.vb 现有入队路径的等价承载）：执行宿主层 LoaderDownload.Start 的输入校验对应段（:1218-1236），
    /// 随后以自驱线程承担 NetManager ThreadStarter 的逐文件调度（:1529-1571）。Task 3 将改为 NetManager 注册制。
    /// </summary>
    public void Start()
    {
        //输入检测（对照 LoaderDownload.Start :1223-1236）
        foreach (var source in Sources)
        {
            //! 逐字移植豁免（Rider 反转 if）：宿主层 LoaderDownload.Start :1226 的校验结构镜像
            if (!source.Url.StartsWithF("https://", true) && !source.Url.StartsWithF("http://", true))
            {
                source.Ex = new ArgumentException("输入的下载链接不正确：" + source.Url);
                source.IsFailed = true;
            }
        }
        if (!HasAvailableSource()) throw new ArgumentException("输入的下载链接不正确！");
        LocalPath = LocalPath.Replace("/", "\\");
        if (!LocalPath.Contains(":\\")) throw new ArgumentException("输入的本地文件地址不正确：" + LocalPath);
        if (LocalPath.EndsWithF("\\")) throw new ArgumentException("请输入含文件名的完整文件路径：" + LocalPath);
        DirectoryUtils.Create(PathUtils.RemoveLastPart(LocalPath)); //创建目标文件夹
        lock (LockState)
        {
            State = NetState.WaitingToDownload;
            IsCopy = false;
        }
        //? 过渡自驱：无宿主任务（测试/直用）时注册空宿主以满足 TryBeginThread 的任务在册检查（:679“由于中断，已没有可用任务”）；
        //? Task 3 由 NetManager.Register 接管后随本段一并移除
        Tasks.Add(new SelfTaskHost());
        //自驱调度线程（裸 Thread + BelowNormal，对应 VB ThreadStarter 的裸线程模型）
        var driver = new Thread(DriveLoop) { Name = $"下载 自驱调度 {Uuid}#", Priority = ThreadPriority.BelowNormal, IsBackground = true };
        driver.Start();
    }

    /// <summary>过渡自驱用的空宿主：无宿主任务时占用 Tasks 槽位，回调无副作用（Task 3 移除）。</summary>
    private sealed class SelfTaskHost : INetTaskHost
    {
        public int Uuid { get; } = GetUuid();
        public int FailCount { get; set; }
        public void OnFileFail(NetFile file) { }
        public void OnFileFinish(NetFile file) { }
    }

    /// <summary>NetManagerClass.ThreadStarter（:1529-1571）的逐文件等价自驱循环，终态（Finished/Canceled）退出。</summary>
    private void DriveLoop()
    {
        try
        {
            while (State < NetState.Finished)
            {
                Thread.Sleep(20);
                if (State == NetState.WaitingToDownload)
                {
                    //为等待中的文件开始线程
                    if (NetTaskThreadCount >= NetTaskThreadLimit) continue; //最大线程数检查
                    var newThread = TryBeginThread();
                    if (newThread != null && newThread.Source.Url.Contains("bmclapi")) Thread.Sleep(100); //减少 BMCLAPI 请求频率
                } else if (State < NetState.Merging)
                {
                    //为进行中的文件追加线程
                    if (Speed >= NetTaskSpeedLimitLow) continue; //下载速度足够，无需新增（过渡语义以本文件速度替代全局 NetManager.Speed）
                    if (NetTaskThreadCount >= NetTaskThreadLimit) continue; //最大线程数检查
                    //线程种类计数
                    var preparingCount = 0;
                    var downloadingCount = 0;
                    if (Threads != null)
                    {
                        foreach (var thread in Threads.ToList())
                        {
                            if (thread.State < NetState.Downloading)
                            {
                                preparingCount++;
                            } else if (thread.State == NetState.Downloading)
                            {
                                downloadingCount++;
                            }
                        }
                    }
                    //新增线程
                    if (preparingCount > downloadingCount) continue; //准备中的线程已多于下载中的线程，不再新增
                    var newThread = TryBeginThread();
                    if (newThread != null && newThread.Source.Url.Contains("bmclapi")) Thread.Sleep(100); //减少 BMCLAPI 请求频率
                }
            }
        } catch (Exception ex)
        {
            //VB ThreadStarter 为 AlertThenCrash；过渡自驱只影响单文件，降级为记录（Task 3 归还宿主调度后恢复原行为）
            Logger.Error(ex, $"下载自驱调度出错（{LocalName}）");
        }
    }

    #endregion

    #region 基础设施（随引擎迁入的宿主层等价实现）

    //ModNet.vb:130-132 ThreadClient 随引擎迁入：下载线程共用的 HttpClient
    private static readonly HttpClient ThreadClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
    });

    /// <summary>获取一个单调递增时间值（毫秒）（ModBase.vb:834 GetTimeMs 的等价实现）。</summary>
    internal static long GetTimeMs() => Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000L);

    private static readonly object RandomLock = new();
    private static readonly Random Random = new();

    /// <summary>获取随机整数，两端均含（ModBase.vb RandomInteger 的等价实现；以锁保证 net48 下线程安全）。</summary>
    private static int RandomInteger(int min, int max)
    {
        lock (RandomLock)
        {
            return Random.Next(min, max + 1);
        }
    }

    private static bool IsProgramEnding => IsProgramEndingProvider?.Invoke() ?? false;

    private static string PathTemp => PathTempProvider?.Invoke() ?? Path.GetTempPath();

    /// <summary>为 Task 设置超时，在超时时抛出 TimeoutException（ModBase.vb:764 扩展的私有等价实现，与 NetRequest.cs 同版；NetFile 非静态类故以方法承载）。</summary>
    private static T GetResultWithTimeout<T>(Task<T> targetTask, CancellationTokenSource tokenSource, int timeoutMs)
    {
        var delayTask = Task.Delay(timeoutMs);
        if (Task.WhenAny(targetTask, delayTask).ConfigureAwait(false).GetAwaiter().GetResult() != delayTask)
            return targetTask.GetAwaiter().GetResult(); //VB 的 TargetTask.Run()
        tokenSource.Cancel();
        throw new TimeoutException($"任务超时（{timeoutMs} ms）");
    }

    /// <summary>下载源不支持分段下载（ModNet.vb:1111-1116 随引擎迁入）。</summary>
    private sealed class RangeNotSupportedException : HttpRequestException
    {
        public RangeNotSupportedException(string message) : base(message) { }
    }

    #endregion
}