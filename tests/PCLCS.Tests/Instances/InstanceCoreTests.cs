using PCLCS.Instances;
using PCLCS.Settings;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Instances;

//* 触及 SettingsService（PathIndie / RefreshInfo 内部读取），与设置类测试共享串行集合
[Collection("PCLCS.Settings")]
public class InstanceCoreTests : IDisposable
{
    private readonly string _root;

    public InstanceCoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclcs.core." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        SettingsService.Configure(_root);   // 全局 Settings.json 落在临时目录
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string VersionDir(string name) => Path.Combine(_root, "versions", name + Path.DirectorySeparatorChar);

    private InstanceCore NewCore(string name, Func<bool>? runInUi = null, bool createVersionFolder = true)
    {
        if (createVersionFolder) Directory.CreateDirectory(VersionDir(name));
        return new InstanceCore(InstancePaths.ResolveVersionFolder(name, _root), _root, VersionDir(name), runInUi);
    }

    private void WriteVersionJson(string name, JsonObject json)
    {
        Directory.CreateDirectory(VersionDir(name));
        File.WriteAllText(Path.Combine(VersionDir(name), name + ".json"), json.ToJsonString());
    }

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

    private static JsonObject Lib(string name) => new() { ["name"] = name };

    #region Step 1：JSON 管线

    [Fact]
    public void Vanilla_instance_checks_original()
    {
        var core = NewCore("vanilla");
        WriteVersionJson("vanilla", BaseJson("1.20.1", "release", "com.mojang:brigadier:1.0.18"));
        Assert.True(core.Check());
        Assert.Equal(InstanceState.Original, core.State);
        Assert.Equal("1.20.1", core.Version.VanillaName);
    }

    [Fact]
    public void Inherit_json_merges_libraries_child_first()
    {
        // 父版本 2 个 lib + 子版本 1 个 lib → 合并后子在前（ModMinecraft.vb:476-485 语义）
        NewCore("parent-1.20.1");
        WriteVersionJson("parent-1.20.1", BaseJson("1.20.1", "release", "parent.lib.a", "parent.lib.b"));
        var childJson = BaseJson("child-inherit", "release", "child.lib");
        childJson["inheritsFrom"] = "parent-1.20.1";
        childJson["assets"] = "child-asset";   // 其余项子版本优先于父版本（:481-482 注释）
        WriteVersionJson("child-inherit", childJson);
        var core = NewCore("child-inherit");
        Assert.True(core.Check());
        var merged = core.VersionJson["libraries"]!.AsArray();
        Assert.Equal("child.lib", merged[0]!["name"]!.GetValue<string>());
        Assert.Equal("parent.lib.a", merged[1]!["name"]!.GetValue<string>());
        Assert.Equal("parent.lib.b", merged[2]!["name"]!.GetValue<string>());
        Assert.Null(merged.ElementAtOrDefault(3));   // 共 3 项（xUnit2013 规避）
        Assert.Equal("child-asset", core.VersionJson["assets"]!.GetValue<string>());   // 子版本覆盖父版本（:482）
        Assert.Equal("parent-1.20.1", core.InheritName);   // 合并后保留立即父版本名（子版本的 inheritsFrom 经合并保留）
    }

    [Fact]
    public void Self_referencing_inherit_is_rejected()
    {
        // inheritsFrom 指向自身 → Warn 后按无依赖处理（:465-468）
        var core = NewCore("self-inherit");
        var json = BaseJson("self-inherit", "release", "a.jar");
        json["inheritsFrom"] = "self-inherit";
        WriteVersionJson("self-inherit", json);
        Assert.True(core.Check());
    }

