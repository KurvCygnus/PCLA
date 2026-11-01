using PCLCS.Download;
using PCLCS.Instances;
using PCLCS.Minecraft.Download;
using PCLCS.Net;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace PCLCS.Install;

/// <summary>
/// 安装决策核（ModDownloadLib.vb 的决策段迁入；进程注入器、Loader 链与 WPF 留宿主层）：
/// Forge/NeoForge 安装器下载源（:1122-1141）、新版安装链（:1150-1193：解包合并 → 库清单 → MOJMAPS 追加 → NetFile 组装）、
/// Legacy 方式 1/2（:1293-1342）与注入器输出行映射（:1039-1076）；
/// OptiFine 安装器地址/class 版本/方式 A 参数/方式 B 手写 Json（:359-630 与 :645-670 决策面）、
/// LiteLoader redist 表/安装源/版本 Json 组装（:740-824 决策面）与 Fabric profile 下载决策（:1505-1524 决策面）。
/// JSON 合并复用 MergeJson.MergeOverlay（Newtonsoft JObject.Merge 默认语义的 STJ 等价，无参数重组）。
/// </summary>
public static class InstallerPlans
{
    /// <summary>Forge/NeoForge 安装器 Jar 的本地缓存文件名（ModDownloadLib.vb:1097；宿主层拼接 RequestTaskTempFolder(True)）。</summary>
    public const string ForgelikeInstallerPath = "forge_installer.jar";

    /// <summary>等待原版下载完成的子加载器匹配名（ModDownloadLib.vb:75-76 常量的 C# 侧镜像；宿主层常量保留、等待段经计划引用）。</summary>
    public static readonly string[] VanillaWaitLoaderNames = ["下载原版支持库文件", "下载原版 json 文件"];

    /// <summary>
    /// Forge/NeoForge 安装器的下载文件（安装器 Jar 本体）决策（ModDownloadLib.vb:1122-1141 决策面）：
    /// NeoForge 取 UrlBase 官方源并改写 BMCLAPI 主机；Forge 拼 maven 路径（Inherit 的 "-" 换 "_"）双源；
    /// 校验器为 MinSize 64KB（Forge 源另带 Hash）。
    /// brief 计划稿的 (InstallerUrl, InstallerPath) 收敛为此形态：InstallerPath 是固定常量（见 <see cref="ForgelikeInstallerPath"/>，
    /// 宿主层拼 RequestTaskTempFolder(True)），不随条目变化；Sources/Checker 为 NetFile 构造直用的真实消费数据。
    /// </summary>
    /// <param name="entry">已解析的 Forge/NeoForge 详情条目。</param>
    /// <param name="preferBmclapi">为真时 BMCLAPI 源在前。旧 :1128-1139 恒以 BMCLAPI 为首选，宿主层转发 True 保持行为。</param>
    /// <returns>InstallerUrl：候选首项（按偏好）；Sources：全部候选源（保序）；Checker：下载校验器。</returns>
    public static (string InstallerUrl, IReadOnlyList<string> Sources, FileChecker Checker) ForgelikeInstallerAddress(DlForgelikeEntry entry, bool preferBmclapi)
    {
        List<string> sources;
        var checker = new FileChecker { MinSize = 64 * 1024 };
        if (entry.IsNeoForge)
        {
            //NeoForge（:1124-1130）
            var neo = (DlNeoForgeListEntry) entry;
            var url = neo.UrlBase + "-installer.jar";
            var bmclapiUrl = url.Replace("maven.neoforged.net/releases", "bmclapi2.bangbang93.com/maven");
            sources = [bmclapiUrl, url];
        } else
        {
            //Forge（:1131-1139）
            var forge = (DlForgeVersionEntry) entry;
            checker.Hash = forge.Hash;
            var inherit = forge.Inherit!.Replace("-", "_");
            var fileName = $"{inherit}-{forge.FileVersion}/forge-{inherit}-{forge.FileVersion}-{forge.Category}.{forge.FileExtension}";
            sources =
            [
                $"https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/{fileName}",
                $"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{fileName}"
            ];
        }
        if (!preferBmclapi) sources.Reverse();   //旧 :1128-1139 恒为 BMCLAPI 在前；False 时官方在前
        return (sources[0], sources, checker);
    }

