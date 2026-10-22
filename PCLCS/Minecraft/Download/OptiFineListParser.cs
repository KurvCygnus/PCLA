using Newtonsoft.Json.Linq;

namespace PCLCS.Minecraft.Download;

/// <summary>OptiFine 版本条目，字段与 DlOptiFineListEntry 一一对应。</summary>
/// <param name="RequiredForgeVersion">需要的最低 Forge 版本；空字符串为无限制，null 为不兼容，"28.1.56" 为版本号，"1161" 为版本号的最后一位。</param>
public sealed record OptiFineEntry(
    string DisplayName,
    string FileName,
    string InstanceName,
    bool IsPreview,
    string Inherit,
    string ReleaseTime,
    string? RequiredForgeVersion);

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
            var instanceSuffix = (type + " " + patch).Replace(".0 ", " ").Replace(" ", "_").Replace(mcVersion + "_", "");
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
