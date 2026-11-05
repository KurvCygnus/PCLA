using MeloongCore;
using MeloongCore.Extensions;
using PCLCS.Minecraft.Download;
using PCLCS.Net;
using PCLCS.Results;
using PCLCS.Settings;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

//命名空间留 PCLCS 根：PCLCS.Java（类）已占用 Java 之名，PCLCS.Java 子命名空间会在同层遮蔽对 Java 类的引用（CS0118，已实测）
namespace PCLCS;

/// <summary>
/// Java 下载信息（旧 ModJava.vb :369-373 的 JavaDownloadInfo 随迁）：
/// Mojang piston-meta java-runtime 清单的单个条目，Java 为该条目原文（STJ JsonNode，与宿主层 Newtonsoft JObject 等价承载）。
/// </summary>
/// <param name="ComponentName">组件名（"java-runtime-gamma" 等，作为下载目标文件夹名）。</param>
/// <param name="Java">该条目的 JSON 原文（version/manifest 由匹配与下载清单读取）。</param>
/// <param name="JavaVersion">解析出的 Java 版本号。</param>
public sealed record JavaDownloadInfo(string ComponentName, JsonNode? Java, Version JavaVersion);

/// <summary>
/// 版本实例面：选择编排与迁移核所需的 McInstance 提取基元（宿主层转发时构造，与 JavaRequirementQuery 同模式）。
/// </summary>
/// <param name="PathVersion">版本文件夹路径（旧 Instance.PathVersion：分支 2/迁移的搜索目标与 instanceKey 面）。</param>
/// <param name="Name">版本名（旧 Instance.Name：迁移日志）。</param>
/// <param name="Config">版本独立配置提供器（旧 Instance.Config：JavaForced/JavaMigrated 的实例侧）。</param>
/// <param name="RequirementProvider">需求区间提供者（旧 GetJavaRequirement(Instance) :94-106 的宿主转发；核内按旧行为调用两次）。</param>
public sealed record JavaSelectionInstance(
    string PathVersion,
    string Name,
    IConfigProvider Config,
    Func<(ValueRange<Version> Range, string? RecommendedComponent)> RequirementProvider
);

/// <summary>
/// 宿主回调面：选择编排与迁移核的 UI/宿主机制接缝（旧 ModJava.vb 的内联调用点，签名按旧分支实际读取面收敛）。
/// 宿主层以真实 McInstance 闭包承载各点；为空的成员在核内以 ?.Invoke 触达（与旧 If IsNot Nothing 分支同径）。
/// </summary>
public sealed class JavaSelectionCallbacks
{
    /// <summary>切到实例设置页（旧 SwitchToInstanceSetup :306-312）。</summary>
    public Action? SwitchToInstanceSetup;
    /// <summary>模态弹窗（旧 MyMsgBox :231：文案与 IsWarn/ForceWait 由核内 1:1 供给）。</summary>
    public Action<string, string, bool, bool>? MyMsgBox;
    /// <summary>红字提示（旧 Hint(…, HintType.Red)；本核唯一用到的提示级别）。</summary>
    public Action<string>? HintRed;
    /// <summary>刷新设置页 Java 列表显示（旧 UpdateJavaLists :25-31）。</summary>
    public Action? UpdateJavaLists;
    /// <summary>迁移开始时禁用实例设置页的 Java 下拉框（旧 :320：FrmInstanceSetup 判空与 RunInUiWait 由宿主层承载）。</summary>
    public Action? MigrationUiDisable;
    /// <summary>迁移 Finally 恢复（旧 :356-361：RefreshSettings ×2 + IsEnabled = True）。</summary>
    public Action? MigrationUiRestore;
    /// <summary>Java 列表搜索 worker（旧 JavaListRefreshWorker :18：核内 WaitIfRunning :183 与 Start :202）。</summary>
    public IWorker? ListRefreshWorker;
    /// <summary>构建并执行 Java 下载加载器（旧 :289-294：CreateJavaDownloadLoader + WaitForExit(IsForceRestart:=True) + Finally Cancel 由宿主承载）。</summary>
    public Action<JavaDownloadInfo, CancellationToken, ProgressProvider?>? RunJavaDownload;
}

