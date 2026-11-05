using PCLCS.Instances;
using PCLCS.Minecraft.Launch;
using PCLCS.Net;
using PCLCS.Settings;
using PCLCS.Startup;
using System.Globalization;
using System.Text.Json.Nodes;

namespace PCLCS.Launch;

/// <summary>
/// 登录结果快照（自宿主层 McLoginResult，ModLaunch.vb:365-375，按启动参数的读取面收敛为四项）。
/// 由宿主层从 McLoginLoader.Output 取值后构造（快照语义），C# 不引用宿主层 Loader。
/// </summary>
public sealed class McLoginOutput
{
    /// <summary>登录方式标识（原 Type："Nide"/"Auth" 等）。</summary>
    public string? Type;
    /// <summary>玩家名（原 Name）。</summary>
    public string? Name;
    /// <summary>玩家 UUID（原 Uuid）。</summary>
    public string? Uuid;
    /// <summary>访问令牌（原 AccessToken）。</summary>
    public string? AccessToken;
}

/// <summary>
/// JVM 参数段（ModLaunch.vb:1192-1323）的宿主读取面：设置键经 <see cref="SettingsService"/>（instanceKey 为
/// Instance.PathVersion，W1 面），版本 JSON 经 InstanceCore，其余模块状态（IsGBKEncoding/PathPure/ModeDebug/DPI）
/// 与 UI 页面读取（GetRam、AvailablePhysicalMemory）由宿主层传入。
/// </summary>
public sealed class JvmInputs
{
    /// <summary>当前实例核心（宿主层传 McInstanceSelected.Core；VersionJson/InheritName/Name/设置键的 W1 面）。</summary>
    public InstanceCore Instance = null!;
    /// <summary>规则求值环境（原 New RuleEnvironment(My.Computer.Info.OSVersion)）。</summary>
    public RuleEnvironment Environment = default!; //RuleEnvironment 为结构体，宿主层以对象初始化器必赋
    /// <summary>按 inheritsFrom 解析父版本 JSON（原 Function(name) (New McInstance(name)).JsonObject…）。</summary>
    public Func<string, JsonObject?> ResolveParent = null!;
    /// <summary>选定的 Java（原 McLaunchJavaSelected，即 PCLCS.Java）。</summary>
    public Java LaunchJava = null!;
    /// <summary>原 IsGBKEncoding 模块状态（宿主层 ModBase.vb:70）。</summary>
    public bool IsGbkEncoding;
    /// <summary>PCL 纯净目录（原 PathPure.Value，以 \ 结尾；补丁 jar 的释放目录）。</summary>
    public string PathPure = null!;
    /// <summary>LUA 补丁 jar 的惰性提供者（原 ExtractPatch("Launch\LwjglUnsafeAgent.jar", PathPure.Value)，仅 UseLUA 分支取值，InstallerPlans 先例）。</summary>
    public Func<string> LwjglUnsafeAgentJar = null!;
    /// <summary>JLW 包装 jar 的惰性提供者（原 ExtractPatch("Launch\JavaWrapper.jar", PathPure.Value)，仅 UseJLW 分支取值）。</summary>
    public Func<string> JavaWrapperJar = null!;
    /// <summary>自定义标记替换（原 ArgumentReplace，宿主层 ModMain.vb:819，留宿主层）。</summary>
    public Func<string, string> ArgumentReplace = null!;
    /// <summary>目标内存 GB（原 PageInstanceSetup.GetRam(McInstanceSelected)，UI 页面读取留宿主层）。</summary>
    public double RamGb;
    /// <summary>当前剩余物理内存字节数（原 My.Computer.Info.AvailablePhysicalMemory，仅日志）。</summary>
    public long AvailablePhysicalMemory;
    /// <summary>原 ModeDebug（GC 参数在调试模式下保留 hsperfdata）。</summary>
    public bool Debug;
    /// <summary>Windows 版本（原 Environment.OSVersion.Version，供 GC 策略判定）。</summary>
    public Version WindowsVersion = null!;
    /// <summary>登录输出快照（宿主层从 McLoginLoader.Output 构造）。</summary>
    public McLoginOutput Login = null!;
    /// <summary>UI 线程判定回调（宿主层 RunInUi 委托；null 视为非 UI 线程）。</summary>
    public Func<bool>? RunInUi;
    /// <summary>启动日志（原 McLaunchLog 默认级；null 时静默）。</summary>
    public Action<string>? Log;
    /// <summary>Warn 级启动日志（原 McLaunchLog(…, LogLevel.Warn)；null 时静默）。</summary>
    public Action<string>? WarnLog;
}

