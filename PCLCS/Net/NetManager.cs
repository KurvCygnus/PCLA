namespace PCLCS.Net;

/// <summary>
/// 下载文件管理（ModNet.vb:1635-1837 逐字移植）：AllFiles 按 LocalPath 跨任务去重、三条永不退出的守护线程
/// （线程启动 ×2 + 状态刷新）、Interlocked 统计与下载缓存清理。
/// 宿主任务（VB 侧 LoaderDownload）留守 VB，经 INetTaskHost 桥接（与 NetFile 同模式）。
/// </summary>
public sealed class NetManager
{
    /// <summary>替代 VB 公共字段 ModNet.NetManager 的单例（VB 侧同名字段改为 ReadOnly Property 转发至此）。</summary>
    public static NetManager Instance { get; } = new();

    #region 属性（ModNet.vb:1639-1663）

    /// <summary>需要下载的文件。为“本地地址 - 文件对象”键值对。</summary>
    public ConcurrentDictionary<string, NetFile> AllFiles { get; } = new();

    /// <summary>当前的所有下载任务（VB 为 ConcurrentBag(Of LoaderDownload)；元素以 object 承载，
    /// 保有 VB 侧 LoaderDownload 实例，晚期绑定消费面（PageLaunchLeft）不受影响）。</summary>
    public ConcurrentBag<object> Tasks { get; } = new();

    private long _downloadDone;

    /// <summary>已下载完成的大小（Interlocked 计数）。</summary>
    public long DownloadDone => Interlocked.Read(ref _downloadDone);

    private int _fileRemain;

    /// <summary>尚未完成下载的文件数（Interlocked 计数）。</summary>
    public int FileRemain => _fileRemain;

    //这些属性由 RefreshStat 刷新
    /// <summary>当前的全局下载速度，单位为 Byte / 秒。</summary>
    public long Speed { get; private set; }

    /// <summary>管理器标识（VB 取 ModBase.GetUuid；此处无 VB 全局编号源，以本地计数兜底，无消费方依赖此值）。</summary>
    public int Uuid { get; } = Interlocked.Increment(ref uuidCounter);
    private static int uuidCounter;

    #endregion

    /// <summary>Interlocked.Add(DownloadDone, Δ)（ModNet.vb:821 记数与 :959 Reset 清零经此入账；原 NetFile.DownloadDoneDelta 接线随管理器迁入而收直）。</summary>
    internal void AddDownloadDone(long delta) => Interlocked.Add(ref _downloadDone, delta);

    /// <summary>Interlocked.Add(FileRemain, Δ)（ModNet.vb:1825 登记时 +1、Finish/Cancel 中断时 -1 经此入账）。</summary>
    internal void AddFileRemain(int delta) => Interlocked.Add(ref _fileRemain, delta);

    //宿主层接线（ModNet.Sub New 赋值，与 NetRequest.CdnSigner 同模式）；null 时为测试直用语义

    /// <summary>下载缓存根目录接线（VB 接 ModBase.PathTemp；null 时回退 %TEMP%，测试注入临时目录）。</summary>
    public static Func<string>? TempFolderProvider { get; set; }

    /// <summary>守护线程异常上报接线（VB 接 LogBehavior.AlertThenCrash 路径；null 时吞错，仅测试直用语义）。</summary>
    public static Action<Exception>? CrashAlert { get; set; }

    private static string TempFolder => TempFolderProvider?.Invoke() ?? Path.GetTempPath();

    /// <summary>
    /// 进度与下载速度由下载管理线程每隔约 0.1 秒刷新一次（ModNet.vb:1669-1707 逐字）。
    /// </summary>
    private void RefreshStat()
    {
        try
        {
            var deltaTime = NetFile.GetTimeMs() - _refreshStatLast;
            if (deltaTime == 0) return;
            _refreshStatLast += deltaTime;
            #region 刷新整体速度
            //计算瞬时速度
            var actualSpeed = Math.Max(0, (DownloadDone - _speedLastDone) / (deltaTime / 1000.0));
            _speedLast.Insert(0, Convert.ToInt64(actualSpeed)); //VB List(Of Long).Insert 的隐式窄化（CLng 语义）
            if (_speedLast.Count >= 31) _speedLast.RemoveAt(30);
            _speedLastDone = DownloadDone;
            //计算用于显示的速度
            var speedSum = 0L;
            var speedDiv = 0L;
            var weight = (long) _speedLast.Count;
            foreach (var speedRecord in _speedLast)
            {
                speedSum += speedRecord * weight;
                speedDiv += weight;
                weight -= 1;
            }
            Speed = speedDiv > 0 ? Convert.ToInt64((double) speedSum / speedDiv) : 0; //VB Long/Long→Double 再窄化回 Long（CLng 语义）
            //计算新的速度下限
            var limit = 0L;
            if (_speedLast.Count >= 10) limit = Convert.ToInt64(_speedLast.Take(10).Average() * 0.85); //取近 1 秒的平均速度的 85%
            if (limit > NetSpeed.SpeedLimitLow)
            {
                NetSpeed.SpeedLimitLow = limit;
                Logger.Info($"速度下限已提升到 {StringUtils.FormatByteSize(limit)}");
            }
            #endregion
            #region 刷新下载任务属性
            foreach (var task in Tasks)
            {
                (task as INetTaskHost)?.RefreshStat(); //在册任务均实现 INetTaskHost（VB LoaderDownload / 占位宿主）
            }
            #endregion
        } catch (Exception ex)
        {
            Logger.Warn(ex, "刷新下载公开属性失败");
        }
    }
    private long _refreshStatLast;
    private readonly List<long> _speedLast = []; //记录至多最近 30 次下载速度的记录，较新的在前面（VB Static 随单例提升为实例字段）
    private long _speedLastDone; //上次记速时的已下载大小

