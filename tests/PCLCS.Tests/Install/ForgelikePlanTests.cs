using MeloongCore.Extensions;
using PCLCS.Download;
using PCLCS.Install;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Install;

/// <summary>
/// Forge/NeoForge 安装决策核（ModDownloadLib.vb:1122-1141 地址、:1150-1193 新版链、:1293-1342 Legacy 1/2、
/// :1039-1076 注入器行映射）的语义锁定。断言按旧代码实际行为收敛（brief 计划稿为示意，已修正）：
/// 地址决策返回 NetFile 直用形态（候选源 + 校验器），行映射的累进行相对当前进度 +0.003。
/// </summary>
public class ForgelikePlanTests
{
    private static string TempRoot(string name) => Path.Combine(Path.GetTempPath(), "pclcs.forge." + name + "." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;

    #region 注入器行映射（:1039-1076 逐字）

    /// <summary>ModDownloadLib.vb:1041-1070 的全部绝对赋值映射对。</summary>
    public static IEnumerable<object[]> LineMapRows() =>
    [
        ["Extracting json", 0.27],
        ["Downloading libraries", 0.28],
        ["Building Processors", 0.38],
        ["Task: DOWNLOAD_MOJMAPS", 0.4],
        ["Task: MERGE_MAPPING", 0.5],
        ["Splitting: ", 0.55],
        ["Parameter Annotations", 0.6],
        ["Processing Complete", 0.67],
        ["log: null", 0.67],
        ["Sorting", 0.8],
        ["Remapping final jar", 0.85],
        ["Remapping jar... 50%", 0.9],
        ["Remapping jar... 100%", 0.95],
        ["Injecting profile", 0.98]
    ];

    [Theory]
    [MemberData(nameof(LineMapRows))]
    public void Injector_line_maps_absolute_progress(string line, double expected)
        => Assert.Equal(expected, InstallerPlans.InjectorLineProgress(line, 0.1));

    [Fact]
    public void Injector_checksum_line_accumulates_on_current_progress()   // :1045-1046 的 += 0.003
    {
        Assert.Equal(0.28 + 0.003, InstallerPlans.InjectorLineProgress("  File exists: Checksum validated.", 0.28));
        Assert.Equal(0.28 + 0.003 + 0.003, InstallerPlans.InjectorLineProgress("  File exists: Checksum validated.", 0.28 + 0.003)); //连续命中叠加
    }

    [Theory]
    [InlineData("Considering library ...")]   // brief 计划稿误列的行：实际未命中映射表
    [InlineData("The process finished")]
    [InlineData("")]
    public void Injector_unmapped_line_returns_null(string line)
        => Assert.Null(InstallerPlans.InjectorLineProgress(line, 0.5));

    #endregion

    #region 安装器地址决策（:1122-1141）

    [Fact]
    public void Installer_address_forge_lists_bmclapi_then_official()
    {
        var entry = new DlForgeVersionEntry("41.2.8", null, "1.19.3") { Category = "installer", Hash = "d34db33f" };
        var (installerUrl, sources, checker) = InstallerPlans.ForgelikeInstallerAddress(entry, preferBmclapi: true);
        Assert.Equal("https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/1.19.3-41.2.8/forge-1.19.3-41.2.8-installer.jar", installerUrl);
        Assert.Equal(
        [
            "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/1.19.3-41.2.8/forge-1.19.3-41.2.8-installer.jar",
            "https://files.minecraftforge.net/maven/net/minecraftforge/forge/1.19.3-41.2.8/forge-1.19.3-41.2.8-installer.jar"
        ], sources);
        Assert.Equal(64 * 1024, checker.MinSize);
        Assert.Equal("d34db33f", checker.Hash);
    }

    [Fact]
    public void Installer_address_legacy_forge_uses_underscored_maven_path()   // :1134-1135 Inherit.Replace("-", "_")
    {
        var entry = new DlForgeVersionEntry("10.13.4.1614", "1.7.10-pre4", "1.7.10-pre4") { Category = "installer" };
        var (installerUrl, sources, checker) = InstallerPlans.ForgelikeInstallerAddress(entry, preferBmclapi: false);
        Assert.Equal("https://files.minecraftforge.net/maven/net/minecraftforge/forge/1.7.10_pre4-10.13.4.1614-1.7.10-pre4/forge-1.7.10_pre4-10.13.4.1614-1.7.10-pre4-installer.jar", installerUrl);
        Assert.Equal(2, sources.Count);
        Assert.Equal(64 * 1024, checker.MinSize);
        Assert.Null(checker.Hash);
    }

    [Fact]
    public void Installer_address_neoforge_rewrites_maven_host()   // :1124-1130
    {
        var entry = new DlNeoForgeListEntry("20.4.30-beta");
        var (installerUrl, sources, checker) = InstallerPlans.ForgelikeInstallerAddress(entry, preferBmclapi: true);
        Assert.Equal("https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/20.4.30-beta/neoforge-20.4.30-beta-installer.jar", installerUrl);
        Assert.Equal(
        [
            "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/20.4.30-beta/neoforge-20.4.30-beta-installer.jar",
            "https://maven.neoforged.net/releases/net/neoforged/neoforge/20.4.30-beta/neoforge-20.4.30-beta-installer.jar"
        ], sources);
        Assert.Equal(64 * 1024, checker.MinSize);
        Assert.Null(checker.Hash);
    }

    [Fact]
    public void Installer_address_neoforge_legacy_uses_forge_package_name()   // UrlBase 的 1.20.1 特判（DownloadModels.cs）
    {
        var entry = new DlNeoForgeListEntry("1.20.1-47.1.99");
        var (installerUrl, _, _) = InstallerPlans.ForgelikeInstallerAddress(entry, preferBmclapi: true);
        Assert.Equal("https://bmclapi2.bangbang93.com/maven/net/neoforged/forge/1.20.1-47.1.99/forge-1.20.1-47.1.99-installer.jar", installerUrl);
    }

    #endregion

    #region 新版安装链决策（:1150-1193）

    private const string ModernInstallProfile = """
        {
          "id": "1.19.3",
          "minecraftArguments": "--a 1",
          "libraries": [
            {"name": "net.minecraftforge:forge:1.19.3-41.2.8"},
            {"name": "net.minecraftforge:forge:1.19.3-41.2.8:universal"},
            {"name": "org.ow2.asm:asm:9.3", "downloads": {"artifact": {"path": "org/ow2/asm/asm/9.3/asm-9.3.jar", "url": "https://libraries.minecraft.net/org/ow2/asm/asm/9.3/asm-9.3.jar", "size": 120000, "sha1": "bbbb"}}}
          ],
          "data": {"MOJMAPS": {"client": "[net.minecraft:client:1.17.1-20210706.113038:mappings@txt]"}}
        }
        """;

    private const string ModernVersionJson = """
        {
          "id": "forge-1.19.3",
          "minecraftArguments": "--b 2",
          "libraries": [
            {"name": "net.minecraftforge:mergetool:1.0.9", "downloads": {"artifact": {"path": "net/minecraftforge/mergetool/1.0.9/mergetool-1.0.9.jar", "url": "https://maven.minecraftforge.net/net/minecraftforge/mergetool/1.0.9/mergetool-1.0.9.jar", "size": 5000, "sha1": "cccc"}}}
          ]
        }
        """;

    private const string VanillaJson = """
        {"downloads": {"client_mappings": {"url": "https://piston-data.mojang.com/v1/objects/93942af0/client.txt", "size": 910000, "sha1": "93942af0"}}}
        """;

    private static string WriteInstallerZip(string root, params (string Name, string Content)[] entries)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "installer.jar");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return path;
    }

    [Fact]
    public void Modern_plan_merges_profiles_and_builds_library_files()
    {
        var root = TempRoot("modern");
        var installerPath = WriteInstallerZip(root, ("install_profile.json", ModernInstallProfile), ("version.json", ModernVersionJson));
        var providerCalls = 0;

        var plan = InstallerPlans.BuildModernPlan(
            installerPath, root, "1.19.3", "Forge", "41.2.8", preferMojang: false,
            () => { providerCalls++; return JsonNode.Parse(VanillaJson)!.AsObject(); });

        Assert.Equal(1, providerCalls);   //仅 MOJMAPS 命中时取原版 Json（:1167 在分支内）
        Assert.Equal(4, plan.Files.Count);   //universal + asm + mergetool + mappings（forge 主 Jar 项已被移除）
        Assert.Equal(["下载原版支持库文件", "下载原版 json 文件"], plan.WaitVanillaLoaderNames);   //:1207 匹配名
        //合并方向：version.json 为覆盖层，标量覆盖 install_profile（:1160 Json.Merge(Json2)）
        Assert.Equal("--b 2", plan.MergedVersionJson["minecraftArguments"]!.GetValue<string>());
        Assert.Equal("forge-1.19.3", plan.MergedVersionJson["id"]!.GetValue<string>());
        //去除其中的原始 Forgelike 项（:1179-1186）
        Assert.DoesNotContain(plan.Files, static f => f.LocalPath.EndsWithF("forge-1.19.3-41.2.8.jar"));
        //MOJMAPS 追加项（:1169-1174）：GetLocalPath 后改后缀为 @txt
        var mappings = plan.Files.Single(static f => f.LocalPath.EndsWithF("client-1.17.1-20210706.113038-mappings.txt"));
        Assert.Equal("93942af0", mappings.Check!.Hash);
        Assert.Equal(910000, mappings.Check.ActualSize);
        Assert.Contains("https://piston-data.mojang.com/v1/objects/93942af0/client.txt", mappings.Sources.Select(static s => s.Url));
        //#5455 特判：无 URL 的 forge universal 项补 maven.minecraftforge.net 并镜像优先（:1293-1310）
        var universal = plan.Files.Single(static f => f.LocalPath.EndsWithF("forge-1.19.3-41.2.8-universal.jar"));
        Assert.Equal(
        [
            "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/1.19.3-41.2.8/forge-1.19.3-41.2.8-universal.jar",
            "https://maven.minecraftforge.net/net/minecraftforge/forge/1.19.3-41.2.8/forge-1.19.3-41.2.8-universal.jar"
        ], universal.Sources.Take(2).Select(static s => s.Url).ToArray());
        //库清单（未去重，与旧 Libs 变量一致）含 MOJMAPS 追加项
        Assert.Contains(plan.Libraries, static l => l.Path.EndsWithF("-mappings.txt"));
        Assert.Equal(4, plan.Libraries.Count);
    }

    [Fact]
    public void Modern_plan_without_mojmaps_never_fetches_vanilla_json()
    {
        var root = TempRoot("modern-nomaps");
        const string profile = """{"id": "1.19.3", "libraries": [{"name": "org.ow2.asm:asm:9.3", "downloads": {"artifact": {"path": "org/ow2/asm/asm/9.3/asm-9.3.jar", "url": "https://libraries.minecraft.net/org/ow2/asm/asm/9.3/asm-9.3.jar", "size": 120000, "sha1": "bbbb"}}}]}""";
        var installerPath = WriteInstallerZip(root, ("install_profile.json", profile), ("version.json", """{"id": "forge-1.19.3"}"""));

        var plan = InstallerPlans.BuildModernPlan(
            installerPath, root, "1.19.3", "Forge", "41.2.8", preferMojang: false,
            static () => throw new InvalidOperationException("不应取原版 Json"));

        Assert.Single(plan.Files);
        Assert.DoesNotContain(plan.Libraries, static l => l.Path.EndsWithF(".txt"));
    }

    #endregion

    #region Legacy 方式 1/2 决策（:1293-1342）

    [Fact]
    public void Legacy_way1_writes_version_json_and_copies_maven()
    {
        var root = TempRoot("legacy1");
        var installerPath = WriteInstallerZip(root,
            ("install_profile.json", """{"json": "/version-full.json"}"""),
            ("version-full.json", """{"id": "1.12.2-forge1.12.2", "inheritsFrom": "1.12.2", "libraries": []}"""),
            ("maven/net/minecraftforge/forge/1.12.2-14.23.5.2859/forge-1.12.2-14.23.5.2859.jar", "forge-universal"));
        var versionFolder = root + "new-ver" + Path.DirectorySeparatorChar;
        var mcFolder = root + "mc" + Path.DirectorySeparatorChar;

        var plan = InstallerPlans.BuildLegacyPlan(installerPath, versionFolder, "forge-1.12.2", "1.12.2", mcFolder);

        //建立 Json 文件（:1309-1311）：读取 json 项、id 改为新版本名、写入版本文件夹
        var writtenJson = JsonNode.Parse(File.ReadAllText(versionFolder + "forge-1.12.2.json"))!.AsObject();
        Assert.Equal("forge-1.12.2", writtenJson["id"]!.GetValue<string>());
        Assert.Equal("forge-1.12.2", plan.MergedVersionJson["id"]!.GetValue<string>());
        //解压支持库文件（:1314-1317）：maven 目录复制进 libraries，unrar 临时目录删除
        Assert.Equal("forge-universal", File.ReadAllText(mcFolder + @"libraries\net\minecraftforge\forge\1.12.2-14.23.5.2859\forge-1.12.2-14.23.5.2859.jar"));
        Assert.False(Directory.Exists(installerPath + "_unrar"));
        Assert.Empty(plan.Files);
        Assert.Empty(plan.WaitVanillaLoaderNames);
    }

    [Fact]
    public void Legacy_way2_writes_jar_and_version_json_with_inherits_from()
    {
        var root = TempRoot("legacy2");
        var installerPath = WriteInstallerZip(root,
            ("install_profile.json", """{"install": {"path": "net.minecraftforge:forge:1.12.2-14.23.5.2859", "filePath": "forge-universal.jar"}, "versionInfo": {"id": "1.12.2-forge1.12.2", "minecraftArguments": "--tweakClass forge"}}"""),
            ("forge-universal.jar", "legacy-jar-bytes"));
        var versionFolder = root + "new-ver" + Path.DirectorySeparatorChar;
        var mcFolder = root + "mc" + Path.DirectorySeparatorChar;

        var plan = InstallerPlans.BuildLegacyPlan(installerPath, versionFolder, "forge-1.12.2", "1.12.2", mcFolder);

        //解压 Jar 文件（:1322-1323）：McLibGet(Json("install")("path"), CustomMcFolder:=McFolder)
        Assert.Equal("legacy-jar-bytes", File.ReadAllText(mcFolder + @"libraries\net\minecraftforge\forge\1.12.2-14.23.5.2859\forge-1.12.2-14.23.5.2859.jar"));
        //建立 Json 文件（:1326-1328）：id 改写、inheritsFrom 缺失时补 Inherit
        var writtenJson = JsonNode.Parse(File.ReadAllText(versionFolder + "forge-1.12.2.json"))!.AsObject();
        Assert.Equal("forge-1.12.2", writtenJson["id"]!.GetValue<string>());
        Assert.Equal("1.12.2", writtenJson["inheritsFrom"]!.GetValue<string>());
        Assert.Equal("--tweakClass forge", writtenJson["minecraftArguments"]!.GetValue<string>());
        Assert.Equal("forge-1.12.2", plan.MergedVersionJson["id"]!.GetValue<string>());
    }

    [Fact]
    public void Legacy_way2_keeps_explicit_null_inherits_from()   //旧 Newtonsoft "Is Nothing" 判定：JSON null 非 Nothing → 不补 Inherit
    {
        var root = TempRoot("legacy2null");
        var installerPath = WriteInstallerZip(root,
            ("install_profile.json", """{"install": {"path": "net.minecraftforge:forge:1.12.2-14.23.5.2859", "filePath": "f.jar"}, "versionInfo": {"id": "x", "inheritsFrom": null}}"""),
            ("f.jar", "j"));
        var versionFolder = root + "new-ver" + Path.DirectorySeparatorChar;

        InstallerPlans.BuildLegacyPlan(installerPath, versionFolder, "forge-1.12.2", "1.12.2", root + "mc" + Path.DirectorySeparatorChar);

        //STJ 下 JSON null 经索引器返回 null 引用，故以键存在性与落盘原文锁定（null 原样保留、不补 Inherit）
        var writtenText = File.ReadAllText(versionFolder + "forge-1.12.2.json");
        Assert.True(JsonNode.Parse(writtenText)!.AsObject().ContainsKey("inheritsFrom"));
        Assert.Contains("\"inheritsFrom\": null", writtenText);
    }

    [Fact]
    public void Legacy_null_install_key_takes_way2_then_throws()   //TECHDEBT §10 JSON-null 边界：键存在但为 null 走方式 2，在取 path 处抛出
    {
        var root = TempRoot("legacy2nullinstall");
        var installerPath = WriteInstallerZip(root,
            ("install_profile.json", """{"install": null, "json": "/v.json"}"""),
            ("v.json", """{"id": "x"}"""));

        Assert.Throws<NullReferenceException>(() => InstallerPlans.BuildLegacyPlan(
            installerPath, root + "new-ver" + Path.DirectorySeparatorChar, "forge-1.12.2", "1.12.2", root + "mc" + Path.DirectorySeparatorChar));
    }

    #endregion
}
