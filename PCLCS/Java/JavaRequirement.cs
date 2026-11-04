using PCLCS.Settings;
using System.Text.Json.Nodes;

//命名空间留 PCLCS 根：PCLCS.Java（类）已占用 Java 之名，PCLCS.Java 子命名空间会在同层遮蔽对 Java 类的引用（CS0118，已实测）
namespace PCLCS;

/// <summary>JavaInit 决策结果（旧 :35-47 的走向）。</summary>
public enum JavaInitState
{
    /// <summary>缓存中已有可用 Java（旧 :36-40），无需搜索。</summary>
    HasCache,
    /// <summary>未发现可用 Java，宿主层应触发搜索（旧 :41-43；触发动作留在宿主层）。</summary>
    NoJava,
    /// <summary>初始化异常被掩蔽且已重置 Java 列表（旧 :44-47，TECHDEBT §11）；与旧行为一致不触发搜索。</summary>
    Failed,
}

/// <summary>Java 初始化核的配置访问面：默认直读全局 Configs（与旧 ModJava.vb 直读全局一致）；测试注入内存实现以隔离真实 AppData。</summary>
internal interface IJavaConfigAccess
{
    int JavaConfigVersionGet();
    void JavaConfigVersionSet(int value);
    ConcurrentList<Java> JavaListGet();
    void JavaListSet(ConcurrentList<Java> value);
    void JavaListReset();
}

/// <summary>
/// 需求表查询输入：旧 GetJavaRequirement(Instance As McInstance) 的提取基元。
/// 版本信息为共享的 PCLCS.McVersion（VB McInstance.Version 本就是该类型）；Json 提取与显示名经宿主层传入。
/// </summary>
/// <param name="Version">版本信息（旧 Instance.Version：Vanilla/Valid/ModLoaders/ModLoaderVersions/HasOptiFine/ReleaseTime）。</param>
/// <param name="VersionDisplayName">冲突提示用显示名（旧 :148 的 Instance.VersionDisplayName()；宿主层预提取，纯字符串计算无副作用）。</param>
/// <param name="InstanceKey">版本独立设置的实例键（旧 Instance.PathVersion）。</param>
/// <param name="RecommendedCode">Mojang 要求的主版本号（旧 :176-178 逐字提取：JsonObject("javaVersion")("majorVersion") 优先，JsonVersion("java_version") 兜底，缺省 0）。</param>
/// <param name="RecommendedComponentProvider">Mojang 组件名提供者（旧 :182-184 逐字提取；保持惰性——旧代码仅在 RecommendedCode&gt;=22 时求值，避免多余的 jar 读取）。</param>
public sealed record JavaRequirementQuery(
    McVersion Version,
    string VersionDisplayName,
    string InstanceKey,
    int RecommendedCode,
    Func<string?> RecommendedComponentProvider
);

/// <summary>
/// Java 版本需求表与初始化决策核（自宿主层 ModJava.vb 移植）：
/// 需求区间表（旧 GetJavaRequirement :129-274 逐行：原版/OptiFine/Forge/NeoForge/Fabric/LiteLoader/统一通行证 → <see cref="ValueRange{Version}"/> 交集），
/// 初始化决策面（旧 JavaInit :8-48：JavaConfigVersion=0 老版本迁移 → Configs.JavaList 读缓存 → 无 Java 判定）。
/// 搜索触发（JavaListRefreshWorker.Start）与全部弹窗留在宿主层（裁定 8）；旧 :44 Catch 的掩蔽语义（TECHDEBT §11）保持。
/// </summary>
public static class JavaRequirement
{

    internal static IJavaConfigAccess JavaConfigAccess { get; set; } = new GlobalJavaConfigAccess();