/// <summary>JavaSelection 的配置访问面：默认直读全局 Configs（与旧 ModJava.vb 直读一致，含 JavaForced PreviewGet）；测试注入内存实现以隔离真实 AppData。</summary>
internal interface IJavaSelectionConfigAccess
{
    ConcurrentList<Java> JavaListGet();
    void JavaListSet(ConcurrentList<Java> value);
    Java? JavaForcedGet(IConfigProvider provider);
    void JavaForcedSet(Java? value, IConfigProvider provider);
    void JavaForcedReset(IConfigProvider provider);
    bool JavaMigratedGet(IConfigProvider provider);
    void JavaMigratedSet(bool value, IConfigProvider provider);
}

/// <summary>
/// Java 选择编排核（自宿主层 ModJava.vb 移植）：
/// 选择编排（旧 SelectOrDownloadJava :113-305 决策面逐行：迁移判定 → 分支 2 版本文件夹搜索 / 分支 3 JavaForced（PreviewGet 自动重置 + CheckAsync 验活）/
/// 自动 = 需求区间 → 既有列表 → 搜索等待与重搜（#3797 JavaLock）→ Mojang 清单匹配（JavaListFetcher）→ 无匹配手装提示 → 下载重选），
/// 版本独立 Java 设置迁移决策（旧 MigrateInstanceJavaSettings :318-363：UI 禁用/恢复两点经回调），
/// 下载清单产出（旧 CreateJavaDownloadLoader :546-558 决策面：piston-meta manifest → NetFile 清单，3 哈希跳过 #3827；LoaderDownload+LoaderWorker 组装留宿主层）。
/// 搜索触发、弹窗与 Loader 组装留在宿主层。
/// </summary>
public static class JavaSelection
{

    // ============================================ 宿主与测试接线面 ============================================

    /// <summary>Mojang Java 列表的下载结果供给（宿主层经 NetRequestByLoader + DlVersionListOrder 供给，保持 1:1 下载机制；ModJava.vb Sub New 接线）。</summary>
    public static Func<string>? JavaListFetcher { get; set; }

    //* 测试注入面（JavaRequirement.SortAsyncOverride 同模式）：分支 2 与迁移的版本文件夹搜索替换点。
    //* 默认 JavaUtils.SearchFoldersAsync（真实文件系统搜索 + 真实进程检查）；测试注入假搜索，宿主层不接线
    internal static Func<bool, IEnumerable<string>, CancellationToken, ProgressProvider?, Task<List<Java>>>? SearchFoldersOverride;

    //* 测试注入面（SortAsyncOverride 同模式）：Java 检查替换点——迁移与分支 3 的 Java 由核内构造/来自列表，
    //* 迁移构造的 Java 无法由测试预置检查状态，故提供整体替换；默认 Java.CheckAsync（真实进程检查），宿主层不接线
    internal static Func<Java, CancellationToken, Task<Result<Java, JavaCheckError>>>? CheckAsyncOverride;

    /// <summary>检查 Java 可用性（旧 :157/:185/:329 的 CheckAsync(c).Run() 调用面；测试注入替换点）。</summary>
    private static Result<Java, JavaCheckError> Check(Java java, CancellationToken c)
        => (CheckAsyncOverride?.Invoke(java, c) ?? java.CheckAsync(c)).Run();

    //* 测试注入面（IJavaConfigAccess 同模式）：默认直读全局 Configs（与旧 ModJava.vb 直读一致，含 JavaForced PreviewGet）；
    //* 测试注入内存实现以隔离真实 %APPDATA%（ConfigProviders.Default 落真实用户目录）
    internal static IJavaSelectionConfigAccess ConfigAccess { get; set; } = new GlobalJavaSelectionConfigAccess();

