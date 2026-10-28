namespace PCLCS.Minecraft.Download;

/// <summary>下载地址与源的映射，与既有 DlSource* 行为逐字一致。</summary>
public static class DownloadSources
{
    private static readonly string[] MavenHosts = {
        "https://piston-data.mojang.com",
        "https://piston-meta.mojang.com",
        "https://libraries.minecraft.net",
    };

    /// <summary>按偏好拼接官方源与镜像源，Union 去重并保留首次出现。</summary>
    /// <param name="official">官方源地址。</param>
    /// <param name="mirror">镜像源地址。</param>
    /// <param name="preferOfficial">为真时官方源在前，为假时镜像源在前。</param>
    public static IEnumerable<string> Order(IEnumerable<string> official, IEnumerable<string> mirror,
        bool preferOfficial)
    {
        return preferOfficial ? official.Union(mirror) : mirror.Union(official);
    }

    /// <summary>资源文件（assets 索引与对象）的候选地址：resources 主机统一 https，镜像指向 bmclapi 的 assets 路径。</summary>
    /// <param name="url">原始资源地址。</param>
    /// <param name="preferOfficial">为真时官方源在前，为假时镜像源在前。</param>
    public static IEnumerable<string> Assets(string url, bool preferOfficial)
    {
        var original = url.Replace("http://resources.download.minecraft.net", "https://resources.download.minecraft.net");
        var mirror = original
            .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/assets")
            .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/assets")
            .Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets");
        return Order(new[] { original }, new[] { mirror }, preferOfficial);
    }

    /// <summary>依赖库的候选地址：Forge/Fabric/NeoForge 库只给两个镜像候选，其余按偏好排序。</summary>
    /// <param name="url">原始库地址。</param>
    /// <param name="preferOfficial">为真时官方源在前，为假时镜像源在前；modded 库不受此参数影响。</param>
    public static IEnumerable<string> Library(string url, bool preferOfficial)
    {
        var maven = ReplaceHosts(url, "https://bmclapi2.bangbang93.com/maven");
        var libraries = ReplaceHosts(url, "https://bmclapi2.bangbang93.com/libraries");
        if (new[] { "minecraftforge", "fabricmc", "neoforged" }.Any(k => url.Contains(k)))
        {
            return new[] { maven, libraries };
        }
        return Order(new[] { url }, new[] { maven, libraries }, preferOfficial);
    }

    /// <summary>launcher 与 version manifest 等 JSON 的候选地址，按下载源设置排序；版本列表请改用 <see cref="SourcePolicy.PreferMojangForVersionList"/> 的判定。</summary>
    /// <param name="url">原始 JSON 地址。</param>
    /// <param name="preferOfficial">为真时官方源在前，为假时镜像源在前。</param>
    public static IEnumerable<string> LauncherOrMeta(string url, bool preferOfficial)
    {
        var mirror = url
            .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com");
        return Order(new[] { url }, new[] { mirror }, preferOfficial);
    }

    /// <summary>Modrinth/CurseForge 的 API 与 CDN 地址改写为 mcimirror 镜像；固定单地址，没有官方/镜像切换。</summary>
    /// <param name="url">原始 Mod 地址。</param>
    public static string Mod(string url)
    {
        return url
            .Replace("api.modrinth.com", "mod.mcimirror.top/modrinth")
            .Replace("staging-api.modrinth.com", "mod.mcimirror.top/modrinth")
            .Replace("cdn.modrinth.com", "mod.mcimirror.top")
            .Replace("api.curseforge.com", "mod.mcimirror.top/curseforge")
            .Replace("edge.forgecdn.net", "mod.mcimirror.top")
            .Replace("mediafilez.forgecdn.net", "mod.mcimirror.top")
            .Replace("media.forgecdn.net", "mod.mcimirror.top");
    }

    private static string ReplaceHosts(string url, string replacement)
    {
        foreach (var host in MavenHosts)
        {
            url = url.Replace(host, replacement);
        }
        return url;
    }
}