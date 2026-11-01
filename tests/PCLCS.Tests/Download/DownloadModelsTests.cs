using PCLCS.Download;
using PCLCS.Minecraft.Download;
using PCLCS.Settings;
using PCLCS.Tests.Net;

namespace PCLCS.Tests.Download;

public class DownloadModelsTests
{
    //ModDownload.vb:911-944 三档策略的源排列；MCIM 地址来自 C# DownloadSources.Mod（宿主层 DlSourceModGet 的转发目标）
    private const string ModrinthUrl = "https://api.modrinth.com/v2/project/fabric-api/version";

    [Fact]
    public void DlModRequest_orders_mcim_sources_by_setting()
    {
        Assert.Equal("https://mod.mcimirror.top/modrinth/v2/project/fabric-api/version",
            DownloadSources.Mod(ModrinthUrl)); //MCIM 前缀来自 DownloadSources.Mod

        var mcim = DownloadSources.Mod(ModrinthUrl);
        Assert.Equal([(mcim, 10), (mcim, 10), (ModrinthUrl, 30)],
            ModRequests.BuildModRequestUrls(ModrinthUrl, mcim, 0));
        Assert.Equal([(ModrinthUrl, 20), (mcim, 10), (ModrinthUrl, 30), (mcim, 30)],
            ModRequests.BuildModRequestUrls(ModrinthUrl, mcim, 1)); //modrinth 首选 20s
        var nonModrinth = ModrinthUrl.Replace("modrinth", "other"); //非 Modrinth 源首选超时回落 10s
        Assert.Equal([(nonModrinth, 10), (nonModrinth, 30), (mcim, 30)],
            ModRequests.BuildModRequestUrls(nonModrinth, mcim, 2));
        Assert.Equal([(ModrinthUrl, 20), (ModrinthUrl, 30)],
            ModRequests.BuildModRequestUrls(ModrinthUrl, ModrinthUrl, 1)); //无镜像源可用时两连击（Modrinth 首选 20s 同样适用，对照 :933）
    }

    [Fact]
    public void DlModRequest_falls_back_between_sources_on_loopback()
    {
        var root = Path.Combine(Path.GetTempPath(), "pclcs.dlmod." + Guid.NewGuid().ToString("N"));
        SettingsService.Configure(root);
        SettingsService.Set("ToolDownloadMod", 0);
        using var server = new LoopbackServer();
        //URL 携带 api.modrinth.com 字样 → DownloadSources.Mod 改写为同主机不同路径的 MCIM 地址，从而回环可测三档回退
        var url = server.UrlPrefix + "api.modrinth.com/v2/project/x/version";
        server.Responder = static req => req.Url!.PathAndQuery.Contains("mod.mcimirror.top")
            ? (500, "mcim down", null)
            : (200, """{"mcim":false}""", null);

        var result = ModRequests.DlModRequest(url);

        Assert.False(result?["mcim"]!.GetValue<bool>());
        Assert.Equal(3, server.ReceivedPaths.Count); //MCIM 两次 + 原源一次（策略 0）
        Assert.EndsWith("/api.modrinth.com/v2/project/x/version", server.ReceivedPaths[^1]);
    }

    [Fact]
    public void ForgeRecommendedGet_returns_cached_recommended_version()
    {
        var cache = Path.Combine(Path.GetTempPath(), "pclcs.forgerec." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, "ForgeRecommendedList.json"), """{"1.20.1":"47.2.0"}""");
        ModRequests.ForgeRecommendedCacheFolder = () => cache;

