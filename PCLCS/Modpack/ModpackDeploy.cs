using PCLCS.Download;
using PCLCS.Net;
using PCLCS.Settings;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PCLCS.Modpack;

/// <summary>
/// CF modules 分类后的资源种类（ModModpack.vb:281-298 的分类结果；
/// 宿主层 Resource 模块未迁，ResourceTypes 的映射由宿主层按种类一对一承担）。
/// </summary>
public enum ModpackResourceKind
{
    /// <summary>mods 文件夹（ResourceTypes.Mod）。</summary>
    Mod,
    /// <summary>resourcepacks 文件夹（ResourceTypes.ResourcePack）。</summary>
    ResourcePack,
    /// <summary>shaderpacks 文件夹（ResourceTypes.Shader）。</summary>
    Shader,
    /// <summary>saves 文件夹（ResourceTypes.Map）。</summary>
    Map
}

/// <summary>
/// CurseForge 部署核的决策产出（ModModpack.vb:162-363 决策面）。
/// </summary>
/// <param name="MinecraftVersion">manifest.minecraft.version（:174 缺失即拒绝）。</param>
/// <param name="ModLoaderVersions">modLoaders 的 forge-/neoforge-/fabric- 前缀映射（:188-206）。</param>
/// <param name="OverridesHome">manifest.overrides（:210，缺失与 JSON null 均为空串）。</param>
/// <param name="FileIds">files 的 fileID 列表（:224-231，跳过缺 projectID/fileID 的条目）。</param>
/// <param name="OptionalFileIds">required=false 的 fileID 列表（:230）。</param>
/// <param name="FileProjectIds">fileID → projectID 映射（:245 缺失项映射的数据面）。</param>
/// <param name="VersionFolder">安装目标版本文件夹（宿主层的 instanceKey，供 NetFile 目标路径计算）。</param>
public sealed record CurseForgePlan(
    string MinecraftVersion,
    Dictionary<ModLoaders, string> ModLoaderVersions,
    string OverridesHome,
    IReadOnlyList<int> FileIds,
    IReadOnlyList<int> OptionalFileIds,
    IReadOnlyDictionary<int, int> FileProjectIds,
    string VersionFolder);

/// <summary>
/// Modrinth 部署核的决策产出（ModModpack.vb:366-491 决策面）。
/// </summary>
/// <param name="MinecraftVersion">dependencies.minecraft（:377 缺失即拒绝）。</param>
/// <param name="ModLoaderVersions">dependencies 的 forge/neoforge（含 neo-forge）/fabric-loader 映射（:381-397）。</param>
/// <param name="SuggestedName">index.name（:400，版本名对话框的预填建议）。</param>
/// <param name="VersionFolder">版本文件夹（resolveVersionFolder 回调产物或 instanceKey 兜底）。</param>
/// <param name="Files">经 env 过滤与路径校验后的下载文件（:417-444）。</param>
public sealed record ModrinthPlan(
    string MinecraftVersion,
    Dictionary<ModLoaders, string> ModLoaderVersions,
    string SuggestedName,
    string VersionFolder,
    List<NetFile> Files);

/// <summary>
/// MCBBS 部署核的决策产出（ModModpack.vb:677-761 决策面）。
/// </summary>
/// <param name="MinecraftVersion">addons.game（:719 缺失即拒绝）。</param>
/// <param name="ModLoaderVersions">addons 的 forge/neoforge/fabric 映射（:724-727）。</param>
/// <param name="OptiFineEntry">OptiFine 条目（addons.optifine 命中列表时的字段原样序列化，:738）；未要求时为 null。</param>
/// <param name="LaunchJvmArguments">launchInfo.javaArgument 的空格串接（:709）；写入时机与目标留宿主层。</param>
/// <param name="LaunchGameArguments">launchInfo.launchArgument 的空格串接（:710）。</param>
public sealed record McbbsPlan(
    string MinecraftVersion,
    Dictionary<ModLoaders, string> ModLoaderVersions,
    JsonObject? OptiFineEntry,
    string? LaunchJvmArguments,
    string? LaunchGameArguments);

/// <summary>HMCL 部署核的决策产出（ModModpack.vb:496-521）。</summary>
/// <param name="SuggestedName">modpack.json.name（:505，版本名对话框的预填建议）。</param>
/// <param name="GameVersion">modpack.json.gameVersion（:520 缺失即拒绝）。</param>
public sealed record HmclPlan(string SuggestedName, string GameVersion);

/// <summary>CF 批量文件信息请求的组装产出（ModModpack.vb:237）。</summary>
/// <param name="Url">请求地址。</param>
/// <param name="Method">请求方法（POST）。</param>
/// <param name="Content">请求体（fileIds 数组）。</param>
/// <param name="ContentType">请求体类型（application/json）。</param>
public sealed record ModBatchRequest(string Url, HttpMethod Method, string Content, string ContentType);

