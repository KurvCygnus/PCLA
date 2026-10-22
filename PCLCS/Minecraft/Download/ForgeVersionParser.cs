using Newtonsoft.Json.Linq;

namespace PCLCS.Minecraft.Download;

/// <summary>Forge 的 Minecraft 版本列表。</summary>
public static class ForgeVersionParser {
    /// <summary>解析 Forge 官方下载页中的 Minecraft 版本。</summary>
    /// <param name="html">官方下载页的 HTML。</param>
    /// <exception cref="DownloadSourceException">响应过短或版本数量不足。</exception>
    public static IReadOnlyList<string> ParseOfficialHtml(string html) {
        if (html.Length < 200) {
            throw new DownloadSourceException(DownloadFailureKind.TooFewEntries,
                $"Forge 官方源响应只有 {html.Length} 个字符", "Forge 官方源", DownloadDiagnostics.Snippet(html));
        }
        var names = Regex.Matches(html, "(?<=a href=\"index_)[0-9.]+((_pre|[_-]snapshot[_-]?)[0-9]?)?(?=.html)")
            .Cast<Match>().Select(m => m.Value).ToList();
        names.Add("1.2.4"); //! 官方页从 1.2.4 开始，但该版本不在列表中。
        if (names.Count < 10) {
            throw new DownloadSourceException(DownloadFailureKind.TooFewEntries,
                $"Forge 官方源只解析出 {names.Count} 个版本（至少 10 个）", "Forge 官方源", DownloadDiagnostics.Snippet(html));
        }
        return names;
    }

    /// <summary>解析 BMCLAPI 返回的 Forge Minecraft 版本数组。</summary>
    /// <param name="json">BMCLAPI 的 JSON 数组。</param>
    /// <exception cref="DownloadSourceException">JSON 非法或版本数量不足。</exception>
    public static IReadOnlyList<string> ParseBmclapiJson(string json) {
        JArray array;
        try {
            array = JArray.Parse(json);
        } catch (Newtonsoft.Json.JsonException ex) {
            throw new DownloadSourceException(DownloadFailureKind.InvalidPayload,
                "Forge BMCLAPI 返回的内容不是合法 JSON 数组", "BMCLAPI", DownloadDiagnostics.Snippet(json), ex);
        }
        var names = array.Select(token => token.ToString()).ToList();
        if (names.Count < 10) {
            throw new DownloadSourceException(DownloadFailureKind.TooFewEntries,
                $"BMCLAPI 只返回了 {names.Count} 个 Forge 版本（至少 10 个）", "BMCLAPI", DownloadDiagnostics.Snippet(json));
        }
        return names;
    }
}