    /// <summary>
    /// 新版（方式 A）安装链决策（ModDownloadLib.vb:1150-1193 决策面）：打开安装器并合并 install_profile.json 与
    /// version.json（:1155-1160）→ 库清单（:1162，经 W1 Libraries.ListGetJson，保留同名不同版本）→ MOJMAPS 追加
    /// （:1164-1176，原版版本 Json 经 <paramref name="vanillaVersionJsonProvider"/> 由宿主层网络获取）→ 移除 Forgelike 原始项
    /// （:1179-1186）→ NetFile 组装（:1187，经 McLibNetFilesFromTokens 的 LibEntry 映射面）。
    /// 等待原版下载的匹配名见 <see cref="ForgelikePlan.WaitVanillaLoaderNames"/>（:1206-1208 的执行留宿主层）。
    /// 异常由宿主层包装为"获取 XX Forge 支持库列表失败"（:1188-1189）。
    /// </summary>
    /// <param name="installerPath">安装器 Jar 本地路径（旧 InstallerAddress）。</param>
    /// <param name="mcFolder">当前 MC 根文件夹（旧 CurrentMcFolder，以 \ 结尾）。</param>
    /// <param name="inherit">继承的原版版本号。</param>
    /// <param name="loaderName">"Forge" / "NeoForge"（宿主层 LoaderName）。</param>
    /// <param name="loaderVersion">Loader 版本号。</param>
    /// <param name="preferMojang">旧 DlSourcePreferMojang（宿主层读设置后传入）。</param>
    /// <param name="vanillaVersionJsonProvider">原版版本 Json 提供者（旧 :1167 的网络请求；仅 MOJMAPS 命中时调用）。</param>
    /// <param name="progress">进度检查点回调（旧 Task.Progress 的 0.2/0.4/0.8）。</param>
    public static ForgelikePlan BuildModernPlan(
        string installerPath, string mcFolder, string inherit, string loaderName, string loaderVersion,
        bool preferMojang, Func<JsonObject> vanillaVersionJsonProvider, Action<double>? progress = null)
    {
        using var installer = FileUtils.OpenZip(installerPath);
        progress?.Invoke(0.2);   //旧 :1157（OpenZip 后）
        //解压并获取、合并两个 Json 的信息（:1155-1160）
        var profileJson = ReadEntryObject(installer, "install_profile.json");
        MergeJson.MergeOverlay(profileJson, ReadEntryObject(installer, "version.json"));   //旧 Json.Merge(Json2)
        //获取 Lib 下载信息（:1162）
        var libs = Libraries.ListGetJson(profileJson, keepSameNameDifferentVersion: true, customMcFolder: mcFolder);
        //添加 Mappings 下载信息（:1164-1176）
        //? JSON null 边界（W1 TECHDEBT §10 口径）：旧 Newtonsoft 对值为 JSON null 的 data/MOJMAPS 会进入分支并在取值处抛出；
        //? STJ 下 JSON null 与缺失同判，跳过分支（仅病态输入有差异，正常安装器两者一致）
        var mojmaps = (profileJson["data"] as JsonObject)?["MOJMAPS"] as JsonObject;
        if (mojmaps is not null)
        {
            progress?.Invoke(0.4);   //旧 :1166
            //下载原版 Json 文件（:1167，网络请求经宿主层回调）
            var rawJson = vanillaVersionJsonProvider();
            //[net.minecraft:client:1.17.1-20210706.113038:mappings@txt] 或 @tsrg]（:1168）
            var mappingsToken = mojmaps["client"]!.ToString();   //旧 ("client").ToString：缺失时 NRE
            var originalName = mappingsToken.Trim('[', ']').BeforeFirst("@");
            var mappingExtension = mappingsToken.Trim('[', ']').Split('@')[1];
            var localPath = Path.ChangeExtension(Libraries.GetLocalPath(originalName, customMcFolder: mcFolder), "." + mappingExtension);
            var clientMappings = (JsonObject?) rawJson["downloads"]!["client_mappings"];   //旧 :1171：downloads/client_mappings 缺失时在下方取值处 NRE
            libs.Add(new LibEntry(null)
            {
                IsNative = false,
                Path = localPath,
                Name = originalName,
                //? Size/Checksum 的 JSON null 与缺失经 Token 语义收敛为不校验（与旧 JToken→Long/String 转换的可观察结果一致）
                Size = Libraries.Token(clientMappings, "size"),
                Checksum = Libraries.Token(clientMappings, "sha1"),
                Url = string.IsNullOrWhiteSpace(clientMappings!["url"]?.ToString()) ? null : clientMappings["url"]!.ToString()   //旧 Url setter（ModMinecraft.vb:1529-1531）；client_mappings 缺失时此处 NRE
            });
            Logger.Info($"需要下载 Mappings：{clientMappings["url"]} (SHA1: {clientMappings["sha1"]})");   //旧 :1175
        }
        progress?.Invoke(0.8);   //旧 :1177
        //去除其中的原始 Forgelike 项（:1179-1186）
        for (var i = 0; i < libs.Count; i++)
        {
            //! 逐字移植豁免（Rider 反转 if）：旧 :1180-1181 的双条件 OrElse 结构
            if (libs[i].Path!.EndsWithF($"{loaderName.Lower()}-{inherit}-{loaderVersion}.jar") ||
                libs[i].Path!.EndsWithF($"{loaderName.Lower()}-{inherit}-{loaderVersion}-client.jar"))
            {
                Logger.Warn($"已从待下载 {loaderName} 支持库中移除：{libs[i].Path}");
                libs.RemoveAt(i);
                break;   //旧 Exit For
            }
        }
        //NetFile 组装（:1187）并回传计划（:1195-1227 的等待与复制执行留宿主层）
        return new ForgelikePlan
        {
            Files = NetFilesFromLibEntries(libs, mcFolder, preferMojang),
            MergedVersionJson = profileJson,
            WaitVanillaLoaderNames = [.. VanillaWaitLoaderNames],
            Libraries = libs
        };
    }