/// <summary>
/// 游戏参数段（ModLaunch.vb:1325-1398）的宿主读取面。
/// </summary>
public sealed class GameInputs
{
    /// <summary>当前实例核心（宿主层传 McInstanceSelected.Core；VersionJson/InheritName/Name/设置键的 W1 面）。</summary>
    public InstanceCore Instance = null!;
    /// <summary>按 inheritsFrom 解析父版本 JSON（原 Function(name) (New McInstance(name)).JsonObject…）。</summary>
    public Func<string, JsonObject?> ResolveParent = null!;
    /// <summary>规则求值环境（原 New RuleEnvironment(My.Computer.Info.OSVersion)）。</summary>
    public RuleEnvironment Environment = default!; //RuleEnvironment 为结构体，宿主层以对象初始化器必赋
    /// <summary>版本信息（原 McInstanceSelected.Version；ModLoaders/HasOptiFine/OptiFineAsMod/ReleaseTime）。</summary>
    public McVersion Version = null!;
    /// <summary>自定义标记替换（原 ArgumentReplace，宿主层 ModMain.vb:819，留宿主层）。</summary>
    public Func<string, string> ArgumentReplace = null!;
    /// <summary>额外传入的参数（原 CurrentLaunchOptions.ExtraGameArgs）。</summary>
    public IReadOnlyList<string> ExtraGameArgs = null!;
    /// <summary>强制指定的服务器 IP（原 CurrentLaunchOptions.ServerIp；空时使用版本设置 VersionServerEnter）。</summary>
    public string? ServerIp;
    /// <summary>红字提示（原 Hint(…, HintType.Red)，OptiFine 与自动进服冲突时；null 时静默）。</summary>
    public Action<string>? Hint;
    /// <summary>启动日志（原 McLaunchLog；null 时静默）。</summary>
    public Action<string>? Log;
}

/// <summary>
/// 替换标记迭代核（ModLaunch.vb:1400-1473）的宿主读取面。
/// </summary>
public sealed class TokenInputs
{
    /// <summary>当前实例核心（宿主层传 McInstanceSelected.Core；Name/PathVersion/PathIndie/设置键/支持库列表的 W1 面）。</summary>
    public InstanceCore Instance = null!;
    /// <summary>PCL 纯净目录（原 PathPure.Value，以 \ 结尾）。</summary>
    public string PathPure = null!;
    /// <summary>Natives 文件夹（原 GetNativesFolder() 结果，以 \ 结尾）。</summary>
    public string NativesFolder = null!;
    /// <summary>资源索引名（原 McAssetsGetIndexName(McInstanceSelected)，留宿主层）。</summary>
    public string AssetsIndexName = null!;
    /// <summary>自定义标记替换（原 ArgumentReplace，宿主层 ModMain.vb:819，留宿主层）。</summary>
    public Func<string, string> ArgumentReplace = null!;
    /// <summary>登录输出快照（宿主层从 McLoginLoader.Output 构造）。</summary>
    public McLoginOutput Login = null!;
    /// <summary>版本信息（原 McInstanceSelected.Version；DPI #3463 修复判定）。</summary>
    public McVersion Version = null!;
    /// <summary>选定的 Java（原 McLaunchJavaSelected；DPI #3463 修复判定的 8u200~8u321 区间）。</summary>
    public Java LaunchJava = null!;
    /// <summary>当前 DPI（原 DPI 模块状态，宿主层 ModBase）。</summary>
    public double Dpi;
    /// <summary>主窗口像素尺寸（原 GetPixelSize(FrmMain.PanForm…)；经 RunInUiWait 回调在 UI 线程取值）。</summary>
    public Func<(double Width, double Height)> MainWindowPixelSize = null!;
    /// <summary>
    /// 活参数列表（可选）：空自定义信息时会成对移除 --versionType（:1427-1432，与旧代码一致作用于调用方列表）。
    /// 经 <see cref="LaunchArguments.BuildArguments"/> 编排时由其传入自身列表，本字段供独立调用使用。
    /// </summary>
    public List<string>? Arguments;
    /// <summary>启动日志（原 McLaunchLog；null 时静默）。</summary>
    public Action<string>? Log;
}