    /// <summary>默认配置访问面：直读全局 Configs（旧 ModJava.vb :138/:148-158/:319-341 的读取目标）。</summary>
    private sealed class GlobalJavaSelectionConfigAccess : IJavaSelectionConfigAccess
    {
        public ConcurrentList<Java> JavaListGet() => Configs.JavaList.Get()!;
        public void JavaListSet(ConcurrentList<Java> value) => Configs.JavaList.Set(value);
        public Java? JavaForcedGet(IConfigProvider provider) => Configs.JavaForced.Get(provider);
        public void JavaForcedSet(Java? value, IConfigProvider provider) => Configs.JavaForced.Set(value, provider);
        public void JavaForcedReset(IConfigProvider provider) => Configs.JavaForced.Reset(provider);
        public bool JavaMigratedGet(IConfigProvider provider) => Configs.JavaMigrated.Get(provider);
        public void JavaMigratedSet(bool value, IConfigProvider provider) => Configs.JavaMigrated.Set(value, provider);
    }

    //* 测试注入面（MojangApiBase Dispose 同模式）：Mojang Java 列表 worker 的隔离重建。
    //* 旧 :209 的 Static DownloadListWorker 为应用生命周期单例（HasSucceeded 后不再重新下载）；测试间重建以隔离缓存
    internal static RedoableWorker<List<JavaDownloadInfo>> DownloadListWorker { get; private set; } = CreateDownloadListWorker();

    internal static void ResetDownloadListWorker() => DownloadListWorker = CreateDownloadListWorker();

    /// <summary>#3797 的 JavaLock（旧 :313）：自动分支的重搜/下载/重选整段互斥，保证多实例并发选择时清单只抓取、下载只执行一次。</summary>
    private static readonly object JavaLock = new();

    // ============================================ 选择编排核 ============================================

