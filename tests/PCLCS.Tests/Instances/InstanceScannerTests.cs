using MeloongCore;
using MeloongCore.Extensions;
using PCLCS.Instances;
using PCLCS.Settings;
using System.Globalization;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Instances;

//* 触及 SettingsService（Load 内部 RefreshInfo 读取实例设置），与设置类测试共享串行集合
[Collection("PCLCS.Settings")]
public class InstanceScannerTests : IDisposable
{
    private readonly string _root;

    public InstanceScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclcs.scanner." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        SettingsService.Configure(_root);   // 全局 Settings.json 落在临时目录
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string VersionDir(string name) => Path.Combine(_root, "versions", name + Path.DirectorySeparatorChar);

    #region JSON 工厂（沿用 Task 4 InstanceCoreTests 的形状：vanilla/forge/neoforge/fabric/optifine/liteloader/fool/snapshot/old/broken）

    private static JsonObject BaseJson(string id, string type, params string[] libraries)
    {
        var json = new JsonObject
        {
            ["id"] = id,
            ["type"] = type,
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["releaseTime"] = "2023-06-12T10:00:00Z",
            ["time"] = "2023-06-12T10:00:00Z",
            ["assets"] = "5",
            ["assetIndex"] = new JsonObject { ["id"] = "5" },
            ["libraries"] = new JsonArray()
        };
        foreach (var library in libraries) json["libraries"]!.AsArray().Add(new JsonObject { ["name"] = library });
        return json;
    }

    private InstanceCore CreateInstance(string name, string id, string type, string[]? libraries = null, string? releaseTime = null, Action<JsonObject>? mutate = null)
    {
        WriteVersionJson(name, BaseJson(id, type, libraries ?? []), releaseTime, mutate);
        var core = new InstanceCore(InstancePaths.ResolveVersionFolder(name, _root), _root, VersionDir(name));
        core.Load(static (_, _) => { });
        return core;
    }

    /// <summary>仅写版本目录与 JSON，不生成实例（供继承前置等桩使用）。</summary>
    private void WriteVersionJson(string name, JsonObject json, string? releaseTime = null, Action<JsonObject>? mutate = null)
    {
        Directory.CreateDirectory(VersionDir(name));
        if (releaseTime is not null) json["releaseTime"] = releaseTime;
        mutate?.Invoke(json);
        File.WriteAllText(Path.Combine(VersionDir(name), name + ".json"), json.ToJsonString());
    }

    private InstanceCore Vanilla(string name, string id = "1.20.1", string? releaseTime = null) =>
        CreateInstance(name, id, "release", ["com.mojang:brigadier:1.0.18"], releaseTime);

    private InstanceCore OptiFine(string name, string id = "1.20.1", string optiFine = "HD_U_I6", string? releaseTime = null) =>
        CreateInstance(name, id, "release", [$"optifine:OptiFine:{id}-{optiFine}"], releaseTime);

    private InstanceCore Forge(string name, string forgeVersion = "47.2.0", string? releaseTime = null, bool withOptiFine = false, bool withLiteLoader = false)
    {
        var libraries = new List<string> { $"net.minecraftforge:forge:1.20.1-{forgeVersion}" };
        if (withOptiFine) libraries.Add("optifine:OptiFine:1.20.1-HD_U_I6");
        if (withLiteLoader) libraries.Add("com.mumfrey:liteloader:1.12.2");
        return CreateInstance(name, "1.20.1", "release", libraries.ToArray(), releaseTime);
    }

    private InstanceCore NeoForge(string name, string? releaseTime = null) =>
        CreateInstance(name, "1.20.4", "release", ["net.neoforged:neoforge:20.4.80-beta"], releaseTime,
            static json => json["arguments"] = new JsonObject { ["game"] = new JsonArray("--username", "x", "--fml.neoForgeVersion", "20.4.80-beta") });
    private InstanceCore Fabric(string name, string? releaseTime = null) =>
        CreateInstance(name, "1.20.1", "release", ["net.fabricmc:fabric-loader:0.15.11"], releaseTime);

