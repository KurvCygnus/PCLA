using System.Globalization;
using System.Text.Json.Nodes;

namespace PCLCS.Settings;

/// <summary>
/// 设置读写服务，语义自 PCLA\Pages\PageSetup\Settings.vb（Get/Set/Reset/HasSaved）逐条移植。
/// 存储路由：Normal 与 Registry 落全局 Settings.json（Registry 键由一次性导入搬入），
/// Instance 落 &lt;实例目录&gt;\.pcla\Settings.json。
/// </summary>
public static class SettingsService
{
    private const string FallbackIdentify = "PCLCS";

    //* 与 VB ValueCache（Settings.vb:225-234）同构：Instance 键按实例分桶，其余固定落在 "" 桶
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object?>> Caches = new();
    private static readonly ConcurrentDictionary<string, SettingsDocument> InstanceDocuments = new();

    //* VB Get :307-310 的 ForcedSettings：这两键永远读作 True
    private static readonly Dictionary<string, object> ForcedSettings = new()
    {
        ["UiHiddenPageLink"] = true,
        ["UiHiddenSetupLink"] = true
    };

    private static SettingsDocument? globalDocument;

    /// <summary>加密口令提供者；VB 侧启动时指向 GetIdentify()。默认返回常量识别码。</summary>
    public static Func<string> IdentifyProvider { get; set; } = static () => FallbackIdentify;

    /// <summary>值实际改变并保存后触发（key, 新值, 实例键；实例键为 PathVersion 或 ""）。</summary>
    public static event Action<string, object, string>? SettingChanged;

    /// <summary>设定全局数据目录并清空内存状态；测试可重复指向临时目录（模拟重启）。</summary>
    public static void Configure(string globalDataFolder)
    {
        GlobalFolder = globalDataFolder;
        globalDocument = null;   // 下次访问时重读磁盘
        ClearCaches();
        InstanceDocuments.Clear();
    }

    public static object Get(string key, string? instanceKey = null)
    {
        var entry = SettingsCatalog.Get(key);
        var bucket = BucketKey(entry, instanceKey);
        var cache = Caches.GetOrAdd(bucket, static _ => new ConcurrentDictionary<string, object?>());
        if (cache.TryGetValue(entry.Key, out var cached) && cached is not null) return cached;
        try
        {
            if (ForcedSettings.TryGetValue(entry.Key, out var forced))
            {
                //* VB :313 只写缓存；这里额外落盘，使 HasSaved（文档含键即真）对 Forced 键同样成立；
                //* 落盘失败与普通读取一样走下方 catch 回落默认（VB Get 整体兜底语义），不再外抛
                cache[entry.Key] = forced;
                WriteStoredValue(entry, forced, bucket);
                return forced;
            }
            if (entry.Source == SettingsSource.Instance && bucket.Length == 0)
                throw new InvalidOperationException($"读取版本设置 {key} 时未提供目标版本");
            object? got;
            var document = AcquireDocument(entry.Source, bucket);
            if (entry.Encrypted)
            {
                var passphrase = Passphrase;
                var encryptedDefault = SettingsCrypto.Encrypt(Stringify(entry.DefaultValue), passphrase);
                var data = document.Encrypted.TryGetValue(entry.Key, out var node) ? node?["data"]?.GetValue<string>() : null;
                if (data is null || data == encryptedDefault)
                {
                    got = entry.DefaultValue;
                } else
                {
                    try
                    {
                        got = SettingsCrypto.Decrypt(data, passphrase);
                    } catch (Exception ex)
                    {
                        Logger.Warn(ex, $"解密设置失败：{key}");
                        got = entry.DefaultValue;
                        WriteStoredValue(entry, entry.DefaultValue, bucket);   // VB :341-342 解密失败回落默认并重存
                    }
                }
            } else if (document.Values.TryGetValue(entry.Key, out var plain) && plain is not null)
            {
                got = NodeToObject(plain);
            } else
            {
                got = entry.DefaultValue;
            }
            var coerced = Coerce(got, entry.ValueType)!;
            cache[entry.Key] = coerced;
            return coerced;
        } catch (Exception ex)
        {
            Logger.Error(ex, $"读取设置失败：{key}");
            var fallback = Coerce(entry.DefaultValue, entry.ValueType)!;
            cache[entry.Key] = fallback;
            return fallback;
        }
    }

    public static T Get<T>(string key, string? instanceKey = null)
        => (T) Coerce(Get(key, instanceKey), typeof(T))!;

