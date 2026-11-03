using PCLCS.Startup;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCLCS.Launch;

/// <summary>
/// 启动文件核（自宿主层 ModLaunch.vb McLaunchNatives/GetNativesFolder/McLaunchPrerun 合并段 移植）。
/// </summary>
public static class LaunchFiles
{

    /// <summary>
    /// Natives 解压核（McLaunchNatives，ModLaunch.vb:1332-1393 逐行）：
    /// 打开 zip（损坏则删除源文件并抛出）、仅解压 dll、长度一致跳过、原 dll 删除被拒则跳过当前 zip（Exit For）、
    /// 最后删除多余文件（删除被拒则终止清理）。
    /// </summary>
    /// <param name="nativesFolder">Natives 目标文件夹（原 GetNativesFolder() 结果，以 \ 结尾）。</param>
    /// <param name="libraryPaths">Natives 库文件路径列表（原 McLibListGet(Instance, True) 中 IsNatives 项的 LocalPath）。</param>
    /// <param name="progress">进度回调。旧 McLaunchNatives 无进度上报（进度由 LoaderTask 权重计算，:160），故保持不调用。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    /// <param name="debug">调试模式（原 ModeDebug，仅影响“无需解压”日志）。</param>
    public static void ExtractNatives(string nativesFolder, List<string> libraryPaths, Action<double>? progress, Action<string>? log = null, bool debug = false)
    {

        //创建文件夹
        DirectoryUtils.Create(nativesFolder);

        //解压文件
        log?.Invoke("正在解压 Natives 文件");
        var existFiles = new List<string>();
        foreach (var libraryPath in libraryPaths)
        {
            ZipArchive zip;
            try
            {
                zip = FileUtils.OpenZip(libraryPath);
            } catch (InvalidDataException ex)
            {
                Logger.Warn(ex, $"打开 Natives 文件失败（{libraryPath}）");
                FileUtils.Delete(libraryPath);
                throw new Exception($"无法打开 Natives 文件（{libraryPath}），该文件可能已损坏，请重新尝试启动游戏");
            }
            foreach (var entry in zip.Entries)
            {
                var fileName = entry.FullName;
                if (PathUtils.GetExtension(fileName) != "dll") continue;
                //实际解压文件的步骤
                var filePath = nativesFolder + fileName;
                existFiles.Add(filePath);
                var originalFile = FileUtils.GetInfo(filePath);
                if (originalFile.Exists)
                {
                    if (originalFile.Length == entry.Length)
                    {
                        if (debug) log?.Invoke("无需解压：" + filePath);
                        continue;
                    }
                    //删除原文件
                    try
                    {
                        FileUtils.Delete(filePath);
                    } catch (UnauthorizedAccessException ex)
                    {
                        log?.Invoke("删除原 dll 访问被拒绝，这通常代表有一个 MC 正在运行，跳过解压：" + filePath);
                        log?.Invoke("实际的错误信息：" + ex.GetDisplay(true));
                        break;
                    }
                }
                //解压新文件
                FileUtils.Write(filePath, entry.Open());
                log?.Invoke("已解压：" + filePath);
            }
            zip?.Dispose();
        }

        //删除多余文件
        foreach (var fileName in DirectoryUtils.EnumerateFiles(nativesFolder))
        {
            if (existFiles.Contains(fileName)) continue;
            try
            {
                log?.Invoke("删除：" + fileName);
                FileUtils.Delete(fileName);
            } catch (UnauthorizedAccessException ex)
            {
                log?.Invoke("删除多余文件访问被拒绝，跳过删除步骤");
                log?.Invoke("实际的错误信息：" + ex.GetDisplay(true));
                return;
            }
        }

    }

    /// <summary>
    /// 获取 Natives 文件夹路径，以 \ 结尾。（原 GetNativesFolder，ModLaunch.vb:1397-1408，逐字。）
    /// 回退链：GBK/纯 ASCII → 版本文件夹；否则 AppData；AppData 仍含非 ASCII → ProgramData。
    /// </summary>
    /// <param name="versionFolder">版本文件夹（原 Instance.PathVersion）。</param>
    /// <param name="instanceName">实例名（原 Instance.Name）。</param>
    /// <param name="isGbkEncoding">系统是否 GBK（原 IsGBKEncoding，宿主层 ModBase.vb:70）。</param>
    /// <param name="osDrive">系统盘（原 OsDrive，宿主层 ModBase.vb:74；null 时按 #3799 派生）。</param>
    /// <param name="appData">AppData 路径（原 Paths.AppData；null 时取真实值，测试面注入）。</param>
    public static string GetNativesFolder(string versionFolder, string instanceName, bool isGbkEncoding = false, string? osDrive = null, string? appData = null)
    {
        var result = PathUtils.AddSlashSuffix(PathUtils.ToShortPath(versionFolder)) + instanceName + "-natives\\";
        if (!(isGbkEncoding || result.IsAsciiOnly()))
        {
            result = (appData ?? Paths.AppData) + ".minecraft\\bin\\natives\\";
            if (!result.IsAsciiOnly())
            {
                result = (osDrive ?? DefaultOsDrive()) + "ProgramData\\" + LauncherIdentity.FolderName + "\\natives\\";
            }
        }
        DirectoryUtils.Create(result); //提前创建，这样 DirectoryUtils.Shorten 才有结果（否则在长路径下首次启动 Forge 会崩溃）
        return PathUtils.AddSlashSuffix(PathUtils.ToShortPath(result));
    }
    /// <summary>系统盘（原宿主层 OsDrive 模块状态，ModBase.vb:74，#3799）。</summary>
    private static string DefaultOsDrive() => _osDrive.Value;
    private static readonly Lazy<string> _osDrive = new(() => Environment.GetLogicalDrives().First(p => Directory.Exists(p)));