    [Fact]
    public void Nested_inherit_in_parent_json_aborts_merge()
    {
        // 父版本 JSON 自引用 → :474 抛“版本依赖项出现嵌套”，被外层捕获告警，子版本保留原始 JSON
        var parentJson = BaseJson("nested-parent", "release", "p.jar");
        parentJson["inheritsFrom"] = "nested-parent";
        WriteVersionJson("nested-parent", parentJson);
        var childJson = BaseJson("nested-child", "release", "c.jar");
        childJson["inheritsFrom"] = "nested-parent";
        WriteVersionJson("nested-child", childJson);
        var core = NewCore("nested-child");
        Assert.True(core.Check());
        Assert.Single(core.VersionJson["libraries"]!.AsArray());   // 未合并父版本库
    }

    [Fact]
    public void Broken_json_reports_error_state_with_message()
    {
        var core = NewCore("broken");
        WriteVersionJson("broken", new JsonObject { ["type"] = "release", ["id"] = "1.20.1" });   // 缺 mainClass
        Assert.False(core.Check());
        Assert.Equal(InstanceState.Error, core.State);
        Assert.Contains("mainClass", core.Info);   // :594-601 的错误信息保留
    }

    [Fact]
    public void Missing_inherited_json_requires_install()
    {
        var core = NewCore("missing-inherit");
        var json = BaseJson("1.20.1", "release", "a.jar");
        json["inheritsFrom"] = "nonexistent-parent";
        WriteVersionJson("missing-inherit", json);
        Assert.False(core.Check());
        Assert.Contains("需要安装 nonexistent-parent 作为前置版本", core.Info);   // :617
    }

    [Fact]
    public void Hmcl_patches_merge_by_priority_and_skip_empty()
    {
        var core = NewCore("hmcl-case");
        var patches = new JsonArray
        {
            new JsonObject { ["id"] = "forge", ["version"] = "47.2.0", ["priority"] = 5, ["mainClass"] = "forge.Bootstrap", ["libraries"] = new JsonArray(Lib("net.minecraftforge:fmlloader:1.20.1-47.2.0")) },
            new JsonObject { ["id"] = "game", ["version"] = "1.20.1", ["priority"] = "0", ["mainClass"] = "net.minecraft.client.main.Main", ["libraries"] = new JsonArray(Lib("com.mojang:brigadier:1.0.18")) },
            new JsonObject { ["priority"] = "10", ["shouldSkip"] = "yes" }   // 无 id → 跳过（:452-454）
        };
        WriteVersionJson("hmcl-case", new JsonObject { ["patches"] = patches });   // 无 time 键（:431 门条件）
        Assert.True(core.Check());
        Assert.True(core.IsHmclFormatJson);
        Assert.Equal("forge.Bootstrap", core.VersionJson["mainClass"]!.GetValue<string>());   // priority 升序排列，高优先级后合并覆盖（:440-441）
        Assert.Equal("hmcl-case", core.VersionJson["id"]!.GetValue<string>());   // 合并后 id=Name（:458）
        Assert.Null(core.VersionJson["inheritsFrom"]);   // 删 inheritsFrom（:459）
        Assert.Null(core.VersionJson["shouldSkip"]);     // 空 id patch 未参与合并
        Assert.Equal("1.20.1", core.Version.VanillaName);
        Assert.Equal(ModLoaders.Forge, core.Version.ModLoaders & ModLoaders.Forge);
        Assert.Equal("", core.InheritName);   // HMCL 版本无 Json 依赖（:555）
    }

    [Fact]
    public void Missing_json_file_probes_candidates()
    {
        // :361-385 同名缺失时 *.json 探测；缺三键的候选被跳过
        var core = NewCore("probe");
        File.WriteAllText(Path.Combine(VersionDir("probe"), "wrong.json"), new JsonObject { ["mainClass"] = "wrong.Main", ["type"] = "release" }.ToJsonString());
        File.WriteAllText(Path.Combine(VersionDir("probe"), "custom.json"), new JsonObject { ["mainClass"] = "custom.Main", ["type"] = "release", ["id"] = "1.20.1" }.ToJsonString());
        Assert.True(core.Check());
        Assert.Equal("custom.Main", core.VersionJson["mainClass"]!.GetValue<string>());
    }