    /// <summary>获取某个未经加密的设置项的值；若该设置项经过加密，则抛出异常。</summary>
    public static object GetSafe(string key, string? instanceKey = null)
        => SettingsCatalog.Get(key).Encrypted
            ? throw new InvalidOperationException("禁止读取加密设置项：" + key)
            : Get(key, instanceKey);

    public static void Set(string key, object? value, string? instanceKey = null)
    {
        var entry = SettingsCatalog.Get(key);   // 未知键抛出（VB :272），不吞
        try
        {
            var coerced = Coerce(value, entry.ValueType)!;
            var bucket = BucketKey(entry, instanceKey);
            var cache = Caches.GetOrAdd(bucket, static _ => new ConcurrentDictionary<string, object?>());
            if (!cache.TryGetValue(entry.Key, out var current))
            {
                Get(key, instanceKey);   // VB :276：先填充缓存再判等
                cache.TryGetValue(entry.Key, out current);
            }
            if (Equals(current, coerced)) return;
            cache[entry.Key] = coerced;
            WriteStoredValue(entry, coerced, bucket);
            SettingChanged?.Invoke(key, coerced, bucket);   // VB :281 必须在保存之后触发
        } catch (Exception ex)
        {
            Logger.Error(ex, $"设置设置项时出错（{key}, {value}）");
        }
    }

    /// <summary>写入某个未经加密的设置项；若该设置项经过加密，则抛出异常。</summary>
    public static void SetSafe(string key, object? value, string? instanceKey = null)
    {
        if (SettingsCatalog.Get(key).Encrypted) throw new InvalidOperationException("禁止写入加密设置项：" + key);
        Set(key, value, instanceKey);
    }

    /// <summary>初始化某个设置项的值：移除持久化键并回落默认。</summary>
    public static void Reset(string key, string? instanceKey = null)
    {
        var entry = SettingsCatalog.Get(key);
        try
        {
            var bucket = BucketKey(entry, instanceKey);
            if (entry.Source == SettingsSource.Instance && bucket.Length == 0)
                throw new InvalidOperationException($"重置版本设置 {key} 时未提供目标版本");
            var cache = Caches.GetOrAdd(bucket, static _ => new ConcurrentDictionary<string, object?>());
            if (!cache.TryGetValue(entry.Key, out var current))
            {
                Get(key, instanceKey);
                cache.TryGetValue(entry.Key, out current);
            }
            var defaultValue = Coerce(entry.DefaultValue, entry.ValueType)!;
            if (Equals(current, defaultValue)) return;
            cache[entry.Key] = defaultValue;
            var document = AcquireDocument(entry.Source, bucket);
            document.Values.Remove(entry.Key);
            document.Encrypted.Remove(entry.Key);
            PersistDocument(entry.Source, bucket);
            SettingChanged?.Invoke(key, defaultValue, bucket);
        } catch (Exception ex)
        {
            Logger.Error(ex, $"重置设置项时出错（{key}）");
        }
    }

    /// <summary>获取某个设置项的默认值（未经转换的原始对象）。</summary>
    //* 简报接口写的是 string 返回，但其自带测试 Assert.Equal(42, GetDefault(...)) 要求原始默认值；
    //* 与 VB 实际行为一致返回 Object，VB 转发层自行转换
    public static object GetDefault(string key) => SettingsCatalog.Get(key).DefaultValue;

    /// <summary>对应的文档是否已经储存了某个设置项（文档分节含键即真）。</summary>
    public static bool HasSaved(string key, string? instanceKey = null)
    {
        var entry = SettingsCatalog.Get(key);
        var bucket = BucketKey(entry, instanceKey);
        if (entry.Source == SettingsSource.Instance && bucket.Length == 0)
            throw new InvalidOperationException($"判断版本设置 {key} 是否存在时未提供目标版本");
        var document = AcquireDocument(entry.Source, bucket);
        return entry.Encrypted ? document.Encrypted.ContainsKey(entry.Key) : document.Values.ContainsKey(entry.Key);
    }

    /// <summary>清空某个实例的设置缓存（仅内存）；持久化值保留，随后的 Get 重新从磁盘读回。</summary>
    //* 对应 VB McInstance.ResetSettingsCache 的第三段（ModMinecraft.vb:563-570，
    //* ValueCache.Remove(PathVersion)）：仅清内存缓存，与 VB 一致；磁盘值刻意保留
    public static void ResetInstanceCache(string instanceKey)
    {
        var bucket = NormalizeInstanceKey(instanceKey);
        if (bucket.Length == 0) return;
        Caches.TryRemove(bucket, out _);
    }