/// <summary>
/// 整合包部署核（ModModpack.vb 的决策段迁入）：
/// 覆写目录复制与版本级设置写入（:124-151）、CurseForge/Modrinth/MMC/MCBBS/HMCL 各自的清单解析与下载组装决策
/// （:162-363/:366-491/:631-650/:677-761/:496-521）。LoaderCombo 树装配、对话框（版本名输入、缺失项、可选 Mod、
/// 不兼容加载器——经回调注入）、ResourceVersion（Resource 模块未迁）的 NetFile 构造经回调由宿主层承担，
/// 与 InstallPlans 的 FabricApi/OptiFabric 回调同模式。
/// </summary>
public static class ModpackDeploy
{
    //* 与 Newtonsoft JObject 的索引语义对齐：属性查询区分大小写（Ordinal），容注释与尾逗号（DeserializeJson 默认行为）
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    #region 覆写部署（ModModpack.vb:124-151）

    /// <summary>
    /// 从整合包的 override 目录复制文件，同时设置 PCL 的配置文件与版本隔离（:124-151 逐段）。
    /// 对路径末尾是否为 \ 没有要求。
    /// </summary>
    /// <param name="extractedRoot">解压目录中的覆写文件夹（overrides / minecraft 等，由宿主层按整合包种类选定）。</param>
    /// <param name="targetRoot">安装目标版本文件夹。</param>
    /// <param name="instanceKey">实例设置的存储键（W1 face 的版本文件夹；旧 VersionIni 写入的 instanceKey 面路由）。</param>
    public static void CopyOverrideDirectory(string extractedRoot, string targetRoot, string instanceKey)
    {
        extractedRoot = PathUtils.AddSlashSuffix(extractedRoot);
        targetRoot = PathUtils.AddSlashSuffix(targetRoot);
        //复制文件（:131-137）
        if (DirectoryUtils.Exists(extractedRoot))
        {
            Logger.Info($"处理整合包覆写文件夹：{extractedRoot} → {targetRoot}");
            DirectoryUtils.Copy(extractedRoot, targetRoot);
        } else
        {
            Logger.Info($"整合包中没有覆写文件夹：{extractedRoot}");
        }
        //设置 ini（:138-149）：VersionArgumentIndie/V2 为设置目录键，经 SettingsService instanceKey 面（W1 facade）路由；
        //IsStar 非目录键（W1 wrapper 自 PCL\Setup.ini 实体读取），与旧一样落实体文件
        var overridesIni = $"{extractedRoot}PCL\\Setup.ini";
        var versionIni = $"{targetRoot}PCL\\Setup.ini";
        if (FileUtils.Exists(overridesIni))
        {
            //旧：三键写入覆写夹的 ini 实体文件，再整体覆写版本的 ini（:142-145）
            EnsureIniKey(overridesIni, "VersionArgumentIndie", "1"); //开启版本隔离
            EnsureIniKey(overridesIni, "VersionArgumentIndieV2", "True");
            EnsureIniKey(overridesIni, "IsStar", "False");
            FileUtils.Copy(overridesIni, versionIni); //覆写已有的 ini（:145）
        } else
        {
            EnsureIniKey(versionIni, "VersionArgumentIndie", "1"); //开启版本隔离（:147）
            EnsureIniKey(versionIni, "VersionArgumentIndieV2", "True"); //:148
        }
        SettingsService.Set("VersionArgumentIndie", 1, instanceKey); //:142 的 instanceKey 面路由
        SettingsService.Set("VersionArgumentIndieV2", true, instanceKey); //:143
        SettingsService.ResetInstanceCache(instanceKey); //:150 IniClearCache 等价：重置缓存，避免被安装过程中写入的 ini 覆盖
    }

    /// <summary>
    /// ModBase ini 的最小写入面（Key:Value 行、CRLF）：同键行去重后追加；无冒号行随旧整文件重写一并丢弃。
    /// 写入失败与旧 WriteIni 一致仅记录不外抛（:561-563）。
    /// </summary>
    private static void EnsureIniKey(string iniPath, string key, string value)
    {
        try
        {
            var lines = FileUtils.Exists(iniPath)
                ? File.ReadAllLines(iniPath)
                    .Where(static line => line.IndexOf(':') > 0)
                    .Where(line => line[..line.IndexOf(':')] != key)
                    .ToList()
                : [];
            lines.Add(key + ":" + value);
            FileUtils.Write(iniPath, string.Join("\r\n", lines) + "\r\n");
        } catch (Exception ex)
        {
            Logger.Error(ex, $"写入文件失败（{iniPath} → {key}:{value}）", LogBehavior.Toast);
        }
    }