    /// <summary>
    /// 启动监控线程，用于新增下载线程（ModNet.vb:1709-1776）。
    /// </summary>
    private void StartManager()
    {
        if (Interlocked.Exchange(ref _isStarted, 1) != 0) return; //VB Static IsStarted
        //! 逐字移植豁免（任务规约）：VB 经 RunInNewThread 建前台线程（默认优先级 Normal）；守护线程永不退出，
        //! 前台线程会令测试进程无法退出，故按任务要求置 IsBackground，优先级与名称保持一致
        var threadStarter0 = new Thread(() => ThreadStarter(0)) { Name = "NetManager ThreadStarter 0", IsBackground = true };
        var threadStarter1 = new Thread(() => ThreadStarter(1)) { Name = "NetManager ThreadStarter 1", IsBackground = true };
        var statRefresher = new Thread(StatRefresherLoop) { Name = "NetManager StatRefresher", IsBackground = true };
        threadStarter0.Start();
        threadStarter1.Start();
        statRefresher.Start();
    }
    private int _isStarted;

    /// <summary>新增下载线程的守护循环（ModNet.vb:1719-1752 逐字；Id 0/1 按文件 Uuid 奇偶分治）。</summary>
    private void ThreadStarter(int id)
    {
        try
        {
            while (true)
            {
                Thread.Sleep(20);
                //获取文件列表
                var waitingFiles = new List<NetFile>();
                var ongoingFiles = new List<NetFile>();
                if (id == 0 && FileRemain == 0) AllFiles.Clear(); //若已完成，则清空
                foreach (var file in AllFiles.Values)
                {
                    if (file.Uuid % 2 == id) continue;
                    if (file.State == NetState.WaitingToDownload)
                    {
                        waitingFiles.Add(file);
                    } else if (file.State < NetState.Merging && file.State >= NetState.WaitingToDownload)
                    {
                        ongoingFiles.Add(file);
                    }
                }
                //为等待中的文件开始线程
                foreach (var file in waitingFiles)
                {
                    //VB 的 Continue While：中止本列表的遍历并跳过“追加线程”段，直接进入下一轮
                    if (NetSpeed.ThreadCount >= NetSpeed.ThreadLimit) goto ContinueWhile; //最大线程数检查
                    var newThread = file.TryBeginThread();
                    if (newThread != null && newThread.Source.Url.Contains("bmclapi")) Thread.Sleep(100); //减少 BMCLAPI 请求频率
                }
                //为进行中的文件追加线程
                if (Speed >= NetSpeed.SpeedLimitLow) continue; //下载速度足够，无需新增
                foreach (var file in ongoingFiles)
                {
                    //VB 的 Continue While：中止本列表的遍历并直接进入下一轮
                    if (NetSpeed.ThreadCount >= NetSpeed.ThreadLimit) goto ContinueWhile; //最大线程数检查
                    //线程种类计数
                    var preparingCount = 0;
                    var downloadingCount = 0;
                    if (file.Threads != null)
                    {
                        foreach (var thread in file.Threads.ToList())
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
                    var newThread = file.TryBeginThread();
                    if (newThread != null && newThread.Source.Url.Contains("bmclapi")) Thread.Sleep(100); //减少 BMCLAPI 请求频率
                }
ContinueWhile:;
            }
        } catch (Exception ex)
        {
            //VB：Logger.Error(ex, $"下载管理启动线程 {Id} 出错", LogBehavior.AlertThenCrash)；线程标识随包装异常一并上报
            CrashAlert?.Invoke(new Exception($"下载管理启动线程 {id} 出错", ex));
        }
    }

    /// <summary>限速余量与公开属性的刷新守护循环（ModNet.vb:1755-1776 逐字）。</summary>
    private void StatRefresherLoop()
    {
        try
        {
            var nextTick = NetFile.GetTimeMs();
            while (true)
            {
                //增加限速余量
                NetSpeed.RefillSpeedLimit(); //原 Interlocked.Add(NetTaskSpeedLimitLeft, CLng(NetTaskSpeedLimitHigh / 10))（状态在 NetSpeed）
                //刷新公开属性
                RefreshStat();
                //等待 100 ms
                nextTick += 100;
                var sleepTime = nextTick - NetFile.GetTimeMs();
                if (sleepTime > 0)
                {
                    Thread.Sleep((int) sleepTime); //VB Option Strict Off 的 Long→Integer 隐式窄化；实际取值恒为 100ms 内的整数值
                } else
                {
                    nextTick = NetFile.GetTimeMs(); //超时，直接追帧，不等待
                }
            }
        } catch (Exception ex)
        {
            //VB：Logger.Error(ex, "下载管理刷新线程出错", LogBehavior.AlertThenCrash)
            CrashAlert?.Invoke(new Exception("下载管理刷新线程出错", ex));
        }
    }

    private readonly object _downloadCacheLock = new();
    private bool _isDownloadCacheCleared;

    /// <summary>
    /// 首次启动：拉起守护线程并清理下载缓存（ModNet.vb:1783-1802，原为 Start(Task) 的前两段，注册制下单列以便测试注入）。
    /// </summary>
    public void Start()
    {
        StartManager();
        //清理缓存
        lock (_downloadCacheLock) //防止同时开启多个下载任务时重复清理
        {
            if (_isDownloadCacheCleared) return;
            try
            {
                Logger.Info("开始清理下载缓存");
                DirectoryUtils.Delete(TempFolder + "Download");
                Logger.Info("下载缓存已清理");
            } catch (Exception ex)
            {
                Logger.Warn(ex, "清理下载缓存失败");
            }
            _isDownloadCacheCleared = true;
        }
    }

    /// <summary>
    /// 开始一个下载任务（ModNet.vb:1782-1829 逐字；宿主任务经 INetTaskHost 桥接，VB 侧调用点 NetManager.Start(Me) 不变）。
    /// </summary>
    public List<NetFile> Start(INetTaskHost task)
    {
        Start();
        //添加每个文件
        var newFiles = new List<NetFile>();
        var newFilesList = new List<NetFile>();
        foreach (var file in task.Files)
        {
            var effectiveFile = file;
            if (AllFiles.TryGetValue(file.LocalPath, out var ongoingFile) && ongoingFile.State < NetState.Finished)
            {
                //该文件正在被另一个任务下载
                Logger.Trace(() => $"{file.LocalName}：无需开始，该文件已在下载中，目标 {file.LocalPath}");
                effectiveFile = ongoingFile; //将列表中的文件替换成下载中的文件，即两个任务指向同一个文件；抛弃现在的 File 对象
            } else
            {
                //常规情况（该文件可能已在其他任务中下载完成，但这并不影响）
                Logger.Trace(() => $"{file.LocalName}：开始，目标 {file.LocalPath}");
                AllFiles[file.LocalPath] = file;
                newFiles.Add(file);
                Interlocked.Increment(ref _fileRemain);
            }
            newFilesList.Add(effectiveFile);
            effectiveFile.Tasks.Add(task);
        }
        task.Files = new ConcurrentQueue<NetFile>(newFilesList);
        Tasks.Add(task);
        return newFiles;
    }

    /// <summary>
    /// 登记一个下载文件（供 NetFile.Start 的无宿主直用/测试路径调用；宿主任务走 Start(INetTaskHost)）。
    /// 去重与计数语义同 Start 的逐文件段（ModNet.vb:1817-1828）；VB 无此入口，为注册制调度新增的承载面。
    /// </summary>
    public void Register(NetFile file)
    {
        StartManager(); //注册即需调度线程（VB 侧由 Start(Task) 触发；此路径无任务壳）
        if (AllFiles.TryGetValue(file.LocalPath, out var ongoingFile) && ongoingFile.State < NetState.Finished) return; //该文件正在被另一个任务下载：保留首个，调用方的文件对象被抛弃
        //常规情况（该文件可能已在其他任务中下载完成，但这并不影响）
        AllFiles[file.LocalPath] = file;
        Interlocked.Increment(ref _fileRemain);
        if (file.Tasks.Count == 0) file.Tasks.Add(new OrphanTaskHost()); //无宿主文件占用任务槽位，满足 TryBeginThread 的任务在册检查（ModNet.vb:679）；原 NetFile.SelfTaskHost 的职责随注册制移交于此
    }

    /// <summary>
    /// 无宿主任务的占位宿主：占用 Tasks 槽位以满足 TryBeginThread 的任务在册检查（ModNet.vb:679“由于中断，已没有可用任务”），
    /// 回调无副作用。原 NetFile 过渡段的 SelfTaskHost 随自驱调度删除，职责由注册制下的管理器承接。
    /// </summary>
    private sealed class OrphanTaskHost : INetTaskHost
    {
        public int Uuid { get; } = Interlocked.Increment(ref uuidCounter);

        public ConcurrentQueue<NetFile> Files { get; set; } = new();

        public int FailCount { get; set; }

        public void OnFileFail(NetFile file) { }

        public void OnFileFinish(NetFile file) { }

        public void RefreshStat() { }
    }
}