    /// <summary>
    /// 根据版本或 Java 范围要求寻找适合的 Java（旧 SelectOrDownloadJava :113-305 决策面逐行）。
    /// 若指定了 <paramref name="tryFixOrHint"/>，则会在找不到时会尝试下载 Java 或提示用户下载，否则只会静默运行。
    /// </summary>
    /// <returns>最适合的 Java。若找不到或失败则返回 null，除非取消否则不会抛出异常。</returns>
    public static Java? SelectOrDownload(
        OneOf<ValueRange<Version>, JavaSelectionInstance> target, bool tryFixOrHint,
        JavaSelectionCallbacks callbacks, CancellationToken c = default, ProgressProvider? p = null)
    {
        try
        {
            JavaSelectionInstance? instance = target.IsT2() ? target.AsT2() : null;   //旧 :116（Target.Is(Of McInstance)）
            if (instance is not null)
            {
                MigrateInstanceJavaSettings(instance, callbacks, c, p?.SplitTo(0.2));   //迁移（旧 :115）
            }
            //旧 :117：无实例按自动分支处理（0），分支值经 instanceKey 面读取
            int versionArgumentJavaV2 = instance is null
                ? 0
                : JavaRequirement.GetInstanceSetting<int>("VersionArgumentJavaV2", instance.PathVersion);
            switch (versionArgumentJavaV2)
            {
            case 2:
            {
                JavaSelectionInstance inst = instance!;   //分支 2/3 仅在实例面可达（旧 :117 无实例时为 0 → Case Else）
                //============================= 使用版本文件夹中的 Java =============================
                //查找版本文件夹下的 Java（旧 :122-123）
                Logger.Info($"版本设置要求使用版本文件夹中的 Java：{inst.PathVersion}");
                Java? javaFound = SearchFolders(true, [inst.PathVersion], c, p?.SplitTo(1)).Run().FirstOrDefault();
                if (javaFound is not null)
                {
                    Logger.Info($"已发现版本文件夹中的 Java：{javaFound}");
                    return javaFound;
                }
                //未能找到（旧 :129-134）
                if (tryFixOrHint)
                {
                    callbacks.SwitchToInstanceSetup?.Invoke();
                    callbacks.MyMsgBox?.Invoke(
                        "该版本的版本设置选择了 “使用版本文件夹中的 Java”，但版本文件夹里没能找到 Java。\r\n请先修改启动选项中的 Java 设置，或在版本文件夹中放一个 Java，然后再试。",
                        "未找到 Java", true, true);   //旧 :131-132（vbLQ/vbRQ/vbCrLf 逐字）
                }
                return null;
            }
            case 3:
            {
                JavaSelectionInstance inst = instance!;
                //=============================== 强制指定特定 Java ===============================
                Java? chosenJava = ConfigAccess.JavaForcedGet(inst.Config);   //旧 :138（PreviewGet 自动重置 1:1）
                Logger.Info($"版本设置中强制指定的 Java：{inst.PathVersion} → {chosenJava}");
                if (chosenJava is null)
                {
                    if (tryFixOrHint)
                    {
                        callbacks.SwitchToInstanceSetup?.Invoke();
                        callbacks.MyMsgBox?.Invoke(
                            "该版本的版本设置选择了 “使用指定的 Java”，但还没有指定任何 Java。\r\n请先选择你想使用的 Java，然后再试。",
                            "未选择 Java", true, true);   //旧 :143-144
                    }
                    p?.Skip();
                    return null;   //<==== 设置中未指定 Java（旧 :146）
                }
                if (!ConfigAccess.JavaListGet().Contains(chosenJava))
                {
                    ConfigAccess.JavaForcedReset(inst.Config);   //旧 :149（手动重置保留，不与 PreviewGet 顺手去重）
                    callbacks.UpdateJavaLists?.Invoke();
                    if (tryFixOrHint)
                    {
                        callbacks.SwitchToInstanceSetup?.Invoke();
                        Logger.Error("版本设置中指定的 Java 不在 Java 列表中，请重新选择一个 Java！", LogBehavior.Alert);   //旧 :153
                    }
                    p?.Skip();
                    return null;   //<==== 设置中指定的 Java 不在列表中（旧 :155）
                }
                if (!Check(chosenJava, c).IsSuccess)
                {
                    ConfigAccess.JavaForcedReset(inst.Config);   //旧 :158
                    callbacks.UpdateJavaLists?.Invoke();
                    if (tryFixOrHint)
                    {
                        callbacks.SwitchToInstanceSetup?.Invoke();
                        Logger.Error("版本设置中指定的 Java 已无法使用，请重新选择一个 Java，然后再试。", LogBehavior.Alert);   //旧 :162
                    } else
                    {
                        Logger.Error("版本设置中指定的 Java 已无法使用，请重新选择一个 Java！", LogBehavior.Toast);   //旧 :164
                    }
                    p?.Finish();
                    return null;   //<==== 指定的 Java 存在异常（旧 :166）
                }
                p?.Finish();
                return chosenJava;   //<==== 成功找到并检查通过（旧 :168）
            }
            default:
            {
                //======================== 根据版本范围自动选择 JavaList 中的 Java ========================
                //获取范围（旧 :173-176：旧代码对 GetJavaRequirement 调用两次——Range 与 RecommendedComponent 各一次——逐字保持）
                ValueRange<Version> range = target.IsT2() ? target.AsT2().RequirementProvider().Range : target.AsT1();
                string? recommendedComponent = target.IsT2() ? target.AsT2().RequirementProvider().RecommendedComponent : null;
                Logger.Info($"自动选择以下范围的 Java：{range} {recommendedComponent}");
                if (range.IsEmpty())
                {
                    throw new Exception("Java 版本需求存在冲突，导致没有任何可能适配的 Java，请查看 PCL 的日志了解详细信息！");   //旧 :178
                }

                //搜索 Java（旧 :181-194）
                Java? SelectJavaFromJavaList()
                {
                    callbacks.ListRefreshWorker?.WaitIfRunning();   //旧 :183：等待进行中的搜索结束
                    foreach (Java chosenJava in ConfigAccess.JavaListGet())
                    {
                        if (!Check(chosenJava, c).IsSuccess)   //检查
                        {
                            Logger.Warn($"Java 检查失败，将尝试列表中的下一个 Java：{chosenJava}");
                            continue;
                        }
                        if (chosenJava.Version is null || !range.Contains(chosenJava.Version)) continue;   //版本范围
                        Logger.Info($"已选定 Java：{chosenJava}");
                        return chosenJava;
                    }
                    return null;
                }
                p?.Set(0.25);   //旧 :195
                Java? result = SelectJavaFromJavaList();
                if (result is not null)
                {
                    p?.Skip();
                    return result;   //旧 :197
                }

                lock (JavaLock)   //#3797（旧 :199 SyncLock JavaLock：重搜/下载/重选整段互斥）
                {
                    //既有列表中没有合适的 Java，重新触发搜索（旧 :200-204）
                    Logger.Warn($"既有列表中没有合适的 Java，重新触发搜索");
                    callbacks.ListRefreshWorker?.Start(c, p?.SplitTo(0.3));
                    result = SelectJavaFromJavaList();
                    if (result is not null)
                    {
                        p?.Skip();
                        return result;
                    }
                    if (!tryFixOrHint) return null;   //旧 :205

                    //电脑中没有合适的 Java，获取 Mojang 提供的 Java 下载列表（旧 :207-243）
                    Logger.Warn($"电脑中没有合适的 Java，开始获取 Mojang 提供的 Java 下载列表");
                    if (!DownloadListWorker.HasSucceeded)
                    {
                        DownloadListWorker.Start(c, p?.SplitTo(0.4));   //旧 :240
                    }
                    DownloadListWorker.WaitIfRunning(cancellationToken: c);   //旧 :241
                    p?.Set(0.4, true);   //旧 :242（skiped:=True）
                    List<JavaDownloadInfo> components = DownloadListWorker.LastResult;

                    //确定要下载的目标 Java（旧 :246-248：FirstOfAny 双谓词——优先指定的 Component，回退区间匹配）
                    JavaDownloadInfo? javaToDownload = components.FirstOrDefault(
                        comp => comp.Java!["version"]!["name"]!.ToString() == (recommendedComponent ?? ""),
                        //? 旧 VB 的 = 比较把 Nothing 归一为 ""，上式以 ?? 保持同判
                        comp => range.Contains(comp.JavaVersion));
                    if (javaToDownload is null)
                    {
                        Logger.Warn($"Mojang 提供的 Java 列表中没有合适的 Java，无法自动下载");   //旧 :250
                        //提示需要手动下载 Java（旧 :252-284）
                        string description;
                        bool hasPureLower = range.HasLower && range.IsLowerInclusive &&
                            range.Lower!.Minor <= 0 && range.Lower.Build <= 0 && range.Lower.Revision <= 0;
                        bool hasPureUpper = range.HasUpper && !range.IsUpperInclusive &&
                            range.Upper!.Minor <= 0 && range.Upper.Build <= 0 && range.Upper.Revision <= 0;
                        if (range.HasLower && range.HasUpper)   //有上下界
                        {
                            if (hasPureLower && hasPureUpper)
                            {
                                if (range.Lower!.Major == range.Upper!.Major - 1)
                                {
                                    description = $" Java {range.Lower.Major}";
                                } else
                                {
                                    description = $" Java {range.Lower.Major} ~ {range.Upper.Major - 1}";
                                }
                            } else
                            {
                                description = $" Java {range}"; //摆烂
                            }
                        } else if (range.HasLower)   //只有下界
                        {
                            if (hasPureLower)
                            {
                                description = $" Java {range.Lower!.Major} 或更高版本的 Java";
                            } else if (range.IsLowerInclusive)
                            {
                                description = $" Java {range.Lower} 或更高版本的 Java";
                            } else
                            {
                                description = $"版本高于 {range.Lower} 的 Java";
                            }
                        } else   //只有上界
                        {
                            if (hasPureUpper)
                            {
                                description = $" Java {range.Upper!.Major - 1} 或更低版本的 Java";
                            } else if (range.IsUpperInclusive)
                            {
                                description = $" Java {range.Upper} 或更低版本的 Java";
                            } else
                            {
                                description = $"版本低于 {range.Upper} 的 Java";
                            }
                        }
                        callbacks.MyMsgBox?.Invoke($"PCL 没能找到合适的 Java。\r\n请在网上搜索并手动安装{description}， 然后在【设置 → 启动 → 高级选项 → Java 列表】中导入它。", "需要手动安装 Java", false, false);   //旧 :284（IsWarn/ForceWait 均默认 False）
                        return null;
                    }

                    //自动下载 Java（旧 :288-294：Loader 构建与 WaitForExit(IsForceRestart:=True)/Finally Cancel 由宿主回调承载）
                    callbacks.RunJavaDownload?.Invoke(javaToDownload, c, p?.SplitTo(0.95));
                    result = SelectJavaFromJavaList();   //旧 :295
                    if (result is not null)
                    {
                        p?.Finish();
                        return result;   //旧 :296
                    }
                    throw new Exception($"下载 Java {javaToDownload.ComponentName} 后仍然无法找到合适的 Java");   //旧 :297
                }
            }
            }
        } catch (Exception ex)
        {
            ex.ThrowIfCanceled();   //旧 :301
            Logger.Error(ex, "选择 Java 时出现异常");   //旧 :302
            p?.Skip();
            return null;
        }
    }