    #endregion

    #region CurseForge 部署核（ModModpack.vb:162-363 决策面）

    /// <summary>
    /// CurseForge manifest 的决策解析（:167-231）：minecraft.version 必需（:174）、modLoaders 前缀映射
    /// （:188-206，forge 推荐版拒绝 ：192，不兼容项经回调）、files → FileIds/OptionalFileIds/FileProjectIds
    /// （:224-231，缺字段条目经 onFileSkipped 提示后跳过）、overrides（:210）。
    /// </summary>
    /// <param name="manifestJson">manifest.json 全文。</param>
    /// <param name="instanceKey">安装目标版本文件夹（宿主层在版本名决策后传入）。</param>
    /// <param name="notifyIncompatibleLoader">不兼容加载器回调（旧 NotifyIncompatibleLoader，取消时抛 OperationCanceledException）。</param>
    /// <param name="onFileSkipped">缺字段条目的提示回调（旧 Hint，:226）。</param>
    public static CurseForgePlan BuildCurseForgePlan(string manifestJson, string instanceKey,
        Action<string>? notifyIncompatibleLoader = null, Action<string>? onFileSkipped = null)
    {
        var json = ParsePackObject(manifestJson, "CurseForge 整合包安装信息存在问题");
        var minecraft = JGet(json, "minecraft");
        var versionNode = JGet(minecraft, "version");
        if (versionNode is null) throw new Exception("CurseForge 整合包未提供 Minecraft 版本信息"); //:174
        var modLoaderVersions = new Dictionary<ModLoaders, string>();
        foreach (var loaderNode in ChildrenOf(JGet(minecraft, "modLoaders")))
        {
            var id = (JGet(loaderNode, "id")?.ToString() ?? "").Lower(); //:189
            if (id.StartsWithF("forge-"))
            {
                //Forge 指定（:190-194）
                if (id.Contains("recommended")) throw new Exception("该整合包版本过老，已不支持进行安装！"); //:192
                Logger.Info($"整合包 Forge 版本：{id}");
                modLoaderVersions[ModLoaders.Forge] = id.Replace("forge-", "");
            } else if (id.StartsWithF("neoforge-"))
            {
                //NeoForge 指定（:195-198）
                Logger.Info($"整合包 NeoForge 版本：{id}");
                modLoaderVersions[ModLoaders.NeoForge] = id.Replace("neoforge-", "");
            } else if (id.StartsWithF("fabric-"))
            {
                //Fabric 指定（:199-202）
                Logger.Info($"整合包 Fabric 版本：{id}");
                modLoaderVersions[ModLoaders.Fabric] = id.Replace("fabric-", "");
            } else
            {
                notifyIncompatibleLoader?.Invoke(id); //:204
            }
        }
        var fileIds = new List<int>();
        var optionalFileIds = new List<int>();
        var fileProjectIds = new Dictionary<int, int>();
        foreach (var fileNode in ChildrenOf(JGet(json, "files")))
        {
            var projectNode = JGet(fileNode, "projectID");
            var fileIdNode = JGet(fileNode, "fileID");
            if (projectNode is null || fileIdNode is null)
            {
                onFileSkipped?.Invoke("某项 Mod 缺少必要信息，已跳过：" + fileNode!.ToJsonString()); //:226-227
                continue;
            }
            var fileId = ToInteger(fileIdNode);
            fileIds.Add(fileId); //:229
            fileProjectIds[fileId] = ToInteger(projectNode); //:245 缺失项映射的数据面
            var requiredNode = JGet(fileNode, "required");
            if (requiredNode is not null && !ToBoolean(requiredNode)) optionalFileIds.Add(fileId); //:230
        }
        return new CurseForgePlan(versionNode.ToString(), modLoaderVersions,
            JGet(json, "overrides")?.ToString() ?? "", //:210（缺失与 JSON null 均为空串）
            fileIds, optionalFileIds, fileProjectIds, instanceKey);
    }

    /// <summary>
    /// CF 批量文件信息请求的组装（:237）：URL、方法、fileIds 请求体与内容类型。
    /// 请求执行（DlModRequest）与响应解析留宿主层。
    /// </summary>
    public static ModBatchRequest BuildCurseForgeBatchRequest(IReadOnlyList<int> fileIds)
        => new("https://api.curseforge.com/v1/mods/files", HttpMethod.Post,
            "{\"fileIds\": [" + string.Join(",", fileIds) + "]}", "application/json");