/// <summary>
/// 参数拼装总编排（ModLaunch.vb:1162-1189）的宿主读取面。
/// </summary>
public sealed class LaunchArgumentInputs
{
    /// <summary>JVM 段输入。</summary>
    public JvmInputs Jvm = null!;
    /// <summary>游戏段输入。</summary>
    public GameInputs Game = null!;
    /// <summary>替换标记段输入。</summary>
    public TokenInputs Token = null!;
    /// <summary>
    /// 版本 JSON 改写的写盘回调（裁定 10：旧 FileUtils.Write 路径 1:1，写函数由宿主层绑定，
    /// 以保 C# 侧无宿主层依赖）；收到的内容即应写入版本 JSON 的全文。
    /// </summary>
    public Action<string>? VersionJsonWriter;
    /// <summary>UI 线程同步执行（原 RunInUiWait，宿主层 ModBase.vb:924；窗口尺寸的 UI 读取经此回调）。</summary>
    public Func<Action, bool> RunInUiWait = null!;
    /// <summary>启动日志（原 McLaunchLog；null 时静默）。</summary>
    public Action<string>? Log;
}

/// <summary>
/// 启动参数拼装核（自宿主层 ModLaunch.vb 启动参数区 :1157-1493 移植）。
/// 版本 JSON 收集（VersionArguments）、参数行拆分去重（ArgumentLine）、GC 策略（GcPolicy）、
/// 补丁释放（LaunchUtils.ExtractPatch）、支持库解析（Libraries）复用既有 C# 面；设置读取经 W1 SettingsService。
/// </summary>
public static class LaunchArguments
{
    /// <summary>拼装完成的启动参数串（原 McLaunchArgument，ModLaunch.vb:1158；宿主层转发属性保持读写面）。</summary>
    public static string? LaunchArgument { get; set; }

    /// <summary>启动使用的编码（原 McLaunchEncoding，ModLaunch.vb:1159；宿主层转发属性保持读写面）。</summary>
    public static System.Text.Encoding? LaunchEncoding { get; set; }

    /// <summary>
    /// 参数拼装总编排核（:1162-1189 决策面）：JVM+mainClass+Game → 替换标记 → 引号转义 → 返回参数串。
    /// </summary>
    public static string BuildArguments(LaunchArgumentInputs inputs)
    {
        var log = inputs.Log;
        var arguments = new List<string>();
        log?.Invoke("参数获取开始");
        arguments.AddRange(BuildJvmArguments(inputs.Jvm)); //JVM 参数
        //? 旧 :1167 为 JObject 索引直取（缺失键时为 Nothing，会在后续 Replace 处 NRE）；
        //? Check()（InstanceCore）保证启动版本必有 mainClass 键，故缺失时以空串收敛
        arguments.Add(inputs.Jvm.Instance.VersionJson["mainClass"]?.ToString() ?? ""); //mainClass
        arguments.AddRange(BuildGameArguments(inputs.Game, inputs.VersionJsonWriter)); //游戏参数
        //替换
        log?.Invoke("=================== 启动参数开始 ===================");
        var replacements = BuildReplacementTable(arguments, inputs.Token, inputs.RunInUiWait);
        var argument = "";
        foreach (var original in arguments)
        {
            var arg = original;
            //应用替换标记
            foreach (var replacement in replacements)
            {
                arg = arg.Replace(replacement.Key, replacement.Value);
            }
            //为带特殊字符的参数添加双引号
            if (arg.IndexOfAny(['&', '|', '<', '>', '^', ' ']) >= 0 && !arg.StartsWithF("\"") && !arg.EndsWithF("\""))
            {
                arg = "\"" + arg.Replace("\"", "\\\"") + "\""; //转义在原本参数中间的双引号
            }
            //输出
            log?.Invoke(arg);
            argument += arg + " ";
        }
        log?.Invoke("=================== 启动参数结束 ===================");
        //输出
        LaunchArgument = argument.TrimEnd(); //:1188（原 McLaunchArgument 模块状态）
        return LaunchArgument!;
    }

