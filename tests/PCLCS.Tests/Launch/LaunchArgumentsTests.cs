using MeloongCore;
using MeloongCore.Extensions;
using PCLCS;
using PCLCS.Instances;
using PCLCS.Launch;
using PCLCS.Minecraft.Launch;
using PCLCS.Settings;
using PCLCS.Startup;
using PCLCS.Tests.Net;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Launch;

//* 触及 SettingsService（设置读写），与设置类测试共享串行集合
[Collection("PCLCS.Settings")]
public class LaunchArgumentsTests : IDisposable
{
    private readonly string _root;

    public LaunchArgumentsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclcs.launch." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        SettingsService.Configure(_root);   // 全局 Settings.json 落在临时目录
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string VersionDir(string name) => Path.Combine(_root, "versions", name + Path.DirectorySeparatorChar);

    private InstanceCore NewInstance(string name, JsonObject json)
    {
        Directory.CreateDirectory(VersionDir(name));
        var jsonPath = Path.Combine(VersionDir(name), name + ".json");
        File.WriteAllText(jsonPath, json.ToJsonString());
        return new InstanceCore(InstancePaths.ResolveVersionFolder(name, _root), _root, VersionDir(name));
    }

    private static Java NewJava(int major, string encoding) =>
        new(@"C:\fake\java", new Version(major, 0, 0)) { FileEncodingName = encoding, NativeEncodingName = encoding };

    private static McLoginOutput NewLogin(string type = "Legacy") =>
        new() { Type = type, Name = "Player", Uuid = "uuid-1", AccessToken = "token-1" };

    private static readonly Func<string, JsonObject?> NoParent = _ => null;
    private static readonly Func<string, string> IdentityReplace = static text => text;

    private static RuleEnvironment Win10 => new("Microsoft Windows NT 10.0.19045.0");

    private static JsonObject BaseJson(string id, JsonArray? jvm = null, JsonArray? game = null, params string[] libraries)
    {
        var json = new JsonObject
        {
            ["id"] = id,
            ["type"] = "release",
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["releaseTime"] = "2023-06-12T10:00:00Z",
            ["libraries"] = new JsonArray()
        };
        if (jvm is not null || game is not null)
        {
            var arguments = new JsonObject();
            if (jvm is not null) arguments["jvm"] = jvm;
            if (game is not null) arguments["game"] = game;
            json["arguments"] = arguments;
        }
        foreach (var library in libraries) json["libraries"]!.AsArray().Add(new JsonObject { ["name"] = library });
        return json;
    }

    private static JsonArray Jvm(params string[] args)
    {
        var array = new JsonArray();
        foreach (var arg in args) array.Add(arg);
        return array;
    }

    private static JsonArray Game(params string[] args)
    {
        var array = new JsonArray();
        foreach (var arg in args) array.Add(arg);
        return array;
    }

    private JvmInputs NewJvmInputs(InstanceCore instance, Java java, McLoginOutput login, bool isGbk = false, double ramGb = 2)
    {
        var jarFolder = Path.Combine(Path.GetTempPath(), "pclcs.launch.jar." + Guid.NewGuid().ToString("N"));
        return new JvmInputs
        {
            Instance = instance,
            Environment = Win10,
            LaunchJava = java,
            IsGbkEncoding = isGbk,
            PathPure = _root + "PCL\\",
            LwjglUnsafeAgentJar = () => Path.Combine(jarFolder, "LwjglUnsafeAgent.jar"),
            JavaWrapperJar = () => Path.Combine(jarFolder, "JavaWrapper.jar"),
            ArgumentReplace = IdentityReplace,
            RamGb = ramGb,
            AvailablePhysicalMemory = 8L * 1024 * 1024 * 1024,
            Debug = false,
            WindowsVersion = new Version(10, 0, 19045),
            Login = login
        };
    }