    private InstanceCore LiteLoader(string name) =>
        CreateInstance(name, "1.12.2", "release", ["com.mumfrey:liteloader:1.12.2"]);

    private InstanceCore Snapshot(string name, string? releaseTime = null) =>
        CreateInstance(name, "23w14a", "snapshot", ["a.jar"], releaseTime);

    private InstanceCore Fool(string name) =>
        CreateInstance(name, "25w14craftmine", "release", ["a.jar"], null,
            static json => json["clientVersion"] = "25w14craftmine");

    private InstanceCore Old(string name) =>
        CreateInstance(name, "beta-old", "release", ["a.jar"], "2011-11-18T00:00:00Z");

    private InstanceCore Broken(string name)
    {
        Directory.CreateDirectory(VersionDir(name));
        File.WriteAllText(Path.Combine(VersionDir(name), name + ".json"),
            new JsonObject { ["type"] = "release", ["id"] = "1.20.1" }.ToJsonString());   // 缺 mainClass
        var core = new InstanceCore(InstancePaths.ResolveVersionFolder(name, _root), _root, VersionDir(name));
        core.Load(static (_, _) => { });
        return core;
    }

    #endregion

    #region ClassifyAndSort

    [Fact]
    public void Api_versions_group_together_and_star_bubbles_first()
    {
        // 十种形状各一：收藏的原版冒泡到 Star 卡，API 类一起进 API 卡（:1177-1191）
        WriteVersionJson("1.12.2", BaseJson("1.12.2", "release", "vanilla.jar"));   // LiteLoader 的前置版本桩
        var cores = new List<InstanceCore>
        {
            Vanilla("vanilla"), Forge("forge"), NeoForge("neoforge"), Fabric("fabric"),
            OptiFine("optifine"), LiteLoader("liteloader"), Fool("fool"), Snapshot("snapshot"),
            Old("old"), Broken("broken")
        };
        cores[0].IsStar = true;   // vanilla 收藏

        var groups = InstanceScanner.ClassifyAndSort(cores);

        Assert.Equal(-1, groups[0].cardType);                       // Star 最前
        Assert.Contains(groups, static g => g.cardType == 2);              // API 卡
        // 卡片序 Star,API,OriginalLike,Rubbish,Fool,Error,Hidden（:1275），非空卡依序全部在场
        Assert.Equal([-1, 2, 3, 4, 5, 6], groups.Select(static g => g.cardType).ToArray());
        Assert.Equal(["vanilla"], groups[0].items.Select(static i => i.Name).ToArray());
        Assert.Equal(["fabric", "neoforge", "forge", "liteloader"], groups[1].items.Select(static i => i.Name).ToArray());   // API 卡按组件旗标降序
        Assert.Equal(["optifine", "snapshot"], groups[2].items.Select(static i => i.Name).ToArray());   // 最新快照与 OptiFine 进 OriginalLike（:1198-1205、:1041-1044）
        Assert.Equal(["old"], groups[3].items.Select(static i => i.Name).ToArray());    // Old 预先入 Rubbish（:993-996）
        Assert.Equal(["fool"], groups[4].items.Select(static i => i.Name).ToArray());
        Assert.Equal(["broken"], groups[5].items.Select(static i => i.Name).ToArray());
    }

    [Fact]
    public void Only_latest_snapshot_is_useful_rest_go_rubbish()
    {
        // 两个快照 + 一个正式版：最新快照进 OriginalLike（:1198-1205），其余快照进 Rubbish（:1207-1208）
        var older = Snapshot("snapshot-old", "2023-01-01T10:00:00Z");
        var newer = Snapshot("snapshot-new", "2023-03-01T10:00:00Z");
        var vanilla = Vanilla("vanilla", releaseTime: "2023-02-01T10:00:00Z");   // 晚于 older、早于 newer

        var groups = InstanceScanner.ClassifyAndSort([older, newer, vanilla]);

        var originalLike = groups.First(static g => g.cardType == 3);
        Assert.Equal(["snapshot-new", "vanilla"], originalLike.items.Select(static i => i.Name).ToArray());   // 发布时间降序
        Assert.Equal(["snapshot-old"], groups.First(static g => g.cardType == 4).items.Select(static i => i.Name).ToArray());
    }

