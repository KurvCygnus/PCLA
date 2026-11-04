using PCLCS.Settings;
using System.ComponentModel;
using System.Diagnostics;

namespace PCLCS.Watch;

/// <summary>
/// 看门狗宿主面：原构造参数 <c>Loader As LoaderTask(Of Process, Integer)</c> / <c>Instance As McInstance</c>
/// 的读取收敛（INetTaskHost 先例，W2 NetFile）。加载器承载游戏进程与进度/取消（ModWatcher.vb:66-70/:96/:253），
/// 实例承载版本发布时间（旧 :171 退出码崩溃判定的 2012+）。VB 侧以 Implements 接入（ModWatcher.WatcherHost）。
/// </summary>
public interface IWatcherHost
{
    /// <summary>游戏进程（原 Loader.Input；即 GameProcess 与 PID 来源，旧 :70/:85）。</summary>
    Process GameProcess { get; }
    /// <summary>启动进度（原 Loader.Progress；ProgressUpdate 写入 0~0.95，旧 :253）。</summary>
    double Progress { get; set; }
    /// <summary>启动任务是否已取消（原 Loader.State = LoadState.Canceled；监控循环退出条件之一，旧 :96）。</summary>
    bool IsCanceled { get; }
    /// <summary>实例版本发布时间（原 Instance.ReleaseTime；退出码崩溃判定的 ReleaseTime.Year ≥2012，旧 :171）。</summary>
    DateTime ReleaseTime { get; }
}

/// <summary>
/// R3 日志看门狗核（自宿主层 ModWatcher.vb:58-392 移植）：进程日志监控（阶段进度/崩溃关键字/退出码判定）、
/// 游戏窗口枚举与最大化、窗口标题节拍、崩溃分析与强制关闭。
/// 宿主层副作用经静态回调接线（ModNet.Sub New 同模式；null 时为测试直用语义）：
/// <see cref="StateChanged"/>（→ MinecraftStart/MinecraftStop，VB 实现两分支）、<see cref="LaunchLog"/>（→ McLaunchLog，脱敏留 VB）、
/// <see cref="CrashHint"/>（→ Hint，文案留 VB）、<see cref="Feedback"/>（→ FeedbackInfo）；
/// 崩溃分析（CrashAnalyzer，W4 域）经构造参数 crashAnalyze 的委托目标留 VB。
/// </summary>
public sealed class Watcher
{

    //宿主层接线（ModNet.Sub New 同模式；null 时为测试直用语义）
    /// <summary>状态汇总翻转通知（原 WatcherStateChanged :21-24 的 MinecraftStart/MinecraftStop 两分支；参数 IsRunning/TriggerLauncherShutdown）。</summary>
    public static Action<bool, bool>? StateChanged;
    /// <summary>监视日志（原 WatcherLog → McLaunchLog；PID 前缀在核内拼装，脱敏留 VB，旧 :239）。</summary>
    public static Action<string, LogLevel>? LaunchLog;
    /// <summary>崩溃提示（原 Hint("检测到 Minecraft 出现错误，错误分析已开始……", HintType.Red)；文案留 VB，旧 :361）。</summary>
    public static Action? CrashHint;
    /// <summary>崩溃反馈上报（原 FeedbackInfo，旧 :362）。</summary>
    public static Action? Feedback;
    /// <summary>窗口枚举注入缝（原 :312-345 EnumWindows 逐窗读取；测试注入假句柄集，null 时走真实 Win32 枚举）。</summary>
    public static Func<IReadOnlyList<WatcherWindow>?>? WindowProbe;

    /// <summary>单个候选窗口的预读数据（原 :315-340 EnumWindows 回调内的逐窗 Win32 读取收敛；测试可构造假句柄集）。</summary>
    public sealed class WatcherWindow
    {
        /// <summary>窗口句柄。</summary>
        public IntPtr Handle;
        /// <summary>窗口类名（原 GetClassName）。</summary>
        public string ClassName = "";
        /// <summary>窗口是否可见（原 IsWindowVisible；SDL 隐藏窗反选依据）。</summary>
        public bool IsVisible;
        /// <summary>窗口标题（原 GetWindowText）。</summary>
        public string Title = "";
        /// <summary>窗口关联进程的启动时间（原 Process.GetProcessById(ProcessId).StartTime，旧 :336）；null 表示进程读取失败（原异常路径 → 跳过该窗）。</summary>
        public DateTime? ProcessStartTime;
    }