    /// <summary>版本文件夹搜索（旧 :123/:344 的 JavaUtils.SearchFoldersAsync 调用面；测试注入替换点）。</summary>
    private static Task<List<Java>> SearchFolders(bool includeSubDirectories, IEnumerable<string> folders, CancellationToken c, ProgressProvider? p)
        => SearchFoldersOverride is null
            ? JavaUtils.SearchFoldersAsync(includeSubDirectories, folders, c, p)
            : SearchFoldersOverride(includeSubDirectories, folders, c, p);

    // ============================================ 迁移决策核 ============================================

    /// <summary>
    /// 从老版本 PCL 迁移版本独立的 Java 设置（旧 MigrateInstanceJavaSettings :318-363 决策面逐行）。
    /// UI 禁用/恢复两点经回调承载（旧 :320/:356-361 的 FrmInstanceSetup 判空与 RunInUiWait 留宿主层）；
    /// 旧 :325-331/:343 的 GoTo NoForcedJava 以 forcedJavaUsable 标志承载，汇入点一致。
    /// </summary>
    public static void MigrateInstanceJavaSettings(JavaSelectionInstance instance, JavaSelectionCallbacks callbacks,
        CancellationToken c = default, ProgressProvider? p = null)
    {
        if (ConfigAccess.JavaMigratedGet(instance.Config))
        {
            p?.Skip();
            return;   //旧 :319
        }
        callbacks.MigrationUiDisable?.Invoke();   //旧 :320
        try
        {
            Logger.Info($"正在尝试迁移版本独立的 Java 设置（{instance.Name}）");   //旧 :322
            //强制使用指定 Java（旧 :324-342）
            bool forcedJavaUsable = false;
            Java? forcedJava = null;
            string forcedJavaSetting = JavaRequirement.GetInstanceSetting<string>("VersionArgumentJavaSelect", instance.PathVersion);   //旧 :324
            if (forcedJavaSetting.StartsWithF("{"))
            {
                forcedJava = new Java(JsonNode.Parse(forcedJavaSetting)!["Path"]!.ToString());   //旧 :328
                if (Check(forcedJava, c).IsSuccess)
                {
                    forcedJavaUsable = true;
                } else
                {
                    callbacks.HintRed?.Invoke("该版本的设置中强制指定的 Java 存在异常，该设置已重置");   //旧 :330
                    p?.Set(0.4);   //旧 :331
                }
            } else
            {
                p?.Set(0.4, true);   //旧 :326（skiped:=True）→ GoTo NoForcedJava
            }
            if (forcedJavaUsable)
            {
                Logger.Info($"将版本独立设置改为老版本中强制指定的 Java：{forcedJava}");   //旧 :333
                if (!ConfigAccess.JavaListGet().Contains(forcedJava!))
                {
                    //加入列表底部（旧 :335：Configs.JavaList.Edit 的 Get→变更→Set 面）
                    ConcurrentList<Java> list = ConfigAccess.JavaListGet();
                    list.Add(forcedJava!);
                    ConfigAccess.JavaListSet(list);
                    Logger.Info($"已将强制指定的 Java 加入 Java 列表底部：{forcedJava}");   //旧 :336
                }
                ConfigAccess.JavaForcedSet(forcedJava, instance.Config);   //旧 :338
                SetInstanceSetting("VersionArgumentJavaV2", 3, instance.PathVersion);   //旧 :339
                callbacks.UpdateJavaLists?.Invoke();   //旧 :340
                ConfigAccess.JavaMigratedSet(true, instance.Config);   //完成（旧 :341）
                p?.Finish();
                return;   //旧 :342
            }
            //NoForcedJava:（旧 :344-354）
            List<Java> javaFound = SearchFolders(true, [instance.PathVersion], c, p?.SplitTo(1)).Run();
            if (javaFound.Any())
            {
                //从版本文件夹中发现了 Java
                Logger.Info($"在版本文件夹中发现了 Java，将版本独立设置改为优先使用版本文件夹的 Java：{javaFound}");   //旧 :347（旧代码对 List 直插值，同为类型名文本）
                SetInstanceSetting("VersionArgumentJavaV2", 2, instance.PathVersion);   //旧 :348
            } else
            {
                //自动选择
                Logger.Info("没有需要迁移的内容，自动选择 Java");   //旧 :351
                SetInstanceSetting("VersionArgumentJavaV2", 0, instance.PathVersion);   //旧 :352
            }
            ConfigAccess.JavaMigratedSet(true, instance.Config);   //完成（旧 :354）
        } finally
        {
            callbacks.MigrationUiRestore?.Invoke();   //旧 :356-361
        }
    }

