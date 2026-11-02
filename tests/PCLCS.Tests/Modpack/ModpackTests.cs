using MeloongCore;
using PCLCS.Download;
using PCLCS.Modpack;
using PCLCS.Net;
using PCLCS.Settings;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Modpack;

/// <summary>
/// 整合包识别与部署核（ModModpack.vb 迁入面）的语义锁定。
/// 断言按旧代码实际行为收敛（brief 计划稿为示意，已修正）：
/// 识别链含一级目录与判定优先级（ModModpack.vb:46-79）、modules 大小写敏感查询与缺失崩溃路径（:284，#1006）、
/// MCBBS OptiFine 后缀重组（:732）、Modrinth 版本文件夹经版本名对话框决策（:399-405 在依赖映射与文件组装之间）。
/// </summary>
public class ModpackTests
{
    #region 识别（ModModpack.vb:44-88）

    /// <summary>构建只含指定根/一级条目的测试压缩包（条目内容均为 {}；带 addons 的 manifest 用 <see cref="WriteZipContent"/> 构造）。</summary>
    private static string WriteZip(string name, params string[] entries)
        => WriteZipContent(name, entries.Select(static entry => (entry, "{}")).ToArray());

    /// <summary>构建含指定条目与内容的测试压缩包。</summary>
    private static string WriteZipContent(string name, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), "pclcs.pack." + name + "." + Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (entry, content) in entries)
            {
                var e = zip.CreateEntry(entry);
                using var writer = new StreamWriter(e.Open());
                writer.Write(content);
            }
        }
        return path;
    }

    private const string ManifestWithAddons = """{"addons": [{"id": "game", "version": "1.12.2"}]}""";

    [Theory]
    [InlineData("mcbbs.packmeta", ModpackKind.Mcbbs)]
    [InlineData("mmc-pack.json", ModpackKind.Mcm)]
    [InlineData("modrinth.index.json", ModpackKind.Modrinth)]
    [InlineData("manifest.json", ModpackKind.CurseForge)]   //无 addons → CF
    [InlineData("modpack.json", ModpackKind.Hmcl)]
    [InlineData("modpack.zip", ModpackKind.LauncherPack)]
    [InlineData("modpack.mrpack", ModpackKind.LauncherPack)]
    [InlineData("nothing.here", ModpackKind.PlainArchive)]
    public void Detect_identifies_pack_kind(string inner, ModpackKind kind)   //:46-58 判定链
    {
        var path = WriteZip("root", inner);
        Assert.Equal(kind, ModpackDetect.Detect(path));
    }

    [Fact]
    public void Detect_manifest_with_addons_is_mcbbs()   //:49-55：根目录 manifest.json 以 addons 区分 CF/MCBBS
    {
        var path = WriteZipContent("addons", ("manifest.json", ManifestWithAddons));
        Assert.Equal(ModpackKind.Mcbbs, ModpackDetect.Detect(path));
    }

    [Theory]
    [InlineData("overrides/mcbbs.packmeta", ModpackKind.Mcbbs, "overrides/")]   //:66/:74
    [InlineData("pack/mmc-pack.json", ModpackKind.Mcm, "pack/")]   //:67
    [InlineData("sub/modrinth.index.json", ModpackKind.Modrinth, "sub/")]   //:68
    [InlineData("h/modpack.json", ModpackKind.Hmcl, "h/")]   //:77
    [InlineData("l/modpack.zip", ModpackKind.LauncherPack, "l/")]   //:78
    public void Detect_identifies_first_level_entries(string inner, ModpackKind kind, string baseFolder)   //:60-79
    {
        var path = WriteZip("first", inner);
        Assert.Equal(kind, ModpackDetect.Detect(path, out var archiveBaseFolder));
        Assert.Equal(baseFolder, archiveBaseFolder);
    }

    [Fact]
    public void Detect_first_level_manifest_splits_by_addons()   //:69-75：一级目录 manifest.json 以 addons 区分，MCBBS 基础文件夹强制 overrides/
    {
        var mcbbsPath = WriteZipContent("firstmcbbs", ("over/manifest.json", ManifestWithAddons));
        Assert.Equal(ModpackKind.Mcbbs, ModpackDetect.Detect(mcbbsPath, out var mcbbsBase));
        Assert.Equal("overrides/", mcbbsBase);

        var cfPath = WriteZip("firstcf", "unrelated/file.txt", "over/manifest.json");
        Assert.Equal(ModpackKind.CurseForge, ModpackDetect.Detect(cfPath, out var cfBase));
        Assert.Equal("over/", cfBase);
    }

    [Fact]
    public void Detect_root_entries_win_over_first_level()   //:46-58 先于 :60-79
    {
        var path = WriteZip("priority", "mcbbs.packmeta", "pack/mmc-pack.json");
        Assert.Equal(ModpackKind.Mcbbs, ModpackDetect.Detect(path));
    }

    [Fact]
    public void Detect_rar_is_rejected_with_dedicated_message()   //:83-84（内容足以避开负向 seek 的 WinIOError 内层，落到 rar 分支）
    {
        var path = Path.Combine(Path.GetTempPath(), "pclcs.pack.rar." + Guid.NewGuid().ToString("N") + ".rar");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("Rar!\u001A\u0007\u0000" + new string('J', 4096)));
        var ex = Assert.Throws<Exception>(() => ModpackDetect.Detect(path));
        Assert.StartsWith("PCL 无法处理 rar 格式的压缩包", ex.Message);
    }

    [Fact]
    public void Detect_corrupt_zip_is_wrapped()   //:86（4096 字节垃圾无 EOCD 且无内层 IO 错误 → 兜底分支）
    {
        var path = Path.Combine(Path.GetTempPath(), "pclcs.pack.bad." + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllText(path, new string('J', 4096));
        var ex = Assert.Throws<Exception>(() => ModpackDetect.Detect(path));
        Assert.Equal("打开整合包文件失败，文件可能损坏或为不支持的压缩包格式", ex.Message);
    }

    [Fact]
    public void Detect_locked_file_wraps_io_error()   //:81-82：占用文件的内层堆栈含 __Error.WinIOError → 第一分支
    {
        var path = WriteZip("locked", "nothing.here");
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = Assert.Throws<Exception>(() => ModpackDetect.Detect(path));
            Assert.Equal("打开整合包文件失败", ex.Message);
        }
    }

    #endregion

    #region CurseForge 部署核（ModModpack.vb:162-363 决策面）

    private const string CurseForgeManifest = """
        {"name":"TestPack","minecraft":{"version":"1.20.1","modLoaders":[
            {"id":"forge-14.23.5.2859"},{"id":"neoforge-20.6.98-beta"},{"id":"fabric-0.16.9"},{"id":"liteloader-1.2"}]},
         "files":[{"projectID":10,"fileID":1},{"projectID":10,"fileID":2,"required":false},{"projectID":11,"fileID":3,"required":true}],
         "overrides":"overrides"}
        """;

    [Fact]
    public void CurseForge_plan_maps_manifest()   //:174/:188-206/:210/:224-231
    {
        var incompatible = new List<string>();
        var plan = ModpackDeploy.BuildCurseForgePlan(CurseForgeManifest, @"C:\mc\versions\t\", incompatible.Add);
        Assert.Equal("1.20.1", plan.MinecraftVersion);
        Assert.Equal("14.23.5.2859", plan.ModLoaderVersions[ModLoaders.Forge]);
        Assert.Equal("20.6.98-beta", plan.ModLoaderVersions[ModLoaders.NeoForge]);
        Assert.Equal("0.16.9", plan.ModLoaderVersions[ModLoaders.Fabric]);
        Assert.Equal(["liteloader-1.2"], incompatible);   //:204 不兼容加载器回调
        Assert.Equal([1, 2, 3], plan.FileIds);
        Assert.Equal([2], plan.OptionalFileIds);   //:230 required=false
        Assert.Equal(10, plan.FileProjectIds[2]);
        Assert.Equal("overrides", plan.OverridesHome);
        Assert.Equal(@"C:\mc\versions\t\", plan.VersionFolder);
    }

    [Fact]
    public void CurseForge_forge_recommended_is_rejected()   //:192
    {
        var ex = Assert.Throws<Exception>(() => ModpackDeploy.BuildCurseForgePlan(
            """{"minecraft":{"version":"1.7.10","modLoaders":[{"id":"forge-recommended"}]}}""", "k"));
        Assert.Equal("该整合包版本过老，已不支持进行安装！", ex.Message);
    }

    [Fact]
    public void CurseForge_missing_minecraft_is_rejected()   //:174
    {
        Assert.Throws<Exception>(() => ModpackDeploy.BuildCurseForgePlan("""{"name":"x"}""", "k"));
    }

    [Fact]
    public void CurseForge_parse_failure_is_wrapped()   //:169-173
    {
        var ex = Assert.Throws<Exception>(() => ModpackDeploy.BuildCurseForgePlan("{", "k"));
        Assert.Equal("CurseForge 整合包安装信息存在问题", ex.Message);
    }

    [Fact]
    public void CurseForge_missing_file_fields_are_skipped_with_hint()   //:225-228
    {
        var hints = new List<string>();
        var plan = ModpackDeploy.BuildCurseForgePlan(
            """{"minecraft":{"version":"1.20.1"},"files":[{"projectID":10,"fileID":1},{"projectID":11}]}""",
            "k", onFileSkipped: hints.Add);
        Assert.Equal([1], plan.FileIds);
        Assert.Single(hints);
        Assert.StartsWith("某项 Mod 缺少必要信息，已跳过：", hints[0]);
    }

    [Theory]
    [InlineData("""{"modules":[{"name":"META-INF"}]}""", "mods", ModpackResourceKind.Mod)]   //:286-288
    [InlineData("""{"modules":[{"name":"mcmod.info"}]}""", "mods", ModpackResourceKind.Mod)]
    [InlineData("""{"modules":[{"name":"fabric.mod.json"}]}""", "mods", ModpackResourceKind.Mod)]
    [InlineData("""{"modules":[{"name":"pack.mcmeta"}]}""", "resourcepacks", ModpackResourceKind.ResourcePack)]   //:289-290
    [InlineData("""{"modules":[{"name":"shaders"}]}""", "shaderpacks", ModpackResourceKind.Shader)]   //:291-292
    [InlineData("""{"modules":[{"name":"unknown"}]}""", "saves", ModpackResourceKind.Map)]   //:293-294
    [InlineData("""{"modules":[]}""", "mods", ModpackResourceKind.Mod)]   //:284 空数组 → Any=False → mods
    [InlineData("""{"modules":null}""", "mods", ModpackResourceKind.Mod)]   //:284 显式 null（#1006）→ mods
    [InlineData("""{"modules":[{"name":"whatever"}],"FileName":"x.jar"}""", "mods", ModpackResourceKind.Mod)]   //:287 大写 FileName 后缀回退
    [InlineData("""{"modules":[{"name":"whatever"}],"fileName":"x.jar"}""", "saves", ModpackResourceKind.Map)]   //小写不命中（Newtonsoft 序数比较，逐字）
    public void CurseForge_modules_classification(string modJson, string folder, ModpackResourceKind kind)   //:281-298
    {
        var (targetFolder, resolvedKind) = ModpackDeploy.ClassifyCurseForgeModules(JsonNode.Parse(modJson)!.AsObject());
        Assert.Equal(folder, targetFolder);
        Assert.Equal(kind, resolvedKind);
    }

    [Fact]
    public void CurseForge_missing_modules_throws_like_old()   //:284 缺键时旧 Enumerable.Any(Nothing) 崩溃路径逐字保留
        => Assert.Throws<ArgumentNullException>(() =>
            ModpackDeploy.ClassifyCurseForgeModules((JsonObject) JsonNode.Parse("""{"id":1}""")!));

    [Fact]
    public void CurseForge_batch_request_is_assembled()   //:237
    {
        var request = ModpackDeploy.BuildCurseForgeBatchRequest([7, 9]);
        Assert.Equal("https://api.curseforge.com/v1/mods/files", request.Url);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("""{"fileIds": [7,9]}""", request.Content);
        Assert.Equal("application/json", request.ContentType);
    }

    [Fact]
    public void CurseForge_missing_file_ids_are_mapped_to_projects()   //:241-246
    {
        var plan = ModpackDeploy.BuildCurseForgePlan(CurseForgeManifest, "k");
        var missing = ModpackDeploy.FindMissingCurseForgeFileIds(
            plan.FileIds, (JsonArray) JsonNode.Parse("""[{"id":1},{"id":3}]""")!);
        Assert.Equal([2], missing);
        Assert.Equal([(10, 2)], ModpackDeploy.MapMissingCurseForgeFiles(plan, missing));
    }

    [Fact]
    public void CurseForge_overrides_folder_resolves_and_rejects_escape()   //#5613（:212-213）
    {
        Assert.Equal(@"C:\t\pack/overrides", ModpackDeploy.ResolveCurseForgeOverridesFolder(@"C:\t\", "pack/", "overrides"));   //旧字符串直拼（分隔符不归一）
        Assert.Equal(@"C:\t\pack/", ModpackDeploy.ResolveCurseForgeOverridesFolder(@"C:\t\", "pack/", "."));   //#5613 点号折叠
        var ex = Assert.Throws<Exception>(() => ModpackDeploy.ResolveCurseForgeOverridesFolder(@"C:\t\", "pack/", @"..\..\evil"));
        Assert.Equal("整合包的 overrides 路径超出了解压目录，请向整合包作者反馈此问题！", ex.Message);
    }

    [Fact]
    public void CurseForge_assembly_deduplicates_and_filters()   //:269-308
    {
        var plan = ModpackDeploy.BuildCurseForgePlan(CurseForgeManifest, @"C:\v\");
        var seen = new List<(string Folder, ModpackResourceKind Kind)>();
        var prompts = 0;
        var files = ModpackDeploy.AssembleCurseForgeFiles(
            (JsonArray) JsonNode.Parse("""[{"id":1,"modules":null,"displayName":"A"},{"id":1,"modules":null,"displayName":"A"},{"id":2,"modules":[],"displayName":"B"}]""")!,   //重复 id（疑似 CurseForge Bug，:274-275）；modules 为 null（#1006）与空数组 → mods
            plan,
            _ => { prompts++; return false; },   //可选文件拒绝（:277-280）
            (modJson, targetFolder, kind) =>
            {
                seen.Add((targetFolder, kind));
                return new NetFile(["http://x/1.jar"], @"C:\v\mods\a.jar", null, false);
            });
        Assert.Single(files);   //重复跳过 + 可选拒绝
        Assert.Single(seen);
        Assert.Equal(("mods", ModpackResourceKind.Mod), seen[0]);
        Assert.Equal(1, prompts);   //仅对可选项弹窗（id=1 非可选）
    }

    [Fact]
    public void CurseForge_assembly_skips_unavailable_downloads()   //:301 DownloadAvailable=false → provider 返回 null
    {
        var plan = ModpackDeploy.BuildCurseForgePlan("""{"minecraft":{"version":"1.20.1"},"files":[{"projectID":10,"fileID":1}]}""", "k");
        var files = ModpackDeploy.AssembleCurseForgeFiles(
            (JsonArray) JsonNode.Parse("""[{"id":1,"modules":null}]""")!, plan, static _ => true,
            static (_, _, _) => null);
        Assert.Empty(files);
    }

    #endregion

    #region Modrinth 部署核（ModModpack.vb:366-491 决策面）

    private const string ModrinthIndex = """
        {"name":"MP","dependencies":{"minecraft":"1.20.1","forge":"41.1.0","neo-forge":"20.6.98-beta","fabric-loader":"0.16.9","quilt-loader":"1.0"},
         "files":[
            {"path":"mods/a.jar","downloads":["http://x/a"],"hashes":{"sha1":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"env":{"client":"optional"}},
            {"path":"mods/b.jar","downloads":["http://x/b"],"hashes":{"sha1":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},"env":{"client":"unsupported"}},
            {"path":"mods/c.jar","downloads":["http://x/c"],"hashes":{"sha1":"cccccccccccccccccccccccccccccccccccccccc"}},
            {"path":"mods/d.jar","downloads":["http://x/d"],"hashes":{"sha1":"dddddddddddddddddddddddddddddddddddddddd"},"env":{"client":"required"}}
         ]}
        """;

    [Fact]
    public void Modrinth_plan_maps_dependencies_and_env_filter()   //:381-397/:418-428
    {
        var incompatible = new List<string>();
        var plan = ModpackDeploy.BuildModrinthPlan(ModrinthIndex, @"C:\v\", notifyIncompatibleLoader: name => incompatible.Add(name));
        Assert.Equal("1.20.1", plan.MinecraftVersion);
        Assert.Equal("41.1.0", plan.ModLoaderVersions[ModLoaders.Forge]);
        Assert.Equal("20.6.98-beta", plan.ModLoaderVersions[ModLoaders.NeoForge]);
        Assert.Equal("0.16.9", plan.ModLoaderVersions[ModLoaders.Fabric]);
        Assert.Equal(["quilt-loader"], incompatible);
        Assert.Equal("MP", plan.SuggestedName);
        //optionalPrompt 未注入（测试直用面）时可选文件视作下载；unsupported 无条件跳过
        Assert.Equal(["a.jar", "c.jar", "d.jar"], plan.Files.Select(static file => file.LocalPath).Select(PathUtils.GetLastPart));
    }

    [Fact]
    public void Modrinth_optional_file_prompt_can_skip()   //:421-426
    {
        var prompted = new List<string>();
        var plan = ModpackDeploy.BuildModrinthPlan(ModrinthIndex, @"C:\v\",
            optionalPrompt: name => { prompted.Add(name); return true; });
        Assert.Equal(["a.jar", "c.jar", "d.jar"], plan.Files.Select(static file => PathUtils.GetLastPart(file.LocalPath)));
        Assert.Equal(["a.jar"], prompted);
    }

    [Fact]
    public void Modrinth_missing_minecraft_is_rejected()   //:377
        => Assert.Throws<Exception>(() => ModpackDeploy.BuildModrinthPlan("""{"dependencies":{}}""", "k"));

    [Fact]
    public void Modrinth_path_escape_is_rejected()   //:436-441
    {
        var messages = new List<string>();
        Assert.Throws<OperationCanceledException>(() => ModpackDeploy.BuildModrinthPlan(
            """{"dependencies":{"minecraft":"1.20.1"},"files":[{"downloads":["http://x/y"],"path":"../../evil.jar","hashes":{"sha1":"abc"}}]}""",
            @"C:\v\", onEscapeRejected: messages.Add));
        Assert.Single(messages);
        Assert.StartsWith("整合包的文件路径超出了版本文件夹", messages[0]);
    }

    [Fact]
    public void Modrinth_netfile_carries_sha1_checker_without_size_check()   //:442-443
    {
        var plan = ModpackDeploy.BuildModrinthPlan(ModrinthIndex, @"C:\v\");
        var file = plan.Files.First(static file => PathUtils.GetLastPart(file.LocalPath) == "c.jar");
        Assert.Equal(@"C:\v\mods\c.jar", file.LocalPath.Replace('/', '\\'));
        Assert.Equal("cccccccccccccccccccccccccccccccccccccccc", file.Check!.Hash);
        Assert.Equal(-1, file.Check.ActualSize);   //Modrinth 整合包不应校验文件大小（旧注释）
        Assert.True(file.SimulateBrowserHeaders);
    }

    [Fact]
    public void Modrinth_urls_go_through_pipeline_then_distinct()   //:432-436
    {
        IReadOnlyList<string>? receivedRaw = null;
        string? receivedVersion = null;
        ModLoaders receivedLoaders = ModLoaders.None;
        var plan = ModpackDeploy.BuildModrinthPlan(
            """{"dependencies":{"minecraft":"1.20.1","forge":"41.1.0"},"files":[{"path":"m.jar","downloads":["http://raw/1","http://raw/2"],"hashes":{"sha1":"a"}}]}""",
            @"C:\v\",
            urlPipeline: (raw, version, loaders) =>
            {
                receivedRaw = raw;
                receivedVersion = version;
                receivedLoaders = loaders;
                return ["http://mirror/1", "http://raw/1", "http://mirror/2", "http://raw/1"];
            });
        Assert.Equal(["http://raw/1", "http://raw/2"], receivedRaw);
        Assert.Equal("1.20.1", receivedVersion);   //回调在计划产出前执行，版本面经入参注入
        Assert.Equal(ModLoaders.Forge, receivedLoaders);   //旧 TargetVersion.ModLoaders 的旗标聚合
        var urls = plan.Files.Single().Sources.Select(static source => source.Url).ToList();
        Assert.Equal(["http://mirror/1", "http://raw/1", "http://mirror/2"], urls);   //:436 Distinct 保序
    }

    [Fact]
    public void Modrinth_version_folder_resolves_through_dialog_callback()   //:399-405 版本名输入在依赖映射与文件组装之间
    {
        var order = new List<string>();
        var plan = ModpackDeploy.BuildModrinthPlan(
            """{"name":"Pack","dependencies":{"minecraft":"1.20.1"},"files":[{"path":"m.jar","downloads":["http://x"],"hashes":{"sha1":"a"},"env":{"client":"optional"}}]}""",
            "fallback",
            resolveVersionFolder: name =>
            {
                order.Add("dialog:" + name);
                return @"C:\mc\versions\My\";
            },
            optionalPrompt: _ => { order.Add("prompt"); return true; });
        Assert.Equal(["dialog:Pack", "prompt"], order);   //版本名对话框先于可选文件对话框
        Assert.Equal(@"C:\mc\versions\My\m.jar", plan.Files.Single().LocalPath.Replace('/', '\\'));
    }

    #endregion

    #region MMC 组件映射（ModModpack.vb:631-651）

    [Fact]
    public void Mmc_components_skip_lwjgl()   //:646-647（#8210）
    {
        var map = ModpackDeploy.MapMmcComponents((JsonArray) JsonNode.Parse(
            """[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"org.lwjgl3","version":"3.3.1"}]""")!);
        Assert.Equal("1.20.1", map["net.minecraft"]);
        Assert.False(map.ContainsKey("org.lwjgl3"));
    }

    [Fact]
    public void Mmc_components_map_loaders_and_notify_unknown()   //:637-650
    {
        var unknown = new List<string>();
        var map = ModpackDeploy.MapMmcComponents((JsonArray) JsonNode.Parse(
            """
            [{"uid":"net.minecraft","version":"1.20.1"},
             {"uid":"net.minecraftforge","version":"14.23.5.2859"},
             {"uid":"net.neoforged","version":"20.6.98-beta"},
             {"uid":"net.fabricmc.fabric-loader","version":"0.16.9"},
             {"uid":"org.lwjglx","version":"3"},
             {"uid":"no.uid.here","version":9},
             {"noUid":true}]
            """)!, unknown.Add);
        Assert.Equal("14.23.5.2859", map["net.minecraftforge"]);
        Assert.Equal("20.6.98-beta", map["net.neoforged"]);
        Assert.Equal("0.16.9", map["net.fabricmc.fabric-loader"]);
        Assert.False(map.ContainsKey("no.uid.here"));   //未知 uid 仅回调不进映射（:649）
        Assert.False(map.ContainsKey("org.lwjglx"));   //org.lwjgl 前缀跳过（#8210）
        Assert.Equal(["no.uid.here"], unknown);   //未知 uid 回调
    }

    [Fact]
    public void Mmc_components_build_install_target()   //:654
    {
        var components = ModpackDeploy.BuildMmcComponents(new Dictionary<string, string>
        {
            ["net.minecraft"] = "1.20.1",
            ["net.minecraftforge"] = "14.23.5.2859",
            ["net.fabricmc.fabric-loader"] = "0.16.9"
        });
        Assert.Equal("1.20.1", components.MinecraftVersion);
        Assert.Equal("14.23.5.2859", components.ModLoaderVersions[ModLoaders.Forge]);
        Assert.Equal("0.16.9", components.ModLoaderVersions[ModLoaders.Fabric]);
        Assert.False(components.ModLoaderVersions.ContainsKey(ModLoaders.NeoForge));
    }

    #endregion

    #region MCBBS 部署核（ModModpack.vb:677-761 决策面）

    private static DlOptiFineListEntry OptiFineEntry(string inherit, string fileName, string instanceName)
        => new() { Inherit = inherit, FileName = fileName, InstanceName = instanceName, DisplayName = inherit + " C8" };

    [Fact]
    public void Mcbbs_plan_maps_addons_and_optifine()   //:714-739
    {
        var providerCalls = 0;
        var plan = ModpackDeploy.BuildMcbbsPlan(
            """
            {"name":"MC","addons":[{"id":"game","version":"1.12.2"},{"id":"forge","version":"14.23.5.2859"},{"id":"optifine","version":"HD_U_C8"}],
             "launchInfo":{"javaArgument":["-Xmx4G"],"launchArgument":["--demo"]}}
            """,
            optiFineListProvider: () => { providerCalls++; return [OptiFineEntry("1.12.2", "OptiFine_1.12.2_HD_U_C8.jar", "1.12.2-OptiFine_HD_U_C8")]; });
        Assert.Equal("1.12.2", plan.MinecraftVersion);
        Assert.Equal("14.23.5.2859", plan.ModLoaderVersions[ModLoaders.Forge]);
        Assert.Equal("1.12.2-OptiFine_HD_U_C8", plan.OptiFineEntry!["InstanceName"]!.ToString());   //:738 字段原样序列化
        Assert.Equal("-Xmx4G", plan.LaunchJvmArguments);   //:709 String.Join
        Assert.Equal("--demo", plan.LaunchGameArguments);   //:710
        Assert.Equal(1, providerCalls);   //仅在有 optifine 项时取列表（旧 WaitForExit 位置）
    }

    [Fact]
    public void Mcbbs_game_version_dot_zero_is_stripped_for_optifine_lookup()   //:730-731
    {
        var plan = ModpackDeploy.BuildMcbbsPlan(
            """{"addons":[{"id":"game","version":"1.8.0"},{"id":"optifine","version":"HD_U_D5"}]}""",
            optiFineListProvider: () => [OptiFineEntry("1.8", "OptiFine_1.8_HD_U_D5.jar", "1.8-OptiFine_HD_U_D5")]);
        Assert.Equal("1.8-OptiFine_HD_U_D5", plan.OptiFineEntry!["InstanceName"]!.ToString());
    }

    [Fact]
    public void Mcbbs_without_optifine_addon_skips_list()   //:729 If 分支不进入
    {
        var plan = ModpackDeploy.BuildMcbbsPlan(
            """{"addons":[{"id":"game","version":"1.12.2"},{"id":"fabric","version":"0.16.9"}]}""",
            optiFineListProvider: () => throw new InvalidOperationException("不应取列表"));
        Assert.Null(plan.OptiFineEntry);
        Assert.Equal("0.16.9", plan.ModLoaderVersions[ModLoaders.Fabric]);
    }

    [Fact]
    public void Mcbbs_missing_game_is_rejected()   //:719
    {
        var ex = Assert.Throws<Exception>(() => ModpackDeploy.BuildMcbbsPlan("""{"addons":[{"id":"forge","version":"1.0"}]}"""));
        Assert.Equal("该 MCBBS 整合包未提供游戏版本信息，无法安装！", ex.Message);
    }

    [Fact]
    public void Mcbbs_missing_addons_is_rejected()   //:714
    {
        var ex = Assert.Throws<Exception>(() => ModpackDeploy.BuildMcbbsPlan("""{"name":"x"}"""));
        Assert.Equal("该 MCBBS 整合包未提供游戏版本附加信息，无法安装！", ex.Message);
    }

    [Fact]
    public void Mcbbs_quilt_is_rejected()   //:720-723
    {
        var rejected = 0;
        Assert.Throws<OperationCanceledException>(() => ModpackDeploy.BuildMcbbsPlan(
            """{"addons":[{"id":"game","version":"1.12.2"},{"id":"quilt","version":"0.20"}]}""",
            onQuiltRejected: () => rejected++));
        Assert.Equal(1, rejected);
    }

    [Fact]
    public void Mcbbs_missing_optifine_entry_is_rejected()   //:737
    {
        var ex = Assert.Throws<Exception>(() => ModpackDeploy.BuildMcbbsPlan(
            """{"addons":[{"id":"game","version":"1.12.2"},{"id":"optifine","version":"HD_U_C8"}]}""",
            optiFineListProvider: () => [OptiFineEntry("1.16.5", "OptiFine_1.16.5_HD_U_G8.jar", "1.16.5-OptiFine_HD_U_G8")]));
        Assert.Equal("OptiFine 列表中没有整合包所需的 OptiFine HD_U_C8（Minecraft 1.12.2）", ex.Message);
    }

    [Fact]
    public void Mcbbs_parse_failure_is_wrapped()   //:683-688
    {
        var ex = Assert.Throws<Exception>(() => ModpackDeploy.BuildMcbbsPlan("{"));
        Assert.Equal("MCBBS 整合包安装信息存在问题", ex.Message);
    }

    #endregion

    #region HMCL 版本判定核（ModModpack.vb:496-521）

    [Fact]
    public void Hmcl_plan_carries_name_and_game_version()   //:505/:520-521
    {
        var plan = ModpackDeploy.BuildHmclPlan("""{"name":"HP","gameVersion":"1.20.1"}""");
        Assert.Equal("HP", plan.SuggestedName);
        Assert.Equal("1.20.1", plan.GameVersion);
    }

    [Fact]
    public void Hmcl_missing_game_version_is_rejected()   //:520
    {
        var ex = Assert.Throws<Exception>(() => ModpackDeploy.BuildHmclPlan("""{"name":"HP"}"""));
        Assert.Equal("该 HMCL 整合包未提供游戏版本信息，无法安装！", ex.Message);
    }

    [Fact]
    public void Hmcl_parse_failure_is_wrapped()   //:499-503
    {
        var ex = Assert.Throws<Exception>(() => ModpackDeploy.BuildHmclPlan("{"));
        Assert.Equal("HMCL 整合包安装信息存在问题", ex.Message);
    }

    #endregion

    #region 覆写部署（ModModpack.vb:124-151）

    //* CopyOverrideDirectory 经 SettingsService instanceKey 面（W1 facade），与服务测试共享静态状态
    [Collection("PCLCS.Settings")]
    public class CopyOverrideTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "pclcs.override." + Guid.NewGuid().ToString("N"));
        private string VersionFolder => Path.Combine(_root, "versions", "t") + Path.DirectorySeparatorChar;

        public CopyOverrideTests()
        {
            Directory.CreateDirectory(VersionFolder);
            Directory.CreateDirectory(Path.Combine(_root, "global"));
            SettingsService.Configure(Path.Combine(_root, "global"));
        }
        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Fact]
        public void Copies_files_and_routes_settings_face()   //:132-150
        {
            var overrides = Path.Combine(_root, "over");
            Directory.CreateDirectory(Path.Combine(overrides, "PCL"));
            File.WriteAllText(Path.Combine(overrides, "config.txt"), "hello");
            File.WriteAllText(Path.Combine(overrides, "PCL", "Setup.ini"), "Custom:Value\r\n");

            ModpackDeploy.CopyOverrideDirectory(overrides, VersionFolder, VersionFolder);

            Assert.Equal("hello", File.ReadAllText(Path.Combine(VersionFolder, "config.txt")));   //:134 目录复制
            var ini = File.ReadAllText(Path.Combine(VersionFolder, "PCL", "Setup.ini"));   //:145 覆写已有的 ini
            Assert.Contains("Custom:Value", ini);
            Assert.Contains("IsStar:False", ini);   //:144（非目录键，落实体 ini——W1 wrapper 的读取面）
            Assert.Equal(1, SettingsService.Get<int>("VersionArgumentIndie", VersionFolder));   //:142 目录键经 instanceKey 面
            Assert.True(SettingsService.Get<bool>("VersionArgumentIndieV2", VersionFolder));   //:143
        }

        [Fact]
        public void Without_overrides_ini_writes_version_ini()   //:146-148
        {
            var overrides = Path.Combine(_root, "over");
            Directory.CreateDirectory(overrides);
            File.WriteAllText(Path.Combine(overrides, "config.txt"), "hello");

            ModpackDeploy.CopyOverrideDirectory(overrides, VersionFolder, VersionFolder);

            var ini = File.ReadAllText(Path.Combine(VersionFolder, "PCL", "Setup.ini"));
            Assert.Contains("VersionArgumentIndie:1", ini);
            Assert.Contains("VersionArgumentIndieV2:True", ini);
            Assert.DoesNotContain("IsStar", ini);   //:141 分支未进入时不写 IsStar
        }

        [Fact]
        public void Without_overrides_folder_still_writes_ini()   //:136/:147-150
        {
            ModpackDeploy.CopyOverrideDirectory(Path.Combine(_root, "missing"), VersionFolder, VersionFolder);
            var ini = File.ReadAllText(Path.Combine(VersionFolder, "PCL", "Setup.ini"));
            Assert.Contains("VersionArgumentIndie:1", ini);
            Assert.Contains("VersionArgumentIndieV2:True", ini);
        }
    }

    #endregion
}