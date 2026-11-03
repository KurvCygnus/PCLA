using MeloongCore.Extensions;
using PCLCS.Launch;
using PCLCS.Settings;
using PCLCS.Tests.Net;
using System.Net;

namespace PCLCS.Tests.Launch;

/// <summary>
/// LoginCores（UUID 算法与 authlib/Nide 请求核）测试。
/// 网络面全部走回环假服务器，无真实网络；设置走 SettingsService 临时目录。
/// </summary>
[Collection("PCLCS.Settings")]   //静态提供者与 SettingsService 均为进程级状态，串行执行
public class LoginCoresTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pclcs.login." + Guid.NewGuid().ToString("N"));
    private readonly List<LoopbackServer> _servers = [];

    public LoginCoresTests()
    {
        Directory.CreateDirectory(_root);
        SettingsService.Configure(_root);
        LoginCores.MojangUuidCacheFolder = () => _root;
    }

    public void Dispose()
    {
        LoginCores.MojangUuidCacheFolder = null;
        LoginCores.SelectedVanillaMajor = null;
        foreach (var server in _servers) server.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>起一个回环假服务器（用例结束自动回收）。</summary>
    private LoopbackServer NewServer(Func<HttpListenerRequest, (int, string, Dictionary<string, string>?)> responder)
    {
        var server = new LoopbackServer();
        _servers.Add(server);
        server.Responder = responder;
        return server;
    }

    private static string? Body(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream);
        return reader.ReadToEnd();
    }

    #region LegacyUuid 已知向量（独立 Python 按旧 :1167-1170 语义预计算）

    [Theory]
    [InlineData("Steve", "0000000000003005998F503099DCF29B")]
    [InlineData("Alex", "0000000000003004998F501A96F4269A")]
    [InlineData("", "0000000000003000998F501BC684162A")]
    [InlineData("PCL", "0000000000003003998F501BCC516AB5")]
    [InlineData("Test1234567890", "000000000000300E9410A4C2B1EBBDDD")]
    [InlineData("玩家名", "0000000000003003998F501BCD958C58")]
    public void LegacyUuid_known_vectors(string userName, string expected)
        => Assert.Equal(expected, LoginCores.LegacyUuid(userName));

    [Fact]
    public void LegacyUuid_splices_version_and_variant_nibbles()
    {
        //旧 :1169：Full[0..12) & "3" & Full[13..16) & "9" & Full[17..32)——第 13 位固定 '3'、第 17 位固定 '9'
        var uuid = LoginCores.LegacyUuid("Steve");
        Assert.Equal(32, uuid.Length);
        Assert.Equal('3', uuid[12]);
        Assert.Equal('9', uuid[16]);
    }

    #endregion

    #region LegacyUuidWithCustomSkin 皮肤调整决策面（旧 :1105-1142）

    [Fact]
    public void CustomSkin_kind0_keeps_legacy_uuid()
        => Assert.Equal(LoginCores.LegacyUuid("PCL"), LoginCores.LegacyUuidWithCustomSkin("PCL", 0, ""));

    [Fact]
    public void CustomSkin_kind1_bumps_until_steve()
    {
        //"PCL" 的离线 UUID 为 Alex 性别（SkinProfile.GetModel），按旧 :1113-1116 递增末 5 位至 Steve
        Assert.Equal("0000000000003003998F501BCC516AB6", LoginCores.LegacyUuidWithCustomSkin("PCL", 1, ""));
    }

    [Fact]
    public void CustomSkin_kind2_bumps_until_alex()
    {
        //"Steve" 的离线 UUID 为 Steve 性别，按旧 :1119-1122 递增至 Alex
        Assert.Equal("0000000000003005998F503099DCF29C", LoginCores.LegacyUuidWithCustomSkin("Steve", 2, ""));
    }

    [Fact]
    public void CustomSkin_kind4_targets_steve_when_not_slim()
    {
        SettingsService.Set("LaunchSkinSlim", false);
        Assert.Equal("0000000000003003998F501BCC516AB6", LoginCores.LegacyUuidWithCustomSkin("PCL", 4, ""));
    }

    [Fact]
    public void CustomSkin_kind4_targets_alex_when_slim()
    {
        SettingsService.Set("LaunchSkinSlim", true);
        //"PCL" 已是 Alex，无需递增（旧 :1136 目标为 If(LaunchSkinSlim, "Alex", "Steve")）
        Assert.Equal("0000000000003003998F501BCC516AB5", LoginCores.LegacyUuidWithCustomSkin("PCL", 4, ""));
    }

    [Fact]
    public void CustomSkin_kind3_uses_mojang_uuid_for_vanilla_below_20()
    {
        using var server = NewServer(static _ => (200, """{"id":"E3B7C9D2A1B4F6058972C4D1E0A3B5C7"}""", null));
        LoginCores.SelectedVanillaMajor = () => 19;
        LoginCores.MojangApiBase = server.UrlPrefix;
        var uuid = LoginCores.LegacyUuidWithCustomSkin("PCL", 3, "Notch");
        Assert.Equal("E3B7C9D2A1B4F6058972C4D1E0A3B5C7", uuid);
    }

    [Fact]
    public void CustomSkin_kind3_falls_back_to_legacy_when_query_fails()
    {
        using var server = NewServer(static _ => (404, """{"errorMessage":"not found"}""", null));
        LoginCores.SelectedVanillaMajor = () => 19;
        LoginCores.MojangApiBase = server.UrlPrefix;
        //旧 :1131-1133：查询异常被吞，游戏以无皮肤（原离线 UUID）方式启动
        Assert.Equal(LoginCores.LegacyUuid("PCL"), LoginCores.LegacyUuidWithCustomSkin("PCL", 3, "Notch"));
    }

    [Fact]
    public void CustomSkin_kind3_skips_query_for_1_20_plus()
    {
        //#3746：1.20+ 或快照版（Vanilla.Major >= 20）不能使用正版用户名皮肤
        var requests = 0;
        using var server = NewServer(_ => { requests++; return (200, """{"id":"0"}""", null); });
        LoginCores.SelectedVanillaMajor = () => 20;
        LoginCores.MojangApiBase = server.UrlPrefix;
        var uuid = LoginCores.LegacyUuidWithCustomSkin("PCL", 3, "Notch");
        Assert.Equal(0, requests);
        Assert.Equal(LoginCores.LegacyUuid("PCL"), uuid);
    }

    [Fact]
    public void CustomSkin_kind3_skips_query_when_no_instance_selected()
    {
        var requests = 0;
        using var server = NewServer(_ => { requests++; return (200, """{"id":"0"}""", null); });
        LoginCores.SelectedVanillaMajor = null;   //旧 :1127：McInstanceSelected Is Nothing → 不查询
        LoginCores.MojangApiBase = server.UrlPrefix;
        LoginCores.LegacyUuidWithCustomSkin("PCL", 3, "Notch");
        Assert.Equal(0, requests);
    }

    [Fact]
    public void CustomSkin_kind3_skips_query_when_skin_name_empty()
    {
        var requests = 0;
        using var server = NewServer(_ => { requests++; return (200, """{"id":"0"}""", null); });
        LoginCores.SelectedVanillaMajor = () => 19;
        LoginCores.MojangApiBase = server.UrlPrefix;
        LoginCores.LegacyUuidWithCustomSkin("PCL", 3, "");
        Assert.Equal(0, requests);
    }

    #endregion

    #region MojangUuid（旧 :1144-1166：ini 缓存 + 官网查询）

    [Fact]
    public void MojangUuid_blank_name_returns_32_zeros()
    {
        var requests = 0;
        using var server = NewServer(_ => { requests++; return (200, """{"id":"0"}""", null); });
        LoginCores.MojangApiBase = server.UrlPrefix;
        Assert.Equal("00000000000000000000000000000000", LoginCores.MojangUuid("  ", false));
        Assert.Equal(0, requests);
    }

    [Fact]
    public void MojangUuid_reads_ini_cache_without_network()
    {
        var requests = 0;
        using var server = NewServer(_ => { requests++; return (200, """{"id":"0"}""", null); });
        LoginCores.MojangApiBase = server.UrlPrefix;
        File.WriteAllLines(Path.Combine(_root, "Mojang.ini"), ["CachedName:AABBCCDDEEFF00011234567890ABCDEF"]);
        Assert.Equal("AABBCCDDEEFF00011234567890ABCDEF", LoginCores.MojangUuid("CachedName", false));
        Assert.Equal(0, requests);
    }

    [Fact]
    public void MojangUuid_queries_writes_cache_then_hits_cache()
    {
        var requests = 0;
        using var server = NewServer(_ => { requests++; return (200, """{"id":"E3B7C9D2A1B4F6058972C4D1E0A3B5C7","name":"FreshName"}""", null); });
        LoginCores.MojangApiBase = server.UrlPrefix;
        Assert.Equal("E3B7C9D2A1B4F6058972C4D1E0A3B5C7", LoginCores.MojangUuid("FreshName", false));
        Assert.Equal(1, requests);
        //二次调用走缓存
        Assert.Equal("E3B7C9D2A1B4F6058972C4D1E0A3B5C7", LoginCores.MojangUuid("FreshName", false));
        Assert.Equal(1, requests);
        //缓存以旧 ini 语义（key:value 行）落盘
        var ini = File.ReadAllText(Path.Combine(_root, "Mojang.ini"));
        Assert.Contains("FreshName:E3B7C9D2A1B4F6058972C4D1E0A3B5C7", ini);
    }

    [Fact]
    public void MojangUuid_throws_when_uuid_length_insufficient()
    {
        using var server = NewServer(static _ => (200, """{"name":"NoIdName"}""", null));
        LoginCores.MojangApiBase = server.UrlPrefix;
        var ex = Assert.Throws<Exception>(() => LoginCores.MojangUuid("NoIdName", false));
        Assert.Equal("获取的正版 Uuid 长度不足（）", ex.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MojangUuid_404_wraps_as_failure(bool throwOnNotFound)
    {
        //旧 :1156 仅对 FileNotFoundException（响应体可解析为 Nothing）回落离线 UUID；
        //HTTP 404 走 HttpRequestCodeException 分支 → 无论 ThrowOnNotFound 均包装抛出
        using var server = NewServer(static _ => (404, """{"errorMessage":"Couldn't find any profile with name"}""", null));
        LoginCores.MojangApiBase = server.UrlPrefix;
        var ex = Assert.Throws<Exception>(() => LoginCores.MojangUuid("GhostName", throwOnNotFound));
        Assert.Equal("从官网获取正版 Uuid 失败", ex.Message);
    }

    #endregion

    #region RequestValidate / RequestRefresh（旧 :710-763）

    [Fact]
    public void RequestValidate_posts_cached_tokens_and_echoes_cache()
    {
        string? body = null;
        string? acceptLanguage = null;
        string? contentType = null;
        using var server = NewServer(req =>
        {
            body = Body(req);
            acceptLanguage = req.Headers["Accept-Language"];
            contentType = req.Headers["Content-Type"];
            return (200, "ok", null);
        });
        SettingsService.Set("CacheAuthAccess", "TA");
        SettingsService.Set("CacheAuthClient", "TC");
        SettingsService.Set("CacheAuthUuid", "TU");
        SettingsService.Set("CacheAuthName", "TN");
        var result = LoginCores.RequestValidate(new McLoginServer(McLoginType.Auth) { BaseUrl = server.UrlPrefix, Token = "Auth" });
        Assert.Equal("""{"accessToken":"TA","clientToken":"TC","requestUser":true}""", body);
        Assert.Equal("zh-CN", acceptLanguage);
        Assert.Equal("application/json; charset=utf-8", contentType);
        Assert.Equal(("TN", "TU", "TA", "TC", "Auth"), (result.Name, result.Uuid, result.AccessToken, result.ClientToken, result.Type));
        //Validate 不更改缓存
        Assert.Equal("TA", SettingsService.Get<string>("CacheAuthAccess"));
    }

    [Fact]
    public void RequestRefresh_outputs_and_writes_cache()
    {
        string? body = null;
        using var server = NewServer(req =>
        {
            body = Body(req);
            return (200, """{"accessToken":"NA","clientToken":"NC","selectedProfile":{"id":"NU","name":"NN"},"user":{"id":"XU"}}""", null);
        });
        SettingsService.Set("CacheNideAccess", "OA");
        SettingsService.Set("CacheNideClient", "OC");
        SettingsService.Set("CacheNideUuid", "OU");
        SettingsService.Set("CacheNideName", "ON");
        var result = LoginCores.RequestRefresh(new McLoginServer(McLoginType.Nide)
        {
            BaseUrl = server.UrlPrefix, Token = "Nide", UserName = "user@example.com", Password = "pw"
        });
        Assert.Equal(("NN", "NU", "NA", "NC", "Nide"), (result.Name, result.Uuid, result.AccessToken, result.ClientToken, result.Type));
        Assert.Equal("NA", SettingsService.Get<string>("CacheNideAccess"));
        Assert.Equal("NC", SettingsService.Get<string>("CacheNideClient"));
        Assert.Equal("NU", SettingsService.Get<string>("CacheNideUuid"));
        Assert.Equal("NN", SettingsService.Get<string>("CacheNideName"));
        Assert.Equal("user@example.com", SettingsService.Get<string>("CacheNideUsername"));
        Assert.Equal("pw", SettingsService.Get<string>("CacheNidePass"));
        //旧 :738-745：请求体带缓存中的 selectedProfile
        Assert.Equal("""{"selectedProfile":{"name":"ON","id":"OU"},"accessToken":"OA","requestUser":true}""", body);
    }

    [Fact]
    public void RequestRefresh_invalid_profile_throws_with_cached_name()
    {
        using var server = NewServer(static _ => (200, """{"accessToken":"NA","clientToken":"NC"}""", null));
        SettingsService.Set("CacheNideName", "ON");
        var ex = Assert.Throws<Exception>(() => LoginCores.RequestRefresh(new McLoginServer(McLoginType.Nide)
        {
            BaseUrl = server.UrlPrefix, Token = "Nide", UserName = "u", Password = "p"
        }));
        Assert.Equal("选择的角色 ON 无效！", ex.Message);
    }

    #endregion

    #region RequestLogin（旧 :764-883：角色选择三态 + 错误分诊表）

    private McLoginServer LoginInput(LoopbackServer server, string token, string userName = "user@example.com", bool forceReselect = false)
        => new(token == "Nide" ? McLoginType.Nide : McLoginType.Auth)
        {
            BaseUrl = server.UrlPrefix, Token = token, UserName = userName, Password = "pw", ForceReselectProfile = forceReselect
        };

    [Fact]
    public void RequestLogin_single_profile_uses_selected_profile_without_selector()
    {
        string? body = null;
        using var server = NewServer(req =>
        {
            body = Body(req);
            return (200, """{"accessToken":"A1","clientToken":"C1","selectedProfile":{"id":"U1","name":"N1"},"availableProfiles":[{"id":"U1","name":"N1"}]}""", null);
        });
        var selectorCalled = false;
        var needRefresh = LoginCores.RequestLogin(LoginInput(server, "Auth"), _ => { selectorCalled = true; return 0; }, out var result);
        Assert.False(selectorCalled);
        Assert.False(needRefresh);
        Assert.Equal(("N1", "U1", "A1", "C1", "Auth"), (result.Name, result.Uuid, result.AccessToken, result.ClientToken, result.Type));
        Assert.Equal("A1", SettingsService.Get<string>("CacheAuthAccess"));
        Assert.Equal("C1", SettingsService.Get<string>("CacheAuthClient"));
        Assert.Equal("U1", SettingsService.Get<string>("CacheAuthUuid"));
        Assert.Equal("N1", SettingsService.Get<string>("CacheAuthName"));
        Assert.Equal("user@example.com", SettingsService.Get<string>("CacheAuthUsername"));
        Assert.Equal("pw", SettingsService.Get<string>("CacheAuthPass"));
        //旧 :768-772：authenticate 请求体
        Assert.Equal("""{"agent":{"name":"Minecraft","version":1},"username":"user@example.com","password":"pw","requestUser":true}""", body);
    }

    [Fact]
    public void RequestLogin_multi_without_selected_asks_selector()
    {
        using var server = NewServer(static _ => (200,
            """{"accessToken":"A2","clientToken":"C2","availableProfiles":[{"id":"U1","name":"N1"},{"id":"U2","name":"N2"}]}""", null));
        IReadOnlyList<string>? asked = null;
        var needRefresh = LoginCores.RequestLogin(LoginInput(server, "Auth"), names =>
        {
            asked = names;
            return 1;   //选择第二个角色
        }, out var result);
        Assert.Equal(["N1", "N2"], asked);
        Assert.True(needRefresh);   //走选择时 NeedRefresh = True（旧 :789）
        Assert.Equal(("N2", "U2", "A2", "C2", "Auth"), (result.Name, result.Uuid, result.AccessToken, result.ClientToken, result.Type));
    }

    [Fact]
    public void RequestLogin_multi_prefers_cache_matching_profile()
    {
        using var server = NewServer(static _ => (200,
            """{"accessToken":"A2","clientToken":"C2","availableProfiles":[{"id":"U1","name":"N1"},{"id":"U2","name":"N2"}]}""", null));
        SettingsService.Set("CacheAuthUuid", "U2");
        var selectorCalled = false;
        var needRefresh = LoginCores.RequestLogin(LoginInput(server, "Auth", forceReselect: true), _ => { selectorCalled = true; return 0; }, out var result);
        Assert.False(selectorCalled);   //缓存命中则不弹选择框（旧 :790-797）
        Assert.True(needRefresh);
        Assert.Equal(("N2", "U2", "A2", "C2", "Auth"), (result.Name, result.Uuid, result.AccessToken, result.ClientToken, result.Type));
    }

    [Fact]
    public void RequestLogin_zero_profiles_throws_verbatim()
    {
        using var server = NewServer(static _ => (200, """{"accessToken":"A3","clientToken":"C3","availableProfiles":[]}""", null));
        var hints = new List<string>();
        var ex = Assert.Throws<Exception>(() => LoginCores.RequestLogin(LoginInput(server, "Auth"), _ => 0, out _, hint: hints.Add));
        Assert.Equal("$你还没有创建角色，请在创建角色后再试！", ex.Message);
        Assert.Empty(hints);
        //ForceReselectProfile 时先红字提示（旧 :780）
        var hints2 = new List<string>();
        var ex2 = Assert.Throws<Exception>(() => LoginCores.RequestLogin(LoginInput(server, "Auth", forceReselect: true), _ => 0, out _, hint: hints2.Add));
        Assert.Equal("$你还没有创建角色，请在创建角色后再试！", ex2.Message);
        Assert.Equal(["你还没有创建角色，无法更换！"], hints2);
    }

    [Fact]
    public void RequestLogin_single_profile_with_reselect_hints_but_uses_selected()
    {
        using var server = NewServer(static _ => (200,
            """{"accessToken":"A1","clientToken":"C1","selectedProfile":{"id":"U1","name":"N1"},"availableProfiles":[{"id":"U1","name":"N1"}]}""", null));
        var hints = new List<string>();
        LoginCores.RequestLogin(LoginInput(server, "Auth", forceReselect: true), _ => 0, out var result, hint: hints.Add);
        Assert.Equal(["你的账户中只有一个角色，无法更换！"], hints);
        Assert.Equal(("N1", "U1", "A1", "C1", "Auth"), (result.Name, result.Uuid, result.AccessToken, result.ClientToken, result.Type));
    }

    [Fact]
    public void RequestLogin_cancel_from_selector_wraps_as_login_failure()
    {
        using var server = NewServer(static _ => (200,
            """{"accessToken":"A2","clientToken":"C2","availableProfiles":[{"id":"U1","name":"N1"},{"id":"U2","name":"N2"}]}""", null));
        //取消选择（回调抛 OperationCanceledException）→ 旧 :879 的兜底包装
        var ex = Assert.Throws<Exception>(() => LoginCores.RequestLogin(LoginInput(server, "Auth"),
            _ => throw new OperationCanceledException(), out _));
        Assert.StartsWith("登录失败：", ex.Message);
    }

    [Fact]
    public void RequestLogin_403_with_error_message_throws_and_exits_login_for_auth()
    {
        using var server = NewServer(static _ => (403, """{"errorMessage":"密码错误"}""", null));
        var exits = new List<McLoginType>();
        var ex = Assert.Throws<Exception>(() => LoginCores.RequestLogin(LoginInput(server, "Auth"),
            _ => 0, out _, onPasswordError: exits.Add));
        Assert.Equal("$登录失败：密码错误", ex.Message);
        Assert.Equal([McLoginType.Auth], exits);   //#5090：密码错误时退出登录
    }

    [Fact]
    public void RequestLogin_403_table_verbatim_for_auth()
    {
        using var server = NewServer(static _ => (403, "denied", null));
        var ex = Assert.Throws<Exception>(() => LoginCores.RequestLogin(LoginInput(server, "Auth"), _ => 0, out _));
        Assert.Equal("$登录失败，以下为可能的原因：\r\n - 输入的账号或密码错误。\r\n" +
            " - 登录尝试过于频繁，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。\r\n" +
            " - 只注册了账号，但没有在皮肤站新建角色。", ex.Message);
    }

    [Fact]
    public void RequestLogin_403_table_for_nide_varies_by_email()
    {
        //邮箱账号（含 @）：不含“游戏角色 ID”一行（旧 :871）
        using var server = NewServer(static _ => (403, "denied", null));
        var ex = Assert.Throws<Exception>(() => LoginCores.RequestLogin(LoginInput(server, "Nide"), _ => 0, out _));
        Assert.Equal("$登录失败，以下为可能的原因：\r\n - 输入的账号或密码错误。\r\n" +
            " - 密码错误次数过多，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。\r\n" +
            " - 只注册了账号，但没有加入对应服务器。", ex.Message);

        //非邮箱账号：多一行提示（旧 :871 的 If 判断）
        var exName = Assert.Throws<Exception>(() => LoginCores.RequestLogin(LoginInput(server, "Nide", userName: "Steve"), _ => 0, out _));
        Assert.Equal("$登录失败，以下为可能的原因：\r\n - 输入的账号或密码错误。\r\n" +
            " - 密码错误次数过多，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。\r\n" +
            " - 登录账号应为邮箱或统一通行证账号，而非游戏角色 ID。\r\n" +
            " - 只注册了账号，但没有加入对应服务器。", exName.Message);
    }

    [Fact]
    public void RequestLogin_non_403_wraps_message()
    {
        using var server = NewServer(static _ => (404, "missing", null));
        var ex = Assert.Throws<Exception>(() => LoginCores.RequestLogin(LoginInput(server, "Auth"), _ => 0, out _));
        Assert.StartsWith("登录失败：", ex.Message);
        Assert.DoesNotContain("$", ex.Message, StringComparison.Ordinal);
    }

    #endregion
}

/// <summary>登录模型（旧 ModLaunch.vb:300-408 随迁段）的 Equals/GetHashCode 语义测试。</summary>
public class LoginModelsTests
{
    [Fact]
    public void McLoginType_enum_values_match_vb()
    {
        Assert.Equal(0, (int) McLoginType.Legacy);
        Assert.Equal(2, (int) McLoginType.Nide);
        Assert.Equal(3, (int) McLoginType.Auth);
        Assert.Equal(5, (int) McLoginType.Ms);
    }

    [Fact]
    public void McLoginServer_equals_by_stable_hash()
    {
        var a = new McLoginServer(McLoginType.Nide) { UserName = "u", Password = "p", BaseUrl = "b", Token = "Nide", Description = "d" };
        var b = new McLoginServer(McLoginType.Nide) { UserName = "u", Password = "p", BaseUrl = "b", Token = "Nide", Description = "d" };
        var c = new McLoginServer(McLoginType.Nide) { UserName = "u", Password = "different", BaseUrl = "b", Token = "Nide", Description = "d" };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.False(a.Equals(null!));
    }

    [Fact]
    public void McLoginServer_equals_is_hash_only_no_type_check()
    {
        //旧 :314-316：Equals 仅比较双方 GetHashCode，不做类型判定（逐字保留）
        var server = new McLoginServer(McLoginType.Nide) { UserName = "x" };
        var legacy = new McLoginLegacy { UserName = "x" };
        Assert.NotEqual(server.GetHashCode(), legacy.GetHashCode());
        Assert.False(server.Equals(legacy));   //哈希不同则不等；若哈希相同则同旧代码一样视为相等
    }

    [Fact]
    public void McLoginServer_hash_uses_concat_of_fields()
    {
        //旧 :350：(UserName & Password & BaseUrl & Token & Type).GetStableHashCode() Mod Integer.MaxValue
        var input = new McLoginServer(McLoginType.Nide) { UserName = "u", Password = "p", BaseUrl = "b", Token = "Nide" };
        var expected = ("u" + "p" + "b" + "Nide" + McLoginType.Nide).GetStableHashCode() % int.MaxValue;
        Assert.Equal((int) expected, input.GetHashCode());
    }

    [Fact]
    public void McLoginServer_null_fields_concat_as_empty()
    {
        //VB 的 & 对 Nothing 按空串处理
        var a = new McLoginServer(McLoginType.Auth);
        var b = new McLoginServer(McLoginType.Auth);
        Assert.Equal(a, b);
        var expected = ("" + "" + "" + "" + McLoginType.Auth).GetStableHashCode() % int.MaxValue;
        Assert.Equal((int) expected, a.GetHashCode());
    }

    [Fact]
    public void McLoginLegacy_hash_uses_skin_fields()
    {
        //旧 :393：(UserName & SkinType & SkinName & Type).GetStableHashCode() Mod Integer.MaxValue
        var input = new McLoginLegacy { UserName = "u", SkinType = 2, SkinName = "s" };
        var expected = ("u" + 2 + "s" + McLoginType.Legacy).GetStableHashCode() % int.MaxValue;
        Assert.Equal((int) expected, input.GetHashCode());
        Assert.Equal(new McLoginLegacy { UserName = "u", SkinType = 2, SkinName = "s" }, input);
    }

    [Fact]
    public void McLoginMs_hash_uses_refresh_token_and_name_only()
    {
        //旧 :371：(OAuthRefreshToken & UserName).GetStableHashCode() Mod Integer.MaxValue
        var input = new McLoginMs { OAuthRefreshToken = "rt", UserName = "n" };
        var expected = ("rt" + "n").GetStableHashCode() % int.MaxValue;
        Assert.Equal((int) expected, input.GetHashCode());
        Assert.Equal(input, new McLoginMs { OAuthRefreshToken = "rt", UserName = "n" });
        Assert.NotEqual(input, new McLoginMs { OAuthRefreshToken = "rt", UserName = "other" });
    }

    [Fact]
    public void McLoginResult_is_mutable_struct()
    {
        //旧 :398-408：Structure，六个字段
        var result = new McLoginResult { Name = "n", Uuid = "u", AccessToken = "a", Type = "Auth", ClientToken = "c", ProfileJson = "p" };
        var copy = result;   //值类型复制
        copy.Name = "changed";
        Assert.Equal("n", result.Name);
        Assert.Equal("p", result.ProfileJson);
    }
}