    /// <summary>响应中缺失的 fileIds（:241：ModList.Except(返回项的 id)）。</summary>
    public static List<int> FindMissingCurseForgeFileIds(IReadOnlyList<int> requestedIds, JsonArray returnedFiles)
        => requestedIds.Except(returnedFiles.Select(static file => ToInteger(JGet(file, "id")!))).ToList();

    /// <summary>缺失 fileIds → (projectId, fileId) 映射（:244-245；清单外 id 与旧 First 一样抛错，此处为键查找）。</summary>
    public static List<(int ProjectId, int FileId)> MapMissingCurseForgeFiles(CurseForgePlan plan, IEnumerable<int> missingFileIds)
        => missingFileIds.Select(fileId => (plan.FileProjectIds[fileId], fileId)).ToList();

    /// <summary>
    /// 解压后覆写文件夹的定位与逃逸校验（#5613，:212-213）：overrides 为 "." 或 "./" 时折叠为基础文件夹，
    /// 目标超出解压目录（InstallTemp 非父级）即拒绝。
    /// </summary>
    public static string ResolveCurseForgeOverridesFolder(string installTemp, string archiveBaseFolder, string overridesHome)
    {
        var overridesFolder = installTemp + archiveBaseFolder + (overridesHome is "." or "./" ? "" : overridesHome);
        if (!PathUtils.IsParentOf(installTemp, overridesFolder))
            throw new Exception("整合包的 overrides 路径超出了解压目录，请向整合包作者反馈此问题！"); //:213
        return overridesFolder;
    }

    /// <summary>
    /// modules + 文件名后缀 → 资源种类与目标文件夹（:281-298）。
    /// modules 键缺失时抛 ArgumentNullException（旧 Enumerable.Any(Nothing) 的崩溃路径，逐字保留）；
    /// 显式 JSON null（#1006）与空数组按无子项处理（→ mods）。
    /// </summary>
    public static (string TargetFolder, ModpackResourceKind Kind) ClassifyCurseForgeModules(JsonObject modJson)
    {
        if (!modJson.ContainsKey("modules")) throw new ArgumentNullException("source"); //:284
        var modules = modJson["modules"];
        var hasModules = modules switch
        {
            JsonArray array => array.Count != 0,
            JsonObject moduleObject => moduleObject.Count != 0, //JObject 子项枚举同构
            _ => false //JValue 等标量无子项
        };
        if (!hasModules) return ("mods", ModpackResourceKind.Mod); //:296-297
        var moduleNames = ((JsonArray) modules!).Select(static l => JGet(l, "name")!.ToString()).ToList(); //:285（缺失 name 在旧代码同处 NRE）
        if (moduleNames.Contains("META-INF") || moduleNames.Contains("mcmod.info") || moduleNames.Contains("fabric.mod.json") ||
            JGet(modJson, "FileName")?.ToString().EndsWithF(".jar", true) == true) //:286-287（键名序数敏感，与旧一致）
        {
            return ("mods", ModpackResourceKind.Mod);
        }
        if (moduleNames.Contains("pack.mcmeta")) return ("resourcepacks", ModpackResourceKind.ResourcePack); //:289-290
        if (moduleNames.Contains("shaders")) return ("shaderpacks", ModpackResourceKind.Shader); //:291-292
        return ("saves", ModpackResourceKind.Map); //:293-294
    }

    /// <summary>
    /// 批量文件信息 → NetFile 的组装决策（:269-308）：重复 id 跳过（:274-275）、可选文件提示（:277-280，经回调）、
    /// modules 分类（:281-296）、NetFile 构造经宿主回调（ResourceVersion.FromPlatformJson/ToNetFile；null = DownloadAvailable=false 跳过 ：301）。
    /// </summary>
    /// <param name="apiFiles">DlModRequest 响应 data 数组。</param>
    /// <param name="plan">BuildCurseForgePlan 的产出。</param>
    /// <param name="optionalPrompt">可选文件提示回调（入参为 displayName，返回 false = 不下载）。</param>
    /// <param name="netFileProvider">NetFile 构造回调（入参：文件 Json、目标文件夹、资源种类）。</param>
    /// <param name="onFileAdded">每项添加后的进度回调（:305 Task.Progress += 的宿主接线点）。</param>
    public static List<NetFile> AssembleCurseForgeFiles(JsonArray apiFiles, CurseForgePlan plan,
        Func<string, bool>? optionalPrompt, Func<JsonObject, string, ModpackResourceKind, NetFile?> netFileProvider,
        Action? onFileAdded = null)
    {
        var files = new Dictionary<int, NetFile>();
        foreach (var modJsonNode in apiFiles)
        {
            var modJson = (JsonObject) modJsonNode!; //旧遍历的 JObject 强转：非对象项同处抛错
            var id = ToInteger(JGet(modJson, "id")!); //:273（缺失 id 在旧代码同处 NRE）
            if (files.ContainsKey(id)) continue; //跳过重复的 Mod（疑似 CurseForge Bug，:274-275）
            //可选 Mod 提示（:277-280）
            if (plan.OptionalFileIds.Contains(id) &&
                optionalPrompt?.Invoke(JGet(modJson, "displayName")!.ToString()) == false) continue;
            //根据 modules 和文件名后缀判断资源类型（:281-296）
            var (targetFolder, kind) = ClassifyCurseForgeModules(modJson);
            var file = netFileProvider(modJson, targetFolder, kind); //:300-301
            if (file is null) continue; //DownloadAvailable=false → 跳过（:301）
            files.Add(id, file); //:303
            onFileAdded?.Invoke(); //:305
        }
        return files.Values.ToList(); //:307
    }