    //全局态（ModWatcher.vb:4-6 移植；裁定 5：C# 持有，VB 以薄转发属性保留公共名）
    /// <summary>全部看门狗（原 Public McWatcherList；构造时剔除终态项并追加自身，旧 :74-81）。</summary>
    public static List<Watcher> McWatcherList = [];
    /// <summary>是否有运行中的 Minecraft（原 Public HasRunningMinecraft；VB MinecraftStart/Stop 经转发属性写入）。</summary>
    public static bool HasRunningMinecraft { get; set; }
    /// <summary>状态汇总翻转守卫（原 Private isWatcherRunning，旧 :5）。</summary>
    private static bool isWatcherRunning;

    /// <summary>监视状态（ModWatcher.vb:130-136 顺序逐字）。</summary>
    public enum MinecraftState
    {
        Loading,
        Running,
        Crashed,
        Ended,
        Canceled
    }

    /// <summary>游戏进程（原 Public GameProcess，旧 :61/:85）。</summary>
    public readonly Process GameProcess;
    /// <summary>宿主读取面（原 Public Loader/Instance，旧 :62/:65——两字段的全部读取收敛于此）。</summary>
    public readonly IWatcherHost Host;
    private readonly string _windowTitle = "";
    private readonly int _pid;
    /// <summary>崩溃分析委托目标（原 :368-373 的 CrashAnalyzer 编排；W4 域，VB 提供）。</summary>
    private readonly Action<Watcher>? _crashAnalyze;

    /// <summary>
    /// 构造（ModWatcher.vb:66-116 逐行）：收敛日志、列表重建与汇总、进程日志读取接线、
    /// 监控线程（64ms×3=192ms 窗口标题节拍；原 RunInNewThread → 裸 Thread，包装 Catch 为死代码——内层 Try 覆盖全段）。
    /// </summary>
    public Watcher(IWatcherHost host, string windowTitle, Action<Watcher>? crashAnalyze = null)
    {
        Host = host;
        _windowTitle = windowTitle;
        _crashAnalyze = crashAnalyze;
        _pid = host.GameProcess.Id;
        WatcherLog("开始 Minecraft 日志监控");
        if (_windowTitle != "") WatcherLog("要求窗口标题：" + _windowTitle);

        //更改列表
        var newWatcherList = new List<Watcher>();
        foreach (var watch in McWatcherList)
        {
            if (watch.State == MinecraftState.Crashed || watch.State == MinecraftState.Ended || watch.State == MinecraftState.Canceled) continue;
            newWatcherList.Add(watch);
        }
        newWatcherList.Add(this);
        McWatcherList = newWatcherList;
        OnWatcherStateChanged();

        //初始化进程与日志读取
        GameProcess = host.GameProcess;
        GameProcess.BeginOutputReadLine();
        GameProcess.BeginErrorReadLine();
        GameProcess.OutputDataReceived += LogReceived;
        GameProcess.ErrorDataReceived += LogReceived;

        //初始化时钟
        var monitorThread = new Thread(MonitorLoop) { Name = "Minecraft Watcher PID " + _pid };
        monitorThread.Start();
    }

