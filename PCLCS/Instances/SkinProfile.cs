using PCLCS.Settings;
using System.Globalization;
using System.Text.Json.Nodes;

namespace PCLCS.Instances;

/// <summary>
/// 皮肤地址解析与模型性别判定（自 VB McSkinGetAddress / McSkinSex 移植，ModMinecraft.vb 旧 :1409-1455 与 :1477-1485）。
/// 判定链整体在 C#；皮肤地址 ini 缓存、皮肤返回值网络请求与“当前所选实例”状态按架构约束留在 VB
/// （ini 工具与下载耦合点不迁 C#），由 VB 转发层经回调参数注入。
/// </summary>
public static class SkinProfile
{
    /// <summary>由 UUID 与皮肤类型解析皮肤地址（McSkinGetAddress 旧 :1409-1455 的判定链），失败将抛出异常。</summary>
    /// <param name="uuid">玩家 UUID；空值抛出（旧 :1410，VB 层传入 Nothing 时同样按空处理）。</param>
    /// <param name="type">皮肤类型：Mojang / Ms / Nide / Auth；其他值抛出（旧 :1424-1425）。</param>
    /// <param name="cacheRead">VB 注入：读取皮肤地址 ini 缓存（旧 :1413 ReadIni，Cache\Skin\Index{Type}.ini）；未命中返回空。</param>
    /// <param name="cacheWrite">VB 注入：写入皮肤地址 ini 缓存（旧 :1452 WriteIni）。</param>
    /// <param name="fetchProfile">VB 注入：请求皮肤返回值（旧 :1427 NetRequestByClientRetry(url, RequireJson:=True)）。</param>
    /// <param name="selectedInstanceKey">VB 注入：当前所选实例的版本路径，未选择实例时为 null（旧 :1421/:1423 的 McInstanceSelected）。</param>
    public static string GetAddress(string? uuid, string? type,
        Func<string, string?, string?> cacheRead,
        Action<string, string?, string> cacheWrite,
        Func<string, string> fetchProfile,
        Func<string?> selectedInstanceKey)
    {
        // 旧 :1410：VB“UUID = ""”对 Nothing 同样成立，null 或空串均抛出
        if (uuid is not { Length: > 0 }) throw new Exception("UUID 为空。");
        // 旧 :1411
        if (uuid.StartsWith("00000", StringComparison.Ordinal) && type != "Auth")
            throw new Exception("离线 UUID 无正版皮肤文件：" + uuid);
        //尝试读取缓存（旧 :1413-1414；VB“Not CacheSkinAddress = ""”即非空命中）
        var cachedAddress = cacheRead(uuid, type);
        if (cachedAddress is { Length: > 0 }) return cachedAddress;
        //获取皮肤地址（旧 :1416-1426）
        string url = type switch
        {
            "Mojang" or "Ms" => "https://sessionserver.mojang.com/session/minecraft/profile/",
            "Nide" => "https://auth.mc-user.com:233/" + ServerName("CacheNideServer", "VersionServerNide", selectedInstanceKey) + "/sessionserver/session/minecraft/profile/",
            "Auth" => ServerName("CacheAuthServerServer", "VersionServerAuthServer", selectedInstanceKey) + "/sessionserver/session/minecraft/profile/",
            _ => throw new ArgumentException("皮肤地址种类无效：" + (type ?? "null"))
        };
        string skinString = fetchProfile(url + uuid);
        // 旧 :1428（VB“SkinString = ""”对 Nothing 同样成立）
        if (string.IsNullOrEmpty(skinString)) throw new Exception("皮肤返回值为空，可能是未设置自定义皮肤的用户");
        //处理皮肤地址（旧 :1430-1442）
        string? skinValue = null;
        try
        {
            var found = false;
            // 旧 :1432 为经 Newtonsoft 的 DeserializeJson()("properties") 迭代：输入非法时在迭代处抛出并走 Catch；
            // STJ 下 Parse / 索引 / AsArray 的异常类型不同，但同样落入下方 Catch 包装
            foreach (JsonNode? property in JsonNode.Parse(skinString)!["properties"]!.AsArray())
            {
                if (property is null || property["name"]?.ToString() != "textures") continue;
                // 旧 :1434：value 缺失或为 JSON null 时 skinValue 为 null，随后在 FromBase64String 处与旧代码同样抛出
                skinValue = property["value"]?.ToString();
                found = true;
                break;   // 旧 Exit Try
            }
            if (!found) throw new Exception("未从皮肤返回值中找到符合条件的 Property");   // 旧 :1438，同样被下方 Catch 包装
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"无法完成解析的皮肤返回值，可能是未设置自定义皮肤的用户：{skinString}");
            throw new Exception("皮肤返回值中不包含皮肤数据项，可能是未设置自定义皮肤的用户", ex);
        }
        // 旧 :1443-1444：Base64 解码后整体转小写再解析（Lower = ToLowerInvariant）
        skinString = Encoding.UTF8.GetString(Convert.FromBase64String(skinValue!));
        JsonNode? skinJson = JsonNode.Parse(skinString.ToLowerInvariant());
        // 旧 :1445-1449（JSON null 的 textures/skin/url 与缺失同样视为未设置自定义皮肤；
        // 旧 JValue 索引在 JSON null 时会抛出或得到空串，此处对齐常见的缺失语义，病态输入的异常类型差异记 TECHDEBT）
        if (skinJson?["textures"]?["skin"]?["url"] is not { } skinUrlNode) throw new Exception("用户未设置自定义皮肤");
        var skinUrl = skinUrlNode.ToString();
        skinValue = skinUrl.Contains("minecraft.net/") ? skinUrl.Replace("http://", "https://") : skinUrl;
        //保存缓存（旧 :1452-1454）
        cacheWrite(uuid, type, skinValue);
        Logger.Info($"UUID {uuid} 对应的皮肤文件为 {skinValue}");
        return skinValue;
    }

    /// <summary>模型性别判定（McSkinSex 旧 :1477-1485）：UUID 第 8/16/24/32 位十六进制值的异或奇偶，返回“Steve”或“Alex”。</summary>
    public static string GetModel(string uuid)
    {
        if (uuid.Length != 32) return "Steve";   // 旧 :1478
        // 旧 :1479-1483：Integer.Parse(char, AllowHexSpecifier) 在 C# 中以单字符字符串等价表达
        int a = int.Parse(uuid[7].ToString(), NumberStyles.AllowHexSpecifier);
        int b = int.Parse(uuid[15].ToString(), NumberStyles.AllowHexSpecifier);
        int c = int.Parse(uuid[23].ToString(), NumberStyles.AllowHexSpecifier);
        int d = int.Parse(uuid[31].ToString(), NumberStyles.AllowHexSpecifier);
        return (a ^ b ^ c ^ d) % 2 != 0 ? "Alex" : "Steve";
    }

    /// <summary>
    /// 旧 :1421/:1423 的验证服务器地址：已选实例时读版本独立设置（读取前懒式导入旧 Setup.ini，
    /// 与 VB Settings.InstanceKey 的“导入失败不阻断读取”一致），否则读全局缓存键。
    /// </summary>
    private static string ServerName(string cachedKey, string versionKey, Func<string?> selectedInstanceKey)
    {
        var instanceKey = selectedInstanceKey();
        if (instanceKey is null) return SettingsService.Get<string>(cachedKey);
        try
        {
            SettingsImport.EnsureInstanceImported(instanceKey);   //导入失败不阻断读取
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"导入实例旧设置失败（{instanceKey}）");
        }
        return SettingsService.Get<string>(versionKey, instanceKey);
    }
}