    [Fact]
    public void Per_drop_keeps_newest_vanilla_and_optifine()
    {
        // 同 Drop 双原版按 ReleaseTime 取最新（:1021-1024），同 Drop 双 OptiFine 按 OptiFineCode 取更高（:1021-1022）
        var oldVanilla = Vanilla("a-old-vanilla", releaseTime: "2023-01-01T10:00:00Z");
        var newVanilla = Vanilla("b-new-vanilla", releaseTime: "2023-07-01T10:00:00Z");
        var h8 = OptiFine("c-optifine-h8", id: "1.19.4", optiFine: "HD_U_H8");   // OptiFineCode 80899
        var i6 = OptiFine("d-optifine-i6", id: "1.19.4", optiFine: "HD_U_I6");   // OptiFineCode 90699

        var groups = InstanceScanner.ClassifyAndSort([oldVanilla, h8, i6, newVanilla]);

        Assert.Equal(["b-new-vanilla", "d-optifine-i6"], groups.First(static g => g.cardType == 3).items.Select(static i => i.Name).ToArray());
        Assert.Equal(["c-optifine-h8", "a-old-vanilla"], groups.First(static g => g.cardType == 4).items.Select(static i => i.Name).ToArray());   // 发布时间降序
    }

    [Fact]
    public void Per_drop_vanilla_loses_to_optifine_in_same_drop()
    {
        // VB :1234 仅在原版 Drop 严格大于 OptiFine Drop 时才加入原版；而 Drop 分组键即 Version.Drop，
        // 同组内两值恒等 → 该分支实际不可达，仅 OptiFine 进 OriginalLike、原版落入 Rubbish（VB 实际行为，保持不“修复”）
        var vanilla = Vanilla("1.20.1");
        var optifine = OptiFine("1.20.1-OptiFine_HD_U_I6");

        var groups = InstanceScanner.ClassifyAndSort([vanilla, optifine]);

        Assert.Equal(["1.20.1-OptiFine_HD_U_I6"], groups.First(static g => g.cardType == 3).items.Select(static i => i.Name).ToArray());
        Assert.Equal(["1.20.1"], groups.First(static g => g.cardType == 4).items.Select(static i => i.Name).ToArray());
    }

    [Fact]
    public void Per_drop_without_pair_keeps_both_kinds()
    {
        // 无原版的 Drop 直接加入 OptiFine；无 OptiFine 的 Drop 直接加入原版（:1241-1249）
        var loneOptifine = OptiFine("1.20.1-OptiFine_HD_U_I6");
        var loneVanilla = Vanilla("1.12.2-vanilla", id: "1.12.2");

        var groups = InstanceScanner.ClassifyAndSort([loneVanilla, loneOptifine]);

        var originalLike = groups.First(static g => g.cardType == 3).items.Select(static i => i.Name).ToArray();
        Assert.Contains("1.20.1-OptiFine_HD_U_I6", originalLike);
        Assert.Contains("1.12.2-vanilla", originalLike);
    }

    [Fact]
    public void Newer_release_prevents_snapshot_from_being_useful()
    {
        // :1198-1205 仅当最新（原版 ∪ 快照）本身是快照时才保留；正式版更新时所有快照进 Rubbish
        var vanilla = Vanilla("vanilla", releaseTime: "2023-05-01T10:00:00Z");
        var olderSnapshot = Snapshot("snapshot-a", "2023-01-01T10:00:00Z");
        var newerSnapshot = Snapshot("snapshot-b", "2023-02-01T10:00:00Z");

        var groups = InstanceScanner.ClassifyAndSort([vanilla, olderSnapshot, newerSnapshot]);

        Assert.Equal(["vanilla"], groups.First(static g => g.cardType == 3).items.Select(static i => i.Name).ToArray());
        Assert.Equal(["snapshot-b", "snapshot-a"], groups.First(static g => g.cardType == 4).items.Select(static i => i.Name).ToArray());   // 发布时间降序
    }