    /// <summary>
    /// 监控主循环（ModWatcher.vb:92-115 逐行）：窗口检查、日志冲刷与退出码判定、
    /// 64ms×3 的窗口标题节拍（{date}/{time} 替换）；Crashed 收尾再汇总一次，退出前记录日志。
    /// </summary>
    private void MonitorLoop()
    {
        try
        {
            while (!(State == MinecraftState.Ended || State == MinecraftState.Canceled ||
                     (Host.IsCanceled && State != MinecraftState.Crashed) ||
                     (State == MinecraftState.Crashed && GameProcess.HasExited)))
            {
                TimerWindow();
                TimerLog();
                //设置窗口标题
                for (var i = 1; i <= 3; i++)
                {
                    if (IsWindowFinished && IsWindowAppeared && _windowTitle != "" && State == MinecraftState.Running && !GameProcess.HasExited)
                    {
                        var realTitle = _windowTitle.Replace("{date}", DateTime.Now.ToString("yyyy'/'M'/'d")).Replace("{time}", DateTime.Now.ToString("HH':'mm':'ss"));
                        //! 逐字移植豁免：VB 以 RealTitle.ToCharArray 隐式转回 String 传给 Ansi Declare（净效果即 RealTitle），C# 直接传串
                        SetWindowText(_windowHandle, realTitle);
                    }
                    Thread.Sleep(64);
                }
            }
            if (State == MinecraftState.Crashed) OnWatcherStateChanged();
            WatcherLog("Minecraft 日志监控已退出");
        } catch (Exception ex)
        {
            Logger.Error(ex, "Minecraft 日志监控主循环出错");
            State = MinecraftState.Ended;
        }
    }

    //状态

