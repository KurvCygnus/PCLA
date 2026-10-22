namespace PCLCS.Minecraft.Download;

/// <summary>下载地址与源的映射，与既有 DlSource* 行为逐字一致。</summary>
public static class DownloadSources {
    private static readonly string[] MavenHosts = {
        "https://piston-data.mojang.com",
        "https://piston-meta.mojang.com",
        "https://libraries.minecraft.net",
    };

    public static IEnumerable<string> Order(IEnumerable<string> official, IEnumerable<string> mirror,
        bool preferOfficial) {
        return preferOfficial ? official.Union(mirror) : mirror.Union(official);
    }

    public static IEnumerable<string> Assets(string url, bool preferOfficial) {
        var original = url.Replace("http://resources.download.minecraft.net", "https://resources.download.minecraft.net");
        var mirror = original
            .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/assets")
            .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/assets")
            .Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets");
        return Order(new[] { original }, new[] { mirror }, preferOfficial);
    }

    public static IEnumerable<string> Library(string url, bool preferOfficial) {
        var maven = ReplaceHosts(url, "https://bmclapi2.bangbang93.com/maven");
        var libraries = ReplaceHosts(url, "https://bmclapi2.bangbang93.com/libraries");
        if (new[] { "minecraftforge", "fabricmc", "neoforged" }.Any(k => url.Contains(k))) {
            return new[] { maven, libraries };
        }
        return Order(new[] { url }, new[] { maven, libraries }, preferOfficial);
    }

    public static IEnumerable<string> LauncherOrMeta(string url, bool preferOfficial) {
        var mirror = url
            .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com");
        return Order(new[] { url }, new[] { mirror }, preferOfficial);
    }

    public static string Mod(string url) {
        return url
            .Replace("api.modrinth.com", "mod.mcimirror.top/modrinth")
            .Replace("staging-api.modrinth.com", "mod.mcimirror.top/modrinth")
            .Replace("cdn.modrinth.com", "mod.mcimirror.top")
            .Replace("api.curseforge.com", "mod.mcimirror.top/curseforge")
            .Replace("edge.forgecdn.net", "mod.mcimirror.top")
            .Replace("mediafilez.forgecdn.net", "mod.mcimirror.top")
            .Replace("media.forgecdn.net", "mod.mcimirror.top");
    }

    private static string ReplaceHosts(string url, string replacement) {
        foreach (var host in MavenHosts) {
            url = url.Replace(host, replacement);
        }
        return url;
    }
}