    private TokenInputs NewTokenInputs(InstanceCore instance, McVersion version, McLoginOutput? login = null)
    {
        _ = instance.PathIndie;   // 预触发 PathIndie 判定（写入版本隔离设置），保证断言值稳定
        return new TokenInputs
        {
            Instance = instance,
            PathPure = _root + "PCL\\",
            NativesFolder = VersionDir(instance.Name) + instance.Name + "-natives\\",
            AssetsIndexName = "5",
            ArgumentReplace = IdentityReplace,
            Login = login ?? NewLogin(),
            Version = version,
            LaunchJava = NewJava(17, "UTF-8"),
            Dpi = 96,
            MainWindowPixelSize = () => (1600, 900)
        };
    }

    [Fact]
    public void Jvm_arguments_follow_segment_order()
    {
        // ModLaunch.vb:1192-1323 分段顺序：版本 JSON → 自定义参数合并去重 → 统一通行证/Authlib → JLW → LUA → 内存 → GC → log4j → 编码 → JLW -jar → 再去重
        var instance = NewInstance("segtest", BaseJson("segtest",
            Jvm("-Dtest.jvm=1"), Game("--username", "${auth_player_name}"),
            "org.lwjgl:lwjgl:3.4.1"));
        var inputs = NewJvmInputs(instance, NewJava(18, "UTF-8"), NewLogin());

        var result = LaunchArguments.BuildJvmArguments(inputs);

        Assert.Equal(new[]
        {
            "-Dtest.jvm=1",
            // LaunchAdvanceJvm 默认值（SettingsCatalog.cs:122）经 ArgumentReplace（此处恒等）合并去重
            "-XX:-OmitStackTraceInFastThrow",
            "-Djdk.lang.Process.allowAmbiguousCommands=True",
            "-Dfml.ignoreInvalidMinecraftCertificates=True",
            "-Dfml.ignorePatchDiscrepancies=True",
            // JLW：Java 18 ≥ 9 需要 --add-exports 对，再 tmpdir
            "--add-exports",
            "cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED",
            "-Doolloo.jlw.tmpdir=${pure_directory}",
            // LUA：库列表含 org.lwjgl:lwjgl:3.4.1（裁定 9：经 Libraries.ListGetJson）
            "-javaagent:\"" + inputs.LwjglUnsafeAgentJar() + "\"",
            // 内存：Floor(2 * 1024) = 2048
            "-Xmx2048m",
            // GC：LaunchAdvanceGC 默认 4（优化 G1），Java 18，非调试（含 PerfDisableSharedMem）
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+UseG1GC",
            "-XX:G1NewSizePercent=20",
            "-XX:G1ReservePercent=20",
            "-XX:G1HeapRegionSize=32M",
            "-XX:MaxGCPauseMillis=50",
            "-XX:+PerfDisableSharedMem",
            "-XX:MinHeapFreeRatio=25",
            "-XX:MaxHeapFreeRatio=40",
            // Log4j 漏洞防御
            "-Dlog4j2.formatMsgNoLookups=true",
            // 编码：Java 18 走 NativeEncodingName（UTF-8）且 Java < 19 用 sun.*，并补 COMPAT
            "-Dsun.stdout.encoding=utf-8",
            "-Dsun.stderr.encoding=utf-8",
            "-Dfile.encoding=COMPAT",
            // JLW -jar 必须在最后
            "-jar",
            inputs.JavaWrapperJar()
        }, result);
    }

