using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCLCS.Settings;

/// <summary>
/// Settings.json 的原子读写：顶层键序固定，损坏时返回空文档并告警，落盘前滚动备份。
/// </summary>
public sealed class SettingsStore
{
    readonly string _filePath;

    public SettingsStore(string filePath) => _filePath = filePath;

    /// <summary>滚动备份的落点：&lt;file&gt;.bak。</summary>
    public static string BackupPathFor(string filePath) => filePath + ".bak";

    public SettingsDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new SettingsDocument();
            var root = JsonNode.Parse(File.ReadAllText(_filePath)) as JsonObject;
            if (root is null)
            {
                Logger.Warn("设置文件顶层不是 JSON 对象，已按空文档处理。");
                return new SettingsDocument();
            }
            var document = new SettingsDocument();
            if (root["values"] is JsonObject values)
                foreach (var pair in values) document.Values[pair.Key] = pair.Value;
            if (root["encrypted"] is JsonObject encrypted)
                foreach (var pair in encrypted) document.Encrypted[pair.Key] = pair.Value;
            if (root["imported"] is JsonObject imported)
            {
                document.RegistryImported = imported["registry"]?.GetValue<bool>() ?? false;
                document.SetupIniImported = imported["setupIni"]?.GetValue<bool>() ?? false;
            }
            document.Extra = root["extra"] as JsonObject;
            return document;
        } catch (JsonException ex)
        {
            //! 损坏时不抛出：返回空文档让程序可启动，.bak 仍保留上一版供人工恢复
            Logger.Warn(ex, "设置文件 JSON 解析失败，已按空文档处理。");
            return new SettingsDocument();
        }
    }

    public void Save(SettingsDocument document)
    {
        //! DeepClone 隔离调用方文档：直接入树会把调用方的节点 re-parent，二次 Save 会抛“已有父节点”
        var values = new JsonObject();
        foreach (var pair in document.Values) values[pair.Key] = pair.Value?.DeepClone();
        var encrypted = new JsonObject();
        foreach (var pair in document.Encrypted) encrypted[pair.Key] = pair.Value?.DeepClone();
        var root = new JsonObject
        {
            ["schemaVersion"] = SettingsDocument.SchemaVersion,
            ["values"] = values,
            ["encrypted"] = encrypted,
            ["imported"] = new JsonObject
            {
                ["registry"] = document.RegistryImported,
                ["setupIni"] = document.SetupIniImported
            },
            ["extra"] = document.Extra?.DeepClone()
        };
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        if (File.Exists(_filePath)) File.Copy(_filePath, BackupPathFor(_filePath), true);
        var tmpPath = _filePath + ".tmp";
        File.WriteAllText(tmpPath, json, new UTF8Encoding(false));
#if NET48
        if (File.Exists(_filePath)) File.Replace(tmpPath, _filePath, null);
        else File.Move(tmpPath, _filePath);
#else
        File.Move(tmpPath, _filePath, true);
#endif
    }
}