    #endregion

    #region Modrinth 部署核（ModModpack.vb:366-491 决策面）

    /// <summary>
    /// Modrinth index 的决策解析与下载组装（:366-491）：dependencies 必需 minecraft（:377）、依赖映射与不兼容回调
    /// （:380-397）、版本名决策回调（:399-405，位于依赖映射与文件组装之间，保持旧对话框顺序）、env 可选/不支持过滤
    /// （:418-428）、URL 经宿主管线后 Distinct（:432-436）、路径逃逸校验（:437-441）、sha1 无大小校验的 NetFile（:442-443）。
    /// </summary>
    /// <param name="indexJson">modrinth.index.json 全文。</param>
    /// <param name="instanceKey">版本文件夹兜底（resolveVersionFolder 未提供时使用，如测试直用）。</param>
    /// <param name="resolveVersionFolder">版本名决策回调（入参为清单建议名，返回最终版本文件夹；旧 ValidateFolderName + MyMsgBoxInput 的宿主面）。</param>
    /// <param name="notifyIncompatibleLoader">不兼容依赖回调（旧 NotifyIncompatibleLoader）。</param>
    /// <param name="optionalPrompt">可选文件提示回调（入参为文件名，返回 false = 不下载）。</param>
    /// <param name="onEscapeRejected">路径逃逸提示回调（:439 的 MyMsgBox 面；随后此处仍抛 OperationCanceledException）。</param>
    /// <param name="urlPipeline">URL 管线回调（入参：原始 downloads、依赖映射出的 MC 版本与加载器旗标——回调在计划产出前执行，宿主面不可闭包引用计划；返回镜像展开与来源追踪后的列表；旧 ParseCurseForgeDownloadUrls/DlSourceModGet/ParseModrinthTrackArguments 的宿主面，旧第四参为 McVersion.ModLoaders 旗标）。</param>
    public static ModrinthPlan BuildModrinthPlan(string indexJson, string instanceKey,
        Func<string, string>? resolveVersionFolder = null, Action<string>? notifyIncompatibleLoader = null,
        Func<string, bool>? optionalPrompt = null, Action<string>? onEscapeRejected = null,
        Func<IReadOnlyList<string>, string, ModLoaders, IEnumerable<string>>? urlPipeline = null)
    {
        var json = ParsePackObject(indexJson, "Modrinth 整合包安装信息存在问题");
        var dependencies = JGet(json, "dependencies");
        if (dependencies is null || JGet(dependencies, "minecraft") is null)
            throw new Exception("Modrinth 整合包未提供 Minecraft 版本信息"); //:377
        var minecraftVersion = "Unknown";
        var modLoaderVersions = new Dictionary<ModLoaders, string>();
        foreach (var property in ((JsonObject) dependencies!))
        {
            switch (property.Key.Lower())
            {
            case "minecraft":
                minecraftVersion = property.Value?.ToString() ?? ""; //:384
                break;
            case "forge": //eg. 14.23.5.2859 / 1.19-41.1.0
                modLoaderVersions[ModLoaders.Forge] = property.Value?.ToString() ?? ""; //:386
                Logger.Info($"整合包 Forge 版本：{modLoaderVersions[ModLoaders.Forge]}");
                break;
            case "neoforge" or "neo-forge": //eg. 20.6.98-beta
                modLoaderVersions[ModLoaders.NeoForge] = property.Value?.ToString() ?? ""; //:389
                Logger.Info($"整合包 NeoForge 版本：{modLoaderVersions[ModLoaders.NeoForge]}");
                break;
            case "fabric-loader": //eg. 0.14.14
                modLoaderVersions[ModLoaders.Fabric] = property.Value?.ToString() ?? ""; //:392
                Logger.Info($"整合包 Fabric 版本：{modLoaderVersions[ModLoaders.Fabric]}");
                break;
            default:
                notifyIncompatibleLoader?.Invoke(property.Key); //:395
                break;
            }
        }
        //获取版本名（:398-405）：版本名对话框位于依赖映射与文件组装之间
        var suggestedName = json["name"]?.ToString() ?? ""; //:400
        var versionFolder = resolveVersionFolder?.Invoke(suggestedName) ?? instanceKey;
        //获取下载文件列表（:416-444）
        var files = new List<NetFile>();
        foreach (var fileNode in ChildrenOf(json["files"]))
        {
            //检查是否需要该文件（:419-430）
            if (fileNode is JsonObject fileObject && fileObject.ContainsKey("env"))
            {
                //VB File("env")("client")：env 为 JSON null 值时取 client 同样 NRE（JValue 索引得 Nothing），以 ! 解引用保持
                var clientValue = JGet(fileNode, "env")!["client"]?.ToString() ?? "";
                switch (clientValue)
                {
                case "optional":
                    if (optionalPrompt?.Invoke(PathUtils.GetLastPart(JGet(fileNode, "path")!.ToString())) == false) continue; //:421-426
                    break;
                case "unsupported":
                    continue; //:427-428
                }
            }
            //添加下载文件（:431-443）
            var downloadsNode = JGet(fileNode, "downloads");
            if (downloadsNode is null) throw new ArgumentNullException("source"); //缺失时旧 SelectMany(Nothing) 的崩溃路径
            List<string> rawDownloads = downloadsNode is JsonArray downloadsArray
                ? downloadsArray.Select(static t => t?.ToString() ?? "").ToList() //标量 JValue 无子项 → 空（旧枚举语义）
                : [];
            var urls = (urlPipeline?.Invoke(rawDownloads, minecraftVersion,
                modLoaderVersions.Keys.Aggregate(ModLoaders.None, static (flags, loader) => flags | loader)) //旧 TargetVersion.ModLoaders 的旗标聚合
                ?? (IEnumerable<string>) rawDownloads).Distinct().ToList(); //:432-436
            var targetPath = Path.Combine(versionFolder,
                JGet(fileNode, "path")!.ToString().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)); //:437
            if (!PathUtils.IsParentOf(versionFolder, targetPath)) //:438
            {
                onEscapeRejected?.Invoke($"整合包的文件路径超出了版本文件夹，请向整合包作者反馈此问题！\r\n目标：{Path.GetFullPath(targetPath)}\r\n错误的文件：{targetPath}"); //:439
                throw new OperationCanceledException(); //:440
            }
            files.Add(new NetFile(urls, targetPath,
                new FileChecker { Hash = JGet(JGet(fileNode, "hashes")!, "sha1")!.ToString() }, true)); //:442-443 Modrinth 整合包不应校验文件大小
        }
        return new ModrinthPlan(minecraftVersion, modLoaderVersions, suggestedName, versionFolder, files);
    }

    #endregion

    #region MMC 组件映射（ModModpack.vb:631-651）

    /// <summary>
    /// MMC components 的 uid → version 映射（:631-651）：四个已知 uid 直收，org.lwjgl 前缀跳过（#8210，:646-647），
    /// 其余经回调提示（旧 NotifyIncompatibleLoader，取消时抛 OperationCanceledException）；无 uid 的条目跳过（:634）。
    /// </summary>
    /// <param name="components">mmc-pack.json 的 components 数组（:630 的存在性检查留宿主层）。</param>
    /// <param name="notifyIncompatibleLoader">未知 uid 回调。</param>
    public static Dictionary<string, string> MapMmcComponents(JsonArray components, Action<string>? notifyIncompatibleLoader = null)
    {
        var map = new Dictionary<string, string>();
        foreach (var componentNode in components)
        {
            var component = (JsonObject) componentNode!; //旧 For Each As JObject 强转：非对象项同处抛错
            if (!component.ContainsKey("uid")) continue; //:634
            var uid = JGet(component, "uid")?.ToString() ?? "";
            switch (uid)
            {
            case "net.minecraft" or "net.minecraftforge" or "net.neoforged" or "net.fabricmc.fabric-loader":
                map[uid] = JGet(component, "version")?.ToString()!; //旧为 JToken→String 直接赋值（缺失为 Nothing）
                break;
            default:
                if (uid.StartsWithF("org.lwjgl"))
                {
                    Logger.Info($"已跳过 LWJGL 项：{uid}"); //#8210（:646-647）
                } else
                {
                    notifyIncompatibleLoader?.Invoke(uid); //:649
                }
                break;
            }
        }
        return map;
    }

    /// <summary>
    /// uid 映射 → 安装组件（:654 的 McComponents 构造）：MinecraftVersion 缺失时为 null，与旧一致（下游安装链处置）。
    /// </summary>
    public static McComponents BuildMmcComponents(IReadOnlyDictionary<string, string> uidMap)
    {
        var modLoaderVersions = new Dictionary<ModLoaders, string>();
        if (uidMap.TryGetValue("net.minecraftforge", out var forgeVersion)) modLoaderVersions[ModLoaders.Forge] = forgeVersion;
        if (uidMap.TryGetValue("net.neoforged", out var neoForgeVersion)) modLoaderVersions[ModLoaders.NeoForge] = neoForgeVersion;
        if (uidMap.TryGetValue("net.fabricmc.fabric-loader", out var fabricVersion)) modLoaderVersions[ModLoaders.Fabric] = fabricVersion;
        uidMap.TryGetValue("net.minecraft", out var minecraftVersion);
        return new McComponents(minecraftVersion!, modLoaderVersions);
    }

    #endregion

    #region MCBBS 部署核（ModModpack.vb:677-761 决策面）

    /// <summary>
    /// MCBBS packmeta 的决策解析（:677-761）：launchInfo 参数串接（:707-711，写入留宿主层）、addons 映射（:714-718）、
    /// game 必需（:719）、Quilt 拒绝（:720-723）、加载器映射（:724-727）、OptiFine 列表匹配（:729-739）。
    /// </summary>
    /// <param name="mcbbsJson">mcbbs.packmeta（或带 addons 的 manifest.json）全文。</param>
    /// <param name="optiFineListProvider">OptiFine 列表的懒取回调（仅 addons.optifine 在场时调用；旧 DlOptiFineListBmclapiLoader 等待与取值留宿主层）。</param>
    /// <param name="onQuiltRejected">Quilt 拒绝提示回调（:721 的 Hint 面；随后此处仍抛 OperationCanceledException）。</param>
    public static McbbsPlan BuildMcbbsPlan(string mcbbsJson,
        Func<IEnumerable<DlOptiFineListEntry>?>? optiFineListProvider = null, Action? onQuiltRejected = null)
    {
        var json = ParsePackObject(mcbbsJson, "MCBBS 整合包安装信息存在问题");
        //JVM 参数（:707-711 决策面）
        string? launchJvmArguments = null, launchGameArguments = null;
        if (json["launchInfo"] is JsonObject launchInfo)
        {
            if (launchInfo.ContainsKey("javaArgument")) launchJvmArguments = JoinTokens(launchInfo["javaArgument"]); //:709
            if (launchInfo.ContainsKey("launchArgument")) launchGameArguments = JoinTokens(launchInfo["launchArgument"]); //:710
        }
        //addons 映射（:714-718）
        if (!json.ContainsKey("addons")) throw new Exception("该 MCBBS 整合包未提供游戏版本附加信息，无法安装！"); //:714
        var addons = new Dictionary<string, string>();
        foreach (var entryNode in ChildrenOf(json["addons"]))
        {
            addons.Add(JGet(entryNode, "id")?.ToString()!, JGet(entryNode, "version")?.ToString()!); //:717（重复 id 与旧 Add 一样抛错；键/值缺失与旧一样为 Nothing）
        }
        if (!addons.ContainsKey("game")) throw new Exception("该 MCBBS 整合包未提供游戏版本信息，无法安装！"); //:719
        if (addons.ContainsKey("quilt"))
        {
            onQuiltRejected?.Invoke(); //:721 Hint 留宿主层
            throw new OperationCanceledException(); //:722
        }
        var modLoaderVersions = new Dictionary<ModLoaders, string>();
        if (addons.TryGetValue("forge", out var forgeVersion)) modLoaderVersions[ModLoaders.Forge] = forgeVersion; //:725
        if (addons.TryGetValue("neoforge", out var neoForgeVersion)) modLoaderVersions[ModLoaders.NeoForge] = neoForgeVersion; //:726
        if (addons.TryGetValue("fabric", out var fabricVersion)) modLoaderVersions[ModLoaders.Fabric] = fabricVersion; //:727
        //OptiFine（:729-739）
        JsonObject? optiFineEntry = null;
        if (addons.TryGetValue("optifine", out var optiFineVersion))
        {
            var minecraftVersion = addons["game"]!;
            if (minecraftVersion.EndsWithF(".0")) minecraftVersion = minecraftVersion[..^2]; //:731
            var optiFineFileSuffix = "_HD_" + optiFineVersion!.AfterLast("HD_") + ".jar"; //:732
            var optiFineList = optiFineListProvider!();
            var entry = optiFineList!.FirstOrDefault(
                v => v.Inherit == minecraftVersion && v.FileName.EndsWithF(optiFineFileSuffix)); //:735-736
            if (entry is null)
                throw new Exception($"OptiFine 列表中没有整合包所需的 OptiFine {optiFineVersion}（Minecraft {addons["game"]}）"); //:737
            //JObject.FromObject(Entry).ToJsonNode 等价：公开字段原样序列化（STJ 默认不含字段，须 IncludeFields）
            optiFineEntry = (JsonObject?) JsonSerializer.SerializeToNode(entry, new JsonSerializerOptions { IncludeFields = true });
        }
        return new McbbsPlan(addons["game"]!, modLoaderVersions, optiFineEntry, launchJvmArguments, launchGameArguments);
    }

    #endregion

    #region HMCL 版本判定核（ModModpack.vb:496-521）

    /// <summary>
    /// HMCL modpack.json 的版本判定核（:496-521）：解析失败包装（:499-503）、name 建议名（:505）、
    /// gameVersion 必需（:520）。版本名对话框与解压装载留宿主层。
    /// </summary>
    public static HmclPlan BuildHmclPlan(string modpackJson)
    {
        var json = ParsePackObject(modpackJson, "HMCL 整合包安装信息存在问题");
        var suggestedName = json["name"]?.ToString() ?? ""; //:505（缺失与 JSON null 均为空串）
        if (!json.ContainsKey("gameVersion")) throw new Exception("该 HMCL 整合包未提供游戏版本信息，无法安装！"); //:520
        return new HmclPlan(suggestedName, json["gameVersion"]?.ToString() ?? "");
    }

    #endregion

    #region 基础设施（Newtonsoft JToken 语义的 STJ 对齐面）

    /// <summary>整合包清单解析（旧 DeserializeJson 的包装面）：失败时按各整合包种类的既有文案包装。</summary>
    private static JsonObject ParsePackObject(string json, string wrapMessage)
    {
        try
        {
            return (JsonObject?) JsonNode.Parse(json, null, ParseOptions)
                ?? throw new JsonException("JSON 输入为空。"); //旧 DeserializeJson 对 null 输入得 Nothing，下游同处 NRE
        } catch (Exception ex)
        {
            throw new Exception(wrapMessage, ex); //:169-173/:371-376/:499-503/:683-688
        }
    }

    /// <summary>Newtonsoft 式子项读取：仅对象节点可查键（Ordinal），标量与非对象一律 null（旧 JToken 索引的 null 容忍语义）。</summary>
    private static JsonNode? JGet(JsonNode? node, string key)
        => node is JsonObject obj && obj.ContainsKey(key) ? obj[key] : null;

    /// <summary>JToken 直接子项枚举（For Each over JToken）：数组为条目、对象为属性值、标量与缺失为空。</summary>
    private static IEnumerable<JsonNode?> ChildrenOf(JsonNode? node) => node switch
    {
        JsonArray array => array,
        JsonObject obj => obj.Select(static pair => pair.Value),
        _ => Enumerable.Empty<JsonNode?>()
    };

    /// <summary>String.Join(" ", JToken) 的枚举语义：数组为条目文本，标量无子项为空，对象为属性值（旧 JProperty 子项枚举的近似）。</summary>
    private static string JoinTokens(JsonNode? node)
        => string.Join(" ", EnumerateChildren(node));

    private static IEnumerable<string?> EnumerateChildren(JsonNode? node) => node switch
    {
        JsonArray array => array.Select(static item => item?.ToString() ?? ""),
        JsonObject obj => obj.Select(static pair => pair.Key + ": " + pair.Value),
        _ => Enumerable.Empty<string?>()
    };

    /// <summary>VB Option Strict Off 的 JToken→Integer 动态转换：数值直取，字符串按不变文化转换，其余抛 InvalidCastException。</summary>
    private static int ToInteger(JsonNode node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue) return (int) longValue;
            if (value.TryGetValue<string>(out var stringValue)) return Convert.ToInt32(stringValue, CultureInfo.InvariantCulture);
            if (value.TryGetValue<bool>(out var boolValue)) return boolValue ? 1 : 0;
        }
        throw new InvalidCastException("无法将 JSON 值转换为整数：" + node.ToJsonString());
    }

    /// <summary>VB ToObject(Of Boolean) 的动态转换：布尔直取，字符串按 Convert.ToBoolean（大小写不敏感），数值按非零真。</summary>
    private static bool ToBoolean(JsonNode node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var boolValue)) return boolValue;
            if (value.TryGetValue<string>(out var stringValue)) return bool.Parse(stringValue);
            if (value.TryGetValue<long>(out var longValue)) return longValue != 0;
        }
        throw new InvalidCastException("无法将 JSON 值转换为布尔值：" + node.ToJsonString());
    }

    #endregion
}