    private MinecraftState _state = MinecraftState.Loading;
    /// <summary>监视状态（ModWatcher.vb:119-129：Setter 触发全局汇总）。</summary>
    public MinecraftState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnWatcherStateChanged();
        }
    }

    /// <summary>
    /// 状态汇总（ModWatcher.vb:7-26 移植）：遍历全局集合得出运行态与是否触发启动器关闭，
    /// 翻转时经 <see cref="StateChanged"/> 通知宿主层（MinecraftStart/MinecraftStop 留 VB）。
    /// </summary>
    private static void OnWatcherStateChanged()
    {
        var isRunning = false;
        var triggerLauncherShutdown = true;
        foreach (var watcher in McWatcherList)
        {
            if (watcher.State == MinecraftState.Loading || watcher.State == MinecraftState.Running ||
                (watcher.State == MinecraftState.Crashed && !watcher.GameProcess.HasExited))
            {
                isRunning = true;
                break;
            } else if (watcher.State == MinecraftState.Crashed || watcher.State == MinecraftState.Canceled)
            {
                triggerLauncherShutdown = false;
            }
        }
        if (isWatcherRunning == isRunning) return;
        isWatcherRunning = isRunning;
        StateChanged?.Invoke(isRunning, triggerLauncherShutdown);
    }

    //日志

    private List<string?> _waitingLog = new(1000);
    private readonly object _waitingLogLock = new();

    /// <summary>进程输出行（ModWatcher.vb:141-145）：加锁入缓冲（e.Data 可为 null，交由 GameLog 判空）。</summary>
    private void LogReceived(object? sender, DataReceivedEventArgs e)
    {
        lock (_waitingLogLock)
        {
            _waitingLog.Add(e.Data);
        }
    }

    /// <summary>
    /// 日志冲刷与退出检查（ModWatcher.vb:146-183）：缓冲整体换出后逐行进 GameLog；
    /// 退出码判定（窗口未出现 / 返回值不为 0 且 Running 且 ReleaseTime ≥2012 → Crashed，否则 Ended）。
    /// </summary>
    internal void TimerLog() //? 可见性放宽为 internal（测试直驱面；VB 为 Private）
    {
        try
        {
            //输出文本
            List<string?> copyed;
            lock (_waitingLogLock)
            {
                if (_waitingLog.Count == 0) return;
                copyed = _waitingLog;
                _waitingLog = new List<string?>(1000);
            }
            foreach (var str in copyed)
            {
                GameLog(str);
            }
            if (State == MinecraftState.Loading) ProgressUpdate();
            //游戏退出检查
            //! 逐字移植豁免：Rider 建议反转 if 减少嵌套，保持旧 :160 If GameProcess.HasExited Then 的块形状（其后无代码，反转与否行为一致）
            if (GameProcess.HasExited)
            {
                WatcherLog("Minecraft 已退出，返回值：" + GameProcess.ExitCode);
                //If Process.ExitCode = 1 Then
                //    '返回值为 1，考虑是任务管理器结束
                //    WatcherLog("Minecraft 返回值为 1，考虑为任务管理器结束") '并不，崩了照样是 1
                //    State = MinecraftState.Ended
                //Else
                if (State == MinecraftState.Loading)
                {
                    //窗口未出现
                    WatcherLog("Minecraft 尚未加载完成，可能已崩溃", LogLevel.Error);
                    Crashed();
                } else if (GameProcess.ExitCode != 0 && State == MinecraftState.Running && Host.ReleaseTime.Year >= 2012)
                {
                    //返回值不为 0 且未结束
                    WatcherLog("Minecraft 返回值异常，可能已崩溃", LogLevel.Error);
                    Crashed();
                } else if (State != MinecraftState.Crashed)
                {
                    //正常关闭
                    State = MinecraftState.Ended;
                }
            }
        } catch (Exception ex)
        {
            Logger.Error(ex, "输出 Minecraft 日志失败");
        }
    }

    /// <summary>最近日志（原 Public LatestLog，旧 :184；上限 501 即出队一条，VB 崩溃分析委托消费）。</summary>
    public readonly ConcurrentQueue<string> LatestLog = new();

    /// <summary>
    /// 单行日志处理（ModWatcher.vb:185-237）：换行归一 → LatestLog 预存储（≥501 出队一条）→ Trace →
    /// 阶段进度 1/5~5/5 → 关闭与崩溃关键字检测（[CHAT] 行豁免）。
    /// </summary>
    internal void GameLog(string? text) //? 可见性放宽为 internal 且允许 null（测试语料直驱面；e.Data 可为 null，VB 为 Private）
    {
        //预处理
        if (text is null) return;
        text = text.ReplaceLineEndings("\r\n"); //? net48 走 MeloongCore 扩展（含 \f），与 VB 同源；net8 走 BCL（仅 \f 差异，仓库既有口径）
        //加入预存储
        LatestLog.Enqueue(text);
        if (LatestLog.Count >= 501) LatestLog.TryDequeue(out _);
        Logger.Trace(text);
        //进度处理
        if (_logProgress < 1)
        {
            WatcherLog("日志 1/5：已出现日志输出");
            _logProgress = 1;
        } //可能第一句就是后面需要判断的 Log（重现：启动 1.15.2 原版）
        if (_logProgress < 2 && text.Contains("Setting user:"))
        {
            WatcherLog("日志 2/5：游戏用户已设置"); //仅确保支持 Minecraft 1.7+
            _logProgress = 2;
        } else if (_logProgress < 3 && text.ContainsIgnoreCase("lwjgl version"))
        {
            WatcherLog("日志 3/5：LWJGL 版本已确认");
            _logProgress = 3;
        } else if (_logProgress < 4 && (text.Contains("OpenAL initialized") || text.Contains("Starting up SoundSystem")))
        {
            WatcherLog("日志 4/5：OpenAL 已加载"); //仅确保支持 Minecraft 1.7+
            _logProgress = 4;
        } else if (_logProgress < 5 && ((text.Contains("Created") && text.Contains("textures") && text.Contains("-atlas")) || text.Contains("Found animation info")))
        {
            WatcherLog("日志 5/5：材质已加载"); //仅确保支持 Minecraft 1.7+
            _logProgress = 5;
        }
        //输出日志
        //Log(Text)
        //关闭与崩溃检测
        //! 逐字移植豁免：Rider 建议反转 if 减少嵌套，保持旧 :215 If Not Text.Contains("[CHAT]") Then 的块形状（其后无代码，行为一致）
        if (!text.Contains("[CHAT]"))
        {
            if (text.Contains("Someone is closing me!") || text.Contains("Restarting Minecraft with command"))
            {
                //#1258
                WatcherLog("识别为关闭的 Log：" + text);
                State = MinecraftState.Ended;
            } else if (text.Contains("Crash report saved to") || text.Contains("This crash report has been saved to:"))
            {
                // Text.Contains("Minecraft ran into a problem! Report saved to:") Then
                //Minecraft 崩溃，忽略 VanillaFix
                WatcherLog("识别为崩溃的 Log：" + text, LogLevel.Error);
                Crashed();
            } else if (text.Contains("Could not save crash report to"))
            {
                //Minecraft 崩溃，无法保存崩溃日志
                WatcherLog("识别为崩溃的 Log：" + text, LogLevel.Error);
                Crashed();
            } else if (text.Contains("/ERROR]: Unable to launch") || text.Contains("An exception was thrown, the game will display an error screen and halt."))
            {
                //Forge 崩溃
                WatcherLog("识别为崩溃的 Log：" + text, LogLevel.Error);
                Crashed();
                //ElseIf Text.Contains("Shutdown failure!") Then
                //    'Minecraft 强行崩溃，由于点 X 强行关闭也会触发这句话，所以不可用
                //    Crashed(Nothing)
            }
        }
    }

    /// <summary>监视日志（ModWatcher.vb:238-240）：PID 前缀拼装后经 <see cref="LaunchLog"/> 上抛（McLaunchLog 脱敏留 VB）。</summary>
    private void WatcherLog(string text, LogLevel level = LogLevel.Info)
    {
        LaunchLog?.Invoke("[" + _pid + "] " + text, level);
    }

    //进度更新

    private int _logProgress;

    /// <summary>进度更新（ModWatcher.vb:244-254）：窗口已出现或 5/5 → 0.95 + Running；否则 Min(_logProgress,3)/3×0.9。</summary>
    private void ProgressUpdate()
    {
        double currentProgress;
        if (IsWindowAppeared || _logProgress == 5)
        {
            currentProgress = 0.95;
            WatcherLog("Minecraft 加载已完成");
            State = MinecraftState.Running;
        } else
        {
            //? VB 的 / 恒为 Double 除法，整数 3 需显式浮点
            currentProgress = Math.Min(_logProgress, 3) / 3d * 0.9;
        }
        Host.Progress = currentProgress;
    }

    //窗口检查

    /// <summary>窗口检查是否已出现（旧 :257；可见性放宽为 internal，测试观察面）。</summary>
    internal bool IsWindowAppeared;
    /// <summary>
    /// 窗口检查是否已经完成。这不一定代表着找到了窗口（如果没有找到，<see cref="IsWindowAppeared"/> 仍为 False）。
    /// （旧 :258-261；可见性放宽为 internal，测试观察面）
    /// </summary>
    internal bool IsWindowFinished;
    private IntPtr _windowHandle;

    /// <summary>窗口检查（ModWatcher.vb:263-307）：枚举过滤、Minecraft/Mod Loader 窗口分支、可选最大化（2 秒延迟 ShowWindow(3)）。</summary>
    internal void TimerWindow() //? 可见性放宽为 internal（测试直驱面；VB 为 Private）
    {
        try
        {
            if (GameProcess.HasExited) return;
            if (IsWindowFinished) return;
            //获取全部窗口，检查是否有新增的
            KeyValuePair<IntPtr, string>? minecraftWindow = null;
            try
            {
                minecraftWindow = TryGetMinecraftWindow();
            } catch (Win32Exception ex)
            {
                //拒绝访问（#1062）
                Logger.Error(ex, "由于反作弊或安全软件拦截，PCL 无法操作游戏窗口", LogBehavior.Toast);
                IsWindowFinished = true;
            }
            if (minecraftWindow is null) return;
            var minecraftWindowName = minecraftWindow.Value.Value;
            var minecraftWindowHandle = minecraftWindow.Value.Key;
            //已找到窗口
            if (!minecraftWindowName.StartsWithF("FML") && !minecraftWindowName.StartsWithF("Quilt Loader"))
            {
                //已找到 Minecraft 窗口
                _windowHandle = minecraftWindowHandle;
                WatcherLog($"Minecraft 窗口已加载：{minecraftWindowName}（{minecraftWindowHandle.ToInt64()}）");
                IsWindowFinished = true;
                //最大化
                if (SettingsService.Get<int>("LaunchArgumentWindowType") == 4)
                {
                    var maximizeThread = new Thread(() =>
                    {
                        try
                        {
                            //如果最大化导致屏幕渲染大小不对，那是 MC 的 Bug，不是我的 Bug
                            //……虽然我很想这样说，但总有人反馈，算了
                            Thread.Sleep(2000);
                            ShowWindow(_windowHandle, 3);
                            WatcherLog($"已最大化 Minecraft 窗口：{minecraftWindowHandle.ToInt64()}");
                        } catch (Exception ex)
                        {
                            Logger.Warn(ex, "最大化 Minecraft 窗口时出现错误");
                        }
                    })
                    { Name = "MinecraftWindowMaximize" };
                    maximizeThread.Start();
                }
            } else if (!IsWindowAppeared)
            {
                //已找到 Mod Loader 窗口
                WatcherLog("Mod Loader 窗口已加载：" + minecraftWindowName + "（" + minecraftWindowHandle.ToInt64() + "）");
            }
            IsWindowAppeared = true;
        } catch (Exception ex)
        {
            Logger.Error(ex, "检查 Minecraft 窗口失败");
        }
    }

    /// <summary>
    /// 获取可能是当前进程对应的 Minecraft 窗口的句柄和标题。
    /// null 代表未找到。（ModWatcher.vb:312-345 移植）
    /// </summary>
    internal KeyValuePair<IntPtr, string>? TryGetMinecraftWindow() //? 可见性放宽为 internal（测试直驱面；VB 为 Private）
    {
        KeyValuePair<IntPtr, string>? result = null;
        var probeWindows = WindowProbe?.Invoke();
        if (probeWindows is not null)
        {
            //? 测试注入面：假句柄集按 Z 序逐窗过同一过滤链
            foreach (var window in probeWindows)
            {
                CheckWindow(window);
            }
        } else
        {
            EnumWindows((hWnd, _) =>
            {
                if (result is not null) return true;
                //检查类名
                var classNameBuilder = new StringBuilder(512);
                GetClassName(hWnd, classNameBuilder, classNameBuilder.Capacity);
                var className = classNameBuilder.ToString();
                if (!IsMinecraftWindowClass(className)) return true;
                //SDL 会先创建隐藏窗口，待初始化完成后才显示（#9073）
                var isVisible = IsWindowVisible(hWnd);
                if (className == "SDL_app" && !isVisible) return true;
                //获取窗口标题名
                var titleBuilder = new StringBuilder(512);
                GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
                var title = titleBuilder.ToString();
                //获取窗口关联的进程
                GetWindowThreadProcessId(hWnd, out var processId);
                DateTime? processStartTime;
                try
                {
                    processStartTime = Process.GetProcessById(processId).StartTime;
                } catch (Exception ex)
                {
                    Logger.Warn(ex, "枚举 Minecraft 窗口进程失败");
                    processStartTime = null;
                }
                CheckWindow(new WatcherWindow { Handle = hWnd, ClassName = className, IsVisible = isVisible, Title = title, ProcessStartTime = processStartTime });
                return true;
            }, 0);
        }
        return result;

        //逐窗过滤（原 :315-342 EnumWindows 回调体逐行；Win32 路径与注入路径共用）
        void CheckWindow(WatcherWindow window)
        {
            if (result is not null) return;
            //检查类名
            //! 逐字移植豁免：Rider 建议合并为逻辑模式，保持 VB OrElse 链逐字形状（旧 :321）
            if (!IsMinecraftWindowClass(window.ClassName)) return;
            //SDL 会先创建隐藏窗口，待初始化完成后才显示（#9073）
            if (window.ClassName == "SDL_app" && !window.IsVisible) return;
            //获取窗口标题名
            var windowText = window.Title;
            //有的 Mod 可以修改窗口标题，所以不能检测是否为 Minecraft 打头，这并不准确
            //部分版本会搞个 GLFW message window，高版本还有个 Hidden Utility Window，都要反选
            if (string.IsNullOrWhiteSpace(windowText) || windowText == "SDL_app" || windowText == "PopupMessageWindow" ||
               windowText.StartsWithF("GLFW") || windowText.EndsWithF("Hidden Utility Window")) return;
            //获取窗口关联的进程
            try
            {
                if (window.ProcessStartTime is not { } processStartTime) return; //原 :336-340 进程读取失败 → 跳过该窗
                if (processStartTime < GameProcess.StartTime) return; //需要是此后启动的进程
            } catch (Exception ex)
            {
                Logger.Warn(ex, "枚举 Minecraft 窗口进程失败");
                return;
            }
            //返回
            result = new KeyValuePair<IntPtr, string>(window.Handle, windowText);
        }
    }

    /// <summary>类名白名单（旧 :321 逐字；Win32 路径的前置惰性读取与注入路径的过滤链共用）。</summary>
    private static bool IsMinecraftWindowClass(string className) =>
        className == "GLFW30" || className == "SDL_app" || className == "LWJGL" || className == "SunAwtFrame";

    //! Win32 声明随类迁移（旧 :346-353 Declare 逐字；VB 的 Integer hWnd 收紧为 IntPtr——HWND 值域在 32 位内，行为一致；
    //! VB Declare 的 Boolean 经 VARIANT_BOOL（2 字节）marshaling，此处以 4 字节 BOOL 对齐 Win32 约定，使用的返回值（IsWindowVisible）等价）
    private delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);
    [DllImport("user32")]
    private static extern bool EnumWindows(EnumWindowsProc callback, int lParam);
    [DllImport("user32", EntryPoint = "GetClassNameA", CharSet = CharSet.Ansi)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder str, int maxCount);
    [DllImport("user32", EntryPoint = "GetWindowTextA", CharSet = CharSet.Ansi)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder str, int maxCount);
    [DllImport("user32", EntryPoint = "SetWindowTextA", CharSet = CharSet.Ansi)]
    private static extern bool SetWindowText(IntPtr hWnd, string str);
    [DllImport("user32")]
    private static extern bool ShowWindow(IntPtr hWnd, uint cmdWindow);
    [DllImport("user32")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    //崩溃处理

    /// <summary>
    /// 崩溃处理（ModWatcher.vb:356-378）：置 Crashed 并汇总 → 提示与反馈（宿主回调）→
    /// 2 秒延迟后开始崩溃分析（CrashAnalyzer 编排经 <see cref="_crashAnalyze"/> 委托目标留 VB，W4 域）。
    /// </summary>
    private void Crashed()
    {
        if (State == MinecraftState.Crashed || State == MinecraftState.Ended) return;
        State = MinecraftState.Crashed;
        //崩溃分析
        WatcherLog("Minecraft 已崩溃，将在 2 秒后开始崩溃分析", LogLevel.Error);
        CrashHint?.Invoke(); //原 Hint("检测到 Minecraft 出现错误，错误分析已开始……", HintType.Red)（文案留 VB）
        Feedback?.Invoke(); //原 FeedbackInfo()
        var analyzerThread = new Thread(() =>
        {
            try
            {
                Thread.Sleep(2000);
                WatcherLog("崩溃分析开始");
                _crashAnalyze?.Invoke(this);
            } catch (Exception ex)
            {
                Logger.Error(ex, "崩溃分析失败");
            }
        })
        { Name = "Crash Analyzer" }; //原 RunInNewThread(…, "Crash Analyzer") → 裸 Thread（包装 Catch 为死代码——内层 Try 覆盖全段）
        analyzerThread.Start();
    }

    //强制关闭

    /// <summary>强制关闭（ModWatcher.vb:381-390）：State=Canceled + 进程 Kill。</summary>
    public void Kill()
    {
        State = MinecraftState.Canceled;
        WatcherLog("尝试强制结束 Minecraft 进程");
        try
        {
            if (!GameProcess.HasExited) GameProcess.Kill();
            WatcherLog("已强制结束 Minecraft 进程");
        } catch (Exception ex)
        {
            Logger.Error(ex, "强制结束 Minecraft 进程失败", LogBehavior.Toast);
        }
    }

    //? 测试隔离面：复位进程级集合与汇总翻转守卫（生产路径不调用）
    internal static void TestResetGlobals()
    {
        McWatcherList = [];
        isWatcherRunning = false;
    }

    //? 测试隔离面：向日志缓冲注入一行（LogReceived 的测试面——DataReceivedEventArgs 无法在测试侧构造；
    //? TimerLog 对空缓冲提前返回（旧 :151），ProgressUpdate 仅在冲刷过至少一行后运行）
    internal void TestEnqueueWaitingLog(string? text)
    {
        lock (_waitingLogLock)
        {
            _waitingLog.Add(text);
        }
    }

}