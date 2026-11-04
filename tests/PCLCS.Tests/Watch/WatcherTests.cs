using MeloongCore;
using PCLCS.Settings;
using PCLCS.Watch;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace PCLCS.Tests.Watch;

//* 触及 SettingsService（LaunchArgumentWindowType）与 Watcher 进程级全局态（McWatcherList/回调接线/翻转守卫），
//* 与设置类测试共享串行集合
[Collection("PCLCS.Settings")]
public class WatcherTests : IDisposable
{
    private readonly string _root;
    //观察容器均线程安全：监控/崩溃分析/最大化线程与测试线程并发写入
    private readonly ConcurrentQueue<(string Text, LogLevel Level)> _logs = new();
    private readonly ConcurrentQueue<(bool IsRunning, bool Trigger)> _summaryCalls = new();
    private readonly ConcurrentQueue<Watcher> _crashAnalyzeCalls = new();
    private int _crashHintCount;
    private int _feedbackCount;
    private int _probeCalls;
    private int _handleSeed = 0x100000;
    private readonly List<Process> _processes = [];
    private readonly List<Watcher> _watchers = [];
    private readonly List<Watcher> _savedList;

    public WatcherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclcs.watcher." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
        SettingsService.Configure(_root);   //全局 Settings.json 落在临时目录
        _savedList = Watcher.McWatcherList;
        Watcher.TestResetGlobals();
        //宿主层接线（ModNet.Sub New 同模式）：全部回调捕获到本实例的观察容器
        Watcher.LaunchLog = (text, level) => _logs.Enqueue((text, level));
        Watcher.StateChanged = (isRunning, trigger) => _summaryCalls.Enqueue((isRunning, trigger));
        Watcher.CrashHint = () => Interlocked.Increment(ref _crashHintCount);
        Watcher.Feedback = () => Interlocked.Increment(ref _feedbackCount);
        SetEmptyProbe();
    }

    public void Dispose()
    {
        //让监控线程（前台线程）尽快退出并清理子进程
        foreach (var watcher in _watchers)
        {
            try { watcher.State = Watcher.MinecraftState.Ended; } catch (Exception) { } //? 尽力而为收尾：终态已置或宿主已断开则忽略（lint 空 catch 豁免口径）
        }
        foreach (var process in _processes)
        {
            try { if (!process.HasExited) process.Kill(); } catch (Exception) { } //? 尽力而为收尾：替身可能已自行退出或被系统回收
            try { process.Dispose(); } catch (Exception) { } //? 尽力而为收尾：句柄可能已失效
        }
        Watcher.LaunchLog = null;
        Watcher.StateChanged = null;
        Watcher.CrashHint = null;
        Watcher.Feedback = null;
        Watcher.WindowProbe = null;
        Watcher.TestResetGlobals();
        Watcher.McWatcherList = _savedList;
        try { SettingsService.Reset("LaunchArgumentWindowType"); } catch (Exception) { } //? 尽力而为收尾：键可能不存在于配置存储
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }

    //────────── 造具 ──────────

    private void SetEmptyProbe() => Watcher.WindowProbe = () =>
    {
        Interlocked.Increment(ref _probeCalls);
        return [];
    };

    private Process StartCmd(string arguments)
    {
        var process = Process.Start(new ProcessStartInfo("cmd.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        _processes.Add(process);
        return process;
    }

    /// <summary>长活且无输出的游戏进程替身（ping 输出重定向到 nul，管道零流量）。</summary>
    private Process StartLiveGame() => StartCmd("/c ping -n 60 127.0.0.1 > nul 2>&1");

    /// <summary>约 1 秒后以指定返回值退出的游戏进程替身。</summary>
    private Process StartExitGame(int exitCode) => StartCmd($"/c ping -n 2 127.0.0.1 > nul 2>&1 & exit {exitCode}");

    private Watcher NewWatcher(Process process, string windowTitle = "", DateTime? releaseTime = null,
        bool isCanceled = false, Action<Watcher>? crashAnalyze = null)
    {
        var watcher = new Watcher(new TestHost
        {
            GameProcess = process,
            ReleaseTime = releaseTime ?? new DateTime(2020, 1, 1),
            IsCanceled = isCanceled
        }, windowTitle, crashAnalyze);
        _watchers.Add(watcher);
        return watcher;
    }

    /// <summary>已退出且置于终态的看门狗（用于全局集合的预置；监控线程立即退出）。</summary>
    private void NewFinishedWatcher(Watcher.MinecraftState state, int exitCode)
    {
        var process = StartExitGame(exitCode);
        var watcher = NewWatcher(process);
        Assert.True(process.WaitForExit(10000), "替身进程应在 10 秒内退出");
        watcher.State = state;
    }

    /// <summary>假窗口；StartTime 默认为 30 秒后（通过 StartTime 过滤），显式传 null 表示进程读取失败。</summary>
    private Watcher.WatcherWindow Win(string className, string title, bool visible = true,
        DateTime? processStartTime = null, IntPtr? handle = null) =>
        new()
        {
            Handle = handle ?? new IntPtr(_handleSeed++),
            ClassName = className,
            IsVisible = visible,
            Title = title,
            ProcessStartTime = processStartTime ?? DateTime.Now.AddSeconds(30)
        };

    /// <summary>假窗口：进程启动时间读取失败（原 :336-340 的异常路径）。</summary>
    private Watcher.WatcherWindow WinWithDeadProcess(string className, string title) =>
        new() { Handle = new IntPtr(_handleSeed++), ClassName = className, IsVisible = true, Title = title, ProcessStartTime = null };

    /// <summary>WatcherLog 的完整落盘文本（核内拼装 PID 前缀，旧 :239）。</summary>
    private static string Payload(Watcher watcher, string text) => "[" + watcher.GameProcess.Id + "] " + text;

    /// <summary>剥去 PID 前缀后的本看门狗日志流。</summary>
    private List<string> LogsOf(Watcher watcher)
    {
        var prefix = "[" + watcher.GameProcess.Id + "] ";
        return _logs.Where(l => l.Text.StartsWith(prefix, StringComparison.Ordinal)).Select(l => l.Text[prefix.Length..]).ToList();
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(50);
        }
        return condition();
    }

    private void ClearLogs()
    {
        while (_logs.TryDequeue(out _)) { }
    }

    private sealed class TestHost : IWatcherHost
    {
        public Process GameProcess { get; init; } = null!;
        public double Progress { get; set; }
        public bool IsCanceled { get; set; }
        public DateTime ReleaseTime { get; set; } = new(2020, 1, 1);
    }

    //────────── GameLog 语料（字符串驱动，无真实 MC）──────────

    [Fact]
    public void GameLog_null_line_is_noop()
    {
        //ModWatcher.vb:187：Text Is Nothing → 直接返回
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog(null);
        Assert.Empty(watcher.LatestLog);
        Assert.DoesNotContain(_logs, static l => l.Text.Contains("日志 1/5"));
    }

    [Fact]
    public void GameLog_normalizes_line_endings()
    {
        //ModWatcher.vb:188：ReplaceLineEndings(vbCrLf)；\f 归一为换行（MeloongCore 扩展，net48 与 VB 同源）
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog("a\nb");
        watcher.GameLog("x\fz");
        Assert.Equal("x\r\nz", watcher.LatestLog.Last());
        Assert.Equal(2, watcher.LatestLog.Count);
    }

    [Fact]
    public void GameLog_progress_stages_emit_verbatim_logs()
    {
        //ModWatcher.vb:196-210：五个阶段文案与判定子串逐字
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog("2024/1/1 boot start");
        watcher.GameLog("Setting user: Player123");
        watcher.GameLog("LWJGL Version: 3.3.1 build 7");
        watcher.GameLog("OpenAL initialized on device OpenAL Soft");
        watcher.GameLog("Created 128x128x4 textures -atlas minecraft");
        var stageLogs = LogsOf(watcher).Where(static t => t.StartsWith("日志 ")).ToList();
        List<string> expected =
        [
            "日志 1/5：已出现日志输出",
            "日志 2/5：游戏用户已设置",
            "日志 3/5：LWJGL 版本已确认",
            "日志 4/5：OpenAL 已加载",
            "日志 5/5：材质已加载"
        ];
        Assert.Equal(expected, stageLogs);
    }

    [Fact]
    public void GameLog_stage_gates_are_monotonic()
    {
        //ModWatcher.vb:198 注：可能第一句就是后面需要判断的 Log——5/5 判定先于 2-4 触发时直接跳段
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog("Found animation info here");
        var stageLogs = LogsOf(watcher).Where(static t => t.StartsWith("日志 ")).ToList();
        List<string> expected = ["日志 1/5：已出现日志输出", "日志 5/5：材质已加载"];
        Assert.Equal(expected, stageLogs);
    }

    [Fact]
    public void GameLog_stage3_is_case_insensitive()
    {
        //ModWatcher.vb:202：ContainsIgnoreCase("lwjgl version")
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog("Lwjgl Version 3.3.1");
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "日志 3/5：LWJGL 版本已确认"));
    }

    [Fact]
    public void GameLog_stage2_is_case_sensitive()
    {
        //ModWatcher.vb:199：Contains("Setting user:") 大小写敏感
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog("setting user: Player");
        Assert.DoesNotContain(_logs, l => l.Text == Payload(watcher, "日志 2/5：游戏用户已设置"));
    }

    [Theory]
    [InlineData("Starting up SoundSystem")]
    [InlineData("OpenAL initialized")]
    public void GameLog_stage4_accepts_both_alternatives(string line)
    {
        //ModWatcher.vb:205：OpenAL initialized 或 Starting up SoundSystem
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog(line);
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "日志 4/5：OpenAL 已加载"));
    }

    [Theory]
    [InlineData("Found animation info")]
    [InlineData("Created 128x128x4 textures -atlas minecraft")]
    public void GameLog_stage5_accepts_both_alternatives(string line)
    {
        //ModWatcher.vb:208：三段组合或 Found animation info
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog(line);
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "日志 5/5：材质已加载"));
    }

    [Fact]
    public void GameLog_stage5_requires_all_three_parts()
    {
        //ModWatcher.vb:208：Created + textures + -atlas 三者同时出现
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog("Created textures");
        Assert.DoesNotContain(_logs, l => l.Text == Payload(watcher, "日志 5/5：材质已加载"));
        watcher.GameLog("Created 1 textures -atlas file");
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "日志 5/5：材质已加载"));
    }

    [Theory]
    [InlineData("Someone is closing me!", true, LogLevel.Info, "识别为关闭的 Log：")]
    [InlineData("Restarting Minecraft with command", true, LogLevel.Info, "识别为关闭的 Log：")]
    [InlineData("Crash report saved to /tmp/crash.txt", false, LogLevel.Error, "识别为崩溃的 Log：")]
    [InlineData("This crash report has been saved to: C:\\a\\b", false, LogLevel.Error, "识别为崩溃的 Log：")]
    [InlineData("Could not save crash report to /tmp", false, LogLevel.Error, "识别为崩溃的 Log：")]
    [InlineData("/ERROR]: Unable to launch the game", false, LogLevel.Error, "识别为崩溃的 Log：")]
    [InlineData("An exception was thrown, the game will display an error screen and halt.", false, LogLevel.Error, "识别为崩溃的 Log：")]
    public void GameLog_keyword_table_transitions(string line, bool isEnded, LogLevel expectedLevel, string expectedPrefix)
    {
        //ModWatcher.vb:215-236：关闭与崩溃关键字表逐条（[CHAT] 行豁免；#1258 关闭对）
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog("boot");
        watcher.GameLog(line);
        var expected = isEnded
            ? Watcher.MinecraftState.Ended
            : Watcher.MinecraftState.Crashed;
        Assert.Equal(expected, watcher.State);
        var hit = Assert.Single(_logs, l => l.Text == Payload(watcher, expectedPrefix + line));
        Assert.Equal(expectedLevel, hit.Level);
        if (!isEnded)
        {
            Assert.Equal(1, _crashHintCount);
            Assert.Equal(1, _feedbackCount);
            Assert.Contains(_logs, l => l.Text == Payload(watcher, "Minecraft 已崩溃，将在 2 秒后开始崩溃分析") && l.Level == LogLevel.Error);
        } else
        {
            Assert.Equal(0, _crashHintCount);
            Assert.Equal(0, _feedbackCount);
        }
    }

    [Fact]
    public void GameLog_chat_lines_skip_crash_detection()
    {
        //ModWatcher.vb:215：整段检测被 [CHAT] 豁免
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog("[CHAT] Crash report saved to nowhere");
        watcher.GameLog("[CHAT] Someone is closing me!");
        Assert.Equal(Watcher.MinecraftState.Loading, watcher.State);
        Assert.Equal(0, _crashHintCount);
        Assert.DoesNotContain(LogsOf(watcher), static t => t.StartsWith("识别为"));
    }

    [Fact]
    public void GameLog_crash_after_ended_is_ignored()
    {
        //ModWatcher.vb:357：Crashed 入口对 Ended 直接返回
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog("Someone is closing me!");
        Assert.Equal(Watcher.MinecraftState.Ended, watcher.State);
        watcher.GameLog("Crash report saved to /tmp/x");
        Assert.Equal(Watcher.MinecraftState.Ended, watcher.State);
        Assert.Equal(0, _crashHintCount);
        Assert.DoesNotContain(_logs, l => l.Text == Payload(watcher, "Minecraft 已崩溃，将在 2 秒后开始崩溃分析"));
    }

    [Fact]
    public void GameLog_crashed_is_idempotent()
    {
        //ModWatcher.vb:357：已 Crashed 时再次进入 Crashed() 直接返回
        var watcher = NewWatcher(StartLiveGame());
        watcher.GameLog("Crash report saved to /tmp/a");
        watcher.GameLog("Crash report saved to /tmp/b");
        Assert.Equal(Watcher.MinecraftState.Crashed, watcher.State);
        Assert.Equal(1, _crashHintCount);
        Assert.Equal(1, _feedbackCount);
        Assert.Equal(1, _logs.Count(l => l.Text == Payload(watcher, "Minecraft 已崩溃，将在 2 秒后开始崩溃分析")));
    }

    [Fact]
    public void GameLog_latest_log_ring_buffer_caps()
    {
        //ModWatcher.vb:191-192：LatestLog 逐行入队，计数 ≥501 即出队一条（稳态 500 条）
        var watcher = NewWatcher(StartLiveGame());
        for (var i = 1; i <= 502; i++) watcher.GameLog("plain line " + i.ToString("D4"));
        Assert.Equal(500, watcher.LatestLog.Count);
        Assert.True(watcher.LatestLog.TryPeek(out var oldest), "LatestLog 应非空");
        Assert.Equal("plain line 0003", oldest);
        Assert.Equal("plain line 0502", watcher.LatestLog.Last());
    }

    [Fact]
    public void GameLog_latest_log_holds_500_without_dequeue()
    {
        var watcher = NewWatcher(StartLiveGame());
        for (var i = 1; i <= 500; i++) watcher.GameLog("plain line " + i.ToString("D4"));
        Assert.Equal(500, watcher.LatestLog.Count);
        Assert.True(watcher.LatestLog.TryPeek(out var oldest), "LatestLog 应非空");
        Assert.Equal("plain line 0001", oldest);
    }

    //────────── TimerLog 退出码判定 ──────────

    [Fact]
    public void TimerLog_exit_code_zero_ends_normally()
    {
        //ModWatcher.vb:175-178：返回值为 0 → 正常关闭 Ended
        var process = StartExitGame(0);
        var watcher = NewWatcher(process);
        watcher.State = Watcher.MinecraftState.Running;
        Assert.True(process.WaitForExit(10000), "替身进程应在 10 秒内退出");
        watcher.TimerLog();
        Assert.Equal(Watcher.MinecraftState.Ended, watcher.State);
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "Minecraft 已退出，返回值：0"));
        Assert.Equal(0, _crashHintCount);
    }

    [Fact]
    public void TimerLog_exit_code_nonzero_running_recent_release_crashes()
    {
        //ModWatcher.vb:171-174：返回值不为 0 且 Running 且 ReleaseTime ≥2012 → Crashed
        var process = StartExitGame(5);
        var watcher = NewWatcher(process, releaseTime: new DateTime(2013, 6, 1), crashAnalyze: analyzed => _crashAnalyzeCalls.Enqueue(analyzed));
        watcher.State = Watcher.MinecraftState.Running;
        Assert.True(process.WaitForExit(10000), "替身进程应在 10 秒内退出");
        watcher.TimerLog();
        Assert.Equal(Watcher.MinecraftState.Crashed, watcher.State);
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "Minecraft 已退出，返回值：5") && l.Level == LogLevel.Info);
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "Minecraft 返回值异常，可能已崩溃") && l.Level == LogLevel.Error);
        Assert.Equal(1, _crashHintCount);
        Assert.Equal(1, _feedbackCount);
        //崩溃分析委托：2 秒延迟后经 CrashAnalyze 回调（VB 提供 CrashAnalyzer 接线，:363-377）
        Assert.True(WaitUntil(() => _crashAnalyzeCalls.Count == 1 && _logs.Count(l => l.Text == Payload(watcher, "崩溃分析开始")) == 1, 3500),
            "2 秒延迟后应开始崩溃分析");
        Assert.Same(watcher, _crashAnalyzeCalls.Single());
    }

    [Fact]
    public void TimerLog_exit_code_nonzero_running_old_release_ends()
    {
        //ReleaseTime <2012 → 返回值异常分支不成立 → 正常关闭
        var process = StartExitGame(5);
        var watcher = NewWatcher(process, releaseTime: new DateTime(2011, 1, 1));
        watcher.State = Watcher.MinecraftState.Running;
        Assert.True(process.WaitForExit(10000), "替身进程应在 10 秒内退出");
        watcher.TimerLog();
        Assert.Equal(Watcher.MinecraftState.Ended, watcher.State);
        Assert.DoesNotContain(_logs, l => l.Text == Payload(watcher, "Minecraft 返回值异常，可能已崩溃"));
    }

    [Fact]
    public void TimerLog_exit_code_nonzero_loading_crashes_as_window_missing()
    {
        //ModWatcher.vb:167-170：窗口未出现即退出 → Crashed
        var process = StartExitGame(1);
        var watcher = NewWatcher(process);
        Assert.True(process.WaitForExit(10000), "替身进程应在 10 秒内退出");
        watcher.TimerLog();
        Assert.Equal(Watcher.MinecraftState.Crashed, watcher.State);
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "Minecraft 尚未加载完成，可能已崩溃") && l.Level == LogLevel.Error);
    }

    [Fact]
    public void TimerLog_exit_code_nonzero_already_crashed_stays()
    {
        //ModWatcher.vb:175：State 已为 Crashed 时不降级也不重复崩溃
        var process = StartExitGame(5);
        var watcher = NewWatcher(process, releaseTime: new DateTime(2013, 6, 1));
        Assert.True(process.WaitForExit(10000), "替身进程应在 10 秒内退出");
        watcher.State = Watcher.MinecraftState.Crashed;
        var hintBefore = _crashHintCount;
        watcher.TimerLog();
        Assert.Equal(Watcher.MinecraftState.Crashed, watcher.State);
        Assert.Equal(hintBefore, _crashHintCount);
        Assert.DoesNotContain(_logs, l => l.Text == Payload(watcher, "Minecraft 返回值异常，可能已崩溃"));
    }

    [Fact]
    public void TimerLog_exit_code_nonzero_ended_stays_ended()
    {
        var process = StartExitGame(5);
        var watcher = NewWatcher(process);
        Assert.True(process.WaitForExit(10000), "替身进程应在 10 秒内退出");
        watcher.State = Watcher.MinecraftState.Ended;
        watcher.TimerLog();
        Assert.Equal(Watcher.MinecraftState.Ended, watcher.State);
    }

    //────────── ProgressUpdate（Loader.Progress 0~0.95）──────────

    [Fact]
    public void ProgressUpdate_maps_log_progress_to_loader()
    {
        //ModWatcher.vb:251：Math.Min(LogProgress, 3) / 3 * 0.9（VB / 恒为 Double 除法）
        //（IsCanceled=true 使监控线程立即退出；TimerLog 对空缓冲提前返回（旧 :151），故经缓冲注入驱动冲刷）
        var process = StartLiveGame();
        var watcher = NewWatcher(process, isCanceled: true);
        var host = (TestHost) watcher.Host;
        watcher.TestEnqueueWaitingLog("boot");
        watcher.TimerLog();
        Assert.Equal(1 / 3d * 0.9, host.Progress, 12);
        watcher.TestEnqueueWaitingLog("Setting user: Player");
        watcher.TimerLog();
        Assert.Equal(2 / 3d * 0.9, host.Progress, 12);
        watcher.TestEnqueueWaitingLog("Lwjgl Version 3");
        watcher.TimerLog();
        Assert.Equal(3 / 3d * 0.9, host.Progress, 12);
        watcher.TestEnqueueWaitingLog("OpenAL initialized");
        watcher.TimerLog();
        Assert.Equal(3 / 3d * 0.9, host.Progress, 12);
        Assert.Equal(Watcher.MinecraftState.Loading, watcher.State);
    }

    [Fact]
    public void ProgressUpdate_stage5_completes_to_running()
    {
        //ModWatcher.vb:246-249：LogProgress=5 → 0.95 + Running
        var process = StartLiveGame();
        var watcher = NewWatcher(process, isCanceled: true);
        var host = (TestHost) watcher.Host;
        watcher.TestEnqueueWaitingLog("Found animation info");
        watcher.TimerLog();
        Assert.Equal(0.95, host.Progress, 12);
        Assert.Equal(Watcher.MinecraftState.Running, watcher.State);
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "Minecraft 加载已完成"));
    }

    [Fact]
    public void ProgressUpdate_window_appeared_completes_to_running()
    {
        //ModWatcher.vb:246-249：窗口已出现（IsWindowAppeared）→ 0.95 + Running，即使 LogProgress 仅为 1
        Watcher.WindowProbe = () => [Win("GLFW30", "FML Loader Window")];
        var process = StartLiveGame();
        var watcher = NewWatcher(process, isCanceled: true);
        var host = (TestHost) watcher.Host;
        watcher.TimerWindow();
        watcher.TestEnqueueWaitingLog("mc boot");
        watcher.TimerLog();
        Assert.Equal(0.95, host.Progress, 12);
        Assert.Equal(Watcher.MinecraftState.Running, watcher.State);
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "Minecraft 加载已完成"));
    }

    //────────── TryGetMinecraftWindow（假句柄集注入）──────────

    [Theory]
    [InlineData("GLFW30")]
    [InlineData("SDL_app")]
    [InlineData("LWJGL")]
    [InlineData("SunAwtFrame")]
    public void TryGetMinecraftWindow_class_whitelist_accepts(string className)
    {
        //ModWatcher.vb:321：类名白名单逐字
        Watcher.WindowProbe = () => [Win(className, "Minecraft 1.20.1")];
        var watcher = NewWatcher(StartLiveGame());
        var found = watcher.TryGetMinecraftWindow();
        Assert.NotNull(found);
        Assert.Equal("Minecraft 1.20.1", found.Value.Value);
    }

    [Theory]
    [InlineData("GLFW32")]
    [InlineData("NetLogo")]
    public void TryGetMinecraftWindow_class_whitelist_rejects(string className)
    {
        Watcher.WindowProbe = () => [Win(className, "Minecraft 1.20.1")];
        var watcher = NewWatcher(StartLiveGame());
        Assert.Null(watcher.TryGetMinecraftWindow());
    }

    [Fact]
    public void TryGetMinecraftWindow_sdl_hidden_window_rejected()
    {
        //ModWatcher.vb:323：SDL 先创建隐藏窗口（#9073）→ 不可见即反选
        Watcher.WindowProbe = () => [Win("SDL_app", "Minecraft 1.20.1", visible: false)];
        var watcher = NewWatcher(StartLiveGame());
        Assert.Null(watcher.TryGetMinecraftWindow());
        Watcher.WindowProbe = () => [Win("SDL_app", "Minecraft 1.20.1", visible: true)];
        Assert.NotNull(watcher.TryGetMinecraftWindow());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("SDL_app")]
    [InlineData("PopupMessageWindow")]
    [InlineData("GLFW message window")]
    [InlineData("GLFW point")]
    [InlineData("MC Hidden Utility Window")]
    public void TryGetMinecraftWindow_title_filter_rejects(string title)
    {
        //ModWatcher.vb:330-331：空白/SDL_app/PopupMessageWindow/GLFW 前缀/Hidden Utility Window 后缀反选
        Watcher.WindowProbe = () => [Win("GLFW30", title)];
        var watcher = NewWatcher(StartLiveGame());
        Assert.Null(watcher.TryGetMinecraftWindow());
    }

    [Theory]
    [InlineData("Minecraft 1.20.1")]
    [InlineData("Godcraft Edition")]
    public void TryGetMinecraftWindow_title_filter_accepts(string title)
    {
        //ModWatcher.vb:328 注：有的 Mod 可以修改窗口标题，不能检测 Minecraft 打头
        Watcher.WindowProbe = () => [Win("GLFW30", title)];
        var watcher = NewWatcher(StartLiveGame());
        Assert.Equal(title, watcher.TryGetMinecraftWindow()!.Value.Value);
    }

    [Fact]
    public void TryGetMinecraftWindow_process_start_time_filter()
    {
        //ModWatcher.vb:336-340：需要是此后启动的进程；读取失败（null）即反选
        var process = StartLiveGame();
        var gameStart = process.StartTime;
        var watcher = NewWatcher(process);
        Watcher.WindowProbe = () => [Win("GLFW30", "Minecraft", processStartTime: gameStart.AddSeconds(-10))];
        Assert.Null(watcher.TryGetMinecraftWindow());
        Watcher.WindowProbe = () => [WinWithDeadProcess("GLFW30", "Minecraft")];
        Assert.Null(watcher.TryGetMinecraftWindow());
        Watcher.WindowProbe = () => [Win("GLFW30", "Minecraft", processStartTime: gameStart.AddSeconds(10))];
        Assert.NotNull(watcher.TryGetMinecraftWindow());
    }

    [Fact]
    public void TryGetMinecraftWindow_first_match_in_z_order_wins()
    {
        //ModWatcher.vb:316：已找到即短路——Z 序首个命中窗口胜出
        Watcher.WindowProbe = () =>
        [
            Win("GLFW30", "Minecraft First", handle: new IntPtr(0x2222)),
            Win("LWJGL", "Minecraft Second", handle: new IntPtr(0x3333))
        ];
        var watcher = NewWatcher(StartLiveGame());
        var found = watcher.TryGetMinecraftWindow();
        Assert.NotNull(found);
        Assert.Equal(new IntPtr(0x2222), found.Value.Key);
        Assert.Equal("Minecraft First", found.Value.Value);
    }

    //────────── TimerWindow 窗口检查流 ──────────

    [Fact]
    public void TimerWindow_finds_loader_window_once()
    {
        //ModWatcher.vb:299-303：FML/Quilt Loader 前缀 → Mod Loader 窗口；IsWindowAppeared 后不再重复记录
        Watcher.WindowProbe = () => [Win("GLFW30", "FML Loader Window", handle: new IntPtr(0x4444))];
        var watcher = NewWatcher(StartLiveGame());
        watcher.TimerWindow();
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "Mod Loader 窗口已加载：FML Loader Window（17476）"));
        Assert.True(watcher.IsWindowAppeared);
        Assert.False(watcher.IsWindowFinished);
        ClearLogs();
        watcher.TimerWindow();
        Assert.DoesNotContain(_logs, static l => l.Text.Contains("窗口已加载"));
    }

    [Fact]
    public void TimerWindow_finds_minecraft_window_and_finishes()
    {
        //ModWatcher.vb:279-283：非 FML/Quilt 前缀 → Minecraft 窗口，IsWindowFinished 后整段短路
        Watcher.WindowProbe = () => [Win("GLFW30", "Minecraft 1.20.1", handle: new IntPtr(0x5555))];
        var watcher = NewWatcher(StartLiveGame());
        watcher.TimerWindow();
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "Minecraft 窗口已加载：Minecraft 1.20.1（21845）"));
        Assert.True(watcher.IsWindowAppeared);
        Assert.True(watcher.IsWindowFinished);
        ClearLogs();
        watcher.TimerWindow();
        Assert.DoesNotContain(_logs, static l => l.Text.Contains("窗口已加载") || l.Text.Contains("最大化"));
    }

    [Fact]
    public void TimerWindow_maximizes_when_launch_argument_window_type_4()
    {
        //ModWatcher.vb:285-298：LaunchArgumentWindowType=4 → 新线程 2 秒后 ShowWindow(3)，文案逐字
        SettingsService.Set("LaunchArgumentWindowType", 4);
        Watcher.WindowProbe = () => [Win("GLFW30", "Minecraft 1.20.1", handle: new IntPtr(0x6666))];
        var watcher = NewWatcher(StartLiveGame());
        watcher.TimerWindow();
        Assert.True(WaitUntil(() => _logs.Any(l => l.Text == Payload(watcher, "已最大化 Minecraft 窗口：26214")), 3200),
            "2 秒延迟后应记录最大化日志");
    }

    [Fact]
    public void TimerWindow_win32_access_denied_marks_finished()
    {
        //ModWatcher.vb:271-275：Win32Exception（#1062 反作弊拦截）→ 记错误并标记完成，不再重试
        Watcher.WindowProbe = static () => throw new Win32Exception(5);
        var watcher = NewWatcher(StartLiveGame());
        watcher.TimerWindow();
        Assert.True(watcher.IsWindowFinished);
        ClearLogs();
        watcher.TimerWindow();
        Assert.DoesNotContain(_logs, static l => l.Text.Contains("窗口已加载"));
    }

    [Fact]
    public void TimerWindow_skips_when_process_exited()
    {
        //ModWatcher.vb:265：进程已退出直接返回，不枚举窗口（快照比较，排除构造期监控节拍的合法计数）
        var process = StartExitGame(0);
        var watcher = NewWatcher(process, isCanceled: true);
        Assert.True(process.WaitForExit(10000), "替身进程应在 10 秒内退出");
        var before = _probeCalls;
        watcher.TimerWindow();
        Assert.Equal(before, _probeCalls);
    }

    //────────── 状态机与全局汇总 ──────────

    [Fact]
    public void Watcher_ctor_logs_and_adds_self_with_running_summary()
    {
        //ModWatcher.vb:71-82：开始日志 + 窗口标题日志（非空才记）+ 列表收敛 + 汇总（Loading 计入运行）
        var watcher = NewWatcher(StartLiveGame(), "我的{date}窗口");
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "开始 Minecraft 日志监控") && l.Level == LogLevel.Info);
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "要求窗口标题：我的{date}窗口"));
        List<(bool IsRunning, bool Trigger)> expected = [(true, true)];
        Assert.Equal(expected, _summaryCalls);
        Assert.Contains(watcher, Watcher.McWatcherList);
    }

    [Fact]
    public void Watcher_ctor_without_title_skips_title_log()
    {
        //ModWatcher.vb:72：WindowTitle 为空不记标题日志
        NewWatcher(StartLiveGame());
        Assert.DoesNotContain(_logs, static l => l.Text.Contains("要求窗口标题"));
    }

    [Fact]
    public void Watcher_ctor_rebuilds_list_purging_finished_watchers()
    {
        //ModWatcher.vb:74-81：Crashed/Ended/Canceled 项剔除，保留项顺序不变，自身追加至末尾
        NewFinishedWatcher(Watcher.MinecraftState.Ended, 0);
        NewFinishedWatcher(Watcher.MinecraftState.Crashed, 5);
        NewFinishedWatcher(Watcher.MinecraftState.Canceled, 0);
        var loading = NewWatcher(StartLiveGame());
        var fresh = NewWatcher(StartLiveGame());
        List<Watcher> expected = [loading, fresh];
        Assert.Equal(expected, Watcher.McWatcherList);
    }

    [Fact]
    public void Watcher_state_setter_triggers_summary_only_on_change()
    {
        //ModWatcher.vb:125：同值赋值不触发；Ended 翻转触发 (false, true)
        var watcher = NewWatcher(StartLiveGame());
        List<(bool IsRunning, bool Trigger)> expected = [(true, true)];
        Assert.Equal(expected, _summaryCalls);   //构造即 Loading → 运行翻转
        watcher.State = Watcher.MinecraftState.Loading;   //同值
        watcher.State = Watcher.MinecraftState.Running;   //汇总不变
        Assert.Equal(expected, _summaryCalls);
        watcher.State = Watcher.MinecraftState.Ended;
        List<(bool IsRunning, bool Trigger)> expectedAfterEnd = [(true, true), (false, true)];
        Assert.Equal(expectedAfterEnd, _summaryCalls);
    }

    [Fact]
    public void Watcher_summary_counts_crashed_alive_as_running_with_default_trigger()
    {
        //ModWatcher.vb:11-14：Crashed 且进程未退出 → 计入运行（Exit For 在 Trigger 置 False 之前）
        var watcher = NewWatcher(StartLiveGame());
        watcher.State = Watcher.MinecraftState.Ended;
        watcher.State = Watcher.MinecraftState.Crashed;
        List<(bool IsRunning, bool Trigger)> expected = [(true, true), (false, true), (true, true)];
        Assert.Equal(expected, _summaryCalls);
    }

    [Fact]
    public void Watcher_summary_crashed_exited_reports_no_shutdown()
    {
        //ModWatcher.vb:15-16：Crashed 且已退出 → 不计入运行且 Trigger=False
        var process = StartExitGame(5);
        var watcher = NewWatcher(process);
        Assert.True(process.WaitForExit(10000), "替身进程应在 10 秒内退出");
        watcher.State = Watcher.MinecraftState.Crashed;
        Assert.Contains(_summaryCalls, static c => c == (false, false));
    }

    //────────── 监控线程节拍与退出 ──────────

    [Fact]
    public void Watcher_monitor_thread_exits_on_loader_cancel()
    {
        //ModWatcher.vb:96：Loader 已取消且未 Crashed → 循环退出
        var watcher = NewWatcher(StartLiveGame(), isCanceled: true);
        Assert.True(WaitUntil(() => _logs.Any(l => l.Text == Payload(watcher, "Minecraft 日志监控已退出")), 2000),
            "取消后监控线程应退出");
    }

    [Fact]
    public void Watcher_monitor_thread_exits_on_crashed_with_exited_process()
    {
        //ModWatcher.vb:97：Crashed 且进程已退出 → 循环退出（退出后再汇总一次，翻转守卫拦住）
        var process = StartExitGame(5);
        var watcher = NewWatcher(process);
        Assert.True(process.WaitForExit(10000), "替身进程应在 10 秒内退出");
        watcher.State = Watcher.MinecraftState.Crashed;
        Assert.True(WaitUntil(() => _logs.Any(l => l.Text == Payload(watcher, "Minecraft 日志监控已退出")), 2000),
            "Crashed + 已退出后监控线程应退出");
    }

    //────────── 强制关闭 ──────────

    [Fact]
    public void Watcher_kill_cancels_and_kills_process()
    {
        //ModWatcher.vb:381-390：State=Canceled + Kill + 文案逐字
        var process = StartLiveGame();
        var watcher = NewWatcher(process);
        watcher.Kill();
        Assert.Equal(Watcher.MinecraftState.Canceled, watcher.State);
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "尝试强制结束 Minecraft 进程"));
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "已强制结束 Minecraft 进程"));
        Assert.True(WaitUntil(() => process.HasExited, 2000), "Kill 后进程应退出");
        Assert.Contains(_summaryCalls, static c => c == (false, false));
    }

    [Fact]
    public void Watcher_kill_on_exited_process_still_logs()
    {
        //ModWatcher.vb:385：已退出则跳过 Kill，但仍记录完成文案
        var process = StartExitGame(0);
        var watcher = NewWatcher(process);
        Assert.True(process.WaitForExit(10000), "替身进程应在 10 秒内退出");
        watcher.Kill();
        Assert.Equal(Watcher.MinecraftState.Canceled, watcher.State);
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "尝试强制结束 Minecraft 进程"));
        Assert.Contains(_logs, l => l.Text == Payload(watcher, "已强制结束 Minecraft 进程"));
    }

    //────────── 全局静态面 ──────────

    [Fact]
    public void Global_statics_hold_list_and_running_flag()
    {
        //裁定 5：McWatcherList/HasRunningMinecraft 由 C# 持有；VB 侧经薄转发属性读写
        Assert.Empty(Watcher.McWatcherList);
        Assert.False(Watcher.HasRunningMinecraft);
        Watcher.HasRunningMinecraft = true;
        Assert.True(Watcher.HasRunningMinecraft);
        Watcher.HasRunningMinecraft = false;
    }
}