    /// <summary>版本独立设置写入（宿主层 Settings.vb Set :160-172 的 instanceKey 面：先懒式导入该实例的旧设置，再经服务写入；导入失败不阻断写入）。</summary>
    private static void SetInstanceSetting(string key, object? value, string instanceKey)
    {
        try
        {
            SettingsImport.EnsureInstanceImported(instanceKey);
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"导入实例旧设置失败（{instanceKey}）");
        }
        SettingsService.Set(key, value, instanceKey);
    }

    // ============================================ 下载清单产出 ============================================

    /// <summary>
    /// 下载清单产出（旧 CreateJavaDownloadLoader :546-558 决策面）：piston-meta manifest → NetFile 清单
    /// （跳过 downloads.raw 缺失项；跳过 3 个无意义大量重复文件 #3827 :556-557）。
    /// manifest 的网络获取经 <paramref name="manifestFetcher"/> 供给（宿主层保持 NetRequestByLoader 的 1:1 下载机制）；
    /// LoaderDownload+LoaderWorker 组装留宿主层。
    /// </summary>
    /// <param name="target">要下载的 Java 条目。</param>
    /// <param name="preferMojang">旧 DlSourcePreferMojang（宿主层读设置后传入，InstallerPlans 同口径）。</param>
    /// <param name="manifestFetcher">有序下载源 → JSON 文本（旧 :548 的 NetRequestByLoader）。</param>
    public static (List<NetFile> Files, string BaseDir) CreateJavaDownloadFiles(
        JavaDownloadInfo target, bool preferMojang, Func<IEnumerable<string>, string> manifestFetcher)
    {
        //分析需要下载的 Java 文件列表（旧 :546-548）
        string address = target.Java!["manifest"]!["url"]!.ToString();
        Logger.Info($"准备下载 Java {target.Java!["version"]!["name"]!.ToString()}（{target.ComponentName}）：{address}");
        string listFileStr = manifestFetcher(DownloadSources.Order(
            [address],
            [address.Replace("piston-meta.mojang.com", "bmclapi2.bangbang93.com")],
            preferMojang));
        string baseDir = $"{Paths.AppData}.minecraft\\runtime\\{target.ComponentName}\\";   //旧 :549
        List<NetFile> filesToDownload = [];
        foreach (KeyValuePair<string, JsonNode?> fileEntry in JsonNode.Parse(listFileStr)!["files"]!.AsObject())
        {
            if (fileEntry.Value?["downloads"]?["raw"] is null) continue;   //旧 :552（旧 ("downloads")?("raw")：downloads 缺失或 raw 缺失同跳过）
            JsonNode info = fileEntry.Value!["downloads"]!["raw"]!;
            FileChecker checker = new()
            {
                //? 旧 Newtonsoft 的 JToken→Long/String 转换把缺失/JSON null 归一为 0/null（Nothing 语义），以可空转换保持同判（W1 TECHDEBT §10 口径）
                ActualSize = (long?) info["size"] ?? 0,
                Hash = (string?) info["sha1"],
            };
            if (checker.Hash is "12976a6c2b227cbac58969c1455444596c894656" or "c80e4bab46e34d02826eab226a4441d0970f2aba" or "84d2102ad171863db04e7ee22a259d1f6c5de4a5")
            {
                continue; //跳过 3 个无意义大量重复文件（#3827，旧 :556-557）
            }
            string? url = (string?) info["url"];   //旧 :558（缺失时旧代码于镜像源 Replace 处 NRE，此处保持同点）
            filesToDownload.Add(new NetFile(
                DownloadSources.Order(
                    [url!],
                    [url!.Replace("piston-data.mojang.com", "bmclapi2.bangbang93.com")],
                    preferMojang),   //旧 :559（DlSourceOrder）
                baseDir + fileEntry.Key, checker));   //旧 :560
        }
        Logger.Info($"该 Java 需要下载 {filesToDownload.Count} 个文件，目标文件夹：{baseDir}");   //旧 :563
        return (filesToDownload, baseDir);
    }

    // ============================================ Mojang Java 列表解析（旧 :209-239 的 Static worker） ============================================

    /// <summary>
    /// 构建 Mojang Java 列表 worker（旧 :209-239）：下载经 <see cref="JavaListFetcher"/> 供给，解析为 <see cref="JavaDownloadInfo"/> 列表。
    /// 成功后缓存（HasSucceeded），应用生命周期内不重复下载（旧 Static 语义）。
    /// </summary>
    private static RedoableWorker<List<JavaDownloadInfo>> CreateDownloadListWorker() => new(token =>
    {
        //下载 Mojang 提供的 Java 列表（旧 :212-215：NetRequestByLoader + DlVersionListOrder 经宿主供给）
        string indexFileStr = (JavaListFetcher ?? throw new InvalidOperationException("JavaListFetcher 未接线（ModJava.vb Sub New）"))();
        token?.ThrowIfCancellationRequested();   //旧 :216
        //解析 Java 列表（旧 :217-238）
        List<JavaDownloadInfo> results = [];
        foreach (KeyValuePair<string, JsonNode?> component in JsonNode.Parse(indexFileStr)!["windows-x64"]!.AsObject())
        {
            token?.ThrowIfCancellationRequested();   //旧 :220
            if (component.Key == "minecraft-java-exe") continue; //不是完整 Java（旧 :221）
            try
            {
                //? 旧 Newtonsoft 的 FirstOrDefault 遍历 JToken 子项；piston-meta 的组件条目恒为数组，STJ 按数组取首项（病态对象条目在旧代码中亦为垃圾输入）
                JsonNode? entry = (component.Value as JsonArray)?.FirstOrDefault();
                if (entry is null) continue;   //旧 :224
                string javaName = entry["version"]!["name"]!.ToString();   //旧 :225（可能的名称格式："12"、"8u51-cacert462b08"、"17.0.8.1"、"17.0.1.12.1"、"21.0.7"）
                if (javaName.Contains('.'))
                {
                    string[] parts = javaName.Split('.');   //旧 :227
                    results.Add(new JavaDownloadInfo(component.Key, entry, new Version(
                        (int) Val(parts[0]), (int) Val(parts[1]),
                        parts.Length >= 3 ? (int) Val(parts[2]) : 0,
                        parts.Length >= 4 ? (int) Val(parts[3]) : 0)));   //旧 :228
                } else if (javaName.StartsWithF("8u51-"))
                {
                    results.Add(new JavaDownloadInfo(component.Key, entry, new Version(8, 0, 51, 0)));   //旧 :230
                } else
                {
                    results.Add(new JavaDownloadInfo(component.Key, entry,
                        new Version((int) Val(javaName.BeforeFirstOfAny(["u", "."])), 0, 0, 0)));   //旧 :232
                }
            } catch (Exception ex)
            {
                Logger.Warn(ex, $"解析 Mojang 提供的 Java 列表项失败，已跳过（{component.Key}）");   //旧 :235
            }
        }
        return results;
    });

    /// <summary>VB Val 的移植（ModBase.vb:609 包装 Conversion.Val）：解析前缀数值，无效为 0（与 Instances\Libraries.cs 的既有移植同源）。</summary>
    private static double Val(string? text)
    {
        if (text is null) return 0;
        var match = ValPrefixRegex.Match(text);
        return match.Success ? double.Parse(match.Value, CultureInfo.InvariantCulture) : 0;
    }

    private static readonly Regex ValPrefixRegex = new(@"^\s*[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?", RegexOptions.Compiled);

}