    /// <summary>
    /// JVM 段（:1192-1323 决策面）：VersionArguments.CollectJvm → 自定义参数合并去重 → 统一通行证/Authlib
    /// （预取经 NetRequestByClientRetry）→ JLW 判定 → LUA（裁定 9：经 Libraries.ListGetJson）→ 内存 →
    /// GcPolicy → log4j → 编码 → JLW -jar → 再去重。
    /// </summary>
    public static List<string> BuildJvmArguments(JvmInputs inputs)
    {
        var args = new List<string>();
        var log = inputs.Log;
        var instance = inputs.Instance;
        var json = instance.VersionJson;

        //Minecraft 参数
        if (json["arguments"]?["jvm"] is not null)
        {
            log?.Invoke($"新版 JVM 参数：从版本 JSON 获取（{instance.Name}）");
        } else
        {
            log?.Invoke("旧版 JVM 参数");
        }
        args.AddRange(VersionArguments.CollectJvm(
            json,
            inputs.ResolveParent,
            inputs.Environment,
            message => Logger.Warn(message),
            inheritName: instance.InheritName));

        //设置里自定义的参数
        var customArg = SettingsService.Get<string>("VersionAdvanceJvm", instance.InstanceSettingsKey);
        if (customArg == "") customArg = SettingsService.Get<string>("LaunchAdvanceJvm");
        if (customArg != "") customArg = inputs.ArgumentReplace(customArg);

        //重新分割字符串并去重
        args = DeduplicateJavaArguments(
            ArgumentLine.Split(string.Join(" ", args).Replace("McEmu= ", "McEmu=") + " " + customArg).ToList(),
            true, log);

        //统一通行证
        if (inputs.Login.Type == "Nide")
        {
            args.Add($@"-javaagent:""${{pure_directory}}\nide8auth.jar""=""{SettingsService.Get<string>("VersionServerNide", instance.InstanceSettingsKey)}""");
        }
        //Authlib-Injector
        if (inputs.Login.Type == "Auth")
        {
            if (inputs.LaunchJava.Version!.Major >= 6) args.Add("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT"); //信任系统根证书（5252#）
            var server = SettingsService.Get<string>("VersionServerAuthServer", instance.InstanceSettingsKey);
            try
            {
                log?.Invoke($"开始 Authlib Injector Prefetch");
                var response = NetRequest.NetRequestByClientRetry(server, new NetRequestOptions { Encoding = System.Text.Encoding.UTF8, RunInUi = inputs.RunInUi });
                args.Add($@"-javaagent:""${{pure_directory}}\authlib-injector.jar""=""{server}""");
                args.Add("-Dauthlibinjector.side=client");
                args.Add("-Dauthlibinjector.yggdrasil.prefetched=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(response)));
            } catch (Exception ex)
            {
                throw new Exception("无法连接到第三方登录服务器（" + server + "）", ex);
            }
        }

        //JLW
        //非 GBK 编码下命令行参数乱码的问题（JDK-8272352）
        var useJlw =
            !SettingsService.Get<bool>("LaunchAdvanceDisableJLW") && !SettingsService.Get<bool>("VersionAdvanceDisableJLW", instance.InstanceSettingsKey) &&
            !inputs.IsGbkEncoding; //即使游戏文件夹名称不含非 ASCII 字符，但玩家名等也可能出现中文，因此依然需要 JLW（#8739）
        if (useJlw && customArg.Contains("-javaagent"))
        {
            useJlw = false;
            inputs.WarnLog?.Invoke("由于使用了自定义 javaagent，已禁用 JLW，这可能导致游戏崩溃！");
        }
        if (useJlw)
        {
            if (inputs.LaunchJava.Version!.Major >= 9)
            {
                args.Add("--add-exports");
                args.Add("cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED");
            }
            args.Add("-Doolloo.jlw.tmpdir=${pure_directory}"); //这里需要不以 \ 结尾
        }
        log?.Invoke("使用 JLW：" + useJlw);

        //LUA
        var useLua =
            !SettingsService.Get<bool>("LaunchAdvanceDisableLUA") && !SettingsService.Get<bool>("VersionAdvanceDisableLUA", instance.InstanceSettingsKey) &&
            Libraries.ListGetJson(instance.VersionJson, customMcFolder: instance.McFolderSelected) //裁定 9：LUA 判定经 C# 支持库解析等价（旧 McLibListGet(…, False)）
                .Any(entry => entry.Name == "org.lwjgl:lwjgl:3.4.1");
        if (useLua) args.Add($@"-javaagent:""{inputs.LwjglUnsafeAgentJar()}""");
        log?.Invoke("使用 LUA：" + useLua);

        #region 内存管理

        //? 旧 :1262 为 Long/Integer 的 VB “/” 浮点除，这里显式 1024d 保持同一语义
        log?.Invoke("当前剩余内存：" + Math.Round(inputs.AvailablePhysicalMemory / 1024d / 1024d / 1024d * 10) / 10 + "G");
        var targetRam = (int) Math.Floor(inputs.RamGb * 1024);
        args.Add("-Xmx" + targetRam + "m");

        //获取设置的管理方式
        //0：在 Java 21+ 使用分代 ZGC，20~15 使用非分代的 ZGC，14- 使用 G1GC
        //1：在 Java 21+ 使用分代 ZGC，20- 使用 G1GC
        //2：仅 G1GC
        //3：不指定，玩家自定义
        var setupType = SettingsService.Get<int>("LaunchAdvanceGC");
        //? 旧 :1272 的 “McInstanceSelected IsNot Nothing” 恒真（启动必已选中版本），收敛为直接读取
        if (SettingsService.Get<int>("VersionAdvanceGC", instance.InstanceSettingsKey) > 0)
        {
            setupType = SettingsService.Get<int>("VersionAdvanceGC", instance.InstanceSettingsKey) - 1;
        }
        if (setupType != 3)
        {
            var useG1Gc = GcPolicy.UsesG1Gc(
                setupType,
                inputs.LaunchJava.Version!.Major,
                inputs.WindowsVersion);
            log?.Invoke($"GC 设置：{setupType}，选取 {(useG1Gc ? "G1GC" : "ZGC")}，Java 版本：{inputs.LaunchJava.Version!.Major}");
            args = args.Where(a => !GcPolicy.IsGcArgument(a)).ToList();
            args.AddRange(GcPolicy.Build(
                setupType,
                inputs.LaunchJava.Version!.Major,
                inputs.WindowsVersion,
                inputs.Debug));
        }

        args.Remove("-XX:MaxDirectMemorySize=256M"); //#3511 的清理

        #endregion

        //Log4j 漏洞防御参数
        args.Add("-Dlog4j2.formatMsgNoLookups=true");

        //编码（#4700、#5892、#5909、#6934、#8950、#9241）
        //? net8 差异（Fix 评审项，账本已记）：net48 为运行目标；net8（仅 AOT 冒烟）未注册 CodePagesEncodingProvider
        //? 时，GBK 等 exotic 代码页在下方 GetEncoding 处抛出，ANSICodePage 回退同样会抛且不受保护。
        //? 如需 net8 运行：启动时 Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)（随共享框架分发，
        //? 无需 NuGet）；当前按约束不接线、不加包。
        var useCompatEncoding = inputs.LaunchJava.Version!.Major >= 18;
        var encodingName = useCompatEncoding
            ? inputs.LaunchJava.NativeEncodingName //Java 18+ 会添加 COMPAT，因此 Java 会回退到 native.encoding
            : inputs.LaunchJava.FileEncodingName; //Java 17- 直接使用 file.encoding
        try
        {
            //? 与旧 :1302 一致：EncodingName 为 null 时 .Equals 处 NRE 并走 Catch 回退
            LaunchEncoding = System.Text.Encoding.GetEncoding(encodingName!.Equals("GBK", StringComparison.OrdinalIgnoreCase) ? "GB18030" : encodingName!);
        } catch (Exception)
        {
            var fallbackEncoding = System.Text.Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage); //老版本 Java 的编码选择行为
            inputs.WarnLog?.Invoke($"无法识别 Java 编码 {encodingName ?? "Nothing"}，已回退为 {fallbackEncoding.WebName}");
            LaunchEncoding = fallbackEncoding;
        }
        log?.Invoke($"使用编码：{LaunchEncoding!.WebName}");
        var launchEncodingName = LaunchEncoding.CodePage == 54936 ? "GBK" : LaunchEncoding.WebName;
        var stdoutArgName = inputs.LaunchJava.Version!.Major < 19 ? "sun.stdout.encoding" : "stdout.encoding"; //老版本 Java 应该设置 sun.std
        var stderrArgName = inputs.LaunchJava.Version!.Major < 19 ? "sun.stderr.encoding" : "stderr.encoding";
        if (!args.Any(a => a.StartsWithF($"-D{stdoutArgName}="))) args.Add($"-D{stdoutArgName}={launchEncodingName}");
        if (!args.Any(a => a.StartsWithF($"-D{stderrArgName}="))) args.Add($"-D{stderrArgName}={launchEncodingName}");
        if (useCompatEncoding && !args.Any(a => a.StartsWithF("-Dfile.encoding="))) args.Add("-Dfile.encoding=COMPAT"); //防止老版本 MC 与部分 Mod 不兼容 UTF-8

        //为 JLW 添加 -jar，它必须放在最后
        if (useJlw)
        {
            args.Add("-jar");
            args.Add(inputs.JavaWrapperJar());
        }

        //再次去重并输出
        return DeduplicateJavaArguments(args, true, log);
    }