    [Fact]
    public void Invalid_version_is_skipped_by_drop_dedup_and_goes_rubbish()
    {
        // :1214 版本号无法识别（Version.Valid=false）的实例不参与 Drop 去重，直接落入 Rubbish；
        // JSON 缺 mainClass 与 libraries → Check 失败（State=Error），手动改回 Original 以进入分类主体
        var unknown = Broken("unknown-version");
        unknown.State = InstanceState.Original;

        var groups = InstanceScanner.ClassifyAndSort([unknown]);

        Assert.Equal([4], groups.Select(static g => g.cardType).ToArray());
        Assert.Equal(["unknown-version"], groups[0].items.Select(static i => i.Name).ToArray());
    }

    [Fact]
    public void Only_optifine_leaves_no_largest_and_still_classifies()
    {
        // :1198-1205 无原版与快照时 LargestInstance 为空（VB MaxBy 空序列返回 Nothing），其余流程不受影响
        var optifine = OptiFine("1.20.1-OptiFine_HD_U_I6");

        var groups = InstanceScanner.ClassifyAndSort([optifine]);

        Assert.Equal([3], groups.Select(static g => g.cardType).ToArray());
        Assert.Equal(["1.20.1-OptiFine_HD_U_I6"], groups[0].items.Select(static i => i.Name).ToArray());
    }

    [Fact]
    public void Sort_order_release_time_then_loader_then_name()
    {
        // :1292-1307 比较链逐分支：发布时间（此时全等）→ Fabric → NeoForge → Forge → OptiFine → LiteLoader → 组件版本 → 名称
        WriteVersionJson("1.20.1", BaseJson("1.20.1", "release", "vanilla.jar"));   // lite-combo 无 inheritsFrom，LiteLoader 规则要求 1.20.1 前置
        var fabric = Fabric("fabric");
        var neoforge = NeoForge("neoforge");
        var liteCombo = Forge("lite-combo", withOptiFine: true, withLiteLoader: true);
        var optiCombo = Forge("opti-combo", withOptiFine: true);
        var highCode = Forge("mid-code");   // 默认即 47.2.0，高于 47.1.3 → 组件版本更前
        var bigName = Forge("zzz-api", "47.1.3");
        var smallName = Forge("aaa-api", "47.1.3");

        var groups = InstanceScanner.ClassifyAndSort([bigName, smallName, highCode, optiCombo, liteCombo, neoforge, fabric]);

        Assert.Equal(
            ["fabric", "neoforge", "lite-combo", "opti-combo", "mid-code", "zzz-api", "aaa-api"],
            groups.First(static g => g.cardType == 2).items.Select(static i => i.Name).ToArray());
    }

    [Fact]
    public void Sort_release_time_descends_within_card()
    {
        // :1292-1293 发布时间降序
        var older = Forge("time-old", releaseTime: "2022-06-01T10:00:00Z");
        var newer = Forge("time-new", releaseTime: "2024-01-01T10:00:00Z");

        var groups = InstanceScanner.ClassifyAndSort([older, newer]);

        Assert.Equal(["time-new", "time-old"], groups.First(static g => g.cardType == 2).items.Select(static i => i.Name).ToArray());
    }