    [Fact]
    public void Missing_json_file_throws_when_no_candidate()
    {
        // :378-379 无候选 → 抛出“未找到版本 JSON 文件”，Check 捕获为 Error
        var core = NewCore("empty-case");
        Assert.False(core.Check());
        Assert.Equal(InstanceState.Error, core.State);
        Assert.Contains("未找到版本 JSON 文件", core.Info);
    }

    [Fact]
    public void Json_read_on_ui_thread_skips_retry()
    {
        // :402-404 主线程分支：立即触发异常，不再重试（runInUi 返回 true）
        var core = NewCore("ui-broken", runInUi: static () => true);
        File.WriteAllText(Path.Combine(VersionDir("ui-broken"), "ui-broken.json"), "");
        Assert.False(core.Check());
        Assert.Equal(InstanceState.Error, core.State);
    }

    [Fact]
    public void Json_read_off_ui_thread_retries_after_delay()
    {
        // :405-410 非主线程分支：2s 后重读再触发异常（runInUi=null 视为非 UI）
        var core = NewCore("retry-broken");
        File.WriteAllText(Path.Combine(VersionDir("retry-broken"), "retry-broken.json"), "x");
        Assert.False(core.Check());
        Assert.Equal(InstanceState.Error, core.State);
    }

    [Fact]
    public void Jar_version_json_supplies_vanilla_name()
    {
        // :518-539 从版本 jar 读 version.json（FileUtils.OpenZip）
        var core = NewCore("jar-case");
        WriteVersionJson("jar-case", BaseJson("jarnamed", "release", "a.jar"));   // id 不匹配版本模式，才轮到 jar 判定
        using (var zip = ZipFile.Open(Path.Combine(VersionDir("jar-case"), "jar-case.jar"), ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("version.json").Open())
        using (var writer = new StreamWriter(entry))
            writer.Write("""{"name": "99.99.99"}""");
        Assert.Equal("99.99.99", core.Version.VanillaName);
    }

    [Fact]
    public void Old_json_format_detected_by_minecraft_arguments()
    {
        // :504-508 IsOldJson = minecraftArguments 非空
        var core = NewCore("old-format");
        var json = BaseJson("1.20.1", "release", "a.jar");
        json["minecraftArguments"] = "--username ${auth_player_name}";
        WriteVersionJson("old-format", json);
        Assert.True(core.Check());
        Assert.True(core.IsOldJson);
        var modern = NewCore("modern-format");
        WriteVersionJson("modern-format", BaseJson("1.20.1", "release", "a.jar"));
        Assert.True(modern.Check());
        Assert.False(modern.IsOldJson);
    }

    #endregion

    #region Step 2：PathIndie 隔离判定

    [Theory]
    [InlineData(0, false)]   // 关闭（:266）
    [InlineData(1, false)]   // 仅隔离可安装 Mod 的版本——vanilla 1.20.1 不可装 Mod，Modable=false（:268）
    [InlineData(2, false)]   // 仅隔离非正式版，1.20.1 为正式版（:270）
    [InlineData(3, false)]   // 隔离非正式版与可装 Mod 版本，两者皆否（:272）
    [InlineData(4, true)]    // 隔离所有版本（:274）
    public void PathIndie_follows_global_setting(int global, bool expectedIndie)
    {
        var core = NewCore("vanilla");
        WriteVersionJson("vanilla", BaseJson("1.20.1", "release", "a.jar"));
        SettingsService.Set("LaunchArgumentIndieV2", global);
        Assert.Equal(expectedIndie ? VersionDir("vanilla") : _root, core.PathIndie);
    }

    [Fact]
    public void PathIndie_global_one_isolates_modable_version()
    {
        // :268 仅隔离可安装 Mod 的版本：Forge 版本 Modable=true → 隔离
        var core = NewCore("forgey");
        WriteVersionJson("forgey", BaseJson("1.20.1", "release", "net.minecraftforge:forge:1.20.1-47.2.0"));
        SettingsService.Set("LaunchArgumentIndieV2", 1);
        Assert.Equal(VersionDir("forgey"), core.PathIndie);
    }

    [Fact]
    public void PathIndie_auto_enables_when_mods_folder_non_empty()
    {
        // :255-260 版本文件夹下存在 mods 文件 → 自动开启，即使全局为“关闭”；:277 写回 V2
        var core = NewCore("modded");
        WriteVersionJson("modded", BaseJson("1.20.1", "release", "a.jar"));
        Directory.CreateDirectory(Path.Combine(VersionDir("modded"), "mods"));
        File.WriteAllText(Path.Combine(VersionDir("modded"), "mods", "a.jar"), "x");
        SettingsService.Set("LaunchArgumentIndieV2", 0);
        Assert.Equal(VersionDir("modded"), core.PathIndie);
        Assert.True(SettingsService.Get<bool>("VersionArgumentIndieV2", VersionDir("modded")));
    }

    [Fact]
    public void PathIndie_auto_enables_when_saves_subfolder_exists()
    {
        // :257 saves 判定按子文件夹（EnumerateDirectories）计数
        var core = NewCore("saved");
        WriteVersionJson("saved", BaseJson("1.20.1", "release", "a.jar"));
        Directory.CreateDirectory(Path.Combine(VersionDir("saved"), "saves", "world"));
        SettingsService.Set("LaunchArgumentIndieV2", 0);
        Assert.Equal(VersionDir("saved"), core.PathIndie);
    }

    [Fact]
    public void PathIndie_ignores_empty_mods_and_saves_files()
    {
        // 空 mods 与 saves 下只有文件（无子文件夹）→ 不触发自动开启，回落全局关闭（:266）
        var core = NewCore("plain");
        WriteVersionJson("plain", BaseJson("1.20.1", "release", "a.jar"));
        Directory.CreateDirectory(Path.Combine(VersionDir("plain"), "mods"));
        Directory.CreateDirectory(Path.Combine(VersionDir("plain"), "saves"));
        File.WriteAllText(Path.Combine(VersionDir("plain"), "saves", "onlyfile.txt"), "x");
        SettingsService.Set("LaunchArgumentIndieV2", 0);
        Assert.Equal(_root, core.PathIndie);
    }

    [Theory]
    [InlineData(1, true)]    // 老开关 1=手动开启 → V2=true（:250-253）
    [InlineData(2, false)]   // 老开关 2=手动关闭 → V2=false
    public void Legacy_indie_setting_migrates(int legacy, bool expectedIndie)
    {
        var core = NewCore("legacy");
        WriteVersionJson("legacy", BaseJson("1.20.1", "release", "a.jar"));
        SettingsService.Set("VersionArgumentIndie", legacy, VersionDir("legacy"));
        Assert.Equal(expectedIndie ? VersionDir("legacy") : _root, core.PathIndie);
        Assert.Equal(expectedIndie, SettingsService.Get<bool>("VersionArgumentIndieV2", VersionDir("legacy")));
    }

    [Fact]
    public void PathIndie_returns_saved_value_without_recompute()
    {
        // :244 已保存 V2 时直接返回，不再探测 mods/全局
        var core = NewCore("cached");
        WriteVersionJson("cached", BaseJson("1.20.1", "release", "a.jar"));
        SettingsService.Set("LaunchArgumentIndieV2", 4);
        Assert.Equal(VersionDir("cached"), core.PathIndie);
        SettingsService.Set("LaunchArgumentIndieV2", 0);   // 全局再变不影响已写回的实例值
        Assert.Equal(VersionDir("cached"), core.PathIndie);
    }

    #endregion

    #region Step 3：Check / Load / 显示名

    [Fact]
    public void Missing_version_folder_reports_error()
    {
        // :587-591 检查文件夹
        var core = NewCore("ghost", createVersionFolder: false);
        Assert.False(core.Check());
        Assert.Equal(InstanceState.Error, core.State);
        Assert.Equal("未找到版本 ghost", core.Info);
    }

    [Fact]
    public void Unknown_version_number_becomes_error_state()
    {
        // :640-641 版本号 Unknown → Error
        var core = NewCore("weirdname");
        WriteVersionJson("weirdname", BaseJson("totally-not-a-version", "release", "a.jar"));
        core.Load(static (_, _) => { });
        Assert.Equal(InstanceState.Error, core.State);
    }

    [Theory]
    [InlineData("vanilla-1.20.1", "1.20.1", "release", "com.mojang:brigadier:1.0.18", InstanceState.Original)]
    [InlineData("old-case", "beta-old", "release", "a.jar", InstanceState.Old)]             // releaseTime 2011 → "Old"（:642-643）
    [InlineData("fool-case", "25w14craftmine", "release", "a.jar", InstanceState.Fool)]     // :646-647
    [InlineData("snapshot-case", "23w14a", "snapshot", "a.jar", InstanceState.Snapshot)]    // :648-649
    [InlineData("optifine-case", "1.20.1", "release", "optifine:OptiFine:1.20.1-HD_U_I6", InstanceState.OptiFine)]   // :652-653
    [InlineData("forge-case", "1.20.1", "release", "net.minecraftforge:forge:1.20.1-47.2.0", InstanceState.Forge)]   // :662-663
    [InlineData("fabric-case", "1.20.1", "release", "net.fabricmc:fabric-loader:0.15.11", InstanceState.Fabric)]     // :660-661
    [InlineData("neoforge-case", "1.20.4", "release", "net.neoforged:neoforge:20.4.80-beta", InstanceState.NeoForge)] // :664-665
    public void Load_state_follows_branch_table(string name, string id, string type, string library, InstanceState expected)
    {
        var core = NewCore(name);
        var json = BaseJson(id, type, library);
        if (name == "old-case") json["releaseTime"] = "2011-11-18T00:00:00Z";   // 2011 年 → GetVanillaName 返回 "Old"
        if (name == "fool-case") json["clientVersion"] = "25w14craftmine";   // 完整愚人节名（id 会被版本号正则截断为 25w14c）
        if (id == "neoforge-case")
            json["arguments"] = new JsonObject { ["game"] = new JsonArray("--username", "x", "--fml.neoForgeVersion", "20.4.80-beta") };
        WriteVersionJson(name, json);
        core.Load(static (_, _) => { });
        Assert.Equal(expected, core.State);
    }

    [Fact]
    public void Liteloader_state_with_vanilla_dependency()
    {
        // :656-657 LiteLoader 判定；:551-552 老 LiteLoader（JSON 无 inheritsFrom）手动替换依赖为原版号
        NewCore("1.12.2");
        WriteVersionJson("1.12.2", BaseJson("1.12.2", "release", "vanilla.jar"));
        var core = NewCore("1.12.2-LiteLoader");
        WriteVersionJson("1.12.2-LiteLoader", BaseJson("1.12.2", "release", "com.mumfrey:liteloader:1.12.2"));
        core.Load(static (_, _) => { });
        Assert.Equal(InstanceState.LiteLoader, core.State);
    }

    [Fact]
    public void Load_marks_loaded_and_sets_info()
    {
        var core = NewCore("vanilla");
        WriteVersionJson("vanilla", BaseJson("1.20.1", "release", "a.jar"));
        core.Load(static (_, _) => { });
        Assert.True(core.IsLoaded);
        Assert.Equal("正式版 1.20.1", core.Info);
    }

    [Fact]
    public void Load_writes_ini_cache_via_callback()
    {
        // :702-717 写缓存经 persistCache 回调交 VB；Logo 缓存留在 VB
        var core = NewCore("vanilla");
        WriteVersionJson("vanilla", BaseJson("1.20.1", "release", "a.jar"));
        var writes = new List<KeyValuePair<string, string?>>();
        core.Load((key, value) => writes.Add(new(key, value)));
        Assert.Equal("State", writes[0].Key);
        Assert.Equal("Original", writes[0].Value);
        Assert.Equal("正式版 1.20.1", writes[1].Value);
        KeyValuePair<string, string?>[] expectedCacheWrites =
        [
            new("ReleaseTime", "2023-06-12 10:00"),
            new("VersionFabric", null),
            new("VersionOptiFine", null),
            new("VersionLiteLoader", "False"),
            new("VersionForge", null),
            new("VersionNeoForge", null),
            new("VersionVanillaName", "1.20.1"),
            new("VersionVanilla", "20.0.1")
        ];
        Assert.Equal(expectedCacheWrites, writes.Skip(2));
    }

    [Fact]
    public void Load_failure_sets_error_and_loaded()
    {
        // :718-725 加载异常路径 → State=Error，finally IsLoaded=true
        var core = NewCore("badload", createVersionFolder: false);
        core.Load(static (_, _) => { });
        Assert.Equal(InstanceState.Error, core.State);
        Assert.True(core.IsLoaded);
    }

    [Theory]
    [InlineData("snapshot-case", "23w14a", "snapshot", "快照版 23w14a")]
    [InlineData("pre-case", "1.20.1-pre2", "release", "预发布版 1.20.1-pre2")]
    [InlineData("rc-case", "1.20.1-rc1", "release", "发布候选 1.20.1-rc1")]
    [InlineData("vanilla-1.20.1", "1.20.1", "release", "正式版 1.20.1")]
    public void VersionDisplayName_prefixes(string name, string id, string type, string expected)
    {
        // :760-774 基础信息前缀
        var core = NewCore(name);
        WriteVersionJson(name, BaseJson(id, type, "a.jar"));
        core.Load(static (_, _) => { });
        Assert.Equal(expected, core.VersionDisplayName());
    }

    [Fact]
    public void VersionDisplayName_old_and_fool()
    {
        var old = NewCore("old-case");
        var oldJson = BaseJson("beta-old", "release", "a.jar");
        oldJson["releaseTime"] = "2011-11-18T00:00:00Z";
        WriteVersionJson("old-case", oldJson);
        old.Load(static (_, _) => { });
        Assert.Equal("远古版本", old.VersionDisplayName());   // :775-776

        var fool = NewCore("fool-case");
        var foolJson = BaseJson("25w14craftmine", "release", "a.jar");
        foolJson["clientVersion"] = "25w14craftmine";   // 完整愚人节名
        WriteVersionJson("fool-case", foolJson);
        fool.Load(static (_, _) => { });
        Assert.Equal("愚人节版本 25w14craftmine", fool.VersionDisplayName());   // :777-778
    }

    [Fact]
    public void VersionDisplayName_appends_mod_loader_info()
    {
        // :753-756 Forge/OptiFine 后缀
        var core = NewCore("forgey");
        var json = BaseJson("1.20.1", "release", "net.minecraftforge:forge:1.20.1-47.2.0");
        json["libraries"]!.AsArray().Add(Lib("optifine:OptiFine:1.20.1-HD_U_I6"));
        WriteVersionJson("forgey", json);
        core.Load(static (_, _) => { });
        Assert.Equal("正式版 1.20.1, Forge 47.2.0, OptiFine I6", core.VersionDisplayName());
    }

    [Fact]
    public void VersionDisplayName_error_returns_existing_info()
    {
        // :779-780 Error 态返回已有错误信息
        var core = NewCore("broken");
        WriteVersionJson("broken", new JsonObject { ["type"] = "release", ["id"] = "1.20.1" });
        core.Load(static (_, _) => { });
        Assert.Equal(core.Info, core.VersionDisplayName());
    }

    [Fact]
    public void RefreshInfo_appends_server_suffix()
    {
        // :741-742 统一通行证 / Authlib 后缀
        var core = NewCore("vanilla");
        WriteVersionJson("vanilla", BaseJson("1.20.1", "release", "a.jar"));
        core.Load(static (_, _) => { });
        core.RefreshInfo(static () => 3);
        Assert.Equal("正式版 1.20.1, 统一通行证验证", core.Info);
        core.RefreshInfo(static () => 4);
        Assert.Equal("正式版 1.20.1, Authlib 验证", core.Info);
    }

    [Fact]
    public void RefreshInfo_fool_uses_fool_name()
    {
        // :738-739 愚人节版本描述来自名表
        var core = NewCore("fool-case");
        var foolJson = BaseJson("25w14craftmine", "release", "a.jar");
        foolJson["clientVersion"] = "25w14craftmine";   // 完整愚人节名
        WriteVersionJson("fool-case", foolJson);
        core.Load(static (_, _) => { });
        core.RefreshInfo(static () => 0);
        Assert.Equal("2025 | 你可以合成任何东西——包括合成你的世界！", core.Info);
    }

    #endregion

    #region Rename 与路径运算

    [Fact]
    public void Rename_moves_files_and_rewrites_json()
    {
        // :818-855 文件/jar/json 面；Setup.ini 与 PCL.ini 修正留 VB wrapper
        var core = NewCore("renamable");
        WriteVersionJson("renamable", BaseJson("1.20.1", "release", "a.jar"));
        Directory.CreateDirectory(Path.Combine(VersionDir("renamable"), "renamable-natives"));
        File.WriteAllText(Path.Combine(VersionDir("renamable"), "renamable-natives", "x.dll"), "x");
        File.WriteAllText(Path.Combine(VersionDir("renamable"), "renamable.jar"), "jar");
        core.Rename("renamed");
        Assert.False(Directory.Exists(VersionDir("renamable")));
        var newPath = VersionDir("renamed");
        Assert.True(Directory.Exists(newPath));
        Assert.True(File.Exists(Path.Combine(newPath, "renamed-natives", "x.dll")));
        Assert.True(File.Exists(Path.Combine(newPath, "renamed.jar")));
        Assert.False(File.Exists(Path.Combine(newPath, "renamable.json")));
        var renamedJson = JsonNode.Parse(File.ReadAllText(Path.Combine(newPath, "renamed.json")))!.AsObject();
        Assert.Equal("renamed", renamedJson["id"]!.GetValue<string>());
        Assert.Equal("net.minecraft.client.main.Main", renamedJson["mainClass"]!.GetValue<string>());
    }

    [Fact]
    public void SamePath_compares_paths_case_insensitively()
    {
        // :858-869 运算符 =/<> 语义（PathUtils.IsSame）
        Assert.True(InstanceCore.SamePath(@"D:\A\B\", @"d:\a\b"));
        Assert.False(InstanceCore.SamePath(@"D:\A\B\", @"D:\A\C"));
        Assert.True(InstanceCore.SamePath(null, ""));
        Assert.False(InstanceCore.SamePath(null, @"D:\x"));
    }

    [Fact]
    public void Resolve_version_folder_relative_and_absolute()
    {
        // :575-578 相对名 → <mcFolder>versions\<name>\；完整路径（含盘符冒号）原样保留
        Assert.Equal(VersionDir("t"), InstancePaths.ResolveVersionFolder("t", _root));
        Assert.Equal(VersionDir("t"), InstancePaths.ResolveVersionFolder(VersionDir("t"), _root));
    }

    [Fact]
    public void GetJsonPath_returns_same_name_file_or_probe()
    {
        // :361-385 同名 json 优先；缺失时探测 *.json；无候选抛出
        var dir = VersionDir("probe2");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "probe2.json"), BaseJson("1.20.1", "release", "a.jar").ToJsonString());
        Assert.Equal(Path.Combine(dir, "probe2.json"), InstancePaths.GetJsonPath(dir, "probe2"));
        File.Delete(Path.Combine(dir, "probe2.json"));
        File.WriteAllText(Path.Combine(dir, "other.json"), BaseJson("1.20.1", "release", "a.jar").ToJsonString());
        Assert.Equal(Path.Combine(dir, "other.json"), InstancePaths.GetJsonPath(dir, "probe2"));
        Assert.Throws<Exception>(() => InstancePaths.GetJsonPath(VersionDir("empty-case"), "probe3"));
    }

    #endregion
}
