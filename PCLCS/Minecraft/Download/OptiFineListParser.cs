using Newtonsoft.Json.Linq;

namespace PCLCS.Minecraft.Download;

/// <summary>OptiFine 版本条目，字段与 DlOptiFineListEntry 一一对应。</summary>
public sealed record OptiFineEntry {
    /// <summary>创建条目；继承版本与实例名的版本前缀会去掉末尾的 .0。</summary>
    /// <param name="requiredForgeVersion">需要的最低 Forge 版本；空字符串为无限制，null 为不兼容，"28.1.56" 为版本号，"1161" 为版本号的最后一位。</param>
    public OptiFineEntry(
        string displayName,
        string fileName,
        string instanceName,
        bool isPreview,
        string inherit,
        string releaseTime,
        string? requiredForgeVersion) {
        DisplayName = displayName;
        FileName = fileName;
        IsPreview = isPreview;
        Inherit = NormalizeInherit(inherit);
        InstanceName = NormalizeInstanceName(instanceName, inherit);
        ReleaseTime = releaseTime;
        RequiredForgeVersion = requiredForgeVersion;
    }

    /// <summary>显示名称。</summary>
    public string DisplayName { get; init; }

    /// <summary>下载文件名。</summary>
    public string FileName { get; init; }

    /// <summary>实例名称。</summary>
    public string InstanceName { get; init; }

    /// <summary>是否为测试版。</summary>
    public bool IsPreview { get; init; }

    /// <summary>对应的 Minecraft 版本。</summary>
    public string Inherit { get; init; }

    /// <summary>发布时间。</summary>
    public string ReleaseTime { get; init; }

    /// <summary>需要的最低 Forge 版本；空字符串为无限制，null 为不兼容。</summary>
    public string? RequiredForgeVersion { get; init; }

    /// <summary>去掉继承版本末尾的 .0，与 DlOptiFineListEntry.Inherit 的 setter 一致。</summary>
    internal static string NormalizeInherit(string mcVersion) {
        return mcVersion.EndsWith(".0", StringComparison.Ordinal)
            ? mcVersion.Substring(0, mcVersion.Length - 2)
            : mcVersion;
    }

    private static string NormalizeInstanceName(string instanceName, string inherit) {
        var normalized = NormalizeInherit(inherit);
        if (normalized == inherit) return instanceName;
        var prefix = inherit + "-OptiFine_";
        return instanceName.StartsWith(prefix, StringComparison.Ordinal)
            ? normalized + instanceName.Substring(inherit.Length)
            : instanceName;
    }
}

/// <summary>OptiFine 版本列表解析。</summary>
public static class OptiFineListParser {
    /// <summary>解析 BMCLAPI JSON 列表。</summary>
    public static IReadOnlyList<OptiFineEntry> ParseBmclapiJson(string json) {
        JArray array;
        try {
            array = JArray.Parse(json);
        } catch (Newtonsoft.Json.JsonException ex) {
            throw new DownloadSourceException(DownloadFailureKind.InvalidPayload,
                "OptiFine BMCLAPI 返回的内容不是合法 JSON 数组", "BMCLAPI", DownloadDiagnostics.Snippet(json), ex);
        }
        var entries = new List<OptiFineEntry>();
        foreach (var token in array.OfType<JObject>()) {
            var mcVersion = token["mcversion"]?.ToString()
                ?? throw new DownloadSourceException(DownloadFailureKind.MissingField,
                    "OptiFine BMCLAPI 条目缺少 mcversion", "BMCLAPI", DownloadDiagnostics.Snippet(token.ToString()));
            var type = token["type"]?.ToString() ?? "";
            var patch = token["patch"]?.ToString() ?? "";
            var display = (mcVersion + type.Replace("HD_U", "").Replace("_", " ") + " " + patch).Replace(".0 ", " ");
            var required = (token["forge"]?.ToString() ?? "").Replace("Forge ", "").Replace("#", "");
            var inherit = OptiFineEntry.NormalizeInherit(mcVersion);
            var instanceSuffix = (type + " " + patch).Replace(".0 ", " ").Replace(" ", "_").Replace(inherit + "_", "");
            entries.Add(new OptiFineEntry(
                display,
                token["filename"]?.ToString() ?? "",
                mcVersion + "-OptiFine_" + instanceSuffix,
                patch.ContainsIgnoreCase("pre"),
                mcVersion,
                "",
                required.Contains("N/A") ? null : required));
        }
        return entries;
    }
}