    /// <summary>
    /// 游戏段（:1325-1398 决策面）：CollectGame → 自定义参数 → OptiFineForgeTweaker 挪位 + 版本 JSON 改写
    /// （裁定 10：写盘经 <paramref name="versionJsonWriter"/> 回调，宿主层绑定 FileUtils.Write）→ 分割去重 →
    /// 额外参数 → 全屏/进服（QuickPlay 分支）→ 再去重。
    /// </summary>
    public static List<string> BuildGameArguments(GameInputs inputs, Action<string>? versionJsonWriter)
    {
        var log = inputs.Log;
        var instance = inputs.Instance;
        var json = instance.VersionJson;

        //Minecraft 参数
        var basicString = json["minecraftArguments"]?.ToString();
        if (!string.IsNullOrEmpty(basicString)) log?.Invoke($"旧版游戏参数：{basicString}");
        if (json["arguments"]?["game"] is not null)
        {
            log?.Invoke($"新版游戏参数：从版本 JSON 获取（{instance.Name}）");
        }
        var arg = VersionArguments.CollectGame(
            json,
            inputs.ResolveParent,
            inputs.Environment,
            message => Logger.Warn(message),
            inheritName: instance.InheritName);

        //设置里自定义的参数
        var customArg = SettingsService.Get<string>("VersionAdvanceGame", instance.InstanceSettingsKey);
        if (customArg == "") customArg = SettingsService.Get<string>("LaunchAdvanceGame");
        if (customArg != "") customArg = inputs.ArgumentReplace(customArg);
        arg += " " + customArg;

        //把 OptiFineForgeTweaker 放在参数的末尾
        var version = inputs.Version;
        if ((version.ModLoaders.HasFlagF(ModLoaders.Forge) || version.ModLoaders.HasFlagF(ModLoaders.LiteLoader)) &&
                version.HasOptiFine && !version.OptiFineAsMod)
        {
            if (arg.Contains("--tweakClass optifine.OptiFineForgeTweaker"))
            {
                log?.Invoke("发现正确的 OptiFineForge TweakClass，目前的游戏参数：" + arg);
                arg = arg.Replace(" --tweakClass optifine.OptiFineForgeTweaker", "") + " --tweakClass optifine.OptiFineForgeTweaker";
            }
            if (arg.Contains("--tweakClass optifine.OptiFineTweaker"))
            {
                log?.Invoke("发现错误的 OptiFineForge TweakClass，目前的游戏参数：" + arg);
                arg = arg.Replace(" --tweakClass optifine.OptiFineTweaker", "") + " --tweakClass optifine.OptiFineForgeTweaker";
                try
                {
                    //裁定 10：旧 :1357-1359 的 FileUtils.Write(path, ReadAsString(path).Replace(…)) 1:1——
                    //读取在 C# 侧，写盘经回调交宿主层绑定
                    versionJsonWriter?.Invoke(FileUtils.ReadAsString(
                        InstancePaths.GetJsonPath(instance.PathVersion, instance.Name))
                        .Replace("optifine.OptiFineTweaker", "optifine.OptiFineForgeTweaker"));
                } catch (Exception ex)
                {
                    Logger.Warn(ex, "替换 OptiFineForge TweakClass 失败");
                }
            }
        }

        //重新分割字符串并去重
        var args = DeduplicateJavaArguments(ArgumentLine.Split(arg).ToList(), false, log);

        //额外传入的参数
        args.AddRange(inputs.ExtraGameArgs);

        //全屏
        if (SettingsService.Get<int>("LaunchArgumentWindowType") == 0) args.Add("--fullscreen");

        //自动进入服务器
        var server = string.IsNullOrEmpty(inputs.ServerIp)
            ? SettingsService.Get<string>("VersionServerEnter", instance.InstanceSettingsKey)
            : inputs.ServerIp!;
        if (server.Any())
        {
            if (version.ReleaseTime > new DateTime(2023, 4, 4))
            {
                //QuickPlay
                args.Add("--quickPlayMultiplayer");
                args.Add(server);
            } else
            {
                //老版本
                if (server.Contains(":"))
                {
                    //包含端口号
                    args.Add("--server");
                    args.Add(server.Split(':')[0]);
                    args.Add("--port");
                    args.Add(server.Split(':')[1]);
                } else
                {
                    //不包含端口号
                    args.Add("--server");
                    args.Add(server);
                    args.Add("--port");
                    args.Add("25565");
                }
                if (version.HasOptiFine) inputs.Hint?.Invoke("OptiFine 与自动进入服务器可能不兼容，有概率导致材质丢失甚至游戏崩溃！");
            }
        }

        //再次去重并输出
        return DeduplicateJavaArguments(args, false, log);
    }

