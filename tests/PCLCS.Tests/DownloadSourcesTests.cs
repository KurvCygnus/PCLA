using PCLCS.Minecraft.Download;

namespace PCLCS.Tests;

public class DownloadSourcesTests {
    private static readonly string[] Official = { "https://a/1", "https://a/2" };
    private static readonly string[] Mirror = { "https://b/1", "https://b/2" };

    [Fact]
    public void OrderPrefersOfficialWhenAsked() {
        Assert.Equal(new[] { "https://a/1", "https://a/2", "https://b/1", "https://b/2" },
            DownloadSources.Order(Official, Mirror, true));
    }

    [Fact]
    public void OrderPrefersMirrorWhenOfficialIsDown() {
        Assert.Equal(new[] { "https://b/1", "https://b/2", "https://a/1", "https://a/2" },
            DownloadSources.Order(Official, Mirror, false));
    }

    [Fact]
    public void OrderDeduplicatesKeepingFirstOccurrence() {
        var official = new[] { "https://x/1", "https://x/2" };
        var mirror = new[] { "https://x/2", "https://x/3" };
        Assert.Equal(new[] { "https://x/1", "https://x/2", "https://x/3" },
            DownloadSources.Order(official, mirror, true));
    }

    [Fact]
    public void AssetsNormalizesResourcesHostToHttps() {
        var urls = DownloadSources.Assets("http://resources.download.minecraft.net/ab/cdef", true).ToArray();
        Assert.Equal("https://resources.download.minecraft.net/ab/cdef", urls[0]);
        Assert.Equal("https://bmclapi2.bangbang93.com/assets/ab/cdef", urls[1]);
    }

    [Fact]
    public void LibrarySpecialCasesModdedLibraries() {
        var urls = DownloadSources.Library("https://libraries.minecraft.net/net/minecraftforge/forge/1.0/x.jar", true).ToArray();
        Assert.Equal(2, urls.Length);
        Assert.All(urls, u => Assert.Contains("bmclapi2.bangbang93.com", u));
        Assert.DoesNotContain(urls, u => u.Contains("libraries.minecraft.net"));
    }

    [Fact]
    public void LibraryKeepsOriginalForVanillaLibraries() {
        var urls = DownloadSources.Library("https://libraries.minecraft.net/com/mojang/x.jar", true).ToArray();
        Assert.Equal("https://libraries.minecraft.net/com/mojang/x.jar", urls[0]);
        Assert.Equal("https://bmclapi2.bangbang93.com/maven/com/mojang/x.jar", urls[1]);
        Assert.Equal("https://bmclapi2.bangbang93.com/libraries/com/mojang/x.jar", urls[2]);
    }

    [Fact]
    public void LauncherOrMetaRewritesAllFourHosts() {
        var urls = DownloadSources.LauncherOrMeta("https://launchermeta.mojang.com/mc/game/version_manifest.json", false).ToArray();
        Assert.Equal("https://bmclapi2.bangbang93.com/mc/game/version_manifest.json", urls[0]);
    }

    [Fact]
    public void ModRewritesKnownHosts() {
        Assert.Equal("https://mod.mcimirror.top/modrinth/v2/project",
            DownloadSources.Mod("https://api.modrinth.com/v2/project"));
    }
}