    /// <summary>
    /// Legacy 方式 1（中版，纯解包 + Json 写入）/ 方式 2（旧版，Jar 解压 + versionInfo 写入）安装决策
    /// （ModDownloadLib.vb:1293-1342 决策面）：读取 install_profile.json，按 "install" 键判定方式并完成
    /// 版本 Json 写入（id / inheritsFrom 修改）、方式 1 的 maven 解压复制与 unrar 清理、方式 2 的 Jar 条目落盘
    /// （经 W1 Libraries.GetLocalPath）。安装器文件与 _unrar 的兜底清理留宿主层 Finally（:1332-1340）。
    /// 异常由宿主层包装为"非新版方式安装 Forge 失败"（:1330-1331）。
    /// </summary>
    /// <param name="installerPath">安装器 Jar 本地路径（旧 InstallerAddress）。</param>
    /// <param name="versionFolder">目标版本文件夹（旧 VersionFolder，以 \ 结尾）。</param>
    /// <param name="newInstanceId">新版本名（旧 NewInstanceName）。</param>
    /// <param name="inherit">继承的原版版本号。</param>
    /// <param name="mcFolder">目标 MC 根文件夹（旧 McFolder 参数，以 \ 结尾；方式 2 的 Jar 落盘用）。</param>
    /// <param name="progress">进度检查点回调（旧 Task.Progress 的 0.2/0.4/0.5/0.6/0.9）。</param>
    public static ForgelikePlan BuildLegacyPlan(
        string installerPath, string versionFolder, string newInstanceId, string inherit, string mcFolder, Action<double>? progress = null)
    {
        var isWay1 = false;
        JsonObject planJson;
        using (var installer = FileUtils.OpenZip(installerPath))
        {
            progress?.Invoke(0.2);   //旧 :1299
            var profileJson = ReadEntryObject(installer, "install_profile.json");   //旧 :1300
            progress?.Invoke(0.4);   //旧 :1301
            //新建版本文件夹（旧 :1303）
            DirectoryUtils.Create(versionFolder);
            progress?.Invoke(0.5);   //旧 :1304
            //? JSON null 边界（W1 TECHDEBT §10 口径）：旧 Newtonsoft 的 "install" 键存在但为 JSON null 时非 Nothing，走入方式 2
            //? 并在取 path 处抛出；此处以 ContainsKey 同判（含 JSON null），病态输入同样在取值处抛出，正常安装器两者一致
            if (!profileJson.ContainsKey("install"))
            {
                //中版：Legacy 方式 1（:1306-1317）
                isWay1 = true;
                Logger.Info($"开始进行 Forge 安装，Legacy 方式 1：{installerPath}");
                //建立 Json 文件（:1309-1311）
                planJson = ReadEntryObject(installer, profileJson["json"]!.ToString().TrimStart('/'));   //旧 :1309：json 项缺失时 NRE
                planJson["id"] = newInstanceId;
                FileUtils.Write(versionFolder + newInstanceId + ".json", planJson.ToJsonString(MergeJson.WriteOptions));
                progress?.Invoke(0.6);   //旧 :1312
            } else
            {
                //旧版：Legacy 方式 2（:1319-1328）
                Logger.Info($"开始进行 Forge 安装，Legacy 方式 2：{installerPath}");
                //解压 Jar 文件（:1322-1323）
                var install = (JsonObject?) profileJson["install"];
                var jarAddress = Libraries.GetLocalPath(install!["path"]!.ToString(), customMcFolder: mcFolder);   //旧 :1322：path 缺失时 NRE
                using (var entryStream = installer.GetEntry(install["filePath"]!.ToString())!.Open())   //旧 :1323：filePath 缺失时 NRE
                {
                    FileUtils.Write(jarAddress, entryStream);
                }
                progress?.Invoke(0.9);   //旧 :1324
                //建立 Json 文件（:1326-1328）
                planJson = (JsonObject) profileJson["versionInfo"]!;   //旧 :1326：versionInfo 缺失/JSON null 时抛出
                planJson["id"] = newInstanceId;
                //? JSON null 边界：旧 Newtonsoft 的 inheritsFrom 为 JSON null 时非 Nothing → 不补 Inherit；ContainsKey 同判
                if (!planJson.ContainsKey("inheritsFrom"))
                {
                    planJson["inheritsFrom"] = inherit;
                }
                FileUtils.Write(versionFolder + newInstanceId + ".json", planJson.ToJsonString(MergeJson.WriteOptions));
            }
        }   //旧 :1314 方式 1 在解压前 Dispose（:1335 方式 2 在 Finally Dispose）
        //! 逐字移植豁免（Rider 反转 if）：反转将复制末端 return（旧 VB 单一返回点 :1341），破坏 1:1 锚点结构
        if (isWay1)
        {
            //解压支持库文件（:1315-1317）
            FileUtils.ExtractToDirectory(installerPath, installerPath + "_unrar" + Path.DirectorySeparatorChar);
            if (DirectoryUtils.Exists(installerPath + "_unrar" + Path.DirectorySeparatorChar + "maven" + Path.DirectorySeparatorChar))
            {
                DirectoryUtils.Copy(installerPath + "_unrar" + Path.DirectorySeparatorChar + "maven" + Path.DirectorySeparatorChar,
                    mcFolder + "libraries" + Path.DirectorySeparatorChar);
            }
            DirectoryUtils.Delete(installerPath + "_unrar" + Path.DirectorySeparatorChar);
        }
        return new ForgelikePlan { MergedVersionJson = planJson, Files = [], WaitVanillaLoaderNames = [], Libraries = [] };
    }