    [Fact]
    public void Authlib_agent_prefetch_hits_provider_once()
    {
        // ModLaunch.vb:1222-1234：Auth 登录时先经 NetRequestByClientRetry 预取（恰好一次），再拼 javaagent 与 prefetched base64
        var instance = NewInstance("authtest", BaseJson("authtest", Jvm("-Dtest.jvm=1")));
        using var server = new LoopbackServer();
        server.Responder = _ => (200, "{\"ok\":true}", null);
        SettingsService.Set("VersionServerAuthServer", server.UrlPrefix, instance.InstanceSettingsKey);

        var result = LaunchArguments.BuildJvmArguments(NewJvmInputs(instance, NewJava(8, "UTF-8"), NewLogin("Auth")));

        Assert.Single(server.ReceivedPaths);   // 预取仅触发一次
        Assert.Contains("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT", result);   // Java 8 ≥ 6，信任系统根证书（5252#）
        Assert.Contains($@"-javaagent:""${{pure_directory}}\authlib-injector.jar""=""{server.UrlPrefix}""", result);
        Assert.Contains("-Dauthlibinjector.side=client", result);
        Assert.Contains("-Dauthlibinjector.yggdrasil.prefetched=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"ok\":true}")), result);
    }

    [Theory]
    [InlineData(false, false, false, 9, false, true, true)]    // 默认：Java 9+ 有 --add-exports 对
    [InlineData(false, false, false, 8, false, false, true)]   // Java 8 只有 tmpdir
    [InlineData(true, false, false, 9, false, false, false)]   // 全局禁用
    [InlineData(false, true, false, 9, false, false, false)]   // 版本禁用
    [InlineData(false, false, true, 9, false, false, false)]   // GBK 编码禁用（JDK-8272352/#8739）
    [InlineData(false, false, false, 9, true, false, false)]   // 自定义 javaagent 禁用并警告
    public void Jlw_decision_matches_old_branches(bool globalDisable, bool versionDisable, bool gbk, int major, bool customAgent, bool expectExports, bool expectTmpdir)
    {
        // ModLaunch.vb:1238-1251：默认开，全局/版本设置、GBK 编码、自定义 javaagent 任一命中即关
        var instance = NewInstance("jlwtest", BaseJson("jlwtest", Jvm("-Dtest.jvm=1")));
        if (globalDisable) SettingsService.Set("LaunchAdvanceDisableJLW", true);
        if (versionDisable) SettingsService.Set("VersionAdvanceDisableJLW", true, instance.InstanceSettingsKey);
        if (customAgent) SettingsService.Set("VersionAdvanceJvm", "-javaagent:custom.jar", instance.InstanceSettingsKey);
        string? warnLog = null;
        var inputs = NewJvmInputs(instance, NewJava(major, "UTF-8"), NewLogin(), isGbk: gbk);
        inputs.WarnLog = text => warnLog = text;

        var result = LaunchArguments.BuildJvmArguments(inputs);

        Assert.Equal(expectExports, result.Contains("--add-exports"));
        Assert.Equal(expectTmpdir, result.Contains("-Doolloo.jlw.tmpdir=${pure_directory}"));
        if (expectExports)
        {
            Assert.Contains("cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED", result);
            Assert.Contains("-jar", result);   // JLW -jar 必须在最后（:1317-1318）
            Assert.Equal(result.Count - 1, result.IndexOf(inputs.JavaWrapperJar()));
            Assert.Equal(result.Count - 2, result.IndexOf("-jar"));
        }
        if (customAgent)
        {
            Assert.Contains("-javaagent:custom.jar", result);   // 自定义参数本身保留
            Assert.NotNull(warnLog);   // ：由于使用了自定义 javaagent，已禁用 JLW（Warn 级）
        }
    }

    [Fact]
    public void Game_arguments_optifine_tweaker_rewrites_version_json()
    {
        // ModLaunch.vb:1347-1364（裁定 10）：错误 TweakClass 挪位 + 版本 JSON 改写经 versionJsonWriter 回调写盘恰好一次
        var instance = NewInstance("tweaker", new JsonObject
        {
            ["id"] = "tweaker",
            ["type"] = "release",
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["releaseTime"] = "2023-06-12T10:00:00Z",
            ["minecraftArguments"] = "--username ${auth_player_name} --tweakClass optifine.OptiFineTweaker",
            ["libraries"] = new JsonArray()
        });
        var version = new McVersion("1.12.2") { OptiFine = "G6" };
        version.ModLoaderVersions[PCLCS.ModLoaders.Forge] = "14.23.5.2859";

        string? written = null;
        var callCount = 0;
        var result = LaunchArguments.BuildGameArguments(
            new GameInputs
            {
                Instance = instance,
                ResolveParent = NoParent,
                Environment = Win10,
                Version = version,
                ArgumentReplace = IdentityReplace,
                ExtraGameArgs = [],
                ServerIp = null
            },
            content => { written = content; callCount++; });

        // TweakClass 挪到末尾
        Assert.Equal(new[]
        {
            "--username", "${auth_player_name}",
            "--height", "${resolution_height}",
            "--width", "${resolution_width}",
            "--tweakClass", "optifine.OptiFineForgeTweaker"
        }, result);
        // 写盘经回调恰好一次，内容为原 JSON 全文的类名替换
        var jsonPath = InstancePaths.GetJsonPath(instance.PathVersion, instance.Name);
        Assert.Equal(1, callCount);
        Assert.Equal(File.ReadAllText(jsonPath).Replace("optifine.OptiFineTweaker", "optifine.OptiFineForgeTweaker"), written);
        // 回调未落盘：源 JSON 保持原状（真实写盘由宿主绑定 FileUtils.Write）
        Assert.Contains("optifine.OptiFineTweaker", File.ReadAllText(jsonPath));
    }

    [Fact]
    public void Replace_tokens_cover_all_marks()
    {
        // ModLaunch.vb:1400-1473 全标记表；未知标记原样保留
        var instance = NewInstance("tokens", BaseJson("tokens", null, null,
            "com.mojang:brigadier:1.0.18", "optifine:OptiFine:1.12.2-HD_U_G6"));
        var inputs = NewTokenInputs(instance, new McVersion("1.20.1"));
        var mainJar = instance.PathVersion + instance.Name + ".jar";

        var replaced = LaunchArguments.ReplaceTokens(
            string.Join("|",
                "${classpath_separator}", "${natives_directory}", "${library_directory}", "${libraries_directory}",
                "${pure_directory}", "${launcher_name}", "${launcher_version}", "${version_name}", "${game_directory}",
                "${assets_root}", "${user_properties}", "${auth_player_name}", "${auth_uuid}", "${auth_access_token}",
                "${access_token}", "${auth_session}", "${user_type}", "${primary_jar}", "${game_assets}",
                "${assets_index_name}", "${version_type}", "${resolution_width}", "${resolution_height}", "${classpath}",
                "${unknown_mark}"),
            inputs, job => { job(); return true; });

        var values = replaced.Split('|');
        Assert.Equal(";", values[0]);
        Assert.Equal(PathUtils.RemoveSlashSuffix(inputs.NativesFolder), values[1]);
        Assert.Equal(PathUtils.ToShortPath(_root + "libraries"), values[2]);
        Assert.Equal(PathUtils.ToShortPath(_root + "libraries"), values[3]);
        Assert.Equal(PathUtils.RemoveSlashSuffix(_root + "PCL\\"), values[4]);
        Assert.Equal(LauncherIdentity.AssemblyName, values[5]);
        Assert.Equal(VersionInfo.ProtocolVersion.ToString(), values[6]);
        Assert.Equal("tokens", values[7]);
        Assert.Equal(PathUtils.ToShortPath(instance.PathIndie[..^1]), values[8]);
        Assert.Equal(PathUtils.ToShortPath(_root + "assets"), values[9]);
        Assert.Equal("{}", values[10]);
        Assert.Equal("Player", values[11]);
        Assert.Equal("uuid-1", values[12]);
        Assert.Equal("token-1", values[13]);
        Assert.Equal("token-1", values[14]);
        Assert.Equal("token-1", values[15]);
        Assert.Equal("msa", values[16]);
        Assert.Equal(PathUtils.ToShortPath(mainJar), values[17]);
        Assert.Equal(PathUtils.ToShortPath(_root + @"assets\virtual\legacy"), values[18]);
        Assert.Equal("5", values[19]);
        // ${version_type}：VersionArgumentInfo 默认空 → 回落 LaunchArgumentInfo 默认值（启动器名），经 ArgumentReplace（此处恒等）
        Assert.Equal(IdentityReplace(LauncherIdentity.AssemblyName), values[20]);
        // 窗口尺寸：WindowType 默认 1 → 854x480；Java 17 且 1.20.1 不触发 DPI #3463 修复
        Assert.Equal("854", values[21]);
        Assert.Equal("480", values[22]);
        // ${classpath}：OptiFine 固定在倒数第二位（主 Jar 之前）
        var brigadier = Libraries.GetLocalPath("com.mojang:brigadier:1.0.18", customMcFolder: _root);
        var optiFine = Libraries.GetLocalPath("optifine:OptiFine:1.12.2-HD_U_G6", customMcFolder: _root);
        Assert.Equal(string.Join(";", brigadier, optiFine, PathUtils.ToShortPath(mainJar)), values[23]);
        Assert.Equal("${unknown_mark}", values[24]);
    }

    [Fact]
    public void Replace_tokens_removes_version_type_pair_and_applies_dpi_fix()
    {
        // ModLaunch.vb:1427-1432（空自定义信息移除 --versionType 对）与 :1451-1456（DPI #3463 修复）
        var instance = NewInstance("dpi", BaseJson("dpi"));
        SettingsService.Set("LaunchArgumentInfo", "");
        var version = new McVersion("1.12.2");   // Vanilla.Major = 12 ≤ 12
        var inputs = NewTokenInputs(instance, version);
        inputs.LaunchJava = NewJava(8, "UTF-8");
        inputs.LaunchJava.Version = new Version(8, 0, 250);   // 8u200~8u321 区间内
        inputs.Dpi = 192;
        var arguments = new List<string> { "keep-a", "--versionType", "vanilla", "keep-b" };
        inputs.Arguments = arguments;

        var replaced = LaunchArguments.ReplaceTokens("${resolution_width}|${resolution_height}|${version_type}", inputs, job => { job(); return true; });

        Assert.Equal(new[] { "keep-a", "keep-b" }, arguments);   // --versionType 与其值成对移除
        var values = replaced.Split('|');
        Assert.Equal("427", values[0]);   // 854 / (192/96) = 427
        Assert.Equal("240", values[1]);   // 480 / 2 = 240
        Assert.Equal("\"\"", values[2]);  // 空自定义信息时 ${version_type} 为两个字面引号
    }

    [Fact]
    public void Lua_check_uses_libraries_list_get_json()
    {
        // 裁定 9：LUA 判定经 C# Libraries.ListGetJson 等价（ModLaunch.vb:1254-1258 两态 + 精确版本匹配 + 设置开关）
        var withLua = NewInstance("withlua", BaseJson("withlua", Jvm("-Dtest.jvm=1"), null, "org.lwjgl:lwjgl:3.4.1"));
        var withoutLua = NewInstance("withoutlua", BaseJson("withoutlua", Jvm("-Dtest.jvm=1"), null, "org.lwjgl:lwjgl:3.2.1"));
        var disabled = NewInstance("disabledlua", BaseJson("disabledlua", Jvm("-Dtest.jvm=1"), null, "org.lwjgl:lwjgl:3.4.1"));
        SettingsService.Set("VersionAdvanceDisableLUA", true, disabled.InstanceSettingsKey);

        var withInputs = NewJvmInputs(withLua, NewJava(17, "UTF-8"), NewLogin());
        var luaJar = withInputs.LwjglUnsafeAgentJar();
        var withResult = LaunchArguments.BuildJvmArguments(withInputs);
        var withoutResult = LaunchArguments.BuildJvmArguments(NewJvmInputs(withoutLua, NewJava(17, "UTF-8"), NewLogin()));
        var disabledResult = LaunchArguments.BuildJvmArguments(NewJvmInputs(disabled, NewJava(17, "UTF-8"), NewLogin()));

        Assert.Contains(withResult, a => a == "-javaagent:\"" + luaJar + "\"");
        Assert.DoesNotContain(withoutResult, a => a.StartsWithF("-javaagent:"));   // 3.2.1 不精确匹配 3.4.1
        Assert.DoesNotContain(disabledResult, a => a.StartsWithF("-javaagent:"));  // 版本设置禁用
    }

    [Theory]
    [InlineData("2023-05-10T00:00:00Z", "a.b.c:1234", new[] { "--username", "${auth_player_name}", "-Dextra", "--fullscreen", "--quickPlayMultiplayer", "a.b.c:1234" })]
    [InlineData("2020-01-01T00:00:00Z", "a.b.c:1234", new[] { "--username", "${auth_player_name}", "-Dextra", "--fullscreen", "--server", "a.b.c", "--port", "1234" })]
    [InlineData("2020-01-01T00:00:00Z", "a.b.c", new[] { "--username", "${auth_player_name}", "-Dextra", "--fullscreen", "--server", "a.b.c", "--port", "25565" })]
    public void Game_arguments_server_and_quickplay(string releaseTime, string serverIp, string[] expected)
    {
        // ModLaunch.vb:1373-1394：全屏（WindowType 0）→ QuickPlay（2023-04-04 后）或老版 --server/--port
        var instance = NewInstance("servertest", BaseJson("servertest", null, Game("--username", "${auth_player_name}")));
        SettingsService.Set("LaunchArgumentWindowType", 0);
        var release = DateTime.Parse(releaseTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        var version = new McVersion("1.20.1") { ReleaseTime = release };

        var result = LaunchArguments.BuildGameArguments(
            new GameInputs
            {
                Instance = instance,
                ResolveParent = NoParent,
                Environment = Win10,
                Version = version,
                ArgumentReplace = IdentityReplace,
                ExtraGameArgs = ["-Dextra"],
                ServerIp = serverIp
            },
            null);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Build_arguments_quotes_and_joins()
    {
        // ModLaunch.vb:1162-1189 编排：JVM + mainClass + 游戏 → 替换 → 含空格参数加引号 → 空格拼接并 TrimEnd
        var instance = NewInstance("build", BaseJson("build", Jvm("-Dtest.jvm=1"), Game("--username", "${auth_player_name}")));
        SettingsService.Set("LaunchAdvanceJvm", "");                    // 排除默认自定义 JVM 参数
        SettingsService.Set("LaunchAdvanceDisableJLW", true);           // 排除 JLW 段
        SettingsService.Set("LaunchAdvanceGC", 3);                      // 排除 GC 段
        var login = new McLoginOutput { Type = "Legacy", Name = "Player One", Uuid = "uuid-1", AccessToken = "token-1" };
        var jvm = NewJvmInputs(instance, NewJava(17, "UTF-8"), login);
        var game = new GameInputs
        {
            Instance = instance,
            ResolveParent = NoParent,
            Environment = Win10,
            Version = new McVersion("1.20.1"),
            ArgumentReplace = IdentityReplace,
            ExtraGameArgs = [],
            ServerIp = null
        };
        var inputs = new LaunchArgumentInputs
        {
            Jvm = jvm,
            Game = game,
            Token = NewTokenInputs(instance, new McVersion("1.20.1"), login),
            VersionJsonWriter = _ => throw new InvalidOperationException("不应触发版本 JSON 改写"),
            RunInUiWait = job => { job(); return true; }
        };

        var result = LaunchArguments.BuildArguments(inputs);

                Assert.Equal(
            "-Dtest.jvm=1 -Xmx2048m -Dlog4j2.formatMsgNoLookups=true -Dsun.stdout.encoding=utf-8 -Dsun.stderr.encoding=utf-8 "
            + "net.minecraft.client.main.Main --username \"Player One\"",
            result);
        Assert.Equal(result, LaunchArguments.LaunchArgument);   // :1188 模块状态随迁（VB 转发读写此值）
        Assert.Equal(result, result.TrimEnd());
    }
}
