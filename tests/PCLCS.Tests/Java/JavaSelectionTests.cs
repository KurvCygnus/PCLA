using MeloongCore;
using PCLCS.Net;
using PCLCS.Results;
using PCLCS.Settings;
using System.Text.Json.Nodes;

//命名空间留 PCLCS.Tests 根：PCLCS.Java（类）已占用 Java 之名，任何 PCLCS.*.Java 子命名空间都会遮蔽同层对 Java 类的引用（CS0118，已实测）
namespace PCLCS.Tests;

/// <summary>
/// JavaSelection（Java 选择编排核）测试。
/// 分支 2/3/自动与迁移决策对照 ModJava.vb 旧锚点逐行（迁移判定 → 分支 2 版本文件夹搜索 / 3 JavaForced /
/// 自动 = 需求区间 → 列表选择 → 搜索等待与重搜（#3797 JavaLock）→ Mojang 清单匹配 → 无匹配弹窗 → 下载重选）；
/// JavaForced PreviewGet 自动重置（裁定 7）经真实处理器挂接；manifest 清单 3 哈希跳过（#3827）与 NetFile 映射经假 fetcher 注入；无进程、无 UI、无网络。
/// </summary>
[Collection("PCLCS.Settings")]   //SettingsService 与 JavaSelection 注入面均为进程级状态，串行执行
public class JavaSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pclcs.javaselection." + Guid.NewGuid().ToString("N"));
    private string InstanceKey => Path.Combine(_root, "versions", "t\\");
    private string ConfigPath => Path.Combine(_root, "instance.json");
    private readonly IJavaSelectionConfigAccess _savedConfigAccess;
    private readonly Func<ConcurrentList<Java>> _savedJavaListReader;
    private FakeSelectionConfigs _configs = null!;
    private IConfigProvider? _provider;   //测试内单例：Set/Get/HasValue 须同一 provider 实例（缓存一致性）

    public JavaSelectionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "versions", "t"));
        SettingsService.Configure(_root);   //全局 Settings.json 落在临时目录
        _savedConfigAccess = JavaSelection.ConfigAccess;
        _savedJavaListReader = Configs.JavaListReader;
    }
    public void Dispose()
    {
        JavaSelection.ConfigAccess = _savedConfigAccess;
        Configs.JavaListReader = _savedJavaListReader;
        JavaSelection.JavaListFetcher = null;
        JavaSelection.SearchFoldersOverride = null;
        JavaSelection.CheckAsyncOverride = null;
        JavaSelection.ResetDownloadListWorker();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    #region 注入面

    /// <summary>内存配置面：隔离全局 Configs（ConfigProviders.Default 落真实 %APPDATA%，测试不可触碰）。
    /// JavaForced 挂接真实 PreviewGet 处理器（Configs.JavaForcedPreviewGet，裁定 7），其 JavaList 成员判定经 JavaListReader 覆写指向本面列表。</summary>
    private sealed class FakeSelectionConfigs : IJavaSelectionConfigAccess
    {
        public ConcurrentList<Java> List = new();
        public DynamicConfigEntry<Java> Forced { get; } = new("InstanceForcedJava", null);
        public DynamicConfigEntry<bool> Migrated { get; } = new("InstanceMigratedJava", false);
        public int ForcedResetCount;

        public FakeSelectionConfigs(bool wirePreviewGet = true)
        {
            if (wirePreviewGet) Forced.PreviewGet += Configs.JavaForcedPreviewGet;
        }

        public ConcurrentList<Java> JavaListGet() => List;
        public void JavaListSet(ConcurrentList<Java> value) => List = value;
        public Java? JavaForcedGet(IConfigProvider provider) => Forced.Get(provider);
        public void JavaForcedSet(Java? value, IConfigProvider provider) => Forced.Set(value, provider);
        public void JavaForcedReset(IConfigProvider provider)
        {
            ForcedResetCount++;
            Forced.Reset(provider);
        }
        public bool JavaMigratedGet(IConfigProvider provider) => Migrated.Get(provider);
        public void JavaMigratedSet(bool value, IConfigProvider provider) => Migrated.Set(value, provider);
    }

    private FakeSelectionConfigs UseFakeConfigs(bool wirePreviewGet = true)
    {
        _provider = null;
        _configs = new FakeSelectionConfigs(wirePreviewGet);
        JavaSelection.ConfigAccess = _configs;
        Configs.JavaListReader = () => _configs.List;   //真实 PreviewGet 处理器的成员判定指向隔离列表
        return _configs;
    }

    /// <summary>回调记录面：核对 UI 分支点被恰好触发。</summary>
    private sealed class RecordedUi
    {
        public int SwitchCalls;
        public List<(string Caption, string Title, bool IsWarn, bool ForceWait)> MsgBoxes = new();
        public List<string> Hints = new();
        public int UpdateListsCount;
        public int DisableCount;
        public int RestoreCount;
        public List<JavaDownloadInfo> Downloads = new();
    }

    private RecordedUi Ui { get; } = new();

    private JavaSelectionCallbacks BuildCallbacks(IWorker worker, Action<JavaDownloadInfo, CancellationToken, ProgressProvider?>? onDownload = null)
    {
        Ui.MsgBoxes.Clear();
        Ui.Hints.Clear();
        Ui.Downloads.Clear();
        Ui.SwitchCalls = 0;
        Ui.UpdateListsCount = 0;
        Ui.DisableCount = 0;
        Ui.RestoreCount = 0;
        return new JavaSelectionCallbacks
        {
            SwitchToInstanceSetup = () => Ui.SwitchCalls++,
            MyMsgBox = (caption, title, isWarn, forceWait) => Ui.MsgBoxes.Add((caption, title, isWarn, forceWait)),
            HintRed = message => Ui.Hints.Add(message),
            UpdateJavaLists = () => Ui.UpdateListsCount++,
            MigrationUiDisable = () => Ui.DisableCount++,
            MigrationUiRestore = () => Ui.RestoreCount++,
            ListRefreshWorker = worker,
            RunJavaDownload = onDownload ?? ((target, _, _) => Ui.Downloads.Add(target)),
        };
    }

    /// <summary>IWorker 假实现：Start 即完成（记录调用），供 WaitIfRunning 立即返回。</summary>
    private sealed class FakeRefreshWorker : IWorker
    {
        public int StartCount;
        public List<(CancellationToken Token, ProgressProvider? Progress)> Starts = new();

        public void Start(CancellationToken cancellationToken = default, ProgressProvider? progressOverride = null)
        {
            StartCount++;
            Starts.Add((cancellationToken, progressOverride));
        }
        public void Cancel() { }
        public bool WaitIfRunning(int millisecondsTimeout = -1, CancellationToken cancellationToken = default) => true;
        public Task<bool> WaitIfRunningAsync(int millisecondsTimeout = -1, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public bool Running => false;
        public bool HasSucceeded => true;
        public bool LastSucceeded => true;
        public ProgressProvider Progress { get; } = new();
        public event Action? Started { add { } remove { } }
        public event Action? Stopped { add { } remove { } }
        public event Action? Succeeded { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public event Action? Canceled { add { } remove { } }
    }

    private static Java JavaAt(string folder)
    {
        Java java = new(folder);
        java.isChecked = 1;   //预置已检查状态，避免触发真实进程检查
        java.available = true;
        return java;
    }

    private static Java JavaWithVersion(string folder, Version version)
    {
        Java java = JavaAt(folder);
        java.Version = version;
        return java;
    }

    private static Java BrokenJavaAt(string folder)
    {
        Java java = new(folder);
        java.isChecked = 1;   //预置检查失败状态，避免触发真实进程检查
        java.available = false;
        java.LastError = new JavaCheckError(JavaFailureReason.Unexpected, "测试用不可用 Java", folder);
        return java;
    }

    private IConfigProvider TestProvider() => _provider ??= new JsonConfigProvider(ConfigPath);

    private JavaSelectionInstance InstanceFace(Func<(ValueRange<Version> Range, string? RecommendedComponent)>? requirement = null)
        => new(InstanceKey, "t", TestProvider(), requirement ?? DefaultRequirement);

    private static (ValueRange<Version> Range, string? RecommendedComponent) DefaultRequirement()
        => throw new InvalidOperationException("分支 2/3 不读取需求表");

    /// <summary>Mojang java-runtime 清单 fixture：gamma 条目为 21.0.3（含 manifest 地址，供下载清单映射用）。</summary>
    private const string IndexJson =
        """{"windows-x64":{"java-runtime-gamma":[{"version":{"name":"21.0.3"},"manifest":{"url":"https://piston-meta.mojang.com/v1/packages/aa/bb/all.json"}}]}}""";

    private static string ThrowingFetcher() => throw new InvalidOperationException("不应触发 Mojang 清单下载");

    private static string ThrowingManifestFetcher(IEnumerable<string> _) => throw new InvalidOperationException("不应获取 manifest");

    #endregion

    #region 分支 2：版本文件夹中的 Java（旧 :119-134）

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void SelectOrDownload_branch2_searches_version_folder(bool tryFixOrHint, bool searchFinds)
    {
        UseFakeConfigs();
        _configs.Migrated.Set(true, TestProvider());   //分支测试代表迁移完成后的状态（旧 :115 迁移先行）
        SettingsService.Set("VersionArgumentJavaV2", 2, InstanceKey);
        Java java = JavaWithVersion(InstanceKey, new Version(17, 0));
        List<(bool IncludeSub, string[] Folders)> searchCalls = new();
        JavaSelection.SearchFoldersOverride = (includeSub, folders, _, _) =>
        {
            searchCalls.Add((includeSub, folders.ToArray()));
            return Task.FromResult(searchFinds ? new List<Java> { java } : new List<Java>());
        };
        Java? result = JavaSelection.SelectOrDownload(InstanceFace(), tryFixOrHint, BuildCallbacks(new FakeRefreshWorker()));

        //搜索参数逐字（旧 :123：含子目录 + {Instance.PathVersion}）
        Assert.Single(searchCalls);
        Assert.True(searchCalls[0].IncludeSub);
        Assert.Equal(new[] { InstanceKey }, searchCalls[0].Folders);
        if (searchFinds)
        {
            Assert.Same(java, result);
            Assert.Equal(0, Ui.SwitchCalls);
            Assert.Empty(Ui.MsgBoxes);
        } else
        {
            Assert.Null(result);
            if (tryFixOrHint)
            {
                //旧 :130-132：切设置页 + 警告弹窗（ForceWait）
                Assert.Equal(1, Ui.SwitchCalls);
                (string caption, string title, bool isWarn, bool forceWait) = Assert.Single(Ui.MsgBoxes);
                Assert.Equal("未找到 Java", title);
                Assert.Contains("使用版本文件夹中的 Java", caption);
                Assert.True(isWarn);
                Assert.True(forceWait);
            } else
            {
                Assert.Equal(0, Ui.SwitchCalls);
                Assert.Empty(Ui.MsgBoxes);
            }
        }
    }

    #endregion

    #region 分支 3：强制指定特定 Java（旧 :136-168）

    [Theory]
    [InlineData("unset", true)]
    [InlineData("unset", false)]
    [InlineData("ok", true)]
    [InlineData("broken", true)]
    [InlineData("broken", false)]
    [InlineData("notinlist", true)]
    [InlineData("previewget-null", true)]
    public void SelectOrDownload_branch3_forced_java_states(string mode, bool tryFixOrHint)
    {
        //notinlist 需绕过 PreviewGet 以触达旧 :148 的手动重置分支；previewget-null 用真实处理器（裁定 7 交互）
        bool wirePreviewGet = mode != "notinlist";
        FakeSelectionConfigs configs = UseFakeConfigs(wirePreviewGet);
        configs.Migrated.Set(true, TestProvider());   //分支测试代表迁移完成后的状态（旧 :115 迁移先行）
        SettingsService.Set("VersionArgumentJavaV2", 3, InstanceKey);
        Java java = JavaWithVersion(@"C:\fake\forced\", new Version(21, 0));
        if (mode is "ok" or "broken")
        {
            configs.List.Add(java);
            configs.Forced.Set(mode == "ok" ? java : BrokenJavaAt(@"C:\fake\forced\"), TestProvider());
        } else if (mode is "notinlist" or "previewget-null")
        {
            configs.Forced.Set(java, TestProvider());
        }
        JavaSelectionCallbacks callbacks = BuildCallbacks(new FakeRefreshWorker());

        Java? result = JavaSelection.SelectOrDownload(InstanceFace(), tryFixOrHint, callbacks);

        switch (mode)
        {
        case "ok":
            //旧 :168：成功找到并检查通过
            Assert.Same(java, result);
            Assert.Empty(Ui.MsgBoxes);
            Assert.Equal(0, Ui.SwitchCalls);
            Assert.Equal(0, configs.ForcedResetCount);
            Assert.Equal(0, Ui.UpdateListsCount);
            break;
        case "unset" when tryFixOrHint:
            //旧 :141-146：未指定 Java → 弹窗 + 跳过
            Assert.Null(result);
            Assert.Equal(1, Ui.SwitchCalls);
            (string caption, string title, bool isWarn, bool forceWait) = Assert.Single(Ui.MsgBoxes);
            Assert.Equal("未选择 Java", title);
            Assert.Contains("使用指定的 Java", caption);
            Assert.True(isWarn);
            Assert.True(forceWait);
            break;
        case "unset":
            Assert.Null(result);
            Assert.Empty(Ui.MsgBoxes);
            Assert.Equal(0, Ui.SwitchCalls);
            break;
        case "broken":
            //旧 :157-166：检查失败 → 重置 + 刷新列表 + Alert（hint）/Toast（无 hint）
            Assert.Null(result);
            Assert.Equal(1, configs.ForcedResetCount);
            Assert.Equal(1, Ui.UpdateListsCount);
            Assert.False(configs.Forced.HasValue(TestProvider()));   //重置已生效（同一 provider 缓存）
            Assert.Equal(tryFixOrHint ? 1 : 0, Ui.SwitchCalls);
            Assert.Empty(Ui.MsgBoxes);
            break;
        case "notinlist":
            //旧 :148-156：手动重置分支（裁定 7：与 PreviewGet 并存不去重）→ 重置 + 刷新 + 跳过，无弹窗
            Assert.Null(result);
            Assert.Equal(1, configs.ForcedResetCount);
            Assert.Equal(1, Ui.UpdateListsCount);
            Assert.Equal(1, Ui.SwitchCalls);
            Assert.Empty(Ui.MsgBoxes);
            break;
        case "previewget-null":
            //PreviewGet 自动重置后与 unset 同径（未选择 Java），且存储已被处理器自动重置（裁定 7）
            Assert.Null(result);
            Assert.Equal(0, configs.ForcedResetCount);   //处理器内 Reset 直达全局条目，不经访问面
            (string caption2, string title2, _, _) = Assert.Single(Ui.MsgBoxes);
            Assert.Equal("未选择 Java", title2);
            Assert.False(configs.Forced.HasValue(TestProvider()));
            break;
        }
    }

    [Fact]
    public void JavaForced_previewget_resets_value_absent_from_list()
    {
        //裁定 7：Configs.cs:24-31 的 PreviewGet 自动重置语义（真实处理器经隔离列表挂接）
        FakeSelectionConfigs configs = UseFakeConfigs();
        var provider = new JsonConfigProvider(Path.Combine(_root, "forced.json"));
        Java java = JavaAt(@"C:\fake\forced-previewget\");
        configs.Forced.Set(java, provider);
        Assert.True(configs.Forced.HasValue(provider));
        Assert.Null(configs.Forced.Get(provider));   //不在列表 → 取值置空
        Assert.False(configs.Forced.HasValue(provider));   //并自动 Reset 存储
    }

    [Fact]
    public void JavaForced_previewget_keeps_value_present_in_list()
    {
        //裁定 7：在列表中的值原样保留
        FakeSelectionConfigs configs = UseFakeConfigs();
        var provider = new JsonConfigProvider(Path.Combine(_root, "forced.json"));
        Java java = JavaAt(@"C:\fake\forced-previewget\");
        configs.List.Add(java);
        configs.Forced.Set(java, provider);
        Assert.Same(java, configs.Forced.Get(provider));
        Assert.True(configs.Forced.HasValue(provider));
    }

    #endregion

    #region 自动分支：需求区间匹配（旧 :170-205）

    [Fact]
    public void SelectOrDownload_auto_selects_java_in_range_from_list()
    {
        UseFakeConfigs();
        Java java8 = JavaWithVersion(@"C:\fake\j8\", new Version(8, 0, 19));
        Java java21 = JavaWithVersion(@"C:\fake\j21\", new Version(21, 0, 3));
        _configs.List.Add(java8);
        _configs.List.Add(java21);
        JavaSelection.JavaListFetcher = ThrowingFetcher;   //既有列表命中，不应触达清单下载
        ProgressProvider p = new();

        Java? result = JavaSelection.SelectOrDownload(ValueRange<Version>.AtLeast(new Version(17, 0)), false, BuildCallbacks(new FakeRefreshWorker()), p: p);

        Assert.Same(java21, result);
        //进度标记（旧 :195 Set(0.25) + :197 Skip）
        Assert.Equal((0.25, 0.75), p.GetRaw());
    }

    [Fact]
    public void SelectOrDownload_auto_calls_requirement_provider_twice_like_vb()
    {
        //旧 :173-176 对 GetJavaRequirement 调用两次（Range 与 RecommendedComponent 各一次），逐字保持
        UseFakeConfigs();
        Java java21 = JavaWithVersion(@"C:\fake\j21\", new Version(21, 0, 3));
        _configs.List.Add(java21);
        JavaSelection.JavaListFetcher = ThrowingFetcher;
        int providerCalls = 0;

        Java? result = JavaSelection.SelectOrDownload(InstanceFace(() =>
        {
            providerCalls++;
            return (ValueRange<Version>.AtLeast(new Version(21, 0)), "java-runtime-gamma");
        }), false, BuildCallbacks(new FakeRefreshWorker()));

        Assert.Same(java21, result);
        Assert.Equal(2, providerCalls);
    }

    [Fact]
    public void SelectOrDownload_conflicting_range_returns_null_and_skips()
    {
        //旧 :178：区间为空 → 抛异常 → 外层 Catch（旧 :300-304）吞掉并跳过
        UseFakeConfigs();
        JavaSelection.JavaListFetcher = ThrowingFetcher;
        ProgressProvider p = new();

        Java? result = JavaSelection.SelectOrDownload(
            ValueRange<Version>.Open(new Version(9, 0), new Version(9, 0)), true, BuildCallbacks(new FakeRefreshWorker()), p: p);

        Assert.Null(result);
        Assert.Equal((0.0, 1.0), p.GetRaw());   //未走到 Set(0.25)，直接 Skip
    }

    [Fact]
    public void SelectOrDownload_auto_without_hint_returns_null_after_research()
    {
        //旧 :199-205：锁内重搜（#3797）后仍无 → TryFixOrHint=False 只静默返回，不触达清单下载
        UseFakeConfigs();
        FakeRefreshWorker worker = new();
        JavaSelection.JavaListFetcher = ThrowingFetcher;
        ProgressProvider p = new();

        Java? result = JavaSelection.SelectOrDownload(ValueRange<Version>.AtLeast(new Version(21, 0)), false, BuildCallbacks(worker), p: p);

        Assert.Null(result);
        Assert.Equal(1, worker.StartCount);   //锁内重搜恰一次（旧 :202）
        Assert.NotNull(worker.Starts[0].Progress);   //SplitTo(0.3) 进度子项
        Assert.Empty(Ui.MsgBoxes);
        Assert.Equal(0, Ui.SwitchCalls);
    }

    public static TheoryData<ValueRange<Version>, string> NoMatchDescriptions() => new()
    {
        //旧 :252-283 的手装提示文案表（逐行）
        { ValueRange<Version>.AtLeast(new Version(21, 0)), " Java 21 或更高版本的 Java" },
        { ValueRange<Version>.LessThan(new Version(9, 0)), " Java 8 或更低版本的 Java" },
        { ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(9, 0)), " Java 8" },
        { ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(10, 0)), " Java 8 ~ 9" },
        { ValueRange<Version>.Closed(new Version(8, 0), new Version(10, 0)), " Java [8.0, 10.0]" },   //摆烂行
        { ValueRange<Version>.GreaterThan(new Version(17, 0, 1)), "版本高于 17.0.1 的 Java" },
        { ValueRange<Version>.AtMost(new Version(8, 0, 320)), " Java 8.0.320 或更低版本的 Java" },
    };

    [Theory, MemberData(nameof(NoMatchDescriptions))]
    public void SelectOrDownload_no_match_prompts_manual_install_with_range_description(ValueRange<Version> range, string expectedDescription)
    {
        UseFakeConfigs();
        JavaSelection.JavaListFetcher = static () => """{"windows-x64":{}}""";   //空清单 → 无匹配
        JavaSelectionCallbacks callbacks = BuildCallbacks(new FakeRefreshWorker());

        Java? result = JavaSelection.SelectOrDownload(range, true, callbacks);

        Assert.Null(result);
        (string caption, string title, bool isWarn, bool forceWait) = Assert.Single(Ui.MsgBoxes);
        Assert.Equal("需要手动安装 Java", title);
        Assert.Contains("PCL 没能找到合适的 Java。", caption);
        Assert.Contains($"请在网上搜索并手动安装{expectedDescription}， 然后在【设置 → 启动 → 高级选项 → Java 列表】中导入它。", caption);
        Assert.False(isWarn);   //旧 :284 未传 IsWarn
        Assert.False(forceWait);
    }

    #endregion

    #region 自动分支：Mojang 清单匹配与下载（旧 :207-297）

    [Fact]
    public void SelectOrDownload_auto_downloads_matching_component_and_reselects()
    {
        UseFakeConfigs();
        FakeRefreshWorker worker = new();
        Java downloaded = JavaWithVersion(@"C:\fake\runtime\", new Version(21, 0, 3));
        JavaSelection.JavaListFetcher = static () => IndexJson;
        JavaSelectionCallbacks callbacks = BuildCallbacks(worker, (target, _, _) =>
        {
            Ui.Downloads.Add(target);
            _configs.List.Add(downloaded);   //下载完成后出现在列表中（宿主层刷新语义）
        });

        Java? result = JavaSelection.SelectOrDownload(ValueRange<Version>.AtLeast(new Version(21, 0)), true, callbacks);

        Assert.Same(downloaded, result);
        JavaDownloadInfo target = Assert.Single(Ui.Downloads);
        Assert.Equal("java-runtime-gamma", target.ComponentName);
        Assert.Equal(new Version(21, 0, 3, 0), target.JavaVersion);
        Assert.Equal(1, worker.StartCount);   //锁内重搜恰一次（旧 :202）
        Assert.True(JavaSelection.DownloadListWorker.HasSucceeded);

        //第二次调用：worker 缓存语义（旧 :240 HasSucceeded → 不再触发下载）且既有列表已命中
        Java? second = JavaSelection.SelectOrDownload(ValueRange<Version>.AtLeast(new Version(21, 0)), true, callbacks);
        Assert.Same(downloaded, second);
        Assert.Single(Ui.Downloads);   //下载仍只一次
    }

    [Fact]
    public void SelectOrDownload_auto_preferred_component_wins_over_range()
    {
        //旧 :246-248：FirstOfAny 双谓词——优先 RecommendedComponent，回退区间匹配
        UseFakeConfigs();
        JavaSelection.JavaListFetcher = static () =>
            """{"windows-x64":{"java-runtime-alpha":[{"version":{"name":"8.0.19"},"manifest":{"url":"https://piston-meta.mojang.com/a"}}],"java-runtime-gamma":[{"version":{"name":"17.0.8"},"manifest":{"url":"https://piston-meta.mojang.com/g"}}]}}""";
        Java downloaded = JavaWithVersion(@"C:\fake\runtime-a\", new Version(8, 0, 19));
        JavaSelectionCallbacks callbacks = BuildCallbacks(new FakeRefreshWorker(), (target, _, _) =>
        {
            Ui.Downloads.Add(target);
            _configs.List.Add(downloaded);
        });

        //区间为全域（两者都在区间内），指定组件未命中（核内 RecommendedComponent 为空）→ 区间回退命中首个（alpha）
        Java? result = JavaSelection.SelectOrDownload(ValueRange<Version>.All(), true, callbacks);

        Assert.Same(downloaded, result);
        Assert.Equal("java-runtime-alpha", Assert.Single(Ui.Downloads).ComponentName);
    }

    [Fact]
    public void SelectOrDownload_download_without_resulting_java_throws_masked()
    {
        //旧 :295-297：下载后仍找不到 → 抛异常 → 外层 Catch 掩蔽（p.Skip + null）
        UseFakeConfigs();
        JavaSelection.JavaListFetcher = static () => IndexJson;
        JavaSelectionCallbacks callbacks = BuildCallbacks(new FakeRefreshWorker());   //下载回调不添加 Java

        Java? result = JavaSelection.SelectOrDownload(ValueRange<Version>.AtLeast(new Version(21, 0)), true, callbacks);

        Assert.Null(result);
        JavaDownloadInfo download = Assert.Single(Ui.Downloads);
        Assert.Equal("java-runtime-gamma", download.ComponentName);
    }

    [Fact]
    public async Task SelectOrDownload_concurrent_threads_download_only_once()
    {
        //#3797（旧 :199 SyncLock JavaLock）：两线程并发进入自动分支，清单只抓一次、下载只执行一次
        UseFakeConfigs();
        FakeRefreshWorker worker = new();
        Java downloaded = JavaWithVersion(@"C:\fake\runtime\", new Version(21, 0, 3));
        int fetchCount = 0;
        int downloadCount = 0;
        using var fetchStarted = new ManualResetEventSlim(false);
        using var fetchGate = new ManualResetEventSlim(false);
        JavaSelection.JavaListFetcher = () =>
        {
            fetchStarted.Set();
            Interlocked.Increment(ref fetchCount);
            fetchGate.Wait(10000);
            return IndexJson;
        };
        JavaSelectionCallbacks callbacks = BuildCallbacks(worker, (target, _, _) =>
        {
            Interlocked.Increment(ref downloadCount);
            _configs.List.Add(downloaded);
        });
        ValueRange<Version> range = ValueRange<Version>.AtLeast(new Version(21, 0));

        //线程 A 先行进入锁并在清单抓取处阻塞；线程 B 随后启动（先空选 → 阻塞于 JavaLock）
        Task<Java?> taskA = Task.Run(() => JavaSelection.SelectOrDownload(range, true, callbacks));
        Assert.True(fetchStarted.Wait(10000), "线程 A 未进入清单抓取");
        Task<Java?> taskB = Task.Run(() => JavaSelection.SelectOrDownload(range, true, callbacks));
        //持锁期间列表仍为空（A 的下载回调在放行后才执行），B 不经锁无法完成——互斥单向断言
        Thread.Sleep(50);   //给 B 到达锁的调度余量
        Assert.False(taskB.IsCompleted, "线程 B 在 A 持锁期间完成选择（JavaLock 互斥失效）");
        fetchGate.Set();
        Java?[] results = await Task.WhenAll(taskA, taskB);

        Assert.All(results, r => Assert.NotNull(r));
        Assert.Same(downloaded, results[0]);
        Assert.Same(downloaded, results[1]);
        Assert.Equal(1, fetchCount);      //清单只抓一次
        Assert.Equal(1, downloadCount);   //下载只执行一次
        Assert.Equal(2, worker.StartCount);   //两线程各触发一次锁内重搜（串行进入）
    }

    #endregion

    #region 迁移决策（旧 MigrateInstanceJavaSettings :318-363）

    [Fact]
    public void MigrateInstanceJavaSettings_already_migrated_skips()
    {
        //旧 :319：JavaMigrated=True → p.Skip 直接返回，UI 两点不触达
        UseFakeConfigs();
        _configs.Migrated.Set(true, TestProvider());
        ProgressProvider p = new();
        JavaSelection.SearchFoldersOverride = static (_, _, _, _) => throw new InvalidOperationException("已迁移不应搜索");

        JavaSelection.MigrateInstanceJavaSettings(InstanceFace(), BuildCallbacks(new FakeRefreshWorker()), p: p);

        Assert.Equal((0.0, 1.0), p.GetRaw());
        Assert.Equal(0, Ui.DisableCount);
        Assert.Equal(0, Ui.RestoreCount);
    }

    [Theory]
    [InlineData(false, 0)]   //搜索无结果 → 自动选择（旧 :349-352）
    [InlineData(true, 2)]    //搜索发现 Java → 优先版本文件夹（旧 :345-348）
    public void MigrateInstanceJavaSettings_no_forced_setting_uses_search(bool searchFinds, int expectedV2)
    {
        UseFakeConfigs();
        SettingsService.Set("VersionArgumentJavaSelect", "使用全局设置", InstanceKey);   //不以 { 开头（旧 :325）
        List<(bool IncludeSub, string[] Folders)> searchCalls = new();
        JavaSelection.SearchFoldersOverride = (includeSub, folders, _, _) =>
        {
            searchCalls.Add((includeSub, folders.ToArray()));
            return Task.FromResult(searchFinds ? new List<Java> { JavaAt(InstanceKey) } : new List<Java>());
        };
        ProgressProvider p = new();

        JavaSelection.MigrateInstanceJavaSettings(InstanceFace(), BuildCallbacks(new FakeRefreshWorker()), p: p);

        //UI 禁用/恢复两点各恰一次（旧 :320/:356-361）
        Assert.Equal(1, Ui.DisableCount);
        Assert.Equal(1, Ui.RestoreCount);
        Assert.Single(searchCalls);
        Assert.Equal(new[] { InstanceKey }, searchCalls[0].Folders);
        Assert.Equal(expectedV2, SettingsService.Get<int>("VersionArgumentJavaV2", InstanceKey));
        Assert.True(_configs.Migrated.Get(TestProvider()));   //完成（旧 :354）
        Assert.Empty(Ui.Hints);
    }

    [Fact]
    public void MigrateInstanceJavaSettings_forced_java_usable_sets_v2_3()
    {
        //旧 :328-342：有效强制 Java → 加入列表底部 + JavaForced.Set + V2=3 + 刷新 + 完成
        UseFakeConfigs();
        SettingsService.Set("VersionArgumentJavaSelect", """{"Path":"C:\\fake\\migrate-forced\\"}""", InstanceKey);
        ProgressProvider p = new();

        JavaSelection.CheckAsyncOverride = static (java, _) =>
            Task.FromResult(PCLCS.Results.Result<Java, JavaCheckError>.Ok(java));   //核内构造的 Java 视为可用（无进程）
        JavaSelection.MigrateInstanceJavaSettings(InstanceFace(), BuildCallbacks(new FakeRefreshWorker()), p: p);

        Java migrated = Assert.Single(_configs.List);   //加入列表底部
        Assert.Equal(@"C:\fake\migrate-forced\", migrated.Folder);
        Assert.Equal(@"C:\fake\migrate-forced\", _configs.Forced.Get(TestProvider())!.Folder);
        Assert.Equal(3, SettingsService.Get<int>("VersionArgumentJavaV2", InstanceKey));
        Assert.Equal(1, Ui.UpdateListsCount);
        Assert.True(_configs.Migrated.Get(TestProvider()));
        Assert.Equal(1, Ui.DisableCount);
        Assert.Equal(1, Ui.RestoreCount);
        Assert.Empty(Ui.Hints);
        Assert.Equal((1.0, 0.0), p.GetRaw());   //p.Finish（旧 :342）
    }

    [Fact]
    public void MigrateInstanceJavaSettings_broken_forced_java_hints_and_falls_back_to_search()
    {
        //旧 :329-331：检查失败 → 红字提示 + p.Set(0.4) → 落到 NoForcedJava 段
        UseFakeConfigs();
        SettingsService.Set("VersionArgumentJavaSelect", """{"Path":"C:\\fake\\broken\\"}""", InstanceKey);
        JavaSelection.SearchFoldersOverride = (_, _, _, _) => Task.FromResult(new List<Java> { JavaAt(InstanceKey) });
        JavaSelection.CheckAsyncOverride = static (java, _) =>
            Task.FromResult(PCLCS.Results.Result<Java, JavaCheckError>.Err(
                new JavaCheckError(JavaFailureReason.Unexpected, "测试用不可用 Java", java.Folder)));   //核内构造的 Java 视为不可用（无进程）

        JavaSelection.MigrateInstanceJavaSettings(InstanceFace(), BuildCallbacks(new FakeRefreshWorker()));

        string hint = Assert.Single(Ui.Hints);
        Assert.Equal("该版本的设置中强制指定的 Java 存在异常，该设置已重置", hint);
        Assert.Equal(2, SettingsService.Get<int>("VersionArgumentJavaV2", InstanceKey));   //搜索命中 → V2=2
        Assert.True(_configs.Migrated.Get(TestProvider()));
        Assert.Empty(_configs.List);   //不可用的 Java 未加入列表
    }

    [Fact]
    public void MigrateInstanceJavaSettings_invalid_forced_json_propagates()
    {
        //旧 :328：解析失败向外抛（宿主层由 SelectOrDownloadJava 外层 Catch 掩蔽），Finally 仍恢复 UI
        UseFakeConfigs();
        SettingsService.Set("VersionArgumentJavaSelect", "{不是 JSON", InstanceKey);

        Assert.ThrowsAny<Exception>(() =>
            JavaSelection.MigrateInstanceJavaSettings(InstanceFace(), BuildCallbacks(new FakeRefreshWorker())));

        Assert.Equal(1, Ui.DisableCount);
        Assert.Equal(1, Ui.RestoreCount);   //旧 :355-361 Finally
    }

    #endregion

    #region 下载清单产出（旧 CreateJavaDownloadLoader :546-558 决策面）

    /// <summary>构造清单目标（manifest 地址与组件名与旧 :546-548 一致）。</summary>
    private static JavaDownloadInfo DownloadTarget() => new(
        "java-runtime-gamma",
        JsonNode.Parse("""{"version":{"name":"21.0.3"},"manifest":{"url":"https://piston-meta.mojang.com/v1/packages/aa/bb/all.json"}}"""),
        new Version(21, 0, 3, 0));

    private const string ManifestJson =
        """
        {"files":{
          "bin/java.exe":{"downloads":{"raw":{"url":"https://piston-data.mojang.com/v1/objects/aaa/java.exe","size":123,"sha1":"hash-java"}}},
          "bin/server.jar":{"downloads":{"raw":{"url":"https://piston-data.mojang.com/v1/objects/bbb/server.jar","size":456,"sha1":"12976a6c2b227cbac58969c1455444596c894656"}}},
          "bin/lib1":{"downloads":{"raw":{"url":"https://piston-data.mojang.com/v1/objects/ccc/lib1","size":10,"sha1":"c80e4bab46e34d02826eab226a4441d0970f2aba"}}},
          "bin/lib2":{"downloads":{"raw":{"url":"https://piston-data.mojang.com/v1/objects/ddd/lib2","size":11,"sha1":"84d2102ad171863db04e7ee22a259d1f6c5de4a5"}}},
          "meta.json":{"downloads":{"raw":null}},
          "no-raw.json":{"type":"directory"}
        }}
        """;

    [Fact]
    public void CreateJavaDownloadFiles_maps_netfiles_and_skips_three_hashes()
    {
        //#3827：3 个无意义大量重复文件跳过；无 raw 的条目跳过（旧 :552-557）
        List<IEnumerable<string>> fetchCalls = new();
        (List<NetFile> Files, string BaseDir) = JavaSelection.CreateJavaDownloadFiles(
            DownloadTarget(), preferMojang: true,
            urls =>
            {
                fetchCalls.Add(urls);
                return ManifestJson;
            });

        Assert.Single(fetchCalls);
        NetFile file = Assert.Single(Files);
        Assert.StartsWith(BaseDir, file.LocalPath);
        Assert.EndsWith("\\.minecraft\\runtime\\java-runtime-gamma\\bin/java.exe", file.LocalPath);
        Assert.NotNull(file.Check);
        Assert.Equal(123, file.Check!.ActualSize);
        Assert.Equal("hash-java", file.Check!.Hash);
        //BaseDir 形状（旧 :549：{Paths.AppData}.minecraft\runtime\{ComponentName}\）
        Assert.EndsWith("\\.minecraft\\runtime\\java-runtime-gamma\\", BaseDir);
    }

    [Theory]
    [InlineData(true, "https://piston-meta.mojang.com/v1/packages/aa/bb/all.json", "https://piston-data.mojang.com/v1/objects/aaa/java.exe")]
    [InlineData(false, "https://bmclapi2.bangbang93.com/v1/packages/aa/bb/all.json", "https://bmclapi2.bangbang93.com/v1/objects/aaa/java.exe")]
    public void CreateJavaDownloadFiles_orders_sources_by_prefer_mojang(bool preferMojang, string expectedManifestFirstUrl, string expectedFileFirstUrl)
    {
        //旧 :548/:559-560：DlSourceOrder({地址}, {地址.Replace("piston-(meta|data).mojang.com", "bmclapi2.bangbang93.com")})
        string capturedFirst = "";
        (List<NetFile> Files, string BaseDir) = JavaSelection.CreateJavaDownloadFiles(
            DownloadTarget(), preferMojang,
            urls =>
            {
                capturedFirst = urls.First();
                return ManifestJson;
            });

        Assert.Equal(expectedManifestFirstUrl, capturedFirst);   //manifest 抓取的源顺序
        Assert.Equal(expectedFileFirstUrl, Files[0].Sources[0].Url);   //文件下载的源顺序
        Assert.Equal(2, Files[0].Sources.Count);   //官方 + 镜像各一
    }

    #endregion
}