    /// <summary>默认配置访问面：直读全局 Configs（旧 ModJava.vb:11/:28-30/:36/:46 的读取目标）。</summary>
    private sealed class GlobalJavaConfigAccess : IJavaConfigAccess
    {
        public int JavaConfigVersionGet() => Configs.JavaConfigVersion.Get();
        public void JavaConfigVersionSet(int value) => Configs.JavaConfigVersion.Set(value);
        public ConcurrentList<Java> JavaListGet() => Configs.JavaList.Get()!;
        public void JavaListSet(ConcurrentList<Java> value) => Configs.JavaList.Set(value);
        public void JavaListReset() => Configs.JavaList.Reset();
    }

    //* 测试注入面（MojangApiBase 同模式）：迁移期 Java 排序替换点。默认 JavaUtils.SortAsync（对每个 Java 起真实进程检查）；
    //* 测试注入无进程排序，宿主层不接线
    internal static Func<List<Java>, CancellationToken, Task<List<Java>>>? SortAsyncOverride;

    /// <summary>
    /// 是否需要从老版本迁移 Java 配置（旧 :11 判定点：Configs.JavaConfigVersion = 0）。
    /// </summary>
    public static bool NeedsLegacyMigration() => JavaConfigAccess.JavaConfigVersionGet() == 0;