    /// <summary>
    /// 注入器输出行 → 进度映射（ModDownloadLib.vb:1039-1076 映射表逐字；VB Select Case 为序数精确匹配）。
    /// </summary>
    /// <param name="line">安装器输出行原文。</param>
    /// <param name="currentProgress">任务当前进度（累进行 "  File exists: Checksum validated." 的 += 0.003 基准）。</param>
    /// <returns>更新后的进度（赋值与命中日志留宿主层）；null 为未命中（宿主层记 Trace 后跳过）。</returns>
    public static double? InjectorLineProgress(string line, double currentProgress)
    {
        switch (line)
        {
        case "Extracting json": return 0.27;
        case "Downloading libraries": return 0.28;
        case "  File exists: Checksum validated.": return currentProgress + 0.003;
        case "Building Processors": return 0.38;
        case "Task: DOWNLOAD_MOJMAPS": return 0.4;   //B
        case "Task: MERGE_MAPPING": return 0.5;   //B
        case "Splitting: ": return 0.55;
        case "Parameter Annotations": return 0.6;   //B
        case "Processing Complete": return 0.67;   //B
        case "log: null": return 0.67;   //new
        case "Sorting": return 0.8;   //new
        case "Remapping final jar": return 0.85;   //A
        case "Remapping jar... 50%": return 0.9;   //A
        case "Remapping jar... 100%": return 0.95;   //A
        case "Injecting profile": return 0.98;
        default: return null;   //旧 Case Else：Logger.Trace(Content) 后跳过（宿主层承载）
        }
    }

    #region OptiFine 安装决策（ModDownloadLib.vb:359-630 与 :645-670 的决策面；进程运行段与 Loader 链留宿主层）

    /// <summary>
    /// OptiFine 安装器 class 头解析与 Java 版本需求决策（McDownloadOptiFineInstall :361-374 决策面）：
    /// 校验 CAFEBABE 头与 49..100 版本界，回传 class 版本与 Java 范围（旧 AtLeast(New Version(ClassVersion - 44, 0))，:372）。
    /// 读取失败（条目缺失 :364 / 头长不足 :367 / 头无效 :368 / 版本越界 :370-371）按旧异常类型上抛；
    /// 宿主层 Catch 记 Warn 后回退默认 Java 21（:360 默认值 + :376-378）。
    /// brief 计划稿将其并入 BuildOptiFinePlan 收敛为独立决策：解析发生在安装入口（Dispatch 令"include it if it feeds the plan"——
    /// 其产物 JavaRange 供 SelectOrDownloadJava（VB 真值宿主段）消费，是安装计划的前置决策）。
    /// </summary>
    public static (int ClassVersion, ValueRange<Version> JavaRange) OptiFineClassVersion(string installerPath)
    {
        using var installer = FileUtils.OpenZip(installerPath);
        var installerClass = installer.GetEntry("optifine/Installer.class") ?? throw new FileNotFoundException("未找到 optifine/Installer.class");   //旧 :363-364
        using var stream = installerClass.Open();
        using var reader = new BinaryReader(stream);
        var header = reader.ReadBytes(8);
        if (header.Length < 8) throw new IOException("Installer.class 长度不足");   //旧 :367
        if (header[0] != 0xCA || header[1] != 0xFE || header[2] != 0xBA || header[3] != 0xBE) throw new InvalidDataException("Installer.class 文件头无效");   //旧 :368
        var classVersion = header[6] * 0x100 + header[7];   //旧 :369（6-7 字节大端 major）
        if (classVersion < 49) throw new InvalidDataException("Installer.class 版本过低");   //旧 :370
        if (classVersion > 100) throw new InvalidDataException("Installer.class 版本过高");   //旧 :371
        var javaRange = ValueRange<Version>.AtLeast(new Version(classVersion - 44, 0));   //旧 :372
        Logger.Info($"OptiFine 安装器 class 版本：{classVersion}，需要 Java {javaRange}");   //旧 :373
        return (classVersion, javaRange);
    }

    /// <summary>
    /// OptiFine 安装链的版本名、安装器本地目标与方式 A/B 判定（McDownloadOptiFineLoader :485-490 决策面）：
    /// 新版（Inherit 含 "w" 或 Drop >= 140，:487）安装器落 tempFolder + "OptiFine.jar"，旧版落
    /// libraries\optifine\OptiFine\{名}\{文件}（:490）。tempFolder 为旧 RequestTaskTempFolder(True) 的惰性提供者
    /// （旧 If(IsNewerVersion, ...) 短路：仅方式 A 取值），mcFolder 以 \ 结尾。
    /// brief 计划稿的 (InstallerUrl, InstallerPath) 收敛：URL 决策见 <see cref="OptiFineDownloadSources"/>（源无偏好开关，二者拆分）。
    /// </summary>
    public static (string Id, string InstallerPath, bool IsNewerVersion) OptiFineInstallTarget(DlOptiFineListEntry entry, string mcFolder, Func<string> tempFolder)
    {
        var id = entry.InstanceName!;   //旧 :485
        var isNewerVersion = entry.Inherit!.Contains("w") || McVersion.VersionToDrop(entry.Inherit) >= 140;   //旧 :487（快照名或 Drop 序数）
        var fileName = entry.FileName!;
        var installerPath = isNewerVersion
            ? tempFolder() + "OptiFine.jar"
            : mcFolder + @"libraries\optifine\OptiFine\" +
              PathUtils.GetFileNameWithoutExtension(fileName).Replace("OptiFine_", "").Replace("preview_", "") + @"\" +
              fileName.Replace("OptiFine_", "OptiFine-").Replace("preview_", "");   //旧 :490
        return (id, installerPath, isNewerVersion);
    }

