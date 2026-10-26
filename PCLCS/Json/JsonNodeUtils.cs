using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCLCS.Json;

/// <summary>System.Text.Json 节点解析的公共入口，统一解析选项与取值辅助。</summary>
public static class JsonNodeUtils {
    public static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    public static readonly JsonDocumentOptions DocumentOptions = new() {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    public static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public static JsonNode Parse(string json) =>
        JsonNode.Parse(json, NodeOptions, DocumentOptions)
        ?? throw new JsonException("JSON 输入为空。");

    public static JsonObject ParseObject(string json) =>
        Parse(json).AsObject();

    public static JsonObject? AsObject(JsonNode? node) => node as JsonObject;
    public static JsonArray? AsArray(JsonNode? node) => node as JsonArray;

    public static string? GetString(JsonNode? node, string name) =>
        node?[name]?.GetValue<string>();

    public static bool? GetBool(JsonNode? node, string name) =>
        node?[name]?.GetValue<bool>();

    public static int? GetInt(JsonNode? node, string name) =>
        node?[name]?.GetValue<int>();
}
