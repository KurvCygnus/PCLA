using PCLCS.Instances;
using PCLCS.Settings;
using System.Text;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Instances;

//* 判定链经注入回调全确定性驱动：ini 缓存→内存字典、网络→假响应、所选实例→假路径；
//* Nide/Auth 服务器地址走真实设置目录（临时），与设置类测试共享串行集合
[Collection("PCLCS.Settings")]
public class SkinProfileTests : IDisposable
{
    private const string Uuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OfflineUuid = "00000aaaaaaaaaaaaaaaaaaaaaaaaaaa";   // "00000" + 27 个 'a'，共 32 位

    private const string ExpectedSkinAddress = "https://textures.minecraft.net/texture/aa";

    private readonly string _root;
    private readonly Dictionary<string, string> _iniCache = new();
    private string? _fetchedUrl;
    private string? _selectedKey;
    private Func<string, string> _respond = static url => throw new InvalidOperationException("测试未预期的网络请求：" + url);

    public SkinProfileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclcs.skin." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        SettingsService.Configure(_root);
        //预置验证服务器设置（旧 :1421/:1423 读取的四个键）：全局两键 + 已选实例两键
        SettingsService.Set("CacheNideServer", "global.nide.example");
        SettingsService.Set("CacheAuthServerServer", "https://global.auth.example");
        SettingsService.Set("VersionServerNide", "inst.nide.example", InstanceKey);
        SettingsService.Set("VersionServerAuthServer", "https://inst.auth.example", InstanceKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    //* 与 VB 转发层回调同构（ModMinecraft.vb SkinCacheRead 等）：缓存未命中返回空
    private string? CacheRead(string uuid, string? type) => _iniCache.TryGetValue(type + "|" + uuid, out var value) ? value : null;

    private void CacheWrite(string uuid, string? type, string address) => _iniCache[type + "|" + uuid] = address;

    private string FetchProfile(string url)
    {
        _fetchedUrl = url;
        return _respond(url);
    }

    private string? SelectedInstanceKey() => _selectedKey;

    private string InstanceKey => Path.Combine(_root, "versions", "v1") + Path.DirectorySeparatorChar;

    private static string Payload(string innerJson) => Convert.ToBase64String(Encoding.UTF8.GetBytes(innerJson));

    /// <summary>标准的“properties 内含 textures”皮肤返回值，皮肤地址 http 升级 https 后返回。</summary>
    private static string SuccessResponse() => new JsonObject
    {
        ["properties"] = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "textures",
                ["value"] = Payload("""{"timestamp":1690000000,"profilename":"test","textures":{"skin":{"url":"http://textures.minecraft.net/texture/aa"}}}""")
            }
        }
    }.ToJsonString();

    #region GetModel（McSkinSex，旧 :1477-1485）

    [Theory]
    [InlineData("00000000000000000000000000000000", "Steve")]   // 旧 :1483：Xor 结果 0（偶）
    [InlineData("00000003000000000000000000000000", "Alex")]    // 第 8 位 '3'：奇
    [InlineData("00000008000000080000000000000000", "Steve")]   // 8 Xor 8 = 0（偶）
    [InlineData("0000000f0000000f0000000f0000000e", "Alex")]    // 15 Xor 15 Xor 15 Xor 14 = 1（奇）
    [InlineData("0000000a000000000000000000000000", "Steve")]   // 10（偶）
    public void ModelDeterminesByUuidParity(string uuid, string expected)
    {
        Assert.Equal(expected, SkinProfile.GetModel(uuid));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("")]
    [InlineData("000000000000000000000000000000000")]   // 33 位
    public void ModelNon32LengthReturnsSteve(string uuid)
    {
        // 旧 :1478：长度不为 32 直接 Steve
        Assert.Equal("Steve", SkinProfile.GetModel(uuid));
    }

    [Fact]
    public void ModelInvalidHexCharThrows()
    {
        // 旧 :1479：Integer.Parse(..., AllowHexSpecifier) 对非十六进制字符抛 FormatException
        Assert.Throws<FormatException>(static () => SkinProfile.GetModel("0000000g000000000000000000000000"));
    }

    #endregion

    #region GetAddress 验证分支（旧 :1410-1411、:1424-1425）

    [Fact]
    public void EmptyUuidThrows()
    {
        // 旧 :1410（VB Nothing = "" 同样按空处理）
        Assert.Equal("UUID 为空。", Assert.Throws<Exception>(
            () => SkinProfile.GetAddress("", "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey)).Message);
    }

    [Fact]
    public void OfflineUuidThrowsOutsideAuth()
    {
        // 旧 :1411："00000" 开头且非 Auth 抛出
        Assert.Equal("离线 UUID 无正版皮肤文件：" + OfflineUuid, Assert.Throws<Exception>(
            () => SkinProfile.GetAddress(OfflineUuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey)).Message);
    }

    [Fact]
    public void OfflineUuidAllowedForAuth()
    {
        // 旧 :1411：Type 为 Auth 时豁免，继续走完整判定链（Auth 无前缀常量，直拼服务器地址）
        _respond = static _ => SuccessResponse();
        var address = SkinProfile.GetAddress(OfflineUuid, "Auth", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey);
        Assert.Equal(ExpectedSkinAddress, address);
        Assert.Equal("https://global.auth.example/sessionserver/session/minecraft/profile/" + OfflineUuid, _fetchedUrl);
    }

    [Fact]
    public void UnknownTypeThrows()
    {
        // 旧 :1424-1425
        Assert.Equal("皮肤地址种类无效：Unknown", Assert.Throws<ArgumentException>(
            () => SkinProfile.GetAddress(Uuid, "Unknown", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey)).Message);
    }

    [Fact]
    public void NullTypeThrows()
    {
        // 旧 :1425：If(Type, "null")
        Assert.Equal("皮肤地址种类无效：null", Assert.Throws<ArgumentException>(
            () => SkinProfile.GetAddress(Uuid, null, CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey)).Message);
    }

    #endregion

    #region GetAddress URL 选择（旧 :1417-1426）

    [Theory]
    [InlineData("Mojang", null, "https://sessionserver.mojang.com/session/minecraft/profile/" + Uuid)]
    [InlineData("Ms", null, "https://sessionserver.mojang.com/session/minecraft/profile/" + Uuid)]
    [InlineData("Nide", null, "https://auth.mc-user.com:233/global.nide.example/sessionserver/session/minecraft/profile/" + Uuid)]
    [InlineData("Nide", "inst", "https://auth.mc-user.com:233/inst.nide.example/sessionserver/session/minecraft/profile/" + Uuid)]
    [InlineData("Auth", null, "https://global.auth.example/sessionserver/session/minecraft/profile/" + Uuid)]
    [InlineData("Auth", "inst", "https://inst.auth.example/sessionserver/session/minecraft/profile/" + Uuid)]
    public void UrlResolvesPerTypeAndInstance(string type, string? selectedKey, string expectedUrl)
    {
        _selectedKey = selectedKey == "inst" ? InstanceKey : null;
        _respond = static _ => SuccessResponse();
        var address = SkinProfile.GetAddress(Uuid, type, CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey);
        Assert.Equal(expectedUrl, _fetchedUrl);
        Assert.Equal(ExpectedSkinAddress, address);
    }

    #endregion

    #region GetAddress 缓存（旧 :1413-1414、:1452）

    [Fact]
    public void CacheHitReturnsCachedWithoutFetch()
    {
        _iniCache["Mojang|" + Uuid] = "http://cached.example/skin.png";
        var address = SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey);
        Assert.Equal("http://cached.example/skin.png", address);
        Assert.Null(_fetchedUrl);
    }

    [Fact]
    public void CacheMissFetchesThenWrites()
    {
        _respond = static _ => SuccessResponse();
        Assert.Equal(ExpectedSkinAddress, SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey));
        // 旧 :1452：解析结果写入缓存；随后再取直接命中，不再请求
        Assert.Equal(ExpectedSkinAddress, _iniCache["Mojang|" + Uuid]);
        var address = SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey);
        Assert.Equal(ExpectedSkinAddress, address);
        Assert.Equal(ExpectedSkinAddress, _iniCache["Mojang|" + Uuid]);
    }

    #endregion

    #region GetAddress 皮肤返回值解析（旧 :1428-1449）

    [Fact]
    public void EmptyResponseThrows()
    {
        // 旧 :1428
        _respond = static _ => "";
        Assert.Equal("皮肤返回值为空，可能是未设置自定义皮肤的用户", Assert.Throws<Exception>(
            () => SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey)).Message);
    }

    [Fact]
    public void InvalidJsonResponseWrapped()
    {
        // 旧 :1439-1441：解析异常经 Logger.Warn 后包装重抛
        _respond = static _ => "not-json";
        var ex = Assert.Throws<Exception>(
            () => SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey));
        Assert.Equal("皮肤返回值中不包含皮肤数据项，可能是未设置自定义皮肤的用户", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void ResponseWithoutTexturesPropertyWrapped()
    {
        // 旧 :1438：循环结束未命中 textures → 抛出并被同一 Catch 包装
        _respond = static _ => """{"properties":[{"name":"cape","value":"x"}]}""";
        var ex = Assert.Throws<Exception>(
            () => SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey));
        Assert.Equal("皮肤返回值中不包含皮肤数据项，可能是未设置自定义皮肤的用户", ex.Message);
        Assert.Equal("未从皮肤返回值中找到符合条件的 Property", ex.InnerException?.Message);
    }

    [Fact]
    public void ResponseWithoutPropertiesKeyWrapped()
    {
        // 旧 :1432：properties 缺失，取值即异常并包装
        _respond = static _ => "{}";
        var ex = Assert.Throws<Exception>(
            () => SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey));
        Assert.Equal("皮肤返回值中不包含皮肤数据项，可能是未设置自定义皮肤的用户", ex.Message);
    }

    [Fact]
    public void PayloadWithoutSkinKeyThrows()
    {
        // 旧 :1445-1446：载荷 textures 下没有 skin
        _respond = static _ => new JsonObject
        {
            ["properties"] = new JsonArray
            {
                new JsonObject { ["name"] = "textures", ["value"] = Payload("""{"timestamp":1,"textures":{"cape":{"url":"http://x/c.png"}}}""") }
            }
        }.ToJsonString();
        Assert.Equal("用户未设置自定义皮肤", Assert.Throws<Exception>(
            () => SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey)).Message);
    }

    [Fact]
    public void PayloadWithoutTexturesKeyThrows()
    {
        // 旧 :1445：载荷无 textures 键
        _respond = static _ => new JsonObject
        {
            ["properties"] = new JsonArray
            {
                new JsonObject { ["name"] = "textures", ["value"] = Payload("""{"timestamp":1}""") }
            }
        }.ToJsonString();
        Assert.Equal("用户未设置自定义皮肤", Assert.Throws<Exception>(
            () => SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey)).Message);
    }

    [Fact]
    public void PayloadAfterFirstTexturesPropertyWins()
    {
        // 旧 :1433-1435：命中首个 name=textures 的 Property 即 Exit Try
        _respond = static _ => new JsonObject
        {
            ["properties"] = new JsonArray
            {
                new JsonObject { ["name"] = "textures", ["value"] = Payload("""{"textures":{"skin":{"url":"http://first.example/a.png"}}}""") },
                new JsonObject { ["name"] = "textures", ["value"] = Payload("""{"textures":{"skin":{"url":"http://second.example/b.png"}}}""") }
            }
        }.ToJsonString();
        Assert.Equal("http://first.example/a.png", SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey));
    }

    [Fact]
    public void MinecraftNetUrlUpgradedToHttps()
    {
        // 旧 :1449：包含 minecraft.net/ 的地址 http 升级 https
        _respond = static _ => SuccessResponse();
        Assert.Equal(ExpectedSkinAddress, SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey));
    }

    [Fact]
    public void NonMinecraftUrlKeptVerbatim()
    {
        // 旧 :1449：非 minecraft.net/ 地址原样返回
        _respond = static _ => new JsonObject
        {
            ["properties"] = new JsonArray
            {
                new JsonObject { ["name"] = "textures", ["value"] = Payload("""{"textures":{"skin":{"url":"http://skins.example/a.png"}}}""") }
            }
        }.ToJsonString();
        Assert.Equal("http://skins.example/a.png", SkinProfile.GetAddress(Uuid, "Mojang", CacheRead, CacheWrite, FetchProfile, SelectedInstanceKey));
    }

    #endregion
}