    /// <summary>移除某个设置项在某个实例（或全局 ""）缓存桶中的内存缓存值；持久化值保留。</summary>
    //* VB Setting.ValueCache.Remove 的转发目标（ModMinecraft.ResetSettingsCache 的单键兼容面），
    //* 与 ResetInstanceCache 同为仅内存语义；桶不存在时静默返回
    public static void RemoveInstanceCacheEntry(string key, string instanceKey)
    {
        if (!Caches.TryGetValue(NormalizeInstanceKey(instanceKey), out var cache)) return;
        cache.TryRemove(key, out _);
    }

    /// <summary>全局数据目录根，由 <see cref="Configure"/> 指定。</summary>
    internal static string GlobalFolder { get; private set; } = "";

    //* VB DesEncrypt/DesDecrypt 的固定口令前缀（Settings.vb:245）
    internal static string Passphrase => "PCL" + IdentifyProvider();

    //* 实例键遵循 VB PathVersion 约定（尾部带分隔符）；内部统一归一，使带与不带尾分隔符指向同一文档
    internal static string NormalizeInstanceKey(string? instanceKey)
        => (instanceKey ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    internal static object? Coerce(object? value, Type targetType)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is null) return type.IsValueType ? Activator.CreateInstance(type) : null;
        //* bool 接受 "True"/"False"（旧 ini 文本语义），其余走 Convert（对齐 VB CTypeDynamic）
        return type.IsInstanceOfType(value) ? value : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }

    internal static string Stringify(object? value)
    {
        if (value is null) return "";
        if (value is string s) return s;
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    internal static JsonNode ToNode(object? value) => value switch
    {
        null => JsonValue.Create(""),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        _ => JsonValue.Create(Stringify(value))
    };

    /// <summary>把明文值写入文档对应分节（加密键先加密为 v1 标记节点），供服务与导入共用。</summary>
    internal static void WritePlainToDocument(SettingsDocument document, SettingsEntry entry, object? plainValue)
    {
        if (entry.Encrypted)
            document.Encrypted[entry.Key] = new JsonObject { ["$enc"] = "v1", ["data"] = SettingsCrypto.Encrypt(Stringify(plainValue), Passphrase) };
        else
            document.Values[entry.Key] = ToNode(plainValue);
    }

    internal static SettingsDocument AcquireGlobalDocument()
        => globalDocument ??= new SettingsStore(Path.Combine(GlobalFolder, "Settings.json")).Load();

    internal static void SaveGlobalDocument()
    {
        Directory.CreateDirectory(GlobalFolder);
        new SettingsStore(Path.Combine(GlobalFolder, "Settings.json")).Save(AcquireGlobalDocument());
    }

    internal static SettingsDocument AcquireInstanceDocument(string bucket)
        => InstanceDocuments.GetOrAdd(bucket, static key => new SettingsStore(Path.Combine(key, ".pcla", "Settings.json")).Load());

    internal static void SaveInstanceDocument(string bucket)
    {
        Directory.CreateDirectory(Path.Combine(bucket, ".pcla"));
        new SettingsStore(Path.Combine(bucket, ".pcla", "Settings.json")).Save(AcquireInstanceDocument(bucket));
    }

    internal static void ClearCaches() => Caches.Clear();

    internal static void ClearInstanceCache(string bucket) => Caches.TryRemove(bucket, out _);

    private static string BucketKey(SettingsEntry entry, string? instanceKey)
        => entry.Source == SettingsSource.Instance ? NormalizeInstanceKey(instanceKey) : "";

    private static void WriteStoredValue(SettingsEntry entry, object? value, string bucket)
    {
        if (entry.Source == SettingsSource.Instance && bucket.Length == 0)
            throw new InvalidOperationException($"保存版本独立设置 {entry.Key} 时未提供目标版本");
        var document = AcquireDocument(entry.Source, bucket);
        WritePlainToDocument(document, entry, value);
        PersistDocument(entry.Source, bucket);
    }

    private static void PersistDocument(SettingsSource source, string bucket)
    {
        if (source == SettingsSource.Instance) SaveInstanceDocument(bucket);
        else SaveGlobalDocument();
    }

    private static SettingsDocument AcquireDocument(SettingsSource source, string bucket)
        => source == SettingsSource.Instance ? AcquireInstanceDocument(bucket) : AcquireGlobalDocument();

    private static object NodeToObject(JsonNode node)
    {
        if (node is not JsonValue value) return node.ToJsonString();
        if (value.TryGetValue<bool>(out var b)) return b;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<int>(out var i)) return i;
        return value.TryGetValue<string>(out var s) ? s : node.ToJsonString();
    }
}