    /// <summary>
    /// OptiFine 主文件下载源决策（:496-517 与 :648-669 两处逐字共用的决策面）：
    /// [0] 恒为 BMCLAPI 源（#4281 的 1.8/1.9 → ".0" 补全；preview 走 "HD_U_" 段且空格换 "/"，正式走 "HD_U/" 段）；
    /// officialPage 为宿主层抓取的 adloadx 页面（:508/:659 网络请求留宿主层），解析 "downloadx?f=..." 链接排第二，
    /// 无匹配时抛 InvalidOperationException（旧 RegexSearch(...).First 的空序列语义，宿主层 Catch 记 Warn 后以 null 回退仅 BMCLAPI）。
    /// checker 的 MinSize 由调用方按链承载（安装链 300KB :517 / 保存链 64KB :669）。
    /// brief 计划稿的 preferBmclapi 参数无 VB 真值（旧代码无源偏好开关，BMCLAPI 恒首选），未迁入。
    /// </summary>
    public static (IReadOnlyList<string> Sources, FileChecker Checker) OptiFineDownloadSources(DlOptiFineListEntry entry, string? officialPage, long minSize)
    {
        var sources = new List<string>();
        //BMCLAPI 源（:496-504 / :648-655）
        var bmclapiInherit = entry.Inherit!;
        if (bmclapiInherit is "1.8" or "1.9") bmclapiInherit += ".0";   //#4281
        var name = entry.DisplayName!.Replace(entry.Inherit + " ", "");
        sources.Add(entry.IsPreview
            ? $"https://bmclapi2.bangbang93.com/optifine/{bmclapiInherit}/HD_U_{name.Replace(" ", "/")}"
            : $"https://bmclapi2.bangbang93.com/optifine/{bmclapiInherit}/HD_U/{name}");
        //官方源（:505-515 / :656-666）：页面抓取与 Warn 留宿主层，此处仅解析链接
        if (officialPage is not null)
        {
            sources.Add("https://optifine.net/" + officialPage.RegexSearch("downloadx\\?f=[^\"']+").First());
        }
        return (sources, new FileChecker { MinSize = minSize });
    }

    /// <summary>
    /// OptiFine 方式 A 的安装器进程参数（McDownloadOptiFineInstall :384-390 决策面）：
    /// useJavaWrapper 由宿主层合并重试状态与设置项（旧 If UseJavaWrapper AndAlso Not Settings("LaunchAdvanceDisableJLW")，
    /// 条件求值留宿主层以保 ExtractPatch 的惰性）；javaWrapperJar 为旧 ExtractPatch("Launch\JavaWrapper.jar", PathPure.Value)
    /// 的惰性提供者（仅 JLW 分支取值，:386）；javaMajor >= 9 时前置 --add-exports（:390）。
    /// 进程运行段（:392-475 的 Process/InstallSyncLock）留宿主层。
    /// </summary>
    public static string OptiFineInstallerArgs(string pathPure, string baseMcFolderHome, string target, Func<string> javaWrapperJar, int javaMajor, bool useJavaWrapper)
    {
        var arguments = useJavaWrapper
            ? $"-Doolloo.jlw.tmpdir=\"{PathUtils.RemoveSlashSuffix(pathPure)}\" -Duser.home=\"{PathUtils.RemoveSlashSuffix(baseMcFolderHome)}\" -cp \"{target}\" -jar \"{javaWrapperJar()}\" optifine.Installer"
            : $"-Duser.home=\"{PathUtils.RemoveSlashSuffix(baseMcFolderHome)}\" -cp \"{target}\" optifine.Installer";   //旧 :384-389
        if (javaMajor >= 9) arguments = "--add-exports cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED " + arguments;   //旧 :390
        return arguments;
    }

    /// <summary>
    /// OptiFine 方式 B 的手写版本 Json 全文（McDownloadOptiFineLoader :600-628 决策面，4 空格缩进原文逐字）：
    /// ReleaseTime 为空（或缺失，VB "Nothing = "" 为真）时回退继承版本发布日期（:603-604，inheritReleaseDate 由宿主层读
    /// McInstance 后传入）；isOldJson 为真走旧版 Json 格式（minimumLauncherVersion 数值 18 + minecraftArguments 双空格追加
    /// TweakClass，:614-617），否则新版格式（"21" 字符串 + arguments.game，:620-628——旧代码原样的形态差异）。
    /// minecraftArguments 为惰性提供者（旧 InheritInstance.JsonObject("minecraftArguments") 的缺失 NRE 保持在分支内）。
    /// 行尾保真：旧 VB 源文件在本仓库为 LF，编译产物以 LF 写盘，C# 侧以显式 \n 拼接（InstallerPlans.cs 源文件为 CRLF）。
    /// 写盘（:630 FileUtils.Write）与异常包装（"安装 OptiFine（方式 B）失败"）留宿主层。
    /// brief 计划稿的 JsonObject HandwrittenJson 收敛为字符串：旧真值为手写字符串直接落盘，经 JsonObject 重组会改写布局与形态（"21" 字符串等）。
    /// </summary>
    public static string OptiFineHandwrittenJson(DlOptiFineListEntry entry, string inheritReleaseDate, bool isOldJson, Func<string> minecraftArguments, Action<double>? progress = null)
    {
        var date = string.IsNullOrEmpty(entry.ReleaseTime) ? inheritReleaseDate : entry.ReleaseTime!.Replace("/", "-");   //旧 :603-604
        var libName = PathUtils.GetFileNameWithoutExtension(entry.FileName!).Replace("OptiFine_", "").Replace("preview_", "");   //旧 :607
        var prefix = "{\n" +
            $"    \"id\": \"{entry.InstanceName}\",\n" +
            $"    \"inheritsFrom\": \"{entry.Inherit}\",\n" +
            $"    \"time\": \"{date}T23:33:33+08:00\",\n" +
            $"    \"releaseTime\": \"{date}T23:33:33+08:00\",\n" +
            "    \"type\": \"release\",\n" +
            "    \"libraries\": [\n" +
            $"        {{\"name\": \"optifine:OptiFine:{libName}\"}},\n" +
            "        {\"name\": \"net.minecraft:launchwrapper:1.12\"}\n" +
            "    ],\n" +
            "    \"mainClass\": \"net.minecraft.launchwrapper.Launch\",\n";
        progress?.Invoke(0.8);   //旧 :611（前缀组装后、方式分支前）
        return prefix + (isOldJson
            ? "    \"minimumLauncherVersion\": 18,\n" +
              $"    \"minecraftArguments\": \"{minecraftArguments()}  --tweakClass optifine.OptiFineTweaker\"\n" +
              "}"
            : "    \"minimumLauncherVersion\": \"21\",\n" +
              "    \"arguments\": {\n" +
              "        \"game\": [\n" +
              "            \"--tweakClass\",\n" +
              "            \"optifine.OptiFineTweaker\"\n" +
              "        ]\n" +
              "    }\n" +
              "}");
    }

