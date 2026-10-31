using System.Text.Json.Nodes;

namespace PCLCS.Settings;

/// <summary>
/// 设置值的来源，决定读写落点（文件 / 注册表 / 实例目录）。
/// </summary>
public enum SettingsSource { Normal, Registry, Instance }

/// <summary>
/// 单项设置的定义：键、默认值、来源与是否加密存储。
/// </summary>
public sealed class SettingsEntry
{
    public SettingsEntry(string key, object? defaultValue, SettingsSource source, bool encrypted)
    {
        Key = key;
        DefaultValue = defaultValue!;
        Source = source;
        Encrypted = encrypted;
        ValueType = defaultValue?.GetType() ?? typeof(string);
    }

    public string Key { get; }
    public object DefaultValue { get; }
    public SettingsSource Source { get; }
    public bool Encrypted { get; }
    public Type ValueType { get; }
}

/// <summary>
/// Settings.json 的内存模型。加密节存放 {"$enc":"v1","data":...} 节点，解密由读取方负责。
/// </summary>
public sealed class SettingsDocument
{
    public const int SchemaVersion = 1;

    /// <summary>明文节。</summary>
    public Dictionary<string, JsonNode?> Values { get; } = [];

    /// <summary>加密节，值为 {"$enc":"v1","data":...} 节点。</summary>
    public Dictionary<string, JsonNode?> Encrypted { get; } = [];

    public bool RegistryImported { get; set; }
    public bool SetupIniImported { get; set; }

    /// <summary>未知键原样保真，避免未来版本的键在回写时丢失。</summary>
    public JsonObject? Extra { get; set; }
}