    /// <summary>
    /// 替换标记迭代核（:1400-1473 决策面）：构建全量标记表（natives/classpath/登录信息/窗口尺寸/DPI #3463/
    /// OptiFine cp 位置）并对 <paramref name="template"/> 应用替换。
    /// 空自定义信息时对 <see cref="TokenInputs.Arguments"/> 成对移除 --versionType（:1427-1432，活列表语义）。
    /// </summary>
    public static string ReplaceTokens(string template, TokenInputs inputs, Func<Action, bool> runInUiWait)
    {
        var replacements = BuildReplacementTable(inputs.Arguments ?? [], inputs, runInUiWait);
        var result = template;
        foreach (var replacement in replacements)
        {
            result = result.Replace(replacement.Key, replacement.Value);
        }
        return result;
    }

    /// <summary>
    /// 构建 :1400-1473 的全量替换标记表（迭代核）；<paramref name="arguments"/> 为活参数列表，
    /// 空自定义信息时会成对移除其中的 --versionType（与旧代码一致作用于调用方列表）。
    /// </summary>
    public static List<(string Key, string Value)> BuildReplacementTable(List<string> arguments, TokenInputs inputs, Func<Action, bool> runInUiWait)
    {
        var instance = inputs.Instance;
        var replacements = new List<(string Key, string Value)>();
        //基础参数
        replacements.Add(("${classpath_separator}", ";"));
        replacements.Add(("${natives_directory}", PathUtils.RemoveSlashSuffix(inputs.NativesFolder)));
        replacements.Add(("${library_directory}", PathUtils.ToShortPath(instance.McFolderSelected + "libraries")));
        replacements.Add(("${libraries_directory}", PathUtils.ToShortPath(instance.McFolderSelected + "libraries")));
        //由 PCL 添加，这会允许在分割参数并去重后再替换路径，防止路径中的特殊字符影响参数分割和去重
        replacements.Add(("${pure_directory}", PathUtils.RemoveSlashSuffix(inputs.PathPure)));
        replacements.Add(("${launcher_name}", LauncherIdentity.AssemblyName));
        replacements.Add(("${launcher_version}", VersionInfo.ProtocolVersion.ToString(CultureInfo.InvariantCulture)));
        replacements.Add(("${version_name}", instance.Name));
        replacements.Add(("${game_directory}", PathUtils.ToShortPath(instance.PathIndie[..^1])));
        replacements.Add(("${assets_root}", PathUtils.ToShortPath(instance.McFolderSelected + "assets")));
        replacements.Add(("${user_properties}", "{}"));
        replacements.Add(("${auth_player_name}", inputs.Login.Name!));
        replacements.Add(("${auth_uuid}", inputs.Login.Uuid!));
        replacements.Add(("${auth_access_token}", inputs.Login.AccessToken!));
        replacements.Add(("${access_token}", inputs.Login.AccessToken!));
        replacements.Add(("${auth_session}", inputs.Login.AccessToken!));
        replacements.Add(("${user_type}", "msa")); //#1221
        replacements.Add(("${primary_jar}", PathUtils.ToShortPath(instance.PathVersion + instance.Name + ".jar"))); //#6942
        //1.5.2 的 pre-1.6 资源索引应与 legacy 合并
        replacements.Add(("${game_assets}", PathUtils.ToShortPath(instance.McFolderSelected + @"assets\virtual\legacy")));
        replacements.Add(("${assets_index_name}", inputs.AssetsIndexName));

        //自定义信息
        var argumentInfo = SettingsService.Get<string>("VersionArgumentInfo", instance.InstanceSettingsKey);
        if (argumentInfo == "") argumentInfo = SettingsService.Get<string>("LaunchArgumentInfo");
        if (argumentInfo == "")
        {
            //若自定义信息为空，则必须从参数中去掉该部分，不然 MC 会显示 ${version_type} 或者一个空字符串
            if (arguments.Contains("--versionType"))
            {
                var index = arguments.IndexOf("--versionType");
                arguments.RemoveAt(index);
                if (index < arguments.Count) arguments.RemoveAt(index);
            }
            replacements.Add(("${version_type}", "\"\""));
        } else
        {
            replacements.Add(("${version_type}", inputs.ArgumentReplace(argumentInfo)));
        }

        //窗口尺寸
        //? 舍入语义（Fix 评审项，账本条目"舍入核验"）：旧 VB 的 Size 解析为 System.Windows.Size（Double；
        //? PCLA.vbproj:727 项目级导入 System.Windows，System.Drawing 仅为 Reference 未导入，MeloongCore 无自定义
        //? Size），全程 Double 运算、仅在下方 Yield 处 Math.Round（银行家舍入 ToEven）一次，与本 C# 面逐位一致——
        //? 中间无取整，"Case 2 + DPI 叠加最坏差 1px"的中间舍入分歧在当前工程解析下不存在；若未来把整型 Size
        //? （如 System.Drawing）引入导入面，旧语义将出现中间舍入（.Width /= … 处两次银行家舍入），届时须复核此处。
        double targetWidth, targetHeight;
        switch (SettingsService.Get<int>("LaunchArgumentWindowType"))
        {
            case 2: //与启动器尺寸一致
            {
                (double Width, double Height) result = default;
                runInUiWait(() => result = inputs.MainWindowPixelSize());
                targetWidth = result.Width;
                targetHeight = result.Height;
                targetHeight -= 29.5 * inputs.Dpi / 96; //标题栏高度
                break;
            }
            case 3: //自定义
                targetWidth = Math.Max(100, SettingsService.Get<int>("LaunchArgumentWindowWidth"));
                targetHeight = Math.Max(100, SettingsService.Get<int>("LaunchArgumentWindowHeight"));
                break;
            default:
                targetWidth = 854;
                targetHeight = 480;
                break;
        }
        var javaVersion = inputs.LaunchJava.Version!;
        if (inputs.Version.Vanilla.Major <= 12 && javaVersion >= new Version(8, 0, 200) && javaVersion <= new Version(8, 0, 321) &&
                !inputs.Version.HasOptiFine && !inputs.Version.ModLoaders.HasFlagF(ModLoaders.Forge))
        {
            //修复 1.12.2- 下 JRE 8u200~321 下窗口大小为设置大小的 DPI% 倍（#3463）
            inputs.Log?.Invoke($"已应用窗口大小过大修复（Java 版本：{javaVersion}）");
            targetWidth /= inputs.Dpi / 96;
            targetHeight /= inputs.Dpi / 96;
        }
        replacements.Add(("${resolution_width}", Math.Round(targetWidth).ToString(CultureInfo.InvariantCulture)));
        replacements.Add(("${resolution_height}", Math.Round(targetHeight).ToString(CultureInfo.InvariantCulture)));

        //支持库
        var cps = new List<string>();
        string? optiFineCp = null;
        foreach (var library in Libraries.ListGet(instance, includeMainJar: true))
        {
            if (library.IsNative) continue;
            if (library.Name is not null && library.Name == "optifine:OptiFine")
            {
                optiFineCp = library.Path;
            } else
            {
                cps.Add(library.Path!);
            }
        }
        if (optiFineCp is not null) cps.Insert(cps.Count - 2, optiFineCp); //OptiFine 的总是需要放到倒数第二位
        replacements.Add(("${classpath}", string.Join(";", cps.Select(c => PathUtils.ToShortPath(c)))));
        return replacements;
    }

    /// <summary>
    /// 对 Java 参数进行去重并输出诊断日志（原 DeduplicateJavaArguments，:1484-1491）。
    /// </summary>
    private static List<string> DeduplicateJavaArguments(IReadOnlyList<string> args, bool isJvm, Action<string>? log)
    {
        var diagnostics = new List<string>();
        var result = ArgumentLine.Deduplicate(args, isJvm, diagnostics).ToList();
        foreach (var line in diagnostics)
        {
            log?.Invoke(line);
        }
        return result;
    }
}
