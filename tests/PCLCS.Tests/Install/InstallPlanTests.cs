using PCLCS.Download;
using PCLCS.Install;
using PCLCS.Net;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Install;

/// <summary>
/// 安装总装决策核（ModDownloadLib.vb McInstallLoadersGet/McModificationStart 的决策段迁入面）的语义锁定。
/// 断言按旧代码实际行为收敛（brief 计划稿为示意，已修正）：
/// 无空格规则的真实输入是组件面（OptiFine/Forge/NeoForge 在场，:1536-1537）而非 mcFolder 字符串，
/// 字符边界在 RequestTaskTempFolder 的主候选判定（ModMain.vb:947，半角空格序数比较，全角空格不触发）；
/// 重复版本判定为文件存在性（:1576）而非名称集合扫描；.pclignore 写入无条件（:1584），真实条件是删除（:1693）。
/// </summary>
public class InstallPlanTests
{
    private static string TempRoot(string name) => Path.Combine(Path.GetTempPath(), "pclcs.installplan." + name + "." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;

    /// <summary>McComponents 工厂：默认原版空组件。</summary>
    private static McComponents Components(Dictionary<ModLoaders, string>? loaders = null, JsonObject? optiFine = null,
        string? fabricApi = null, string? optiFabric = null, string mc = "1.20.1")
        => new(mc, loaders ?? [], optiFine, fabricApi, optiFabric);

    #region TempMcFolder 无空格规则（:1535-1537 + ModMain.vb:947）

    [Fact]
    public void Vanilla_and_fabric_liteloader_do_not_require_space_free_temp()   //:1536-1537：Fabric/LiteLoader 不在规则内
    {
        Assert.False(InstallPlans.RequiresNonSpaceTempFolder(Components()));
        Assert.False(InstallPlans.RequiresNonSpaceTempFolder(Components(new Dictionary<ModLoaders, string> { [ModLoaders.Fabric] = "0.14.21" })));
        Assert.False(InstallPlans.RequiresNonSpaceTempFolder(Components(new Dictionary<ModLoaders, string> { [ModLoaders.LiteLoader] = "1.12" })));
    }

    [Fact]
    public void Optifine_or_forgelike_requires_space_free_temp()   //:1536-1537：OptiFineEntry / Forge / NeoForge 任一在场
    {
        Assert.True(InstallPlans.RequiresNonSpaceTempFolder(Components(optiFine: new JsonObject())));
        Assert.True(InstallPlans.RequiresNonSpaceTempFolder(Components(new Dictionary<ModLoaders, string> { [ModLoaders.Forge] = "47.1.0" })));
        Assert.True(InstallPlans.RequiresNonSpaceTempFolder(Components(new Dictionary<ModLoaders, string> { [ModLoaders.NeoForge] = "47.1.99" })));
    }

    [Theory]
    [InlineData(@"C:\ProgramData\PCL\TaskTemp\u-1\", true, false)]    //无空格：接受主候选（不进备用路径）
    [InlineData(@"C:\my temp\TaskTemp\u-1\", true, true)]             //半角空格：拒绝主候选，走备用路径（ModMain.vb:947 Exit Try）
    [InlineData(@"C:\my temp\TaskTemp\u-1\", false, false)]           //无要求：短路不检查
    public void Temp_candidate_space_boundary(string candidate, bool requireNonSpace, bool shouldFallback)
        => Assert.Equal(shouldFallback, InstallPlans.ShouldUseTempFallback(candidate, requireNonSpace));

    [Fact]
    public void Temp_candidate_full_width_space_does_not_fallback()   //ModMain.vb:947 Contains(" ") 为半角序数比较：全角 U+3000 不触发备用路径
        => Assert.False(InstallPlans.ShouldUseTempFallback(@"C:\my　temp\TaskTemp\u-1\", requireNonSpace: true));

    #endregion

    #region OptiFineAsMod 判定（:1553-1564）

    [Fact]
    public void Modable_requires_any_mod_loader()   //:1554
    {
        Assert.False(InstallPlans.IsModable(Components()));
        Assert.True(InstallPlans.IsModable(Components(new Dictionary<ModLoaders, string> { [ModLoaders.Fabric] = "0.14.21" })));
    }

    [Fact]
    public void OptiFine_as_mod_requires_optifine_and_loader()   //:1556：选择了 OptiFine 与任意 Mod 加载器
    {
        Assert.True(InstallPlans.IsOptiFineAsMod(Components(new Dictionary<ModLoaders, string> { [ModLoaders.Forge] = "47.1.0" }, optiFine: new JsonObject())));
        Assert.False(InstallPlans.IsOptiFineAsMod(Components(optiFine: new JsonObject())));   //仅 OptiFine
        Assert.False(InstallPlans.IsOptiFineAsMod(Components(new Dictionary<ModLoaders, string> { [ModLoaders.Forge] = "47.1.0" })));   //仅加载器
    }

    [Fact]
    public void OptiFine_as_mod_folder_segments_by_lite_loader()   //:1557-1564（#8147）：选了 LiteLoader 时按 MC 版本分目录
    {
        Assert.Equal(@"T:\mods\1.12.2\", InstallPlans.OptiFineAsModFolder(@"T:\mods\", "1.12.2", "1.12.2"));
        Assert.Equal(@"T:\mods\", InstallPlans.OptiFineAsModFolder(@"T:\mods\", "1.12.2", null));
    }

    #endregion

    #region 重复版本判定（:1574-1579）

    [Fact]
    public void Duplicate_version_detected_by_existing_json()   //:1576：目标版本 Json 已存在即重复
    {
        var root = TempRoot("dup");
        var versionFolder = root + @"versions\";
        Directory.CreateDirectory(versionFolder);
        Assert.False(InstallPlans.IsDuplicateVersion(versionFolder, "1.20.1"));
        File.WriteAllText(versionFolder + "1.20.1.json", "{}");
        Assert.True(InstallPlans.IsDuplicateVersion(versionFolder, "1.20.1"));
    }

    [Fact]
    public void Duplicate_version_null_name_probes_folder_dot_json()   //:1576 插值语义：新版本名为 Nothing 时探测 {folder}.json
    {
        var root = TempRoot("dupnull");
        var versionFolder = root + @"ver\";
        Directory.CreateDirectory(versionFolder);
        Assert.False(InstallPlans.IsDuplicateVersion(versionFolder, null));
        File.WriteAllText(versionFolder + ".json", "{}");
        Assert.True(InstallPlans.IsDuplicateVersion(versionFolder, null));
    }

    #endregion

    #region .pclignore 内容与删除决策（:1584 / :1692-1695）

    [Fact]
    public void Pclignore_content_is_verbatim()   //:1584 常量逐字
        => Assert.Equal("用于临时地在 PCL 的版本列表中屏蔽此版本。", InstallPlans.PclIgnoreContent);

    [Fact]
    public void Pclignore_delete_decision()   //:1693：仅非中间产物在安装尾删除
    {
        Assert.True(InstallPlans.ShouldDeletePclIgnore(isIntermediateOutput: false));
        Assert.False(InstallPlans.ShouldDeletePclIgnore(isIntermediateOutput: true));
    }

    #endregion

    #region Fabric API / OptiFabric 依赖下载组装（:1585-1598）

    [Fact]
    public void Dependency_downloads_follow_component_versions_in_order()
    {
        var keys = new List<string>();
        var fabricFile = new NetFile(["https://example.org/fabric-api.jar"], @"T:\mods\fabric-api.jar");
        var optiFabricFile = new NetFile(["https://example.org/optifabric.jar"], @"T:\mods\optifabric.jar");
        var downloads = InstallPlans.BuildDependencyDownloads(
            Components(fabricApi: "0.92.2", optiFabric: "1.12.2"),
            key =>
            {
                keys.Add(key);
                return key == "FabricApi" ? fabricFile : optiFabricFile;
            });
        Assert.Equal(2, downloads.Count);
        Assert.Equal(("FabricApi", "下载 Fabric API", fabricFile.LocalPath), (downloads[0].SourceName, downloads[0].LoaderTitle, downloads[0].File.LocalPath));
        Assert.Equal(("OptiFabric", "下载 OptiFabric", optiFabricFile.LocalPath), (downloads[1].SourceName, downloads[1].LoaderTitle, downloads[1].File.LocalPath));
        Assert.Equal(["FabricApi", "OptiFabric"], keys);   //回调按旧 If 段顺序各取一次
    }

    [Fact]
    public void Dependency_downloads_skip_unset_versions_and_never_call_provider()
    {
        var calls = 0;
        var onlyFabric = InstallPlans.BuildDependencyDownloads(Components(fabricApi: "0.92.2"),
            _ => { calls++; return new NetFile(["https://example.org/a.jar"], @"T:\mods\a.jar"); });
        Assert.Single(onlyFabric);
        Assert.Equal("FabricApi", onlyFabric[0].SourceName);
        Assert.Equal("下载 Fabric API", onlyFabric[0].LoaderTitle);
        var none = InstallPlans.BuildDependencyDownloads(Components(), _ => { calls++; return new NetFile([], ""); });
        Assert.Empty(none);
        Assert.Equal(1, calls);   //无版本时不触发 NetFile 构造（旧 If 守卫语义）
    }

    #endregion

    #region 修改安装的构建请求组装（McModificationStart :1765-1776）

    [Fact]
    public void Modification_build_request_overrides_and_copies_fields()
    {
        var components = Components(fabricApi: "0.92.2");
        var liteLoader = new DlLiteLoaderListEntry { FileName = "liteloader.jar" };
        var forge = new DlForgeVersionEntry("47.1.0", null, "1.20.1");
        var request = new McInstallRequest
        {
            NewInstanceName = "old-name",
            InstanceFolder = @"T:\versions\old-name\",
            Components = components,
            MinecraftJson = "https://example.org/client.json",
            ForgeEntry = forge,
            FabricApi = "fabric-api-resource",   //Resource 模块未迁，FabricApi/OptiFabric 以 Object 承载（占位串代替宿主层实例）
            OptiFabric = "optifabric-resource",
            LiteLoaderEntry = liteLoader,
            IsIntermediateOutput = false
        };
        var build = InstallPlans.BuildModificationRequest(request, "1.20.1-改", @"T:\TaskTemp\u-1\1.20.1-改\");
        Assert.Equal("1.20.1-改", build.NewInstanceName);   //旧 :1766：覆写为实例名
        Assert.Equal(@"T:\TaskTemp\u-1\1.20.1-改\", build.InstanceFolder);   //旧 :1767：覆写为构建目录
        Assert.Same(components, build.Components);   //旧 :1768
        Assert.Equal("https://example.org/client.json", build.MinecraftJson);   //旧 :1769
        Assert.Same(forge, build.ForgeEntry);   //旧 :1770
        Assert.Null(build.NeoForgeEntry);   //旧 :1771（源请求未设 → Nothing 直拷）
        Assert.Equal("fabric-api-resource", build.FabricApi);   //旧 :1772
        Assert.Equal("optifabric-resource", build.OptiFabric);   //旧 :1773
        Assert.Same(liteLoader, build.LiteLoaderEntry);   //旧 :1774
        Assert.True(build.IsIntermediateOutput);   //旧 :1775
        Assert.False(request.IsIntermediateOutput);   //源请求不被改动
    }

    [Fact]
    public void Request_fields_default_like_vb()   //:1488-1529 字段逐字：VB 默认值（引用字段 Nothing、IsIntermediateOutput False）
    {
        var request = new McInstallRequest();
        Assert.Null(request.NewInstanceName);
        Assert.Null(request.InstanceFolder);
        Assert.Null(request.Components);
        Assert.Null(request.MinecraftJson);
        Assert.Null(request.ForgeEntry);
        Assert.Null(request.NeoForgeEntry);
        Assert.Null(request.FabricApi);
        Assert.Null(request.OptiFabric);
        Assert.Null(request.LiteLoaderEntry);
        Assert.False(request.IsIntermediateOutput);
    }

    #endregion

    #region Natives 占用检查判定（McModificationStart :1757-1762）

    [Fact]
    public void Natives_occupied_file_is_reported_first()   //:1760：首个不可独占打开的 dll
    {
        var root = TempRoot("natives");
        Directory.CreateDirectory(root);
        var free = root + "a.dll";
        var locked = root + "b.dll";
        File.WriteAllText(free, "free");
        File.WriteAllText(locked, "locked");
        Assert.Null(InstallPlans.FirstOccupiedNativesFile([free, locked]));
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(locked, InstallPlans.FirstOccupiedNativesFile([free, locked]));
            Assert.Equal(locked, InstallPlans.FirstOccupiedNativesFile([locked, free]));   //旧循环语义：按枚举序首个
        }
        Assert.Null(InstallPlans.FirstOccupiedNativesFile([free]));   //释放后可用
    }

    #endregion
}