    #endregion

    #region LiteLoader 安装决策（ModDownloadLib.vb:740-824 的决策面；下载入口、写盘与 WPF 留宿主层）

    /// <summary>
    /// LiteLoader 老版本 redist 硬编码地址表（McDownloadLiteLoaderSave :759-769 Select Case 逐字）；
    /// 未列出的版本抛 NotSupportedException（旧 Case Else，:770-771）。
    /// brief 计划稿的 (Url, Path)? 可空收敛：旧真值为抛出而非返回空，落盘路径由宿主层 SaveFile 对话框决定。
    /// </summary>
    public static string LegacyLiteLoaderRedist(string inherit) => inherit switch
    {
        "1.7.10" => "https://dl.liteloader.com/redist/1.7.10/liteloader-installer-1.7.10-04.jar",
        "1.7.2" => "https://dl.liteloader.com/redist/1.7.2/liteloader-installer-1.7.2-04.jar",
        "1.6.4" => "https://dl.liteloader.com/redist/1.6.4/liteloader-installer-1.6.4-01.jar",
        "1.6.2" => "https://dl.liteloader.com/redist/1.6.2/liteloader-installer-1.6.2-04.jar",
        "1.5.2" => "https://dl.liteloader.com/redist/1.5.2/liteloader-installer-1.5.2-01.jar",
        _ => throw new NotSupportedException("未知的 Minecraft 版本（" + inherit + "）")
    };

    /// <summary>
    /// LiteLoader 安装器下载源决策（McDownloadLiteLoaderSave :756-776 决策面）：
    /// 老版本（IsLegacy）走 <see cref="LegacyLiteLoaderRedist"/>（单源），其余走 jenkins 官方源
    /// （Inherit=1.8 走 ant/dist/，其余 build/libs/，:775）。
    /// </summary>
    public static IReadOnlyList<string> LiteLoaderInstallerSources(DlLiteLoaderListEntry entry)
        => entry.IsLegacy
            ? [LegacyLiteLoaderRedist(entry.Inherit!)]
            : [$"http://jenkins.liteloader.com/job/LiteLoaderInstaller%20{entry.Inherit}/lastSuccessfulBuild/artifact/" +
               $"{(entry.Inherit == "1.8" ? "ant/dist/" : "build/libs/")}{entry.FileName}"];

