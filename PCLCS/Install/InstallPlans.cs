using PCLCS.Download;
using PCLCS.Net;

namespace PCLCS.Install;

/// <summary>
/// 安装总装决策核（ModDownloadLib.vb McInstallLoadersGet/McModificationStart 的决策段迁入；
/// LoaderCombo 树装配、清单写入调用点、Natives 枚举、GetNativesFolder、Rename 协调与 WPF 留宿主层——裁定 10）：
/// TempMcFolder 无空格需求判定（:1535-1537）、主候选空格边界（ModMain.vb:947）、Modable 与 OptiFineAsMod 判定
/// （:1553-1564，含 #8147 的 OptiFine 缓存改写）、重复版本判定（:1574-1576）、.pclignore 内容常量与删除决策
/// （:1584/:1692-1695）、Fabric API/OptiFabric 依赖下载组装（:1585-1598）、Natives 占用检查判定
/// （McModificationStart :1757-1762）与修改安装构建请求组装（:1765-1776）。
/// </summary>
public static class InstallPlans
{
    /// <summary>
    /// TempMcFolder 的无空格需求判定（ModDownloadLib.vb:1535-1537 决策面）：安装 OptiFine、Forge 或 NeoForge 时，
    /// 安装器与缓存路径不能包含空格，临时文件夹须走无空格候选。Fabric 与 LiteLoader 不在规则内。
    /// 候选是否含空格的字符边界见 <see cref="ShouldUseTempFallback"/>；候选生成、建目录与权限检查留宿主层（RequestTaskTempFolder）。
    /// brief 计划稿的 ResolveTempMcFolder(mcFolder) 收敛为此形态：旧真值的输入是组件面而非 mcFolder 字符串。
    /// </summary>
    public static bool RequiresNonSpaceTempFolder(McComponents components)
        => components.OptiFineEntry is not null ||
           components.ModLoaderVersions.ContainsKey(ModLoaders.Forge) ||
           components.ModLoaderVersions.ContainsKey(ModLoaders.NeoForge);

    /// <summary>
    /// 任务临时文件夹主候选的空格边界（ModMain.vb:947 决策面）：无空格需求命中半角空格时拒绝主候选、走备用路径
    /// （旧 <c>If RequireNonSpace AndAlso ResultFolder.Contains(" ") Then Exit Try</c>）。
    /// Contains(" ") 为序数比较：全角空格（U+3000）不触发备用路径（旧真值）；要求缺席时短路不检查。
    /// </summary>
    public static bool ShouldUseTempFallback(string candidate, bool requireNonSpace)
        => requireNonSpace && candidate.Contains(' ');

    /// <summary>
    /// Modable 判定（ModDownloadLib.vb:1554）：存在任意 Mod 加载器。缓存文件夹布局、OptiFine 权重（16/24）、
    /// Block 链与 mods 文件夹创建均依赖此判定。
    /// </summary>
    public static bool IsModable(McComponents components) => components.ModLoaderVersions.Count != 0;

    /// <summary>
    /// OptiFineAsMod 判定（ModDownloadLib.vb:1553-1556 决策面）：选择了 OptiFine 与任意 Mod 加载器时，
    /// OptiFine 作为 Mod 下载进 mods 缓存（而非独立版本文件夹）。旧真值以反序列化条目非空判定 OptiFine 在场；
    /// 与 Components.OptiFineEntry 非空同判（非 null JsonObject 反序列化必得非空条目）。
    /// </summary>
    public static bool IsOptiFineAsMod(McComponents components)
        => components.OptiFineEntry is not null && components.ModLoaderVersions.Count != 0;

    /// <summary>
    /// OptiFineAsMod 时的 OptiFine 缓存文件夹改写（ModDownloadLib.vb:1557-1564 决策面，#8147）：
    /// 同装 LiteLoader 时按 MC 版本分目录（mods\{MC 版本}\，避免 LiteLoader 注入的 OptiFine 与其他版本混淆），
    /// 否则直接用 mods\。日志与调用时机留宿主层。
    /// </summary>
    public static string OptiFineAsModFolder(string modsTempFolder, string minecraftVersion, string? liteLoaderVersion)
        => liteLoaderVersion is not null ? $"{modsTempFolder}{minecraftVersion}\\" : modsTempFolder;