    [Fact]
    public void Sort_skips_release_time_when_both_before_year_2000()
    {
        // :1292 任一边年份 >= 2000 才比较发布时间；两边都更早 → 跳过，组件旗标先行
        var fabricEarlier = Fabric("fabric-1999", releaseTime: "1999-01-01T10:00:00Z");
        var forgeLater = Forge("forge-1999", releaseTime: "1999-06-01T10:00:00Z");   // 发布时间更晚也不靠前

        var groups = InstanceScanner.ClassifyAndSort([forgeLater, fabricEarlier]);

        Assert.Equal(["fabric-1999", "forge-1999"], groups.First(static g => g.cardType == 2).items.Select(static i => i.Name).ToArray());
    }

    [Fact]
    public void DisplayType_moves_instance_between_cards_and_hidden_sorts_last()
    {
        // DisplayType 覆盖（:1257-1264）：0 保留计算结果，其余取自定义值；Hidden 卡按卡片序排在最后（:1275）
        var moved = Vanilla("moved-to-api");
        moved.DisplayType = 2;   // McInstanceCardType.API
        var hidden = Vanilla("hidden-one", id: "1.19.4");
        hidden.DisplayType = 1;  // McInstanceCardType.Hidden
        var normal = Vanilla("normal", id: "1.18.2");

        var groups = InstanceScanner.ClassifyAndSort([moved, hidden, normal]);

        Assert.Equal([2, 3, 1], groups.Select(static g => g.cardType).ToArray());   // API、OriginalLike、Hidden（Hidden 最后）
        Assert.Equal(["moved-to-api"], groups[0].items.Select(static i => i.Name).ToArray());
        Assert.Equal(["normal"], groups[1].items.Select(static i => i.Name).ToArray());
        Assert.Equal(["hidden-one"], groups[2].items.Select(static i => i.Name).ToArray());
    }

    [Fact]
    public void Hidden_star_stays_in_normal_flow()
    {
        // 收藏且隐藏的版本不进 Star 卡（:1180），经常规分类后由 DisplayType 覆盖进 Hidden 卡（:1260）
        var star = Vanilla("star-hidden");
        star.IsStar = true;
        star.DisplayType = 1;

        var groups = InstanceScanner.ClassifyAndSort([star]);

        Assert.Equal([1], groups.Select(static g => g.cardType).ToArray());
        Assert.Equal(["star-hidden"], groups[0].items.Select(static i => i.Name).ToArray());
    }

    [Fact]
    public void Classify_does_not_mutate_input_list()
    {
        // 纯函数：不修改传入列表
        var a = Vanilla("a-vanilla");
        var b = Vanilla("b-vanilla", id: "1.19.4");
        var input = new List<InstanceCore> { b, a };

        InstanceScanner.ClassifyAndSort(input);

        Assert.Equal(["b-vanilla", "a-vanilla"], input.Select(static i => i.Name).ToArray());
    }

    #endregion

    #region ScanVersionFolders

    [Fact]
    public void Scan_returns_empty_without_versions_folder()
    {
        Assert.Empty(InstanceScanner.ScanVersionFolders(_root, isFirstLoad: true));
    }

    [Fact]
    public void Empty_folders_and_non_version_dirs_are_skipped()
    {
        // 空文件夹与只有子目录的文件夹跳过（:1146-1149 只检查文件）
        Directory.CreateDirectory(VersionDir("empty"));
        Directory.CreateDirectory(Path.Combine(VersionDir("only-subdir"), "sub"));
        // cache/PCL 无同名 json 跳过（:1150-1154）
        Directory.CreateDirectory(VersionDir("cache"));
        File.WriteAllText(Path.Combine(VersionDir("cache"), "notes.txt"), "x");
        Directory.CreateDirectory(VersionDir("PCL"));
        File.WriteAllText(Path.Combine(VersionDir("PCL"), "PCL.ini"), "x");
        // 同名 json 存在的 BLClient 保留
        Directory.CreateDirectory(VersionDir("BLClient"));
        File.WriteAllText(Path.Combine(VersionDir("BLClient"), "BLClient.json"), "{}");
        File.WriteAllText(Path.Combine(VersionDir("BLClient"), "notes.txt"), "x");
        // 正常版本保留，路径以 \ 结尾
        Directory.CreateDirectory(VersionDir("1.20.1"));
        File.WriteAllText(Path.Combine(VersionDir("1.20.1"), "1.20.1.json"), "{}");

        var scanned = InstanceScanner.ScanVersionFolders(_root, isFirstLoad: false)
            .Select(PathUtils.RemoveExtendedPrefix).ToArray();   // 与 VB 一致，扫描结果可能带 \\?\ 前缀，归一后比较

        Assert.Equal([VersionDir("1.20.1"), VersionDir("BLClient")], scanned);
        Assert.All(scanned, static p => Assert.EndsWith("\\", p));
    }