    /// <summary>
    /// LiteLoader 版本 Json 组装决策（McDownloadLiteLoaderLoader :801-815 决策面，键序逐字）：
    /// 从条目 Inherit/ReleaseTime/JsonToken 拼 profile（time/releaseTime 经 yyyy/MM/dd HH:mm 不变文化解析后，
    /// 以旧 Newtonsoft JValue(Date) 的 ISO 形态 yyyy-MM-ddTHH:mm:ss 承载），libraries 深拷贝自 JsonToken 并在末尾
    /// 附加 liteloader 库项（:811）。JsonToken 的 tweakClass/libraries/version 缺失时 NRE（与旧 .ToString 链一致）。
    /// 写盘（:817 FileUtils.Write，缩进布局以 MergeJson.WriteOptions 对齐旧 Newtonsoft ToString）与
    /// 异常包装（"安装新 LiteLoader 版本失败"）留宿主层。
    /// brief 计划稿的 (JsonObject liteLoaderToken, string targetName) 收敛为条目入参：VersionName 与 ReleaseTime
    /// 均取自条目（:801/:806-807），brief 的 targetName 即此处 VersionName 的派生值。
    /// </summary>
    public static LiteLoaderPlan BuildLiteLoaderProfile(DlLiteLoaderListEntry entry)
    {
        var versionName = entry.Inherit + "-LiteLoader";   //旧 :801
        var time = DateTime.ParseExact(entry.ReleaseTime!, "yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);   //旧 :806-807
        var versionJson = new JsonObject
        {
            ["id"] = versionName,   //旧 :805
            //? 旧 Newtonsoft JValue(Date) 的序列化形态（ISO、Kind=Unspecified 无时区）：以等价格式化保持文件输出一致
            ["time"] = time.ToString("yyyy-MM-ddTHH:mm:ss"),   //旧 :806
            ["releaseTime"] = time.ToString("yyyy-MM-ddTHH:mm:ss"),   //旧 :807
            ["type"] = "release",   //旧 :808
            //? JSON null 边界：旧 JsonToken("tweakClass") 缺失时 .ToString 链 NRE；插值的隐式转换为 null 容忍，须显式 ToString 保真
            //! 逐字移植豁免（Rider 冗余 ToString）：该调用承载缺失键的 NRE（旧 :809 真值），不可省略
            ["arguments"] = JsonNode.Parse($"{{\"game\":[\"--tweakClass\",\"{entry.JsonToken!["tweakClass"]!.ToString()}\"]}}"),   //旧 :809
            ["libraries"] = entry.JsonToken["libraries"]!.DeepClone(),   //旧 :810（ToJToken 为拷贝语义）
            ["mainClass"] = "net.minecraft.launchwrapper.Launch",   //旧 :812
            ["minimumLauncherVersion"] = 18,   //旧 :813
            ["inheritsFrom"] = entry.Inherit,   //旧 :814
            ["jar"] = entry.Inherit   //旧 :815
        };
        ((JsonArray) versionJson["libraries"]!).Add(new JsonObject   //旧 :811（缺失/非数组时抛出，由宿主层包装）
        {
            //? 旧 JsonToken("version") 缺失时 .ToString NRE；字符串拼接对 null 容忍，须显式 ToString 保真
            //! 逐字移植豁免（Rider 冗余 ToString）：该调用承载缺失键的 NRE（旧 :811 真值），不可省略
            ["name"] = "com.mumfrey:liteloader:" + entry.JsonToken["version"]!.ToString(),
            ["url"] = "https://dl.liteloader.com/versions/"
        });
        return new LiteLoaderPlan { VersionJson = versionJson, VersionName = versionName };
    }

    #endregion

    #region Fabric 安装决策（ModDownloadLib.vb:1505-1524 的决策面；Loader 链与保存入口留宿主层）

    /// <summary>
    /// Fabric profile 下载决策（McDownloadFabricLoader :1508-1519 决策面）：
    /// 版本 id = "fabric-loader-{版本}-{MC 名}"（:1508，版本文件夹与 Json 文件名同源）；
    /// MC 名的 ∞ → infinite 替换仅用于下载地址（:1513 注释：放在 ID 后面避免影响版本文件夹名称）；
    /// 源为 BMCLAPI 在前、meta.fabricmc.net 在后（:1516-1519）；校验器 IsJson。
    /// VersionJsonPath = {mcFolder}versions\{id}\{id}.json（:1509/:1519）。mcFolder 以 \ 结尾。
    /// brief 计划稿的 (profileJson → VersionJson/LibraryFolder) 无 VB 真值对应：旧代码对下载的 profile json
    /// 零加工直接落盘（:1521），亦无独立 LibraryFolder 决策，收敛为下载源/路径/校验器决策。
    /// </summary>
    public static FabricPlan BuildFabricPlan(string fabricVersion, string minecraftName, string mcFolder)
    {
        var id = "fabric-loader-" + fabricVersion + "-" + minecraftName;   //旧 :1508
        var queryName = minecraftName.Replace("∞", "infinite");   //旧 :1513（仅用于地址）
        return new FabricPlan
        {
            InstanceId = id,
            VersionJsonPath = mcFolder + @"versions\" + id + @"\" + id + ".json",   //旧 :1509/:1519
            Sources =
            [
                $"https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{queryName}/{fabricVersion}/profile/json",   //旧 :1517
                $"https://meta.fabricmc.net/v2/versions/loader/{queryName}/{fabricVersion}/profile/json"   //旧 :1518
            ],
            Checker = new FileChecker { IsJson = true }   //旧 :1519
        };
    }

    #endregion

    #region 内部实现

    /// <summary>读取安装器条目为 JSON 对象（旧 Installer.GetEntry(...).Open().ReadString().DeserializeJson()）：
    /// 条目缺失时 NRE、根非对象/非法 JSON 时抛出，均由宿主层包装。</summary>
    private static JsonObject ReadEntryObject(ZipArchive installer, string entryName)
    {
        using var stream = installer.GetEntry(entryName)!.Open();
        using var reader = new StreamReader(stream);
        return (JsonObject) JsonNode.Parse(reader.ReadToEnd())!;   //根为 "null" 字面量时为 null，由后续取值处 NRE（对照旧 CTYPE 链）
    }

    /// <summary>
    /// LibEntry → NetFile 映射（ModMinecraft.vb:1279-1332 McLibNetFilesFromTokens 的决策面，以 W1 LibEntry 承载）。
    /// 检查器（LabyMod 不校验 #3225 / 实际大小与 SHA1）、#5455 的 forge universal 补 URL、Mappings 的
    /// LauncherOrMeta 源（#4425）、BMCLAPI 镜像排序与普通文件的 Library 回退源逐字。
    /// Transformer 释放与 OptiFine 主 Jar 两个特判分支对安装器支持库不可达（forge 安装器不引用二者；Transformer
    /// 释放依赖宿主层 ExtractResources），未迁入——命中时按普通文件取源。
    /// </summary>
    private static List<NetFile> NetFilesFromLibEntries(List<LibEntry> libs, string customMcFolder, bool preferMojang)
    {
        var result = new List<NetFile>();
        foreach (var token in libs)
        {
            //检查器（:1284-1290）
            FileChecker checker;
            if (token.ShortName()!.ContainsIgnoreCase("labymod"))
            {
                checker = new FileChecker();   //不检查 LabyMod 的文件，它们提供的文件校验信息是错的（#3225）
            } else
            {
                var size = (long) Libraries.Val(token.Size);   //旧 Token.Size 为 Long：Size=0 → 不检查大小（ActualSize=-1）
                checker = new FileChecker { ActualSize = size == 0 ? -1 : size, Hash = token.Checksum };
            }
            //URL（:1292-1311）
            var urls = new List<string>();
            if (token.Url is null && token.ShortName() == "net.minecraftforge:forge:universal")   //特判修复 Forge 部分 universal 文件缺失 URL（#5455）
            {
                token.Url = "https://maven.minecraftforge.net" + token.Path!.Replace(customMcFolder + "libraries", "").Replace("\\", "/");
            }
            if (token.Url is not null)
            {
                //获取 URL 的真实地址
                urls.Add(token.Url);
                if (token.Url.Contains("launcher.mojang.com/v1/objects") || token.Url.Contains("client.txt") || token.Url.Contains(".tsrg"))
                {
                    urls.AddRange(DownloadSources.LauncherOrMeta(token.Url, preferMojang));   //Mappings（#4425）
                }
                if (token.Url.Contains("maven"))
                {
                    var bmclapiUrl = token.Url
                        .Replace(token.Url.Substring(0, token.Url.IndexOfF("maven")), "https://bmclapi2.bangbang93.com/")
                        .Replace("maven.fabricmc.net", "maven")
                        .Replace("maven.minecraftforge.net", "maven")
                        .Replace("maven.neoforged.net/releases", "maven");
                    if (preferMojang)
                    {
                        urls.Add(bmclapiUrl);   //官方源优先
                    } else
                    {
                        urls.Insert(0, bmclapiUrl);   //镜像源优先
                    }
                }
            }
            if (urls.Count <= 2)
            {
                //普通文件（:1324-1326；Transformer/OptiFine 特判对安装器支持库不可达，见方法注释）
                urls.AddRange(DownloadSources.Library(
                    "https://libraries.minecraft.net" + token.Path!.Replace(customMcFolder + "libraries", "").Replace("\\", "/"),
                    preferMojang));
            }
            result.Add(new NetFile(urls.Distinct(), token.Path!, checker));   //旧 :1328
        }
        //去重并返回（:1331；MeloongCore.DistinctBy 的首现序语义，以 GroupBy 等价承载——net48/net8 的 DistinctBy 可用性不同）
        return result.GroupBy(static f => f.LocalPath).Select(static g => g.First()).ToList();
    }

    #endregion
}

/// <summary>
/// Forge/NeoForge 安装计划（ModDownloadLib.vb 新版/Legacy 安装链决策面的产出；brief 计划稿三字段 + 决策面真实消费的库清单）。
/// </summary>
public sealed class ForgelikePlan
{
    /// <summary>待下载的文件（经 LibEntry→NetFile 映射；Legacy 计划为空）。</summary>
    public List<NetFile> Files { get; init; } = [];