    /// <summary>
    /// 重复版本判定（ModDownloadLib.vb:1574-1576 决策面）：目标版本文件夹下已有同名 Json 即重复。
    /// versionFolder 传入前已按旧入口补尾分隔符，此处幂等补全保持输入宽容；
    /// newInstanceName 为 null 时与旧插值语义一致（探测 {folder}.json）。
    /// Hint 提示与 OperationCanceledException 中断留宿主层（:1577-1578）。
    /// brief 计划稿的 (existingNames, candidateName) 集合扫描无 VB 真值：旧代码只做单文件存在性检查。
    /// </summary>
    public static bool IsDuplicateVersion(string? versionFolder, string? newInstanceName)
        => FileUtils.Exists(PathUtils.AddSlashSuffix(versionFolder!) + newInstanceName + ".json");

    /// <summary>.pclignore 写入内容（ModDownloadLib.vb:1584 常量逐字）。写入本身无条件执行（安装头 LoaderTask 常驻），非决策。</summary>
    public const string PclIgnoreContent = "用于临时地在 PCL 的版本列表中屏蔽此版本。";

    /// <summary>
    /// .pclignore 删除决策（ModDownloadLib.vb:1692-1695 决策面）：仅非中间产物在安装尾删除；
    /// 中间产物（修改安装的构建目录）保留忽略标识，交由 McInstallCommit 的提交清理处置。
    /// brief 计划稿的 ShouldWritePclIgnore 无 VB 真值：旧代码对写入无条件，真实条件在删除。
    /// </summary>
    public static bool ShouldDeletePclIgnore(bool isIntermediateOutput) => !isIntermediateOutput;

    /// <summary>
    /// Fabric API / OptiFabric 依赖下载组装决策（ModDownloadLib.vb:1585-1598 决策面）：
    /// 按 Components 的 FabricApiVersion/OptiFabricVersion 判定是否产生对应下载项（保序 Fabric API → OptiFabric），
    /// 并携带 ManagedFileSources 的键与 Loader 标题常量。NetFile 的构造（ResourceVersion.ToNetFile，Resource 模块未迁）
    /// 经回调由宿主层提供，键为 <see cref="InstallDependencyDownload.SourceName"/>（"FabricApi"/"OptiFabric"）；
    /// 仅在对应版本已设时调用（旧 If 守卫语义）。LoaderDownload 的构造与列表挂接留宿主层。
    /// brief 计划稿的 BuildRequest(targetName, mcFolder, Func&lt;string, IEnumerable&lt;NetFile&gt;&gt;) 收敛为此形态：
    /// 旧真值为每组件恰好一个 NetFile + 固定标题，无多文件聚合，回调不需可空。
    /// </summary>
    public static IReadOnlyList<InstallDependencyDownload> BuildDependencyDownloads(
        McComponents components, Func<string, NetFile> resourceNetFileProvider)
    {
        var downloads = new List<InstallDependencyDownload>();
        //Fabric API（:1585-1591）
        if (components.FabricApiVersion is not null)
        {
            downloads.Add(new InstallDependencyDownload("FabricApi", "下载 Fabric API", resourceNetFileProvider("FabricApi")));
        }
        //OptiFabric（:1592-1598）
        if (components.OptiFabricVersion is not null)
        {
            downloads.Add(new InstallDependencyDownload("OptiFabric", "下载 OptiFabric", resourceNetFileProvider("OptiFabric")));
        }
        return downloads;
    }

