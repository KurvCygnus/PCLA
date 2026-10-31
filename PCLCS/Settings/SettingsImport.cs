using PCLCS.Startup;

namespace PCLCS.Settings;

/// <summary>
/// 旧数据的一次性导入：全局（注册表 + 全局 Setup.ini）与每实例（&lt;实例&gt;\PCL\Setup.ini）。
/// 只补缺失键，导入后把标记写回文档本身，旧数据全程保留。
/// </summary>
public static class SettingsImport
{
    private const string RegistryPath = @"HKEY_CURRENT_USER\Software\" + LauncherIdentity.RegistryFolder;

    /// <summary>全局一次性导入：注册表源与旧全局 Setup.ini（Normal 源），标记写入全局文档。</summary>
    public static void EnsureImported()
    {
        var document = SettingsService.AcquireGlobalDocument();
        var imported = false;
        if (!document.RegistryImported)
        {
            ImportFromRegistry(document);
            document.RegistryImported = true;
            imported = true;
        }
        if (!document.SetupIniImported)
        {
            ImportFromSetupIni(document);
            document.SetupIniImported = true;
            imported = true;
        }
        if (!imported) return;
        SettingsService.SaveGlobalDocument();
        SettingsService.ClearCaches();   // 导入前读出的默认值缓存已作废
    }

    /// <summary>懒式导入某个实例的旧设置：&lt;实例&gt;\PCL\Setup.ini → &lt;实例&gt;\.pcla\Settings.json。</summary>
    public static void EnsureInstanceImported(string instanceKey)
    {
        var bucket = SettingsService.NormalizeInstanceKey(instanceKey);
        if (bucket.Length == 0) return;
        var document = SettingsService.AcquireInstanceDocument(bucket);
        if (document.SetupIniImported) return;
        var iniPath = Path.Combine(bucket, "PCL", "Setup.ini");
        if (File.Exists(iniPath)) ImportMissing(document, SettingsSource.Instance, ParseIni(iniPath));
        document.SetupIniImported = true;
        SettingsService.SaveInstanceDocument(bucket);
        SettingsService.ClearInstanceCache(bucket);
    }

    private static void ImportFromRegistry(SettingsDocument document)
    {
        foreach (var entry in SettingsCatalog.Entries.Values)
        {
            if (entry.Source != SettingsSource.Registry) continue;
            object? raw;
            bool exists;
            try
            {
                (raw, exists) = RegistryUtils.Read(RegistryPath, entry.Key);
            } catch (Exception ex)
            {
                Logger.Warn(ex, $"读取旧注册表设置失败：{entry.Key}");
                continue;
            }
            if (exists && raw is not null) FillMissing(document, entry, raw);
        }
    }

    private static void ImportFromSetupIni(SettingsDocument document)
    {
        var iniPath = Path.Combine(SettingsService.GlobalFolder, "Setup.ini");
        if (File.Exists(iniPath)) ImportMissing(document, SettingsSource.Normal, ParseIni(iniPath));
    }

    //* 只补缺失：文档已有值（含新写入的）优先于旧数据
    private static void ImportMissing(SettingsDocument document, SettingsSource source, Dictionary<string, string> values)
    {
        foreach (var entry in SettingsCatalog.Entries.Values)
        {
            if (entry.Source != source || !values.TryGetValue(entry.Key, out var raw)) continue;
            FillMissing(document, entry, raw);
        }
    }

    private static void FillMissing(SettingsDocument document, SettingsEntry entry, object? raw)
    {
        if (entry.Encrypted ? document.Encrypted.ContainsKey(entry.Key) : document.Values.ContainsKey(entry.Key)) return;
        try
        {
            var plain = entry.Encrypted ? DecryptLegacy(raw) : raw;
            if (plain is null) return;
            SettingsService.WritePlainToDocument(document, entry, SettingsService.Coerce(plain, entry.ValueType));
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"导入旧设置失败：{entry.Key}");
        }
    }

    //* 加密键先按旧算法解密再入新文档（"PCL"+Identify，与 SettingsCrypto 同一套算法）；解不开即视为不可用，跳过
    private static object? DecryptLegacy(object? raw)
    {
        try
        {
            return SettingsCrypto.Decrypt(raw?.ToString() ?? "", SettingsService.Passphrase);
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"解密旧加密设置失败，已跳过");
            return null;
        }
    }

    //* ModBase.IniGetContent 同款规则：首个冒号切分，键值两侧原样保留，后行覆盖前行；无冒号行忽略
    private static Dictionary<string, string> ParseIni(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            var index = line.IndexOf(':');
            if (index > 0) map[line[..index]] = line[(index + 1)..];
        }
        return map;
    }
}