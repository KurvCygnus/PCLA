using PCLCS.Json;
using PCLCS.Results;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCLCS.Minecraft.Download;

/// <summary>Fabric 版本列表解析。</summary>
public static class FabricListParser
{
    /// <summary>校验并返回 Fabric 列表；非法 JSON 或缺少字段时返回下载域错误。</summary>
    public static Result<JsonObject, DownloadError> Parse(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNodeUtils.ParseObject(json);
        } catch (JsonException ex)
        {
            return Result.Err<JsonObject, DownloadError>(new DownloadError(
                DownloadFailureKind.InvalidPayload, "Fabric 返回的内容不是合法 JSON", "Fabric",
                DownloadDiagnostics.Snippet(json), ex));
        }

        if (root["game"] is null || root["loader"] is null || root["installer"] is null)
        {
            return Result.Err<JsonObject, DownloadError>(new DownloadError(
                DownloadFailureKind.MissingField, "Fabric 列表缺少 game、loader 或 installer", "Fabric",
                DownloadDiagnostics.Snippet(json)));
        }

        return Result.Ok<JsonObject, DownloadError>(root);
    }

    /// <summary>VB 边界的兼容入口；失败抛 PclaException，由全局兜底记录。</summary>
    public static JsonObject ParseOrThrow(string json) => Parse(json).OrThrow();
}