    /// <summary>
    /// 修改安装的中间构建请求组装决策（McModificationStart :1765-1776 决策面）：以临时构建目录为目标文件夹、
    /// 实例名为新版本名、标记中间产物，其余字段自原请求直拷（含 FabricApi/OptiFabric 的 Object 承载值）。
    /// 逐字段对照旧 With 块：NewInstanceName/InstanceFolder 覆写（:1766-1767），Components/MinecraftJson/ForgeEntry/
    /// NeoForgeEntry/FabricApi/OptiFabric/LiteLoaderEntry 直拷（:1768-1774），IsIntermediateOutput = True（:1775）。
    /// 返回新实例，不改动原请求。
    /// </summary>
    public static McInstallRequest BuildModificationRequest(McInstallRequest request, string instanceName, string buildFolder)
        => new()
        {
            NewInstanceName = instanceName,
            InstanceFolder = buildFolder,
            Components = request.Components,
            MinecraftJson = request.MinecraftJson,
            ForgeEntry = request.ForgeEntry,
            NeoForgeEntry = request.NeoForgeEntry,
            FabricApi = request.FabricApi,
            OptiFabric = request.OptiFabric,
            LiteLoaderEntry = request.LiteLoaderEntry,
            IsIntermediateOutput = true
        };

    /// <summary>
    /// Natives 占用检查判定（McModificationStart :1759-1761 决策面）：返回首个无法独占打开的 dll 路径（无则 null），
    /// 与旧循环的枚举序首个语义一致（枚举惰性短路，占用文件之后的项不再探测）。
    /// 枚举来源（GetNativesFolder + *.dll，留 ModLaunch）与异常包装（"Natives 文件正在被占用…"）留宿主层。
    /// </summary>
    public static string? FirstOccupiedNativesFile(IEnumerable<string> nativeDllFiles)
        => nativeDllFiles.FirstOrDefault(static file => !FileUtils.IsAvailable(file));
}

/// <summary>
/// 安装请求（ModDownloadLib.vb:1488-1529 字段逐字迁入；VB 侧经 Imports 别名直用本类型，原 VB 类已删除）。
/// FabricApi/OptiFabric 的源类型 <c>ResourceVersion</c> 属宿主层 Resource 模块（未迁），以 <see cref="object"/> 承载——
/// 值为宿主层 ResourceVersion 实例，宿主层在构造 NetFile 处强转使用；Resource 模块迁入后应收紧字段类型。
/// </summary>
public class McInstallRequest
{
    /// <summary>
    /// 新版本安装必填。安装目标版本名称。
    /// </summary>
    public string? NewInstanceName;
    /// <summary>
    /// 必填。安装目标文件夹。
    /// </summary>
    public string? InstanceFolder;
    /// <summary>
    /// 必填。欲安装的 Minecraft 与组件版本。
    /// </summary>
    public McComponents? Components;
    /// <summary>
    /// 可选。欲下载的 Minecraft Json 地址。
    /// </summary>
    public string? MinecraftJson;
    /// <summary>
    /// 欲下载的 Forge。
    /// </summary>
    public DlForgeVersionEntry? ForgeEntry;
    /// <summary>
    /// 欲下载的 NeoForge。
    /// </summary>
    public DlNeoForgeListEntry? NeoForgeEntry;
    /// <summary>
    /// 欲下载的 Fabric API 信息。值为宿主层 ResourceVersion 实例（Resource 模块未迁，见类注释）。
    /// </summary>
    public object? FabricApi;
    /// <summary>
    /// 欲下载的 OptiFabric 信息。值为宿主层 ResourceVersion 实例（Resource 模块未迁，见类注释）。
    /// </summary>
    public object? OptiFabric;
    /// <summary>
    /// 欲下载的 LiteLoader 详细信息。
    /// </summary>
    public DlLiteLoaderListEntry? LiteLoaderEntry;
    /// <summary>
    /// 是否为中间产物。
    /// </summary>
    public bool IsIntermediateOutput;
}

/// <summary>
/// Fabric API / OptiFabric 依赖下载项（ModDownloadLib.vb:1585-1598 组装决策的产出）。
/// </summary>
/// <param name="SourceName">ManagedFileSources 的登记键与回调键（旧 Add("FabricApi"/"OptiFabric", ...)，:1588/:1595）。</param>
/// <param name="LoaderTitle">LoaderDownload 标题常量（"下载 Fabric API"/"下载 OptiFabric"，:1589/:1596）。</param>
/// <param name="File">经宿主层回调构造的下载文件（ResourceVersion.ToNetFile 的产物，:1587/:1594）。</param>
public sealed record InstallDependencyDownload(string SourceName, string LoaderTitle, NetFile File);
