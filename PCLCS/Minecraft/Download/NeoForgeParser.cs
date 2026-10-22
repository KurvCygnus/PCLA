using System.Text.RegularExpressions;

namespace PCLCS.Minecraft.Download;

/// <summary>NeoForge 版本条目，字段与 DlNeoForgeListEntry 一一对应。</summary>
public sealed class NeoForgeEntry : IComparable<NeoForgeEntry> {
    /// <summary>从 API 的原始版本字符串创建条目。</summary>
    /// <param name="apiName">API 使用的原始版本字符串，如 20.4.30-beta、1.20.1-47.1.99。</param>
    public NeoForgeEntry(string apiName) {
        ApiName = apiName;
        IsBeta = apiName.ContainsIgnoreCase("beta") || apiName.ContainsIgnoreCase("alpha");
        if (apiName.Contains("1.20.1")) {
            VersionName = apiName.Replace("1.20.1-", "");
            Version = new Version("19." + VersionName);
            Inherit = "1.20.1";
        } else if (apiName.StartsWith("0.", StringComparison.Ordinal)) {
            VersionName = apiName;
            var segments = apiName.BeforeFirst("-").Split('.');
            Version = new Version(0, 0, int.Parse(segments[segments.Length - 1]));
            Inherit = segments[1];
        } else {
            VersionName = apiName;
            Version = new Version(apiName.BeforeFirst("-"));
            Inherit = Version.Major >= 24
                ? $"{Version.Major}.{Version.Minor}.{Version.Build}"
                : $"1.{Version.Major}.{Version.Minor}";
            if (Inherit.EndsWith(".0", StringComparison.Ordinal)) Inherit = Inherit.Substring(0, Inherit.Length - 2);
            if (VersionName.Contains("+")) Inherit += "-" + VersionName.AfterFirst("+");
        }
    }

    /// <summary>API 使用的原始版本字符串。</summary>
    public string ApiName { get; }

    /// <summary>标准化后的版本号，仅用于比较与排序，不包含 alpha/beta 编号。</summary>
    public Version Version { get; }

    /// <summary>可读的非格式化版本名。</summary>
    public string VersionName { get; }

    /// <summary>对应的 Minecraft 版本，如 1.20.1、26.1-snapshot-1。</summary>
    public string Inherit { get; }

    /// <summary>是否为 Beta 或 Alpha 版。</summary>
    public bool IsBeta { get; }

    /// <summary>按标准化版本号排序，同版本时再比较可读版本名。</summary>
    public int CompareTo(NeoForgeEntry? other) {
        if (other is null) return 1;
        return Version != other.Version
            ? Version.CompareTo(other.Version)
            : McUtils.CompareVersion(VersionName, other.VersionName);
    }
}

/// <summary>NeoForge 版本列表解析。</summary>
public static class NeoForgeParser {
    /// <summary>合并官方最新版与 Legacy 版本两个 JSON 源并排序。</summary>
    /// <param name="latestJson">neoforge 仓库的版本 JSON。</param>
    /// <param name="legacyJson">forge 仓库的 Legacy 版本 JSON。</param>
    /// <exception cref="DownloadSourceException">两个源都没有可用版本。</exception>
    public static IReadOnlyList<NeoForgeEntry> Parse(string latestJson, string legacyJson) {
        var names = Regex.Matches(legacyJson + latestJson,
                "(?<=\")(1\\.20\\.1-)?\\d+\\.[^\\.]+\\.\\d+(\\.\\d+)?(-(beta|alpha)(\\.\\d+)?)?(\\+snapshot-\\d+)?(?=\")")
            .Cast<Match>()
            .Select(m => m.Value)
            .Where(name => name != "47.1.82")
            .Select(name => new NeoForgeEntry(name))
            .OrderByDescending(entry => entry)
            .ToList();
        if (names.Count == 0) {
            throw new DownloadSourceException(DownloadFailureKind.NotFound,
                "NeoForge 源返回了 0 个可用版本", "NeoForge", DownloadDiagnostics.Snippet(latestJson));
        }
        return names;
    }
}
