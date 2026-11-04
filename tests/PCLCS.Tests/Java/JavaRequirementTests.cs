using MeloongCore;
using PCLCS.Settings;
using System.Globalization;
using System.Text.Json.Nodes;

//命名空间留 PCLCS.Tests 根：PCLCS.Java（类）已占用 Java 之名，任何 PCLCS.*.Java 子命名空间都会遮蔽同层对 Java 类的引用（CS0118，已实测）
namespace PCLCS.Tests;

/// <summary>
/// JavaRequirement（Java 需求表与初始化决策核）测试。
/// 需求表 Theory 逐格对照 ModJava.vb :129-274 的版本区间表（每行标注旧行号）；
/// 交集/空集（冲突重置）与强制范围短路均有边界行；JavaInit 三态（需迁移/有缓存/无 Java）+ 掩蔽态经内存配置面注入，无进程、无 UI。
/// </summary>
[Collection("PCLCS.Settings")]   //SettingsService 与 Configs 访问面均为进程级状态，串行执行
public class JavaRequirementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pclcs.javareq." + Guid.NewGuid().ToString("N"));
    private string InstanceKey => Path.Combine(_root, "versions", "t\\");
    private readonly IJavaConfigAccess _savedConfigAccess;

    public JavaRequirementTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "versions", "t"));
        SettingsService.Configure(_root);   //全局 Settings.json 落在临时目录
        _savedConfigAccess = JavaRequirement.JavaConfigAccess;
    }
    public void Dispose()
    {
        JavaRequirement.JavaConfigAccess = _savedConfigAccess;
        JavaRequirement.SortAsyncOverride = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    #region 行数据构造

    /// <summary>需求表单行：旧行号记录在 Label，期望区间为逐格直译的工厂结果。</summary>
    public sealed record TableRow(
        string Label,
        string VanillaName,
        DateTime? ReleaseTime,
        string? OptiFine,
        (ModLoaders Loader, string Version)[] Loaders,
        int RecommendedCode,
        Func<string?>? Component,
        ValueRange<Version> Expected)
    {
        public override string ToString() => Label;
    }

    private static DateTime Release(int year, int month, int day) => new(year, month, day, 12, 0, 0);

    private static (ModLoaders, string)[] Loaders(ModLoaders loader, string version) => [(loader, version)];

    public static TheoryData<TableRow> TableRows() => new()
    {
        //原版（:154-172）
        new TableRow("1.20.5 → 至少 Java 21（:157，1.20.5+）", "1.20.5", null, null, [], 0, null, ValueRange<Version>.AtLeast(new Version(21, 0))),
        new TableRow("1.20.4 → 至少 Java 17（:161，Major>=18）", "1.20.4", null, null, [], 0, null, ValueRange<Version>.AtLeast(new Version(17, 0))),
        new TableRow("1.18 → 至少 Java 17（:161，1.18 pre2+）", "1.18", null, null, [], 0, null, ValueRange<Version>.AtLeast(new Version(17, 0))),
        new TableRow("1.17.1 → 至少 Java 16（:165，1.17+）", "1.17.1", null, null, [], 0, null, ValueRange<Version>.AtLeast(new Version(16, 0))),
        new TableRow("1.16.5 2021 → 至少 Java 8（:168，2017 分界）", "1.16.5", Release(2021, 1, 14), null, [], 0, null, ValueRange<Version>.AtLeast(new Version(8, 0))),
        new TableRow("1.12.2 2017-06-02 → 至少 Java 8（:168，1.12 与 1.11 分界线）", "1.12.2", Release(2017, 6, 2), null, [], 0, null, ValueRange<Version>.AtLeast(new Version(8, 0))),
        new TableRow("1.8.9 2015-12-09 → 全域（:154-172 无匹配）", "1.8.9", Release(2015, 12, 9), null, [], 0, null, ValueRange<Version>.All()),
        new TableRow("1.5.2 2013-04-25 → 最高 Java 8（:171，<=2013-05-01）", "1.5.2", Release(2013, 4, 25), null, [], 0, null, ValueRange<Version>.LessThan(new Version(9, 0))),
        new TableRow("1.5.1 2013-03-20 → 最高 Java 8（:171）", "1.5.1", Release(2013, 3, 20), null, [], 0, null, ValueRange<Version>.LessThan(new Version(9, 0))),
        new TableRow("1.6.4 2013-09-19 → 全域（:171 上边界之外）", "1.6.4", Release(2013, 9, 19), null, [], 0, null, ValueRange<Version>.All()),
        new TableRow("快照 24w14a 2024-04-11 → 至少 Java 21（:157，非标版本按时间）", "24w14a", Release(2024, 4, 11), null, [], 0, null, ValueRange<Version>.AtLeast(new Version(21, 0))),
        new TableRow("快照 24w13a 2024-03-28 → 至少 Java 17（:161，非标版本按时间）", "24w13a", Release(2024, 3, 28), null, [], 0, null, ValueRange<Version>.AtLeast(new Version(17, 0))),
        new TableRow("快照 21w19a 2021-05-19 → 至少 Java 16（:165）", "21w19a", Release(2021, 5, 19), null, [], 0, null, ValueRange<Version>.AtLeast(new Version(16, 0))),
        new TableRow("快照 16w05b 2016-02-04 → 全域（:166，年份线之下）", "16w05b", Release(2016, 2, 4), null, [], 0, null, ValueRange<Version>.All()),
        new TableRow("1.5.2 1960 年 → 全域（:169，避免某些版本写个 1960 年）", "1.5.2", Release(1960, 1, 1), null, [], 0, null, ValueRange<Version>.All()),
        //原版 26+（:174-186）
        new TableRow("1.20.5 + majorVersion=26 → ≥26 与组件（:181-184）", "1.20.5", null, null, [], 26, static () => "26w-java", ValueRange<Version>.AtLeast(new Version(26, 0))),
        new TableRow("1.20.5 + majorVersion=22 → ≥26→22 边界（:179）", "1.20.5", null, null, [], 22, static () => "22w-java", ValueRange<Version>.AtLeast(new Version(22, 0))),
        new TableRow("1.20.5 + majorVersion=21 → 不触发 Mojang 行（:179 边界）", "1.20.5", null, null, [], 21, static () => "21w-java", ValueRange<Version>.AtLeast(new Version(21, 0))),
        new TableRow("1.20.5 + majorVersion=26 + 组件为空串 → 组件置空（:185）", "1.20.5", null, null, [], 26, static () => "", ValueRange<Version>.AtLeast(new Version(26, 0))),
        //OptiFine（:188-200）
        new TableRow("1.6.4 + OF → 最高 Java 8（:192，<1.7）", "1.6.4", null, "H8", [], 0, null, ValueRange<Version>.LessThan(new Version(9, 0))),
        new TableRow("1.8.9 + OF → 恰好 Java 8（:195，1.8-1.11）", "1.8.9", null, "C9", [], 0, null, ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(9, 0))),
        new TableRow("1.11.2 + OF → 恰好 Java 8（:195，Major 11）", "1.11.2", null, "D5", [], 0, null, ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(9, 0))),
        new TableRow("1.12.2 2017 + OF → ≥8 ∩ <9（:198 与 :168 交集）", "1.12.2", Release(2017, 6, 2), "C8", [], 0, null, ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(9, 0))),
        new TableRow("1.7.10 + OF → 全域（:189-200，1.7 无 OptiFine 约束）", "1.7.10", null, "C8", [], 0, null, ValueRange<Version>.All()),
        //Forge（:202-235）
        new TableRow("1.7.2 + Forge 1121 → Java 7（:206，1.6.1-1.7.2）", "1.7.2", null, null, Loaders(ModLoaders.Forge, "1121.540"), 0, null, ValueRange<Version>.ClosedOpen(new Version(7, 0), new Version(8, 0))),
        new TableRow("1.7.10 + Forge 1234 → 最高 Java 8（:209，<=1.12）", "1.7.10", null, null, Loaders(ModLoaders.Forge, "1234.100"), 0, null, ValueRange<Version>.LessThan(new Version(9, 0))),
        new TableRow("1.12.2 2017 + Forge 14.23.5.2859 → ≥8 ∩ <9（:168 + :209）", "1.12.2", Release(2017, 6, 2), null, Loaders(ModLoaders.Forge, "14.23.5.2859"), 0, null, ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(9, 0))),
        new TableRow("1.14.4 + Forge 28.2.26 → Java 8-10（:212）", "1.14.4", Release(2019, 7, 19), null, Loaders(ModLoaders.Forge, "28.2.26"), 0, null, ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(11, 0))),
        new TableRow("1.15.2 + Forge 31.2.31 → Java 8-15（:215）", "1.15.2", Release(2020, 1, 21), null, Loaders(ModLoaders.Forge, "31.2.31"), 0, null, ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(16, 0))),
        new TableRow("1.16.3 + Forge 34.0.0 → [8, 8.0.320]（:218 下端点）", "1.16.3", Release(2020, 9, 10), null, Loaders(ModLoaders.Forge, "34.0.0"), 0, null, ValueRange<Version>.Closed(new Version(8, 0), new Version(8, 0, 320))),
        new TableRow("1.16.5 + Forge 36.2.25 → [8, 8.0.320]（:218 上端点）", "1.16.5", Release(2021, 1, 14), null, Loaders(ModLoaders.Forge, "36.2.25"), 0, null, ValueRange<Version>.Closed(new Version(8, 0), new Version(8, 0, 320))),
        new TableRow("1.16.5 + Forge 36.2.26 → [8, 24)（:221 下端点）", "1.16.5", Release(2021, 1, 14), null, Loaders(ModLoaders.Forge, "36.2.26"), 0, null, ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(24, 0))),
        new TableRow("1.17.1 + Forge 37.0.60 → [16, 17)（:224）", "1.17.1", null, null, Loaders(ModLoaders.Forge, "37.0.60"), 0, null, ValueRange<Version>.ClosedOpen(new Version(16, 0), new Version(17, 0))),
        new TableRow("1.18.2 + Forge 40.2.9 + OF → [17, 19)（:227，#305）", "1.18.2", null, "D3", Loaders(ModLoaders.Forge, "40.2.9"), 0, null, ValueRange<Version>.ClosedOpen(new Version(17, 0), new Version(19, 0))),
        new TableRow("1.19.4 + Forge 45.0.50 → [17, 20)（:230）", "1.19.4", null, null, Loaders(ModLoaders.Forge, "45.0.50"), 0, null, ValueRange<Version>.ClosedOpen(new Version(17, 0), new Version(20, 0))),
        new TableRow("1.20.1 + Forge 47.2.0 → [17, 22)（:233）", "1.20.1", null, null, Loaders(ModLoaders.Forge, "47.2.0"), 0, null, ValueRange<Version>.ClosedOpen(new Version(17, 0), new Version(22, 0))),
        new TableRow("1.20.1 + Forge 48.0.0 → 仅 ≥17（:203-235 无命中）", "1.20.1", null, null, Loaders(ModLoaders.Forge, "48.0.0"), 0, null, ValueRange<Version>.AtLeast(new Version(17, 0))),
        new TableRow("非标版本 + Forge 999 → 最高 Java 8（:209，非标准版本兜底）", "Unknown", null, null, Loaders(ModLoaders.Forge, "999.0"), 0, null, ValueRange<Version>.LessThan(new Version(9, 0))),
        //NeoForge（:237-244）
        new TableRow("1.20.1 + NeoForge → [17, 22)（:242，Vanilla==20.0.1）", "1.20.1", null, null, Loaders(ModLoaders.NeoForge, "20.4.100"), 0, null, ValueRange<Version>.ClosedOpen(new Version(17, 0), new Version(22, 0))),
        new TableRow("1.20.2 + NeoForge 20.2.61-beta → [17, 22)（:240）", "1.20.2", null, null, Loaders(ModLoaders.NeoForge, "20.2.61-beta"), 0, null, ValueRange<Version>.ClosedOpen(new Version(17, 0), new Version(22, 0))),
        new TableRow("1.20.2 + NeoForge 20.2.62-beta → [17, 22)（:240，含端点）", "1.20.2", null, null, Loaders(ModLoaders.NeoForge, "20.2.62-beta"), 0, null, ValueRange<Version>.ClosedOpen(new Version(17, 0), new Version(22, 0))),
        new TableRow("1.20.2 + NeoForge 20.2.63 → 仅 ≥17（:239-244 无命中）", "1.20.2", null, null, Loaders(ModLoaders.NeoForge, "20.2.63"), 0, null, ValueRange<Version>.AtLeast(new Version(17, 0))),
        new TableRow("1.20.2 + NeoForge 含 25w14craftmine → 仅 ≥17（:240 排除）", "1.20.2", null, null, Loaders(ModLoaders.NeoForge, "20.2.60-beta+25w14craftmine"), 0, null, ValueRange<Version>.AtLeast(new Version(17, 0))),
        //Fabric（:246-259）
        new TableRow("1.16.5 + Fabric 0.12.5 → [8, 25)（:250 + :257）", "1.16.5", Release(2021, 1, 14), null, Loaders(ModLoaders.Fabric, "0.12.5"), 0, null, ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(25, 0))),
        new TableRow("1.19.4 + Fabric 0.14.21 → [17, 25)（:253 + :257）", "1.19.4", null, null, Loaders(ModLoaders.Fabric, "0.14.21"), 0, null, ValueRange<Version>.ClosedOpen(new Version(17, 0), new Version(25, 0))),
        new TableRow("1.19.4 + Fabric 0.17.0 → 仅 ≥17（:255，不小于自身）", "1.19.4", null, null, Loaders(ModLoaders.Fabric, "0.17.0"), 0, null, ValueRange<Version>.AtLeast(new Version(17, 0))),
        new TableRow("1.16.5 + Fabric 未知版本 → 仅 ≥8（:255，未知版本跳过）", "1.16.5", Release(2021, 1, 14), null, Loaders(ModLoaders.Fabric, "未知版本"), 0, null, ValueRange<Version>.AtLeast(new Version(8, 0))),
        new TableRow("1.14.4 + Fabric 0.16.0 → [8, 25)（:168 + :257，独立行）", "1.14.4", Release(2019, 7, 19), null, Loaders(ModLoaders.Fabric, "0.16.0"), 0, null, ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(25, 0))),
        new TableRow("1.13 + Fabric 0.17.2 → 仅 ≥8（:168）", "1.13", Release(2018, 7, 18), null, Loaders(ModLoaders.Fabric, "0.17.2"), 0, null, ValueRange<Version>.AtLeast(new Version(8, 0))),
        //LiteLoader（:261-265）
        new TableRow("1.12.2 2017 + LiteLoader → ≥8 ∩ <9（:264）", "1.12.2", Release(2017, 6, 2), null, Loaders(ModLoaders.LiteLoader, "1.12"), 0, null, ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(9, 0))),
    };

    private static string? NullComponent() => null;

    private JavaRequirementQuery Query(McVersion version, int recommendedCode = 0, Func<string?>? component = null)
        => new(version, "正式版 " + version.VanillaName, InstanceKey, recommendedCode, component ?? NullComponent);

    private static McVersion BuildVersion(string vanillaName, DateTime? releaseTime, string? optiFine, (ModLoaders Loader, string Version)[] loaders)
    {
        McVersion version = new(vanillaName);
        if (releaseTime is not null) version.ReleaseTime = releaseTime.Value;
        if (optiFine is not null) version.OptiFine = optiFine;
        foreach ((ModLoaders loader, string loaderVersion) in loaders) version.ModLoaderVersions[loader] = loaderVersion;
        return version;
    }

    [Theory, MemberData(nameof(TableRows))]
    public void GetRequirement_table_rows_match_old_switches(TableRow row)
    {
        var (range, component) = JavaRequirement.Get(Query(BuildVersion(row.VanillaName, row.ReleaseTime, row.OptiFine, row.Loaders), row.RecommendedCode, row.Component));
        Assert.Equal(row.Expected, range);
        //组件语义：仅 Mojang 要求行（>=22）返回；空串归一为 null（:185）
        if (row.RecommendedCode >= 22)
        {
            string? raw = row.Component!();
            Assert.Equal(raw == "" ? null : raw, component);
        } else
        {
            Assert.Null(component);
        }
    }

    #endregion

    #region 统一通行证（:267-271，LoginType 为全局设置）

    public static TheoryData<string, DateTime?, ValueRange<Version>> NideRows() => new()
    {
        { "1.8.9 2015 + Nide → ≥8.0.141（:270）", Release(2015, 12, 9), ValueRange<Version>.AtLeast(new Version(8, 0, 141)) },
        { "1.12.2 2017 + Nide → ≥8 ∩ ≥8.0.141 = ≥8.0.141（:168 + :270）", Release(2017, 6, 2), ValueRange<Version>.AtLeast(new Version(8, 0, 141)) },
    };

    [Theory, MemberData(nameof(NideRows))]
    public void GetRequirement_nide_login_adds_java8u141(string vanillaName, DateTime? releaseTime, ValueRange<Version> expected)
    {
        SettingsService.Set("LoginType", 2);   //旧 :268 Settings.Get(Of McLoginType)("LoginType") = McLoginType.Nide（Nide = 2，ModLaunch.vb:301-306）
        var (range, component) = JavaRequirement.Get(Query(BuildVersion(vanillaName, releaseTime, null, [])));
        Assert.Equal(expected, range);
        Assert.Null(component);
    }

    [Fact]
    public void GetRequirement_non_nide_login_has_no_8u141_constraint()
    {
        SettingsService.Set("LoginType", 5);   //Ms = 5，非 Nide 不追加约束
        var (range, component) = JavaRequirement.Get(Query(BuildVersion("1.12.2", Release(2017, 6, 2), null, [])));
        Assert.Equal(ValueRange<Version>.AtLeast(new Version(8, 0)), range);
        Assert.Null(component);
    }

    #endregion

    #region 版本设置强制范围（:131-139）

    [Fact]
    public void GetRequirement_forced_range_short_circuits_table()
    {
        SettingsService.Set("VersionArgumentJavaV2", 1, InstanceKey);
        SettingsService.Set("VersionArgumentJavaRange", "[8.0, 9.0)", InstanceKey);
        //1.20.5 走表会给出 ≥21；强制范围 [8.0, 9.0) 应原样短路返回（:135）；ParseVersionWithDefaults 补齐为四段
        var (range, component) = JavaRequirement.Get(Query(BuildVersion("1.20.5", null, null, [])));
        Assert.Equal(new Version(8, 0, 0, 0), range.Lower);
        Assert.Equal(new Version(9, 0, 0, 0), range.Upper);
        Assert.True(range.HasLower);
        Assert.True(range.HasUpper);
        Assert.True(range.IsLowerInclusive);
        Assert.False(range.IsUpperInclusive);
        Assert.Null(component);
    }

    [Fact]
    public void GetRequirement_forced_range_invalid_falls_back_to_table()
    {
        SettingsService.Set("VersionArgumentJavaV2", 1, InstanceKey);
        SettingsService.Set("VersionArgumentJavaRange", "junk", InstanceKey);
        var (range, component) = JavaRequirement.Get(Query(BuildVersion("1.20.5", null, null, [])));
        Assert.Equal(ValueRange<Version>.AtLeast(new Version(21, 0)), range);   //旧 :136-138 警告后落回需求表
        Assert.Null(component);
    }

    [Fact]
    public void GetRequirement_v2_zero_ignores_range_setting()
    {
        SettingsService.Set("VersionArgumentJavaV2", 0, InstanceKey);
        SettingsService.Set("VersionArgumentJavaRange", "[8.0, 9.0)", InstanceKey);
        var (range, component) = JavaRequirement.Get(Query(BuildVersion("1.20.5", null, null, [])));
        Assert.Equal(ValueRange<Version>.AtLeast(new Version(21, 0)), range);
        Assert.Null(component);
    }

    #endregion

    #region 交集与空集（:143-151）

    [Fact]
    public void GetRequirement_conflicting_constraints_reset_to_last_constraint()
    {
        //1.20.1 + Forge 36.2.25：≥17 ∩ ≤8.0.320 为空 → 冲突重置为最后追加的约束（:147-150），最终返回 AtMost(8.0.320) 而非空集
        var (range, _) = JavaRequirement.Get(Query(BuildVersion("1.20.1", null, null, Loaders(ModLoaders.Forge, "36.2.25"))));
        Assert.Equal(ValueRange<Version>.AtMost(new Version(8, 0, 320)), range);
    }

    [Fact]
    public void GetRequirement_conflict_keeps_accumulating_after_reset()
    {
        //1.20.1 + Forge 36.2.25 + LiteLoader：冲突重置为 AtMost(8.0.320) 后继续 ∩ <9 → 仍为 AtMost(8.0.320)（:144-151）
        var (range, _) = JavaRequirement.Get(Query(BuildVersion("1.20.1", null, null,
            [(ModLoaders.Forge, "36.2.25"), (ModLoaders.LiteLoader, "1.12")])));
        Assert.Equal(ValueRange<Version>.AtMost(new Version(8, 0, 320)), range);
    }

    [Fact]
    public void GetRequirement_boundary_intersection_is_not_empty()
    {
        //1.16.5 + Forge 36.2.25：≥8 与 ≤8.0.320 恰好相交于 [8.0, 8.0.320]，不触发冲突（:147-150 空集边界）
        var (range, _) = JavaRequirement.Get(Query(BuildVersion("1.16.5", Release(2021, 1, 14), null, Loaders(ModLoaders.Forge, "36.2.25"))));
        Assert.False(range.IsEmpty());
        Assert.True(range.Contains(new Version(8, 0)));
        Assert.True(range.Contains(new Version(8, 0, 320)));
        Assert.False(range.Contains(new Version(8, 0, 321)));
        Assert.False(range.Contains(new Version(7, 9, 9)));
    }

    [Fact]
    public void GetRequirement_component_provider_is_lazy_below_code22()
    {
        //组件提供者仅在 RecommendedCode >= 22 时调用（旧 :182-184 位于 If 内）；< 22 时不触发（返回前亦不读 jar）
        static string Boom() => throw new InvalidOperationException("不应在 <22 时读取组件");
        var (range, component) = JavaRequirement.Get(Query(BuildVersion("1.20.5", null, null, []), 21, Boom));
        Assert.Equal(ValueRange<Version>.AtLeast(new Version(21, 0)), range);
        Assert.Null(component);
    }

    #endregion

    #region JavaInit 决策三态（旧 :8-48 决策面）

    /// <summary>内存配置面：隔离全局 Configs（ConfigProviders.Default 落真实 AppData，测试不可触碰）。</summary>
    private sealed class FakeConfigs : IJavaConfigAccess
    {
        public int ConfigVersion;
        public ConcurrentList<Java> List = new();
        public bool ListSetCalled;
        public bool ListResetCalled;
        public Func<ConcurrentList<Java>>? OnListGet;

        public int JavaConfigVersionGet() => ConfigVersion;
        public void JavaConfigVersionSet(int value) => ConfigVersion = value;
        public ConcurrentList<Java> JavaListGet() => OnListGet is null ? List : OnListGet();
        public void JavaListSet(ConcurrentList<Java> value) { ListSetCalled = true; List = value; }
        public void JavaListReset() { ListResetCalled = true; List = new ConcurrentList<Java>(); }
    }

    private FakeConfigs UseFakeConfigs(int configVersion, ConcurrentList<Java>? list = null)
    {
        FakeConfigs configs = new() { ConfigVersion = configVersion, List = list ?? new ConcurrentList<Java>() };
        JavaRequirement.JavaConfigAccess = configs;
        return configs;
    }

    private static Java JavaAt(string folder)
    {
        Java java = new(folder);
        java.isChecked = 1;   //预置已检查状态，避免测试注入的排序之外触发真实进程检查
        java.available = true;
        return java;
    }

    private void UseLegacySettings(string launchArgumentJavaAll, string? launchArgumentJavaSelect = null)
    {
        SettingsService.Set("LaunchArgumentJavaAll", launchArgumentJavaAll);
        if (launchArgumentJavaSelect is not null) SettingsService.Set("LaunchArgumentJavaSelect", launchArgumentJavaSelect);
    }

    [Fact]
    public void NeedsLegacyMigration_follows_config_version()
    {
        //旧 :11 的判定点
        FakeConfigs configs = UseFakeConfigs(0);
        Assert.True(JavaRequirement.NeedsLegacyMigration());
        configs.ConfigVersion = Versions.JavaConfigVersion;
        Assert.False(JavaRequirement.NeedsLegacyMigration());
    }

    [Fact]
    public async Task InitAsync_migrates_legacy_list_and_selects_forced_to_top()
    {
        FakeConfigs configs = UseFakeConfigs(0);
        UseLegacySettings("""[{"Path":"C:\\fake\\a\\"},{"Path":"C:\\fake\\b\\"}]""", """{"Path":"C:\\fake\\b\\"}""");
        //排序核需真实进程检查，测试注入替换点（MojangApiBase 同模式）；返回标记已检查的 Java，b 在前
        JavaRequirement.SortAsyncOverride = static (javas, _) =>
            Task.FromResult(javas.OrderBy(j => j.Folder, StringComparer.Ordinal).ToList());
        var state = await JavaRequirement.InitAsync();
        Assert.Equal(JavaInitState.HasCache, state);
        Assert.True(configs.ListSetCalled);
        Assert.Equal(Versions.JavaConfigVersion, configs.ConfigVersion);   //旧 :30 完成迁移
        Assert.Equal(2, configs.List.Count);
        Assert.Contains(configs.List, j => PathUtils.IsSame(j.Folder, "C:\\fake\\b\\"));
    }

    [Fact]
    public async Task InitAsync_migration_bad_json_is_masked_and_keeps_config_version()
    {
        //旧 :31-33 内层 Catch：迁移失败仅记日志，版本号未推进，随后照常读缓存（TECHDEBT §11 内层面）
        FakeConfigs configs = UseFakeConfigs(0);
        UseLegacySettings("不是 JSON");
        var state = await JavaRequirement.InitAsync();
        Assert.Equal(JavaInitState.NoJava, state);
        Assert.Equal(0, configs.ConfigVersion);
        Assert.False(configs.ListSetCalled);
    }

    [Fact]
    public async Task InitAsync_migration_with_missing_java_paths_filters_all_and_reports_nojava()
    {
        //不注入排序替换点：真实 SortAsync 检查全部失败（路径不存在，无进程启动），迁移照常完成（旧 :17）
        FakeConfigs configs = UseFakeConfigs(0);
        UseLegacySettings("""[{"Path":"C:\\fake\\missing\\"}]""");
        var state = await JavaRequirement.InitAsync();
        Assert.Equal(JavaInitState.NoJava, state);
        Assert.Equal(Versions.JavaConfigVersion, configs.ConfigVersion);
        Assert.True(configs.ListSetCalled);
        Assert.Empty(configs.List);
    }

    [Fact]
    public async Task InitAsync_has_cache_returns_without_migration()
    {
        //旧 :35-40 有缓存：直接返回，不触发搜索
        FakeConfigs configs = UseFakeConfigs(Versions.JavaConfigVersion, new ConcurrentList<Java>(new[] { JavaAt("C:\\fake\\a\\") }));
        var state = await JavaRequirement.InitAsync();
        Assert.Equal(JavaInitState.HasCache, state);
        Assert.False(configs.ListSetCalled);
        Assert.False(configs.ListResetCalled);
    }

    [Fact]
    public async Task InitAsync_no_java_reports_search_needed()
    {
        //旧 :41-43 无 Java：宿主层据此触发 JavaListRefreshWorker.Start()
        FakeConfigs configs = UseFakeConfigs(Versions.JavaConfigVersion);
        var state = await JavaRequirement.InitAsync();
        Assert.Equal(JavaInitState.NoJava, state);
        Assert.False(configs.ListResetCalled);
    }

    [Fact]
    public async Task InitAsync_outer_failure_is_masked_and_resets_list()
    {
        //旧 :44-47 外层 Catch（TECHDEBT §11 掩蔽点）：吞异常、重置 Java 列表，且不触发搜索
        FakeConfigs configs = UseFakeConfigs(Versions.JavaConfigVersion);
        configs.OnListGet = static () => throw new InvalidOperationException("缓存读取失败");
        var state = await JavaRequirement.InitAsync();
        Assert.Equal(JavaInitState.Failed, state);
        Assert.True(configs.ListResetCalled);
    }

    [Fact]
    public async Task InitAsync_after_masked_failure_does_not_report_search()
    {
        //掩蔽态与 NoJava 必须可区分：旧 Catch 落出 Sub，不会执行 :43 的搜索触发
        FakeConfigs configs = UseFakeConfigs(Versions.JavaConfigVersion);
        configs.OnListGet = static () => throw new InvalidOperationException("缓存读取失败");
        Assert.NotEqual(JavaInitState.NoJava, await JavaRequirement.InitAsync());
    }

    #endregion
}