        Assert.Equal("47.2.0", ModRequests.ForgeRecommendedGet("1.20.1"));
        Assert.Null(ModRequests.ForgeRecommendedGet("1.20.2")); //缓存中没有的版本
        Assert.Null(ModRequests.ForgeRecommendedGet("snapshot")); //不含 "." 的输入直接放弃
    }

    [Fact]
    public void ForgeRecommendedGet_returns_null_without_cache_file()
    {
        var cache = Path.Combine(Path.GetTempPath(), "pclcs.forgerec." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        ModRequests.ForgeRecommendedCacheFolder = () => cache;

        Assert.Null(ModRequests.ForgeRecommendedGet("1.20.1"));
    }

    [Fact]
    public void AllDrops_parses_cached_drop_table()
    {
        var root = Path.Combine(Path.GetTempPath(), "pclcs.drops." + Guid.NewGuid().ToString("N"));
        SettingsService.Configure(root);
        SettingsService.Set("CacheDrops", "263,262,10,0"); //ModDownload.vb:165-178 解析格式
        Drops.ResetForTests(); //模拟宿主层重启，强制重读设置

        Assert.Equal([263, 262, 10, 0], Drops.AllDrops);
    }

    [Fact]
    public void AllDrops_is_null_when_never_fetched()
    {
        var root = Path.Combine(Path.GetTempPath(), "pclcs.drops." + Guid.NewGuid().ToString("N"));
        SettingsService.Configure(root);
        Drops.ResetForTests(); //模拟宿主层重启，强制重读设置

        Assert.Null(Drops.AllDrops); //从未完成过获取时为 null（空表不回写）
    }

    [Fact]
    public void AllDrops_setter_persists_to_setting()
    {
        var root = Path.Combine(Path.GetTempPath(), "pclcs.drops." + Guid.NewGuid().ToString("N"));
        SettingsService.Configure(root);
        Drops.ResetForTests(); //模拟宿主层重启，强制重读设置

        Drops.AllDrops = [300, 299];

        Assert.Equal("300,299", SettingsService.Get<string>("CacheDrops"));
        Assert.Equal([300, 299], Drops.AllDrops);
    }

    [Fact]
    public void DlForgeVersionEntry_special_branches_match_vb()
    {
        var hardcoded = new DlForgeVersionEntry("11.15.1.2318", null, "1.8.9"); //:562 三个司马版本的分支硬编码
        Assert.Equal("11.15.1.2318-1.8.9", hardcoded.FileVersion);

        var patched = new DlForgeVersionEntry("10.13.4.1614", null, "1.7.10"); //1614 >= 1300 → 补分支
        Assert.Equal("10.13.4.1614-1.7.10", patched.FileVersion);

        var old = new DlForgeVersionEntry("10.13.3.1298", null, "1.7.10"); //1298 < 1300 → 无分支
        Assert.Equal("10.13.3.1298", old.FileVersion);

        Assert.Equal("Forge", hardcoded.LoaderName);
        Assert.Equal("zip", hardcoded.FileExtension); //Category 未设时按非 installer 处理
        hardcoded.Category = "installer";
        Assert.Equal("jar", hardcoded.FileExtension);
        Assert.False(hardcoded.IsNeoForge);
    }

    [Fact]
    public void DlNeoForgeListEntry_ctor_matches_vb()
    {
        var modern = new DlNeoForgeListEntry("20.4.30-beta");
        Assert.Equal("20.4.30-beta", modern.VersionName);
        Assert.Equal(new Version(20, 4, 30), modern.Version);
        Assert.Equal("1.20.4", modern.Inherit);
        Assert.True(modern.IsBeta);
        Assert.Equal("https://maven.neoforged.net/releases/net/neoforged/neoforge/20.4.30-beta/neoforge-20.4.30-beta",
            modern.UrlBase);

        var legacy = new DlNeoForgeListEntry("1.20.1-47.1.99");
        Assert.Equal("47.1.99", legacy.VersionName);
        Assert.Equal(new Version("19.47.1.99"), legacy.Version);
        Assert.Equal("1.20.1", legacy.Inherit);
        Assert.False(legacy.IsBeta);
        Assert.Equal("https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.99/forge-1.20.1-47.1.99",
            legacy.UrlBase);

        var snapshot = new DlNeoForgeListEntry("0.25w14craftmine.3-beta");
        Assert.Equal(new Version(0, 0, 3), snapshot.Version);
        Assert.Equal("25w14craftmine", snapshot.Inherit);

        Assert.Equal("NeoForge", modern.LoaderName);
        Assert.Equal("jar", modern.FileExtension);
        Assert.True(modern.IsNeoForge);
    }

    [Fact]
    public void DlOptiFineListEntry_inherit_strips_trailing_zero()
    {
        var entry = new DlOptiFineListEntry { Inherit = "1.8.0" };
        Assert.Equal("1.8", entry.Inherit); //ModDownload.vb:354-362 的 .0 后缀去除
    }

    [Fact]
    public void DlForgelikeEntry_compares_by_version_then_name()
    {
        var a = new DlForgeVersionEntry("47.1.0", null, "1.20.1");
        var b = new DlForgeVersionEntry("47.1.3", null, "1.20.1");
        Assert.True(a.CompareTo(b) < 0);

        var list = new List<DlForgelikeEntry> { b, a };
        list.Sort();
        Assert.Same(a, list[0]);
    }
}