    [Fact]
    public void Pclignore_cleared_on_first_load_and_skipped_later()
    {
        // 首次加载清理忽略标记（#2781，:1156-1161）；后续加载跳过且不清理（:1162-1164）
        Directory.CreateDirectory(VersionDir("ignored-a"));
        File.WriteAllText(Path.Combine(VersionDir("ignored-a"), "ignored-a.json"), "{}");
        File.WriteAllText(VersionDir("ignored-a") + ".pclignore", "");
        Directory.CreateDirectory(VersionDir("kept-b"));
        File.WriteAllText(Path.Combine(VersionDir("kept-b"), "kept-b.json"), "{}");
        File.WriteAllText(VersionDir("kept-b") + ".pclignore", "");

        var first = InstanceScanner.ScanVersionFolders(_root, isFirstLoad: true)
            .Select(PathUtils.RemoveExtendedPrefix).ToArray();
        Assert.Contains(VersionDir("ignored-a"), first);
        Assert.Contains(VersionDir("kept-b"), first);
        Assert.False(File.Exists(VersionDir("ignored-a") + ".pclignore"));   // 首次加载清理
        Assert.False(File.Exists(VersionDir("kept-b") + ".pclignore"));

        File.WriteAllText(VersionDir("kept-b") + ".pclignore", "");   // 首次加载后再出现的忽略标记
        var later = InstanceScanner.ScanVersionFolders(_root, isFirstLoad: false)
            .Select(PathUtils.RemoveExtendedPrefix).ToArray();
        Assert.DoesNotContain(VersionDir("kept-b"), later);            // 后续加载跳过
        Assert.True(File.Exists(VersionDir("kept-b") + ".pclignore")); // 且不清理
        Assert.Contains(VersionDir("ignored-a"), later);               // 无标记的正常版本保留
    }

    #endregion

    #region CacheCheckCode

    [Fact]
    public void Cache_check_code_matches_vb_formula()
    {
        // VB :979：(缓存版本 & "#" & FolderList.Join("#"c)).GetStableHashCode() Mod (Integer.MaxValue - 1)
        string[] folderNames = ["a", "b", "c"];
        var expected = ((Versions.McInstanceCacheVersion + "#" + string.Join("#", folderNames))
            .GetStableHashCode() % (int.MaxValue - 1)).ToString(CultureInfo.InvariantCulture);

        var code = InstanceScanner.CacheCheckCode(_root, folderNames);

        Assert.Equal(expected, code);
        Assert.InRange(long.Parse(code, CultureInfo.InvariantCulture), 0, int.MaxValue - 1L);   // 无符号哈希取模后非负
    }

    [Fact]
    public void Cache_check_code_changes_with_folder_list()
    {
        Assert.Equal(InstanceScanner.CacheCheckCode(_root, ["a", "b"]), InstanceScanner.CacheCheckCode(_root, ["a", "b"]));   // 确定性
        Assert.NotEqual(InstanceScanner.CacheCheckCode(_root, ["a", "b"]), InstanceScanner.CacheCheckCode(_root, ["a", "c"]));
        Assert.Equal("", InstanceScanner.CacheCheckCode(_root, []));   // 文件夹列表为空 → 清空缓存（VB :771-773）
    }

    #endregion
}