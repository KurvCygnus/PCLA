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
    public void LibraryPrefersMirrorsWhenOfficialNotPreferred() {
        var urls = DownloadSources.Library("https://libraries.minecraft.net/com/mojang/x.jar", false).ToArray();
        Assert.Equal(new[] {
            "https://bmclapi2.bangbang93.com/maven/com/mojang/x.jar",
            "https://bmclapi2.bangbang93.com/libraries/com/mojang/x.jar",
            "https://libraries.minecraft.net/com/mojang/x.jar",
        }, urls);
    }

    [Theory]
    [InlineData("https://resources.download.minecraft.net/ab/cdef")]
    [InlineData("https://piston-data.mojang.com/ab/cdef")]
    [InlineData("https://piston-meta.mojang.com/ab/cdef")]
    public void AssetsRewritesEachKnownHost(string url) {
        var urls = DownloadSources.Assets(url, true).ToArray();
        Assert.Equal(new[] { url, "https://bmclapi2.bangbang93.com/assets/ab/cdef" }, urls);
    }

    [Theory]
    [InlineData("https://piston-data.mojang.com/v1/objects/x.json", "https://bmclapi2.bangbang93.com/v1/objects/x.json")]
    [InlineData("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json")]
    [InlineData("https://launcher.mojang.com/v1/objects/x.jar", "https://bmclapi2.bangbang93.com/v1/objects/x.jar")]
    [InlineData("https://launchermeta.mojang.com/mc/game/version_manifest.json", "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json")]
    public void LauncherOrMetaRewritesEachHost(string url, string mirror) {
        Assert.Equal(new[] { url, mirror }, DownloadSources.LauncherOrMeta(url, true).ToArray());
    }

    [Theory]
    [InlineData("https://api.modrinth.com/v2/project", "https://mod.mcimirror.top/modrinth/v2/project")]
    [InlineData("https://cdn.modrinth.com/data/x.jar", "https://mod.mcimirror.top/data/x.jar")]
    [InlineData("https://api.curseforge.com/v1/mods", "https://mod.mcimirror.top/curseforge/v1/mods")]
    [InlineData("https://edge.forgecdn.net/files/x.jar", "https://mod.mcimirror.top/files/x.jar")]
    [InlineData("https://mediafilez.forgecdn.net/files/x.jar", "https://mod.mcimirror.top/files/x.jar")]
    [InlineData("https://media.forgecdn.net/files/x.jar", "https://mod.mcimirror.top/files/x.jar")]
    public void ModRewritesEachKnownHost(string url, string expected) {
        Assert.Equal(expected, DownloadSources.Mod(url));
    }

    //! legacy 行为：staging-api 先被 api.modrinth.com 规则命中，产出 staging-mod.mcimirror.top。
    [Fact]
    public void ModKeepsLegacyStagingQuirk() {
        Assert.Equal("https://staging-mod.mcimirror.top/modrinth/v2/project",
            DownloadSources.Mod("https://staging-api.modrinth.com/v2/project"));
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