    /// <summary>合并后的版本 Json（新版：install_profile.json + version.json 合并产物；Legacy：写入版本文件夹的 Json）。</summary>
    public JsonObject MergedVersionJson { get; init; } = new();

    /// <summary>等待原版下载完成的子加载器匹配名（旧 :1206-1208 的 McDownloadClientLibName/JsonName 常量；Legacy 计划为空）。</summary>
    public List<string> WaitVanillaLoaderNames { get; init; } = [];

    /// <summary>解析出的支持库清单（含 MOJMAPS 追加项与 Forgelike 原始项移除，未按 LocalPath 去重，与旧 Libs 变量一致）；
    /// 宿主层复制到目标 MC 文件夹时消费（:1197-1203）。</summary>
    public List<LibEntry> Libraries { get; init; } = [];
}

/// <summary>
/// LiteLoader 安装计划（ModDownloadLib.vb:801-815 版本 Json 组装决策面的产出）。
/// </summary>
public sealed class LiteLoaderPlan
{
    /// <summary>组装完成的版本 Json（键序逐字；写盘留宿主层，:817）。</summary>
    public JsonObject VersionJson { get; init; } = new();

    /// <summary>版本名（Inherit &amp; "-LiteLoader"，旧 :801；宿主层据此定位版本文件夹与 Json 落盘路径，:802）。</summary>
    public string VersionName { get; init; } = "";
}

/// <summary>
/// Fabric 安装计划（ModDownloadLib.vb:1508-1519 profile 下载决策面的产出）。
/// brief 计划稿的 (VersionJson, LibraryFolder) 收敛：旧代码对下载的 profile json 零加工直接落盘（:1521），
/// 无独立 LibraryFolder 决策，故承载下载源/落盘路径/校验器。
/// </summary>
public sealed class FabricPlan
{
    /// <summary>版本 id / 版本文件夹名（"fabric-loader-{版本}-{MC 名}"，旧 :1508）。</summary>
    public string InstanceId { get; init; } = "";

    /// <summary>profile Json 落盘路径（{mcFolder}versions\{id}\{id}.json，旧 :1509/:1519）。</summary>
    public string VersionJsonPath { get; init; } = "";

    /// <summary>候选下载源（BMCLAPI 在前，官方其后，旧 :1517-1518）。</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>下载校验器（IsJson，旧 :1519）。</summary>
    public FileChecker Checker { get; init; } = new();
}