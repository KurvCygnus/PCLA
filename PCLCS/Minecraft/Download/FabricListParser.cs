using Newtonsoft.Json.Linq;

namespace PCLCS.Minecraft.Download;

/// <summary>Fabric 版本列表解析。</summary>
public static class FabricListParser {
    /// <summary>校验并返回 Fabric 列表；缺少 game、loader 或 installer 时抛结构化错误。</summary>
    public static JObject Parse(string json) {
        JObject root;
        try {
            root = JObject.Parse(json);
        } catch (Newtonsoft.Json.JsonException ex) {
            throw new DownloadSourceException(DownloadFailureKind.InvalidPayload,
                "Fabric 返回的内容不是合法 JSON", "Fabric", DownloadDiagnostics.Snippet(json), ex);
        }
        if (root["game"] is null || root["loader"] is null || root["installer"] is null) {
            throw new DownloadSourceException(DownloadFailureKind.MissingField,
                "Fabric 列表缺少 game、loader 或 installer", "Fabric", DownloadDiagnostics.Snippet(json));
        }
        return root;
    }
}
