using PCLCS.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCLCS.Minecraft.Download;

/// <summary>Minecraft 客户端版本清单。</summary>
public static class ClientListParser
{
    private const int MinimumEntries = 200;

    /// <summary>校验并返回 JSON；条目不足或结构异常时抛结构化错误。</summary>
    public static JsonObject Parse(string json, string sourceName)
    {
        JsonObject root;
        try
        {
            root = JsonNodeUtils.ParseObject(json);
        } catch (JsonException ex)
        {
            throw new DownloadSourceException(DownloadFailureKind.InvalidPayload,
                $"{sourceName} 返回的内容不是合法 JSON", sourceName, DownloadDiagnostics.Snippet(json), ex);
        }
        if (root["versions"] is not JsonArray versions)
        {
            throw new DownloadSourceException(DownloadFailureKind.MissingField,
                $"{sourceName} 的清单缺少 versions 数组", sourceName, DownloadDiagnostics.Snippet(json));
        }
        if (versions.Count < MinimumEntries)
        {
            throw new DownloadSourceException(DownloadFailureKind.TooFewEntries,
                $"{sourceName} 只返回了 {versions.Count} 个版本（至少 {MinimumEntries} 个）",
                sourceName, DownloadDiagnostics.Snippet(json));
        }
        return root;
    }

    /// <summary>提取所有正式版的 Drop 序数，去重后降序。</summary>
    public static IReadOnlyList<int> ComputeDrops(JsonObject json)
    {
        var versions = json["versions"] as JsonArray
            ?? throw new DownloadSourceException(DownloadFailureKind.MissingField, "版本清单缺少 versions 数组");
        return versions
            .Select(v => v?["id"]?.GetValue<string>())
            .Where(id => !string.IsNullOrEmpty(id) && !id!.Contains('-'))
            .Select(id => McVersion.VersionToDrop(id!))
            .Where(drop => drop != 209)
            .Distinct()
            .OrderByDescending(drop => drop)
            .ToList();
    }

    /// <summary>清单中是否存在指定 id。</summary>
    public static bool ContainsVersion(JsonObject json, string id)
    {
        return (json["versions"] as JsonArray)?.Any(v => v?["id"]?.GetValue<string>() == id) == true;
    }

    /// <summary>规范化版本 id：下划线转连字符，非 1.0 去掉尾部 .0。</summary>
    public static string NormalizeId(string id)
    {
        id = id.Replace('_', '-');
        if (id != "1.0" && id.EndsWith(".0", StringComparison.Ordinal)) id = id.Substring(0, id.Length - 2);
        return id;
    }
}