    /// <summary>
    /// 初始化 Java 列表（旧 JavaInit :8-48 决策面逐行）。
    /// 若没有任何可用的 Java，则返回 <see cref="JavaInitState.NoJava"/> 交由宿主层触发搜索；
    /// 异常被掩蔽时返回 <see cref="JavaInitState.Failed"/>（与旧 :44-47 一致不触发搜索）。
    /// </summary>
    public static async Task<JavaInitState> InitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            //从老版本迁移（旧 :10-34）
            if (JavaConfigAccess.JavaConfigVersionGet() == 0)
            {
                try
                {
                    //Java 列表（旧 :13-18）
                    Logger.Warn("正在从老版本迁移 Java 列表");
                    List<Java> oldJavaList = (JsonNode.Parse(SettingsService.Get<string>("LaunchArgumentJavaAll")) as JsonArray)!
                        .Select(javaObject => new Java(javaObject!["Path"]!.ToString())).ToList();
                    oldJavaList = SortAsyncOverride is null
                        ? await JavaUtils.SortAsync(oldJavaList, cancellationToken).NoCapture()
                        : await SortAsyncOverride(oldJavaList, cancellationToken).NoCapture();
                    Logger.Info($"已从老版本设置中发现 {oldJavaList.Count} 个 Java");
                    //强制选择的 Java（旧 :19-27）
                    string selectedJavaSetting = SettingsService.Get<string>("LaunchArgumentJavaSelect");
                    string selectedJava = selectedJavaSetting.StartsWithF("{") ? JsonNode.Parse(selectedJavaSetting)!["Path"]!.ToString() : "";
                    Java? match = oldJavaList.FirstOrDefault(java => PathUtils.IsSame(java.Folder, selectedJava));
                    if (match is not null)
                    {
                        oldJavaList.Remove(match);
                        oldJavaList.Insert(0, match);
                        Logger.Info($"已将老版本设置中强制选择的 Java 置顶：{match}");
                    }
                    JavaConfigAccess.JavaListSet(new ConcurrentList<Java>(oldJavaList));
                    //完成迁移（旧 :29-30）
                    JavaConfigAccess.JavaConfigVersionSet(Versions.JavaConfigVersion);
                } catch (Exception ex)
                {
                    Logger.Error(ex, "从老版本 PCL 迁移 Java 列表失败");
                }
            }
            //读取缓存（旧 :35-40）
            ConcurrentList<Java> list = JavaConfigAccess.JavaListGet();
            if (list.Any())
            {
                Logger.Info($"共有 {list.Count} 个可用的 Java：\r\n- {list.Select(java => java.ToString()).Join("\r\n- ")}");
                return JavaInitState.HasCache;
            }
            //如果没有 Java，则触发搜索（旧 :41-43；触发动作 JavaListRefreshWorker.Start 留在宿主层，裁定 8）
            Logger.Warn("初始化未找到可用的 Java，将自动触发搜索");
            return JavaInitState.NoJava;
        } catch (Exception ex)
        {
            //* TECHDEBT §11 掩蔽点（旧 :44-47）：初始化异常整体吞掉并重置 Java 列表；
            //* 掩蔽后与旧行为一致不触发搜索（旧 Catch 直接落出 Sub），宿主层以 Failed 与 NoJava 区分
            Logger.Error(ex, "初始化 Java 列表失败");
            JavaConfigAccess.JavaListReset();
            return JavaInitState.Failed;
        }
    }

    /// <summary>
    /// 分析指定 Minecraft 版本的基础 Java 版本要求（旧 GetJavaRequirement :129-274 逐行移植）。
    /// 不受版本加载器选择影响；若由于 Java 版本要求冲突而导致没有可用的 Java，区间为最后追加的约束（旧 :147-150 冲突重置语义）。
    /// </summary>
    public static (ValueRange<Version> Range, string? RecommendedComponent) Get(JavaRequirementQuery query)
    {
        McVersion version = query.Version;

        //版本设置已经强制指定了 Java 版本要求（旧 :131-139）
        if (GetInstanceSetting<int>("VersionArgumentJavaV2", query.InstanceKey) == 1)
        {
            try
            {
                ValueRange<Version> range = ValueRange<Version>.FromString(
                    GetInstanceSetting<string>("VersionArgumentJavaRange", query.InstanceKey),
                    static s => StringUtils.ParseVersionWithDefaults(s));
                return (range, null);
            } catch (Exception)
            {
                Logger.Warn($"版本设置中指定的 Java 版本范围有误（{GetInstanceSetting<string>("VersionArgumentJavaRange", query.InstanceKey)}），这可能是因为首次修改此设置，尚未初始化");
            }
        }

        //----------------------------------- 从 Minecraft 版本信息获取需求 -----------------------------------

        ValueRange<Version> javaRange = ValueRange<Version>.All();
        void AddConstraint(ValueRange<Version> constraint)
        {
            //? 旧 :146 为 JavaRange = JavaRange?.Intersect(Constraint)（防御性 ?.）；共享 ValueRange.Intersect 恒返回非 null
            //? 且初值恒为 All()，JavaRange 在此处不可能为 null（VB 若为 null 会在下一行 IsEmpty 处 NRE），直接交集语义一致
            javaRange = javaRange.Intersect(constraint);
            if (javaRange.IsEmpty())
            {
                Logger.Warn($"Java 版本要求冲突，若启动失败请在版本设置手动指定 Java：当前追加的要求为 {constraint}，游戏版本为 {query.VersionDisplayName}", LogBehavior.Toast);
                javaRange = constraint;
            }
        }

        //原版 26-：通过版本号判断（旧 :153-172）
        if ((!version.Valid && version.ReleaseTime >= new DateTime(2024, 4, 2)) ||
            (version.Valid && version.Vanilla >= new Version(20, 0, 5)))
        {
            //1.20.5+（24w14a+）：至少 Java 21
            AddConstraint(ValueRange<Version>.AtLeast(new Version(21, 0)));
        } else if ((!version.Valid && version.ReleaseTime >= new DateTime(2021, 11, 16)) ||
                   (version.Valid && version.Vanilla.Major >= 18))
        {
            //1.18 pre2+：至少 Java 17
            AddConstraint(ValueRange<Version>.AtLeast(new Version(17, 0)));
        } else if ((!version.Valid && version.ReleaseTime >= new DateTime(2021, 5, 11)) ||
                   (version.Valid && version.Vanilla.Major >= 17))
        {
            //1.17+ (21w19a+)：至少 Java 16
            AddConstraint(ValueRange<Version>.AtLeast(new Version(16, 0)));
        } else if (version.ReleaseTime.Year >= 2017)
        {
            //1.12+：至少 Java 8（Minecraft 1.12 与 1.11 的分界线正好是 2017 年，太棒了）
            AddConstraint(ValueRange<Version>.AtLeast(new Version(8, 0)));
        } else if (version.ReleaseTime <= new DateTime(2013, 5, 1) && version.ReleaseTime.Year >= 2001)
        {
            //1.5.2-：最高 Java 8（避免某些版本写个 1960 年）
            AddConstraint(ValueRange<Version>.LessThan(new Version(9, 0)));
        }

        //原版 26+：获取 Mojang 要求的 Java 版本（旧 :174-186）
        string? recommendedComponent = null;
        if (query.RecommendedCode >= 22)
        {
            Logger.Info("Mojang 要求至少使用 Java " + query.RecommendedCode);
            AddConstraint(ValueRange<Version>.AtLeast(new Version(query.RecommendedCode, 0)));
            recommendedComponent = query.RecommendedComponentProvider();
            if (recommendedComponent == "") recommendedComponent = null;
        }

        //OptiFine 检测（旧 :188-200；不管非标准版本）
        if (version.HasOptiFine && version.Valid)
        {
            if (version.Vanilla.Major < 7)
            {
                //<1.7：至多 Java 8
                AddConstraint(ValueRange<Version>.LessThan(new Version(9, 0)));
            } else if (version.Vanilla.Major >= 8 && version.Vanilla.Major < 12)
            {
                //1.8 - 1.11：必须恰好 Java 8
                AddConstraint(ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(9, 0)));
            } else if (version.Vanilla.Major == 12)
            {
                //1.12：最高 Java 8
                AddConstraint(ValueRange<Version>.LessThan(new Version(9, 0)));
            }
        }

        //Forge 检测（部分测试结果见 #8432）（旧 :202-235）；加载器版本串提取自字典（旧 :216 等逐次索引，字典键随 HasFlagF 必在，提取等价）
        if (version.ModLoaders.HasFlagF(ModLoaders.Forge))
        {
            string forgeVersion = version.ModLoaderVersions[ModLoaders.Forge];
            if (version.Vanilla >= new Version(6, 0, 1) && version.Vanilla <= new Version(7, 0, 2))
            {
                //1.6.1 - 1.7.2：必须 Java 7
                AddConstraint(ValueRange<Version>.ClosedOpen(new Version(7, 0), new Version(8, 0)));
            } else if (version.Vanilla.Major <= 12 || !version.Valid)
            {
                //<=1.12：Java 8（非标准版本）
                AddConstraint(ValueRange<Version>.LessThan(new Version(9, 0)));
            } else if (version.Vanilla.Major <= 14)
            {
                //1.13 - 1.14：Java 8 - 10
                AddConstraint(ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(11, 0)));
            } else if (version.Vanilla.Major == 15)
            {
                //1.15：Java 8 - 15
                AddConstraint(ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(16, 0)));
            } else if (McUtils.CompareVersionGE(forgeVersion, "34.0.0") && McUtils.CompareVersionGE("36.2.25", forgeVersion))
            {
                //1.16.3~5，Forge 34.0.0 ~ 36.2.25：最高 Java 8u320
                AddConstraint(ValueRange<Version>.AtMost(new Version(8, 0, 320)));
            } else if (McUtils.CompareVersionGE(forgeVersion, "36.2.26") && McUtils.CompareVersionGE("36.999999.999999", forgeVersion))
            {
                //1.16.5，Forge 36.2.26+，<37.0.0：最高 Java 23
                AddConstraint(ValueRange<Version>.LessThan(new Version(24, 0)));
            } else if (McUtils.CompareVersionGE(forgeVersion, "37.0.0") && McUtils.CompareVersionGE("37.0.79", forgeVersion))
            {
                //1.17.1，Forge 37.0.0 ~ 37.0.79：最高 Java 16
                AddConstraint(ValueRange<Version>.LessThan(new Version(17, 0)));
            } else if (version.Vanilla.Major == 18 && version.HasOptiFine)
            {
                //1.18：若安装了 OptiFine，最高 Java 18（#305）
                AddConstraint(ValueRange<Version>.LessThan(new Version(19, 0)));
            } else if (McUtils.CompareVersionGE(forgeVersion, "45.0.21") && McUtils.CompareVersionGE("45.0.65", forgeVersion))
            {
                //1.19.4，Forge 45.0.21 ~ 45.0.65：最高 Java 19
                AddConstraint(ValueRange<Version>.LessThan(new Version(20, 0)));
            } else if (McUtils.CompareVersionGE(forgeVersion, "45.0.66") && McUtils.CompareVersionGE("47.4.8", forgeVersion))
            {
                //1.19.4~1.20.1，Forge 45.0.66 ~ 47.4.8：最高 Java 21
                AddConstraint(ValueRange<Version>.LessThan(new Version(22, 0)));
            }
        }

        //NeoForge 检测（旧 :237-244）
        if (version.ModLoaders.HasFlagF(ModLoaders.NeoForge))
        {
            string neoForgeVersion = version.ModLoaderVersions[ModLoaders.NeoForge];
            if (version.Vanilla == new Version(20, 0, 1) ||
                (McUtils.CompareVersionGE("20.2.62-beta", neoForgeVersion) && !neoForgeVersion.Contains("25w14craftmine")))
            {
                //1.20.1，以及 1.20.2 的 20.2.62-beta 之前：最高 Java 21
                AddConstraint(ValueRange<Version>.LessThan(new Version(22, 0)));
            }
        }

        //Fabric 检测（旧 :246-259；不管非标准版本）；两段 If 结构照旧（加载器主版本行与 Loader 版本行独立判定）
        if (version.ModLoaders.HasFlagF(ModLoaders.Fabric) && version.Valid)
        {
            string fabricVersion = version.ModLoaderVersions[ModLoaders.Fabric];
            if (version.Vanilla.Major >= 15 && version.Vanilla.Major <= 16)
            {
                //1.15 - 1.16：Java 8+
                AddConstraint(ValueRange<Version>.AtLeast(new Version(8, 0)));
            } else if (version.Vanilla.Major >= 18)
            {
                //1.18+：Java 17+
                AddConstraint(ValueRange<Version>.AtLeast(new Version(17, 0)));
            }
            if (fabricVersion != "未知版本" && McUtils.CompareVersion(fabricVersion, "0.17.0") < 0)
            {
                //Fabric Loader 0.16.x 及更早版本的 Mixin/ASM 不兼容 Java 25
                AddConstraint(ValueRange<Version>.LessThan(new Version(25, 0)));
            }
        }

        //LiteLoader 检测（旧 :261-265；不管非标准版本）
        if (version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) && version.Valid)
        {
            //最高 Java 8
            AddConstraint(ValueRange<Version>.LessThan(new Version(9, 0)));
        }

        //统一通行证检测（旧 :267-271）
        //旧 :268 为 Settings.Get(Of McLoginType)("LoginType") = McLoginType.Nide；枚举 McLoginType 暂留 VB（Nide = 2，ModLaunch.vb:301-306），
        //C# 目录以 Integer 储存（SettingsCatalog.cs:101-103），此处读原始值比较
        if (SettingsService.Get<int>("LoginType") == 2)
        {
            //至少 Java 8u101
            AddConstraint(ValueRange<Version>.AtLeast(new Version(8, 0, 141)));
        }

        return (javaRange, recommendedComponent);
    }

    /// <summary>
    /// 版本独立设置读取（宿主层 Settings.vb:140-149 的 instanceKey 面：先懒式导入该实例的旧设置，再经服务读取；导入失败不阻断读取）。
    /// internal：Task 6 JavaSelection 的实例设置读取共用同一 instanceKey 面。
    /// </summary>
    internal static T GetInstanceSetting<T>(string key, string instanceKey)
    {
        try
        {
            SettingsImport.EnsureInstanceImported(instanceKey);
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"导入实例旧设置失败（{instanceKey}）");
        }
        return SettingsService.Get<T>(key, instanceKey);
    }

}