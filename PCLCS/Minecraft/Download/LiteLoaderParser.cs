using System.Text.Json.Nodes;
using PCLCS.Json;

namespace PCLCS.Minecraft.Download;

/// <summary>LiteLoader 版本条目，字段与 DlLiteLoaderListEntry 一一对应。</summary>
/// <param name="JsonToken">该版本的原始 JSON 条目，调用方从中读取 version 等字段。</param>
public sealed record LiteLoaderEntry(
    string FileName,
    bool IsPreview,
    string Inherit,
    bool IsLegacy,
    string ReleaseTime,
    string MD5,
    JsonNode JsonToken);

/// <summary>LiteLoader 版本列表解析。</summary>
public static class LiteLoaderParser {
    /// <summary>解析 LiteLoader 官方源或 BMCLAPI 的版本列表。</summary>
    public static IReadOnlyList<LiteLoaderEntry> Parse(string json) {
        JsonObject root;
        try {
            root = JsonNodeUtils.ParseObject(json);
        } catch (System.Text.Json.JsonException ex) {
            throw new DownloadSourceException(DownloadFailureKind.InvalidPayload,
                "LiteLoader 返回的内容不是合法 JSON", "LiteLoader", DownloadDiagnostics.Snippet(json), ex);
        }
        if (root["versions"] is not JsonObject versions) {
            throw new DownloadSourceException(DownloadFailureKind.MissingField,
                "LiteLoader 响应缺少 versions 对象", "LiteLoader", DownloadDiagnostics.Snippet(json));
        }
        var entries = new List<LiteLoaderEntry>();
        foreach (var pair in versions) {
            if (pair.Key.StartsWith("1.6", StringComparison.Ordinal) || pair.Key.StartsWith("1.5", StringComparison.Ordinal)) {
                continue;
            }
            var real = (pair.Value?["artefacts"] ?? pair.Value?["snapshots"])?["com.mumfrey:liteloader"]?["latest"]
                ?? throw new DownloadSourceException(DownloadFailureKind.MissingField,
                    $"LiteLoader {pair.Key} 缺少 latest 条目", "LiteLoader", DownloadDiagnostics.Snippet(pair.Value!.ToJsonString()));
            //* 夹具与真实响应里的 timestamp 是字符串数字；JsonNode.GetValue<long> 不做字符串转换，这里显式解析以复刻旧引擎 Value<long> 的宽松行为。
            var timestamp = long.TryParse(real["timestamp"]?.ToString(), out var timestampValue)
                ? timestampValue
                : throw new DownloadSourceException(DownloadFailureKind.MissingField,
                    $"LiteLoader {pair.Key} 缺少 timestamp", "LiteLoader", DownloadDiagnostics.Snippet(real.ToJsonString()));
            var fileName = "liteloader-installer-" + pair.Key + (pair.Key is "1.8" or "1.9" ? ".0" : "") + "-00-SNAPSHOT.jar";
            entries.Add(new LiteLoaderEntry(
                fileName,
                string.Equals(real["stream"]?.ToString(), "snapshot", StringComparison.OrdinalIgnoreCase),
                pair.Key,
                int.Parse(pair.Key.Split('.')[1]) < 8,
                GetDoubleLocalTime(timestamp).ToString("yyyy/MM/dd HH:mm"),
                real["md5"]?.ToString() ?? "",
                real));
        }
        return entries;
    }

    //* 旧实现先按本地时区解析 Unix 秒，再把该值当作 UTC 转本地，形成双重换算；此处保真。
    private static DateTime GetDoubleLocalTime(long timestamp) {
        var local = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
        return DateTime.SpecifyKind(local, DateTimeKind.Utc).ToLocalTime();
    }
}