    /// <summary>
    /// launcher_profiles.json 合并核（原 McLaunchPrerun 更新段，ModLaunch.vb:1439-1502 决策面，失败容忍语义逐字）：
    /// 首次失败 → 记警告 → 删除文件并重建后重试 → 仍失败仅记错误日志（不向上抛）。
    /// </summary>
    /// <param name="dotMinecraftFolder">MC 文件夹（原 McFolderSelected）。</param>
    /// <param name="playerName">玩家名（原 McLoginLoader.Output.Name）。</param>
    /// <param name="clientToken">客户端令牌（原 McLoginLoader.Output.ClientToken，:1457 写入 clientToken 字段）。</param>
    /// <param name="createProfilesJson">确保 launcher_profiles.json 可用（原 McFolderLauncherProfilesJsonCreate，
    /// ModMinecraft.vb:152——文件已存在时由回调自身短路；UI/日志依赖留宿主层）。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    public static void MergeLauncherProfiles(string dotMinecraftFolder, string playerName, string clientToken, Action<string> createProfilesJson, Action<string>? log = null)
    {
        var filePath = dotMinecraftFolder + "launcher_profiles.json";
        try
        {
            //确保可用
            createProfilesJson(dotMinecraftFolder);
            MergeLauncherProfilesCore(filePath, BuildReplaceJson(playerName, clientToken), log, "已更新 launcher_profiles.json");
        } catch (Exception ex)
        {
            Logger.Warn(ex, "更新 launcher_profiles.json 失败，将在删除文件后重试");
            try
            {
                FileUtils.Delete(filePath);
                createProfilesJson(dotMinecraftFolder);
                MergeLauncherProfilesCore(filePath, BuildReplaceJson(playerName, clientToken), log, "已在删除后更新 launcher_profiles.json");
            } catch (Exception innerEx)
            {
                Logger.Error(innerEx, "更新 launcher_profiles.json 失败");
            }
        }
    }

    /// <summary>单次“读取现有 → 合并 → GB18030 写回”（:1463-1468，两分支同构抽取）。</summary>
    private static void MergeLauncherProfilesCore(string filePath, JsonObject replaceJson, Action<string>? log, string successLog)
    {
        //更新文件
        //? 读取经 FileUtils.ReadAsJson 宽松解析（DocumentOptions：跳过注释、允许尾逗号，与旧 Newtonsoft 一致）——
        //? 严格 Parse 会把带注释/尾逗号的用户文件当损坏清掉重建（数据丢失形偏差，评审修正项）
        //? 与旧 :1465 的 JObject 直转一致：文件缺失/纯空白（解析为 null）/根非对象时在此抛出，交由失败容忍链处理
        var profiles = FileUtils.ReadAsJson(filePath) as JsonObject
            ?? throw new InvalidCastException($"launcher_profiles.json 的根不是 JSON 对象（{filePath}）");
        MergeJson(profiles, replaceJson); //与 JObject.Merge 默认语义对齐（见 MergeJson 注）
        //? net8 差异（随 Task 1 编码注）：net8 未注册 CodePagesEncodingProvider 时 GetEncoding("GB18030") 抛出；
        //? 运行目标为 net48，net8 仅 AOT 冒烟
        FileUtils.Write(filePath, profiles.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.GetEncoding("GB18030"));
        log?.Invoke(successLog);
    }

    /// <summary>构建需要替换的 Json 对象（:1445-1462，固定键值；username 中的引号替换为 -）。</summary>
    private static JsonObject BuildReplaceJson(string playerName, string clientToken) => new()
    {
        ["authenticationDatabase"] = new JsonObject
        {
            ["00000111112222233333444445555566"] = new JsonObject
            {
                ["username"] = playerName.Replace("\"", "-"),
                ["profiles"] = new JsonObject
                {
                    ["66666555554444433333222221111100"] = new JsonObject
                    {
                        ["displayName"] = playerName,
                    }
                }
            }
        },
        ["clientToken"] = clientToken,
        ["selectedUser"] = new JsonObject
        {
            ["account"] = "00000111112222233333444445555566",
            ["profile"] = "66666555554444433333222221111100",
        }
    };

    /// <summary>
    /// 递归对象合并（//! 逐字移植豁免：原 JObject.Merge 默认语义——对象递归合并、数组拼接（Concat）、
    /// 其余值覆盖、内容侧 null 跳过（NullValueHandling.Ignore）。替换对象（BuildReplaceJson）不含数组与 null，
    /// 数组/null 分支仅为语义兜底。）
    /// </summary>
    private static void MergeJson(JsonObject target, JsonObject content)
    {
        //? net48 的 System.Text.Json 无 KeyValuePair Deconstruct，使用显式键值迭代
        foreach (var pair in content)
        {
            var key = pair.Key;
            var value = pair.Value;
            if (value is null) continue; //JsonMergeSettings.NullValueHandling 默认 Ignore
            if (target[key] is JsonObject targetObject && value is JsonObject contentObject)
            {
                MergeJson(targetObject, contentObject); //对象递归合并
            } else if (target[key] is JsonArray targetArray && value is JsonArray contentArray)
            {
                foreach (var item in contentArray) targetArray.Add(item?.DeepClone()); //MergeArrayHandling.Concat
            } else
            {
                target[key] = value.DeepClone(); //其余值覆盖
            }
        }
    }

}