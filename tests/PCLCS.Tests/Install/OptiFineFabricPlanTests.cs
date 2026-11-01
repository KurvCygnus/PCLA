using PCLCS.Download;
using PCLCS.Install;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Install;

/// <summary>
/// OptiFine / LiteLoader / Fabric 安装决策核（ModDownloadLib.vb 的决策段迁入面）的语义锁定。
/// 断言按旧代码实际行为收敛（brief 计划稿为示意，已修正）：
/// 方式 A/B 拆分为安装器参数（:384-390）与手写版本 Json（:600-630，4 空格缩进 + LF 行尾原文），
/// 手写 Json 的 minimumLauncherVersion 在新格式为字符串 "21"、旧格式为数值 18（旧代码原样）。
/// </summary>
public class OptiFineFabricPlanTests
{
    private static string TempRoot(string name) => Path.Combine(Path.GetTempPath(), "pclcs.optifabric." + name + "." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;

    #region 安装器 class 版本解析（:361-374）

    /// <summary>写入含 optifine/Installer.class 的最小 Jar（class 文件头：CAFEBABE + 大端 major 版本于 6-7 字节，对照旧 Header(6)*&amp;H100 + Header(7)）。</summary>
    private static string WriteClassJar(string root, int major)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "OptiFine.jar");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("optifine/Installer.class");
        using var writer = new BinaryWriter(entry.Open());
        writer.Write([0xCA, 0xFE, 0xBA, 0xBE]);
        writer.Write((byte) 0);
        writer.Write((byte) 0);   //minor version（4-5 字节）
        writer.Write((byte) (major >> 8));
        writer.Write((byte) major);   //major version（6-7 字节，大端）
        return path;
    }

    [Fact]
    public void Class_version_maps_to_java_range()   //:369-372：ClassVersion = Header(6)*&H100 + Header(7)，Java 主版本 = 版本 - 44
    {
        var plan = InstallerPlans.OptiFineClassVersion(WriteClassJar(TempRoot("v52"), 52));   //0x34 → Java 8
        Assert.Equal(52, plan.ClassVersion);
        Assert.True(plan.JavaRange.HasLower);
        Assert.Equal(new Version(8, 0), plan.JavaRange.Lower);
    }

    [Fact]
    public void Class_version_bounds_49_to_100()   //:370-371
    {
        Assert.Equal(49, InstallerPlans.OptiFineClassVersion(WriteClassJar(TempRoot("v49"), 49)).ClassVersion);
        Assert.Equal(100, InstallerPlans.OptiFineClassVersion(WriteClassJar(TempRoot("v100"), 100)).ClassVersion);
        Assert.Throws<InvalidDataException>(static () => InstallerPlans.OptiFineClassVersion(WriteClassJar(TempRoot("v48"), 48)));
        Assert.Throws<InvalidDataException>(static () => InstallerPlans.OptiFineClassVersion(WriteClassJar(TempRoot("v101"), 101)));
    }

    [Fact]
    public void Class_version_rejects_bad_magic()   //:368
    {
        var root = TempRoot("badmagic");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "OptiFine.jar");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var writer = new BinaryWriter(zip.CreateEntry("optifine/Installer.class").Open());
            writer.Write(new byte[16]);   //无 CAFEBABE 头
        }
        Assert.Throws<InvalidDataException>(() => InstallerPlans.OptiFineClassVersion(path));
    }

    [Fact]
    public void Class_version_throws_on_missing_entry_and_short_header()   //:364/:367
    {
        var root = TempRoot("missing");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "OptiFine.jar");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("other.txt");
        }
        Assert.Throws<FileNotFoundException>(() => InstallerPlans.OptiFineClassVersion(path));

        var shortRoot = TempRoot("short");
        Directory.CreateDirectory(shortRoot);
        var shortPath = Path.Combine(shortRoot, "OptiFine.jar");
        using (var zip = ZipFile.Open(shortPath, ZipArchiveMode.Create))
        {
            using var writer = new BinaryWriter(zip.CreateEntry("optifine/Installer.class").Open());
            writer.Write([0xCA, 0xFE, 0xBA, 0xBE]);   //长度不足 8
        }
        Assert.Throws<IOException>(() => InstallerPlans.OptiFineClassVersion(shortPath));
    }

    #endregion

    #region 安装目标与方式 A/B 判定（:485-490）

    private static DlOptiFineListEntry OptiFineEntry(string displayName = "1.12.2 C8", string fileName = "OptiFine_1.12.2_HD_U_C8.jar",
        string instanceName = "1.12.2-OptiFine_HD_U_C8", string inherit = "1.12.2", bool isPreview = false, string releaseTime = "")
        => new()
        {
            DisplayName = displayName,
            FileName = fileName,
            InstanceName = instanceName,
            IsPreview = isPreview,
            Inherit = inherit,
            ReleaseTime = releaseTime
        };

    [Fact]
    public void Old_version_targets_libraries_folder()   //:490：旧版安装器落 libraries\optifine\OptiFine\{名}\{文件}
    {
        var tempCalls = 0;
        var (id, installerPath, isNewer) = InstallerPlans.OptiFineInstallTarget(
            OptiFineEntry(), @"C:\mc\", () => { tempCalls++; return @"C:\temp\"; });
        Assert.Equal("1.12.2-OptiFine_HD_U_C8", id);
        Assert.False(isNewer);
        Assert.Equal(@"C:\mc\libraries\optifine\OptiFine\1.12.2_HD_U_C8\OptiFine-1.12.2_HD_U_C8.jar", installerPath);
        Assert.Equal(0, tempCalls);   //旧 If(IsNewerVersion, ...) 短路：临时文件夹仅在方式 A 取
    }

    [Fact]
    public void Newer_version_targets_temp_folder()   //:488：新版安装器落 RequestTaskTempFolder(True) & "OptiFine.jar"
    {
        var (_, installerPath, isNewer) = InstallerPlans.OptiFineInstallTarget(
            OptiFineEntry(inherit: "1.21", instanceName: "1.21-OptiFine_HD_U_I7", fileName: "OptiFine_1.21_HD_U_I7.jar"), @"C:\mc\", static () => @"C:\temp\");
        Assert.True(isNewer);   //VersionToDrop("1.21") = 210 >= 140（:487）
        Assert.Equal(@"C:\temp\OptiFine.jar", installerPath);
    }

    [Fact]
    public void Snapshot_inherit_counts_as_newer()   //:487：Inherit 含 "w"（快照名）
    {
        var (_, _, isNewer) = InstallerPlans.OptiFineInstallTarget(
            OptiFineEntry(inherit: "25w14craftmine", instanceName: "25w14craftmine-OptiFine_HD_U_F5", fileName: "OptiFine_25w14craftmine_HD_U_F5.jar"), @"C:\mc\", static () => @"C:\temp\");
        Assert.True(isNewer);
    }

    #endregion

    #region 下载源决策（:496-517 与 :648-669）

    [Fact]
    public void Sources_list_bmclapi_then_official()   //:496-517：BMCLAPI 恒首选，官方链接解析自抓取页面
    {
        var (sources, checker) = InstallerPlans.OptiFineDownloadSources(
            OptiFineEntry(), """<a href="downloadx?f=OptiFine_1.12.2_HD_U_C8.jar&amp;x=1">""", 300 * 1024);
        Assert.Equal(
        [
            "https://bmclapi2.bangbang93.com/optifine/1.12.2/HD_U/C8",
            "https://optifine.net/downloadx?f=OptiFine_1.12.2_HD_U_C8.jar&amp;x=1"
        ], sources);
        Assert.Equal(300 * 1024, checker.MinSize);   //安装链（:517）
    }

    [Fact]
    public void Save_chain_uses_own_min_size()   //:669：保存链 MinSize 64KB
    {
        var (_, checker) = InstallerPlans.OptiFineDownloadSources(OptiFineEntry(), null, 64 * 1024);
        Assert.Equal(64 * 1024, checker.MinSize);
    }

    [Fact]
    public void Bmclapi_inherit_appends_dot_zero()   //#4281（:499）
    {
        var (sources, _) = InstallerPlans.OptiFineDownloadSources(OptiFineEntry(inherit: "1.8", displayName: "1.8 I7", fileName: "OptiFine_1.8_HD_U_I7.jar"), null, 300 * 1024);
        Assert.Equal("https://bmclapi2.bangbang93.com/optifine/1.8.0/HD_U/I7", sources[0]);
    }

    [Fact]
    public void Preview_builds_slashed_hd_u_suffix()   //:501：preview 走 HD_U_ 段且空格换 /
    {
        var (sources, _) = InstallerPlans.OptiFineDownloadSources(
            OptiFineEntry(inherit: "1.9.1", displayName: "1.9.1 E1 pre", fileName: "preview_OptiFine_1.9.1_HD_U_E1_pre.jar", isPreview: true), null, 300 * 1024);
        Assert.Equal("https://bmclapi2.bangbang93.com/optifine/1.9.1/HD_U_E1/pre", sources[0]);
    }

    [Fact]
    public void Missing_or_failed_official_page_keeps_bmclapi_only()   //:513-515：抓取失败仅剩 BMCLAPI 源
    {
        var (sources, _) = InstallerPlans.OptiFineDownloadSources(OptiFineEntry(), null, 300 * 1024);
        Assert.Single(sources);
        Assert.Equal("https://bmclapi2.bangbang93.com/optifine/1.12.2/HD_U/C8", sources[0]);
    }

    [Fact]
    public void Official_page_without_match_throws_like_first()   //:511 RegexSearch(...).First 的空序列语义
    {
        Assert.Throws<InvalidOperationException>(static () =>
            InstallerPlans.OptiFineDownloadSources(OptiFineEntry(), "<html>no link here</html>", 300 * 1024));
    }

    #endregion

    #region 方式 A 安装器参数（:384-390）

    [Fact]
    public void Installer_args_use_java_wrapper_when_enabled()
    {
        var wrapperCalls = 0;
        var arguments = InstallerPlans.OptiFineInstallerArgs(
            @"C:\temp\pcl\", @"C:\mc\", @"C:\temp\pcl\OptiFine.jar",
            () => { wrapperCalls++; return @"C:\temp\pcl\Launch\JavaWrapper.jar"; }, 8, useJavaWrapper: true);
        Assert.Equal(
            @"-Doolloo.jlw.tmpdir=""C:\temp\pcl"" -Duser.home=""C:\mc"" -cp ""C:\temp\pcl\OptiFine.jar"" -jar ""C:\temp\pcl\Launch\JavaWrapper.jar"" optifine.Installer",
            arguments);
        Assert.Equal(1, wrapperCalls);   //旧 ExtractPatch 仅在 JLW 分支求值（:386）
    }

    [Fact]
    public void Installer_args_skip_wrapper_when_disabled()   //:388
    {
        var wrapperCalls = 0;
        var arguments = InstallerPlans.OptiFineInstallerArgs(
            @"C:\temp\pcl\", @"C:\mc\", @"C:\temp\pcl\OptiFine.jar",
            () => { wrapperCalls++; return ""; }, 8, useJavaWrapper: false);
        Assert.Equal(@"-Duser.home=""C:\mc"" -cp ""C:\temp\pcl\OptiFine.jar"" optifine.Installer", arguments);
        Assert.Equal(0, wrapperCalls);
    }

    [Fact]
    public void Installer_args_prefix_add_exports_on_java_9_plus()   //:390
    {
        var arguments = InstallerPlans.OptiFineInstallerArgs(
            @"C:\temp\pcl\", @"C:\mc\", @"C:\temp\pcl\OptiFine.jar", static () => "", 17, useJavaWrapper: false);
        Assert.StartsWith("--add-exports cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED ", arguments);
    }

    #endregion

    #region 方式 B 手写版本 Json（:600-630）

    [Fact]
    public void Handwritten_json_new_format_matches_verbatim()   //:600-610 + :620-628（ReleaseTime 为空回退继承版本日期）
    {
        var progress = new List<double>();
        var targetJson = TestHelpers.OptiFineTargetJson(null);   //新格式继承版本（无 minecraftArguments）
        var providerCalls = 0;
        var json = InstallerPlans.OptiFineHandwrittenJson(
            OptiFineEntry(), "2015-12-27",
            isOldJson: targetJson["minecraftArguments"] is not null && targetJson["minecraftArguments"]!.ToString().Length > 0,
            () => { providerCalls++; return ""; },
            progress.Add);
        Assert.Equal(
        [
            "{",
            "    \"id\": \"1.12.2-OptiFine_HD_U_C8\",",
            "    \"inheritsFrom\": \"1.12.2\",",
            "    \"time\": \"2015-12-27T23:33:33+08:00\",",
            "    \"releaseTime\": \"2015-12-27T23:33:33+08:00\",",
            "    \"type\": \"release\",",
            "    \"libraries\": [",
            "        {\"name\": \"optifine:OptiFine:1.12.2_HD_U_C8\"},",
            "        {\"name\": \"net.minecraft:launchwrapper:1.12\"}",
            "    ],",
            "    \"mainClass\": \"net.minecraft.launchwrapper.Launch\",",
            "    \"minimumLauncherVersion\": \"21\",",   //旧代码：新格式为字符串 "21"（:621）
            "    \"arguments\": {",
            "        \"game\": [",
            "            \"--tweakClass\",",
            "            \"optifine.OptiFineTweaker\"",
            "        ]",
            "    }",
            "}"
        ], json.Split('\n'));
        Assert.Equal([0.8], progress);   //旧 :611（前缀组装后、分支前）
        Assert.Equal(0, providerCalls);   //minecraftArguments 仅旧格式分支取值（新格式不触碰继承 Json）
    }

    [Fact]
    public void Handwritten_json_old_format_appends_tweaker_to_arguments()   //:614-617（IsOldJson 分支）
    {
        var targetJson = TestHelpers.OptiFineTargetJson("--a 1 --b 2");   //旧格式继承版本
        var calls = 0;
        var json = InstallerPlans.OptiFineHandwrittenJson(
            OptiFineEntry(releaseTime: "2015/06/27"),
            "2015-12-27",
            isOldJson: targetJson["minecraftArguments"] is not null && targetJson["minecraftArguments"]!.ToString().Length > 0,
            () => { calls++; return targetJson["minecraftArguments"]!.ToString(); });
        var lines = json.Split('\n');
        Assert.Equal(
        [
            "    \"minimumLauncherVersion\": 18,",   //旧格式为数值 18（:615）
            "    \"minecraftArguments\": \"--a 1 --b 2  --tweakClass optifine.OptiFineTweaker\"",   //双空格追加（:616）
            "}"
        ], lines.Skip(lines.Length - 3));
        Assert.Contains("\"time\": \"2015-06-27T23:33:33+08:00\"", json);   //ReleaseTime 非空时 "/" 换 "-"（:603）
        Assert.Equal(1, calls);
    }

    #endregion

    #region LiteLoader redist 与安装源（:756-776）

    [Theory]
    [InlineData("1.7.10", "https://dl.liteloader.com/redist/1.7.10/liteloader-installer-1.7.10-04.jar")]
    [InlineData("1.7.2", "https://dl.liteloader.com/redist/1.7.2/liteloader-installer-1.7.2-04.jar")]
    [InlineData("1.6.4", "https://dl.liteloader.com/redist/1.6.4/liteloader-installer-1.6.4-01.jar")]
    [InlineData("1.6.2", "https://dl.liteloader.com/redist/1.6.2/liteloader-installer-1.6.2-04.jar")]
    [InlineData("1.5.2", "https://dl.liteloader.com/redist/1.5.2/liteloader-installer-1.5.2-01.jar")]
    public void Legacy_redist_table_matches_verbatim(string inherit, string expected)   //:759-769
        => Assert.Equal(expected, InstallerPlans.LegacyLiteLoaderRedist(inherit));

    [Fact]
    public void Legacy_redist_unknown_version_throws()   //:770-771 Case Else
    {
        Assert.Throws<NotSupportedException>(static () => InstallerPlans.LegacyLiteLoaderRedist("1.4.7"));
    }

    [Fact]
    public void Legacy_entry_takes_redist_source()   //:757-772
    {
        var entry = new DlLiteLoaderListEntry { Inherit = "1.7.10", IsLegacy = true, FileName = "liteloader-installer-1.7.10-00.jar" };
        Assert.Equal(["https://dl.liteloader.com/redist/1.7.10/liteloader-installer-1.7.10-04.jar"],
            InstallerPlans.LiteLoaderInstallerSources(entry));
    }

    [Fact]
    public void Modern_entry_takes_jenkins_official_source()   //:773-776：1.8 走 ant/dist/，其余 build/libs/
    {
        var entry = new DlLiteLoaderListEntry { Inherit = "1.12.2", IsLegacy = false, FileName = "liteloader-installer-1.12.2-00-SNAPSHOT.jar" };
        Assert.Equal(
        [
            "http://jenkins.liteloader.com/job/LiteLoaderInstaller%201.12.2/lastSuccessfulBuild/artifact/build/libs/liteloader-installer-1.12.2-00-SNAPSHOT.jar"
        ], InstallerPlans.LiteLoaderInstallerSources(entry));

        var entry18 = new DlLiteLoaderListEntry { Inherit = "1.8", IsLegacy = false, FileName = "liteloader-installer-1.8.0-00-SNAPSHOT.jar" };
        Assert.Equal(
        [
            "http://jenkins.liteloader.com/job/LiteLoaderInstaller%201.8/lastSuccessfulBuild/artifact/ant/dist/liteloader-installer-1.8.0-00-SNAPSHOT.jar"
        ], InstallerPlans.LiteLoaderInstallerSources(entry18));
    }

    #endregion

    #region LiteLoader 版本 Json 组装（:801-815）

    [Fact]
    public void Lite_loader_profile_matches_token_fields_in_order()
    {
        var entry = new DlLiteLoaderListEntry
        {
            Inherit = "1.12.2",
            ReleaseTime = "2015/12/27 19:06",
            JsonToken = TestHelpers.LiteLoaderToken()
        };
        var profile = InstallerPlans.BuildLiteLoaderProfile(entry);

        Assert.Equal("1.12.2-LiteLoader", profile.VersionName);   //:801（宿主层据此定位版本文件夹 :802）
        //键序逐字（:805-815）：JsonObject 保插入序
        Assert.Equal(
        [
            "id", "time", "releaseTime", "type", "arguments", "libraries", "mainClass", "minimumLauncherVersion", "inheritsFrom", "jar"
        ], profile.VersionJson.Select(static pair => pair.Key));
        Assert.Equal("1.12.2-LiteLoader", profile.VersionJson["id"]!.GetValue<string>());
        //旧 Newtonsoft JValue(Date) 的 ISO 序列化形态（:806-807 ParseExact 后）
        Assert.Equal("2015-12-27T19:06:00", profile.VersionJson["time"]!.GetValue<string>());
        Assert.Equal("2015-12-27T19:06:00", profile.VersionJson["releaseTime"]!.GetValue<string>());
        Assert.Equal("release", profile.VersionJson["type"]!.GetValue<string>());
        Assert.Equal(["--tweakClass", "net.minecraft.launchwrapper.LiteLoaderTweaker"],
            profile.VersionJson["arguments"]!["game"]!.AsArray().Select(static token => token!.GetValue<string>()));
        //libraries = JsonToken 原数组（深拷贝）+ 末尾附加 liteloader 项（:810-811）
        var libraries = profile.VersionJson["libraries"]!.AsArray();
        Assert.Equal(3, libraries.Count);
        Assert.Equal("net.minecraft:launchwrapper:1.12", libraries[0]!["name"]!.GetValue<string>());
        Assert.Equal("org.ow2.asm:asm-all:5.2", libraries[1]!["name"]!.GetValue<string>());
        Assert.Equal("com.mumfrey:liteloader:1.12.2-SNAPSHOT", libraries[2]!["name"]!.GetValue<string>());
        Assert.Equal("https://dl.liteloader.com/versions/", libraries[2]!["url"]!.GetValue<string>());
        Assert.Equal("net.minecraft.launchwrapper.Launch", profile.VersionJson["mainClass"]!.GetValue<string>());
        Assert.Equal(18, profile.VersionJson["minimumLauncherVersion"]!.GetValue<int>());
        Assert.Equal("1.12.2", profile.VersionJson["inheritsFrom"]!.GetValue<string>());
        Assert.Equal("1.12.2", profile.VersionJson["jar"]!.GetValue<string>());
        //token 原数组不被就地修改（:810 的 ToJToken 为拷贝语义）
        Assert.Equal(2, ((JsonObject) entry.JsonToken!)["libraries"]!.AsArray().Count);
    }

    [Fact]
    public void Lite_loader_profile_missing_tweak_class_throws()   //旧 JsonToken("tweakClass").ToString 缺失 NRE（:809）
    {
        var entry = new DlLiteLoaderListEntry
        {
            Inherit = "1.12.2",
            ReleaseTime = "2015/12/27 19:06",
            JsonToken = (JsonObject) JsonNode.Parse("""{"version": "1.12.2-SNAPSHOT", "libraries": []}""")!
        };
        Assert.Throws<NullReferenceException>(() => InstallerPlans.BuildLiteLoaderProfile(entry));
    }

    #endregion

    #region Fabric profile 下载决策（:1508-1519）

    [Fact]
    public void Fabric_plan_builds_id_path_sources_and_checker()
    {
        var plan = InstallerPlans.BuildFabricPlan("0.16.9", "1.21", @"C:\mc\");
        Assert.Equal("fabric-loader-0.16.9-1.21", plan.InstanceId);   //:1508
        Assert.Equal(@"C:\mc\versions\fabric-loader-0.16.9-1.21\fabric-loader-0.16.9-1.21.json", plan.VersionJsonPath);   //:1509/:1519
        Assert.Equal(
        [
            "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/1.21/0.16.9/profile/json",
            "https://meta.fabricmc.net/v2/versions/loader/1.21/0.16.9/profile/json"
        ], plan.Sources);   //:1516-1519：BMCLAPI 在前
        Assert.True(plan.Checker.IsJson);   //:1519
    }

    [Fact]
    public void Fabric_infinity_replacement_keeps_id_verbatim()   //:1513：∞ → infinite 仅用于下载地址，版本名原样
    {
        var plan = InstallerPlans.BuildFabricPlan("0.16.9", "25w14craftmine∞", @"C:\mc\");
        Assert.Equal("fabric-loader-0.16.9-25w14craftmine∞", plan.InstanceId);
        Assert.Contains("/loader/25w14craftmineinfinite/0.16.9/", plan.Sources[0]);
        Assert.Contains("/loader/25w14craftmineinfinite/0.16.9/", plan.Sources[1]);
    }

    #endregion
}