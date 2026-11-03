using MeloongCore;
using PCLCS.Launch;
using PCLCS.Tests.Net;
using System.Net;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Launch;

/// <summary>
/// MsLoginSteps（微软登录六步网络核）测试。
/// 网络面全部走回环假服务器（devicecode/token/XBL/XSTS/MTS/mcstore/profile 七端点形态），无真实网络；
/// WaitingMyMsgBox 轮询弹窗以回调模拟（Succeed/Retry/Fail 三态）。
/// </summary>
[Collection("PCLCS.Settings")]   //静态提供者与网络核心均为进程级状态，串行执行
public class MsLoginStepsTests : IDisposable
{
    private readonly List<LoopbackServer> _servers = [];

    public void Dispose()
    {
        MsLoginSteps.OAuthClientIdProvider = null;
        MsLoginSteps.RunInUiProvider = null;
        MsLoginSteps.DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
        MsLoginSteps.RefreshUrl = "https://login.live.com/oauth20_token.srf";
        MsLoginSteps.XblUrl = "https://user.auth.xboxlive.com/user/authenticate";
        MsLoginSteps.XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
        MsLoginSteps.McLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
        MsLoginSteps.EntitlementsUrl = "https://api.minecraftservices.com/entitlements/mcstore";
        MsLoginSteps.ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
        foreach (LoopbackServer server in _servers) server.Dispose();
    }

    /// <summary>起一个回环假服务器并接管全部七端点（用例结束自动回收，取用经 <c>_servers[^1]</c>）；同时注入假 ClientId。</summary>
    private void UseEndpoints(Func<HttpListenerRequest, (int, string, Dictionary<string, string>?)> responder)
    {
        MsLoginSteps.OAuthClientIdProvider = static () => "test-client-id";
        LoopbackServer server = new();
        _servers.Add(server);
        server.Responder = responder;
        MsLoginSteps.DeviceCodeUrl = server.UrlPrefix + "devicecode";
        MsLoginSteps.RefreshUrl = server.UrlPrefix + "token";
        MsLoginSteps.XblUrl = server.UrlPrefix + "xbl";
        MsLoginSteps.XstsUrl = server.UrlPrefix + "xsts";
        MsLoginSteps.McLoginUrl = server.UrlPrefix + "mts";
        MsLoginSteps.EntitlementsUrl = server.UrlPrefix + "mcstore";
        MsLoginSteps.ProfileUrl = server.UrlPrefix + "profile";
    }

    private static string Body(HttpListenerRequest request)
    {
        using StreamReader reader = new(request.InputStream);
        return reader.ReadToEnd();
    }

    #region BeginDeviceCodeFlow（旧 Step1New :773-803：devicecode 获取 + 轮询回调三态 + GoTo Retry 环）

    [Fact]
    public void DeviceFlow_posts_form_body_and_passes_json_to_poll()
    {
        string? method = null, contentType = null, body = null;
        string? polledJson = null;
        UseEndpoints(req =>
        {
            method = req.HttpMethod;
            contentType = req.Headers["Content-Type"];
            body = Body(req);
            return (200, """{"device_code":"DEV123","user_code":"ABCD-1234","verification_uri":"https://microsoft.com/link","interval":5}""", null);
        });
        MsDeviceCode flow = MsLoginSteps.BeginDeviceCodeFlow(json =>
        {
            polledJson = json;
            return MsPollResult.Succeed("AT", "RT");
        });
        //旧 :779-781：POST 表单体（client_id 经 provider 注入）
        Assert.Equal("POST", method);
        Assert.Equal("application/x-www-form-urlencoded", contentType);
        Assert.Equal("client_id=test-client-id&tenant=/consumers&scope=XboxLive.signin%20offline_access", body);
        //轮询参数产出：devicecode 响应整体交宿主弹窗（旧 :785 Converter.Content = PrepareJson）
        JsonNode polled = JsonNode.Parse(polledJson!)!;
        Assert.Equal("DEV123", polled["device_code"]!.ToString());
        Assert.Equal("ABCD-1234", polled["user_code"]!.ToString());
        Assert.Equal("https://microsoft.com/link", polled["verification_uri"]!.ToString());
        //旧 :801：令牌对原样返回
        Assert.Equal(("AT", "RT"), (flow.OAuthAccessToken, flow.OAuthRefreshToken));
    }

    [Fact]
    public void DeviceFlow_retry_reacquires_devicecode_and_returns_tokens()
    {
        var calls = 0;
        UseEndpoints(_ => { calls++; return (200, """{"device_code":"D"}""", null); });
        var polls = 0;
        MsDeviceCode flow = MsLoginSteps.BeginDeviceCodeFlow(_ =>
        {
            polls++;
            //首轮宿主密码登录引导选“重新登录”（旧 RestartException 面）→ Retry；次轮成功
            return polls == 1 ? MsPollResult.Retry() : MsPollResult.Succeed("AT2", "RT2");
        });
        Assert.Equal(2, calls);   //旧 :794 GoTo Retry：重开设备码流
        Assert.Equal(2, polls);
        Assert.Equal(("AT2", "RT2"), (flow.OAuthAccessToken, flow.OAuthRefreshToken));
    }

    [Fact]
    public void DeviceFlow_fail_rethrows_callback_exception_verbatim()
    {
        UseEndpoints(static _ => (200, """{"device_code":"D"}""", null));
        Exception failure = new("$你拒绝了 PCL 申请的权限……");
        Exception ex = Assert.Throws<Exception>(() => MsLoginSteps.BeginDeviceCodeFlow(_ => MsPollResult.Fail(failure)));
        Assert.Same(failure, ex);   //旧 :798-799：TypeOf Exception → 原样抛出
    }

    [Fact]
    public void DeviceFlow_devicecode_error_propagates_after_retries()
    {
        var calls = 0;
        UseEndpoints(_ => { calls++; return (500, "boom", null); });
        Assert.ThrowsAny<HttpRequestCodeException>(static () =>
            MsLoginSteps.BeginDeviceCodeFlow(static _ => MsPollResult.Succeed("AT", "RT")));
        //旧 NetRequestByClientRetry 语义：前两次均在 5.5 秒内快速失败时不再第三次尝试（快速失败面，与 NetRequestTests 503 用例同真值）
        Assert.Equal(2, calls);
    }

    #endregion

    #region RefreshTokens（旧 Step1Refresh :805-837：refresh_token 交换 + 首赢家竞速 + 错误分诊表）

    [Fact]
    public void Refresh_parses_tokens_with_headers_and_body()
    {
        string? contentType = null, body = null;
        string? acceptLanguage = null, requestedWith = null;
        UseEndpoints(req =>
        {
            contentType = req.Headers["Content-Type"];
            body = Body(req);
            acceptLanguage = req.Headers["Accept-Language"];
            requestedWith = req.Headers["X-Requested-With"];
            return (200, """{"access_token":"AT","refresh_token":"RT","expires_in":3600}""", null);
        });
        MsTokens tokens = MsLoginSteps.RefreshTokens("rt_code");
        //旧 :811-814：表单体（refresh_token 经 FormUrlEscape）、Accept-Language 与 X-Requested-With 头
        Assert.Equal("client_id=test-client-id&refresh_token=rt_code&grant_type=refresh_token&scope=XboxLive.signin%20offline_access", body);
        Assert.Equal("application/x-www-form-urlencoded", contentType);
        //旧 :813 逐字发送 "en-US,en;q=0.5"；HttpListener 会将其规范化渲染为 "en-US, en; q=0.5"
        Assert.StartsWith("en-US", acceptLanguage);
        Assert.Contains("q=0.5", acceptLanguage);
        Assert.Equal("XMLHttpRequest", requestedWith);
        Assert.Equal(("AT", "RT"), (tokens.AccessToken, tokens.RefreshToken));
    }

    [Theory]
    [InlineData("""{"error":"user Must Sign In Again"}""")]   //ContainsIgnoreCase
    [InlineData("""{"error":"Password Expired"}""")]   //ContainsIgnoreCase
    [InlineData("""{"error_description":"refresh_token is NOT valid"}""")]   //refresh_token（区分大小写）+ is not valid（#269）
    [InlineData("""{"error":"token has EXPIRED"}""")]   //#8611：ContainsIgnoreCase("expired")
    public void Refresh_relogin_table_returns_signal(string response)
    {
        UseEndpoints(_ => (400, response, null));
        MsTokens tokens = MsLoginSteps.RefreshTokens("rt");
        //旧 :821：返回 ("Relogin", "")，宿主 McLoginMsStart 判定不变
        Assert.Equal("Relogin", tokens.AccessToken);
        Assert.Equal("", tokens.RefreshToken);
    }

    [Fact]
    public void Refresh_security_interrupt_throws_decision()
    {
        UseEndpoints(static _ => (400, """{"error":"Account security interrupt"}""", null));
        MsLoginDecisionException ex = Assert.Throws<MsLoginDecisionException>(static () => MsLoginSteps.RefreshTokens("rt"));
        Assert.Equal(MsLoginDecision.SecurityInterrupt, ex.Decision);
        Assert.Equal("该账号由于安全问题无法登陆，请前往微软账户页获取更多信息。", ex.Text);   //旧 :823 文案逐字
        Assert.Equal("登录失败", ex.Title);
        Assert.Equal("我知道了", ex.Button1);
        Assert.True(ex.IsWarn);
    }

    [Fact]
    public void Refresh_service_abuse_throws_decision()
    {
        UseEndpoints(static _ => (400, """{"error":"service abuse"}""", null));
        MsLoginDecisionException ex = Assert.Throws<MsLoginDecisionException>(static () => MsLoginSteps.RefreshTokens("rt"));
        Assert.Equal(MsLoginDecision.ServiceAbuse, ex.Decision);
        Assert.Equal("非常抱歉，该账号已被微软封禁，无法登录。", ex.Text);   //旧 :826 文案逐字
        Assert.Equal("登录失败", ex.Title);
        Assert.True(ex.IsWarn);
    }

    [Fact]
    public void Refresh_other_error_rethrows()
    {
        UseEndpoints(static _ => (400, """{"error":"some random error"}""", null));
        Assert.ThrowsAny<HttpRequestCodeException>(static () => MsLoginSteps.RefreshTokens("rt"));   //旧 :829 Else Throw
    }

    [Fact]
    public void Refresh_first_loser_second_thread_wins()
    {
        var calls = 0;
        UseEndpoints(_ => ++calls == 1 ? (500, "first fails", null) : (200, """{"access_token":"AT2","refresh_token":"RT2"}""", null));
        MsTokens tokens = MsLoginSteps.RefreshTokens("rt");
        Assert.Equal(2, calls);   //旧 :810-814 ThreadCount:=2：线程 1 失败后线程 2 胜出
        Assert.Equal(("AT2", "RT2"), (tokens.AccessToken, tokens.RefreshToken));
    }

    [Fact]
    public void Refresh_escapes_refresh_token_via_form_url_escape()
    {
        string? body = null;
        UseEndpoints(req =>
        {
            body = Body(req);
            return (200, """{"access_token":"AT","refresh_token":"RT"}""", null);
        });
        MsLoginSteps.RefreshTokens("ab cd");   //空格若未转义会破坏表单体
        Assert.Contains("&refresh_token=ab+cd&", body);   //旧 :811 FormUrlEscape：空格转 +
    }

    #endregion

    #region XblAuthenticate（旧 Step2 :839-853：RpsTicket 的 d= 前缀与请求体）

    [Fact]
    public void Xbl_adds_d_prefix_to_plain_token()
    {
        string? body = null;
        UseEndpoints(req =>
        {
            body = Body(req);
            return (200, """{"Token":"XBLT","IssueInstant":"2026-01-01"}""", null);
        });
        var token = MsLoginSteps.XblAuthenticate("TOKEN123");
        //旧 :842-850 逐字（不以 d= 开头的令牌补 d= 前缀）；换行随源文件行尾（旧 VB 源为 CRLF），比较不区分行尾
        Assert.Equal("""
            {
               "Properties": {
                   "AuthMethod": "RPS",
                   "SiteName": "user.auth.xboxlive.com",
                   "RpsTicket": "d=TOKEN123"
               },
               "RelyingParty": "http://auth.xboxlive.com",
               "TokenType": "JWT"
            }
            """, body, ignoreLineEndingDifferences: true);
        Assert.Equal("XBLT", token);
    }

    [Fact]
    public void Xbl_keeps_d_prefix_when_present()
    {
        string? body = null;
        UseEndpoints(req =>
        {
            body = Body(req);
            return (200, """{"Token":"XBLT"}""", null);
        });
        MsLoginSteps.XblAuthenticate("d=ABC");
        Assert.Contains("\"RpsTicket\": \"d=ABC\"", body);   //旧 :846：已带 d= 前缀则不重复添加
    }

    #endregion

    #region XstsAuthorize（旧 Step3 :855-905：214891623x 错误码分诊表逐字）

    [Fact]
    public void Xsts_parses_token_and_uhs_with_verbatim_body()
    {
        string? body = null;
        UseEndpoints(req =>
        {
            body = Body(req);
            return (200, """{"Token":"XSTST","DisplayClaims":{"xui":[{"uhs":"UHS1"}]}}""", null);
        });
        MsTokens tokens = MsLoginSteps.XstsAuthorize("XBLT");
        //旧 :858-865 逐字；换行随源文件行尾（旧 VB 源为 CRLF），比较不区分行尾
        Assert.Equal("""
            {
               "Properties": {
                   "SandboxId": "RETAIL",
                   "UserTokens": ["XBLT"]
               },
               "RelyingParty": "rp://api.minecraftservices.com/",
               "TokenType": "JWT"
            }
            """, body, ignoreLineEndingDifferences: true);
        //旧 :901-904：XSTSToken 与 UHS
        Assert.Equal(("XSTST", "UHS1"), (tokens.AccessToken, tokens.Uhs));
        Assert.Equal("", tokens.RefreshToken);
    }

    [Theory]
    [InlineData("2148916227", MsLoginDecision.XstsBanned)]
    [InlineData("2148916233", MsLoginDecision.XstsNotRegistered)]
    [InlineData("2148916235", MsLoginDecision.XstsRegionUnavailable)]
    [InlineData("2148916238", MsLoginDecision.XstsAgeInsufficient)]
    public void Xsts_error_code_table_maps_to_decisions(string errorCode, MsLoginDecision decision)
    {
        UseEndpoints(_ => (401, "{\"XErr\":" + errorCode + ",\"Message\":\"Whoa body\"}", null));
        MsLoginDecisionException ex = Assert.Throws<MsLoginDecisionException>(static () => MsLoginSteps.XstsAuthorize("XBLT"));
        Assert.Equal(decision, ex.Decision);
    }

    [Fact]
    public void Xsts_decision_dialog_specs_verbatim()
    {
        //四档弹窗规格逐字（旧 :873/:876/:881/:884-885 的文案、标题、按钮与 IsWarn），宿主按此映射 MyMsgBox
        UseEndpoints(static _ => (401, "2148916227", null));
        MsLoginDecisionException banned = Assert.Throws<MsLoginDecisionException>(static () => MsLoginSteps.XstsAuthorize("XBLT"));
        Assert.Equal(("该账号似乎已被微软封禁，无法登录。", "登录失败", "我知道了", null, null, true),
            (banned.Text, banned.Title, banned.Button1, banned.Button2, banned.Button3, banned.IsWarn));

        _servers[^1].Responder = static _ => (401, "2148916233", null);
        MsLoginDecisionException notRegistered = Assert.Throws<MsLoginDecisionException>(static () => MsLoginSteps.XstsAuthorize("XBLT"));
        Assert.Equal(("你尚未注册 Xbox 账户，请在注册后再登录。", "登录提示", "注册", "取消", null, false),
            (notRegistered.Text, notRegistered.Title, notRegistered.Button1, notRegistered.Button2, notRegistered.Button3, notRegistered.IsWarn));

        _servers[^1].Responder = static _ => (401, "2148916235", null);
        MsLoginDecisionException region = Assert.Throws<MsLoginDecisionException>(static () => MsLoginSteps.XstsAuthorize("XBLT"));
        Assert.Equal(("你的网络所在的国家或地区无法登录微软账号。\r\n请使用加速器或 VPN，然后再试。", "登录失败", "我知道了", null, null, false),
            (region.Text, region.Title, region.Button1, region.Button2, region.Button3, region.IsWarn));

        _servers[^1].Responder = static _ => (401, "2148916238", null);
        MsLoginDecisionException age = Assert.Throws<MsLoginDecisionException>(static () => MsLoginSteps.XstsAuthorize("XBLT"));
        Assert.Equal(("该账号年龄不足，你需要先修改出生日期，然后才能登录。\r\n该账号目前填写的年龄是否在 13 岁以上？", "登录提示", "13 岁以上", "12 岁以下", "我不知道", false),
            (age.Text, age.Title, age.Button1, age.Button2, age.Button3, age.IsWarn));
    }

    [Fact]
    public void Xsts_other_error_rethrows()
    {
        UseEndpoints(static _ => (401, """{"XErr":1}""", null));
        Assert.ThrowsAny<HttpRequestCodeException>(static () => MsLoginSteps.XstsAuthorize("XBLT"));   //旧 :896 Else Throw
    }

    #endregion

    #region MinecraftToken（旧 Step4 :907-937：identityToken、429/403/503 红字与提前 1 小时过期）

    [Fact]
    public void McToken_posts_identity_token_and_applies_early_expiry()
    {
        string? body = null, contentType = null;
        UseEndpoints(req =>
        {
            body = Body(req);
            contentType = req.Headers["Content-Type"];
            return (200, """{"access_token":"MCAT","expires_in":86400}""", null);
        });
        (string accessToken, int expiresAt) = MsLoginSteps.MinecraftToken("XSTST", "UHS1");
        //旧 :910：{"identityToken":"XBL3.0 x=UHS;XSTST"}
        Assert.Equal("""{"identityToken":"XBL3.0 x=UHS1;XSTST"}""", body);
        Assert.Equal("application/json", contentType);
        Assert.Equal("MCAT", accessToken);
        //旧 :936：expires_in + 当前 Unix 时间戳 - 3600（提前 1 小时视作过期）
        var expected = 86400 + DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600;
        Assert.InRange(expiresAt, expected - 10, expected + 10);
    }

    [Fact]
    public void McToken_403_throws_verbatim_red_text()
    {
        UseEndpoints(static _ => (403, "forbidden", null));
        Exception ex = Assert.Throws<Exception>(static () => MsLoginSteps.MinecraftToken("XSTST", "UHS1"));
        Assert.Equal("$当前 IP 的登录尝试异常。\r\n如果你使用了 VPN 或加速器，请把它们关掉或更换节点后再试！", ex.Message);   //旧 :921 逐字
    }

    [Fact]
    public void McToken_503_throws_verbatim_red_text()
    {
        UseEndpoints(static _ => (503, "unavailable", null));
        Exception ex = Assert.Throws<Exception>(static () => MsLoginSteps.MinecraftToken("XSTST", "UHS1"));
        Assert.Equal("$Mojang 服务器出现问题，请稍后再试。\r\n你的网络是正常的，PCL 也是正常的，是 Mojang 出问题了……", ex.Message);   //旧 :924 逐字
    }

    [Fact]
    public void McToken_account_suspended_throws_decision()
    {
        UseEndpoints(static _ => (400, """{"error":"ACCOUNT_SUSPENDED"}""", null));   //#8655
        MsLoginDecisionException ex = Assert.Throws<MsLoginDecisionException>(static () => MsLoginSteps.MinecraftToken("XSTST", "UHS1"));
        Assert.Equal(MsLoginDecision.AccountSuspended, ex.Decision);
        Assert.Equal("该账号似乎已被微软封禁，无法登录。", ex.Text);   //旧 :926 文案逐字
        Assert.Equal("登录失败", ex.Title);
        Assert.True(ex.IsWarn);
    }

    [Fact]
    public void McToken_other_error_rethrows()
    {
        UseEndpoints(static _ => (400, """{"error":"other"}""", null));
        Assert.ThrowsAny<HttpRequestCodeException>(static () => MsLoginSteps.MinecraftToken("XSTST", "UHS1"));   //旧 :929 Else Throw
    }

    #endregion

    #region EntitlementsOk（旧 Step5 :939-957：items 判定与未购买决策）

    [Fact]
    public void Entitlements_true_with_items_and_bearer_header()
    {
        string? authorization = null;
        string? method = null;
        UseEndpoints(req =>
        {
            authorization = req.Headers["Authorization"];
            method = req.HttpMethod;
            return (200, """{"items":[{"sku":"GAME"}]}""", null);
        });
        Assert.True(MsLoginSteps.EntitlementsOk("MCAT"));
        Assert.Equal("Bearer MCAT", authorization);   //旧 :943
        Assert.Equal("GET", method);   //旧 :942 未传 Method
    }

    [Fact]
    public void Entitlements_empty_items_throws_purchase_decision()
    {
        UseEndpoints(static _ => (200, """{"items":[]}""", null));
        MsLoginDecisionException ex = Assert.Throws<MsLoginDecisionException>(static () => MsLoginSteps.EntitlementsOk("MCAT"));
        Assert.Equal(MsLoginDecision.EntitlementMissing, ex.Decision);
        Assert.Equal("你尚未购买正版 Minecraft，或者 Xbox Game Pass 已到期。", ex.Text);   //旧 :947 文案逐字
        Assert.Equal("登录失败", ex.Title);
        Assert.Equal(("购买 Minecraft", "取消", null, false), (ex.Button1, ex.Button2, ex.Button3, ex.IsWarn));
    }

    [Theory]
    [InlineData("""{"foo":1}""")]   //缺键：旧 :946 AndAlso 短路（ContainsKey=False 即不再评估 .Any）→ Not(False) 走购买弹窗
    [InlineData("""{"items":null}""")]   //JSON null：JValue 空枚举 .Any=False → 购买弹窗（旧代码无 NRE 路径）
    public void Entitlements_missing_or_null_items_throw_purchase_decision(string response)
    {
        UseEndpoints(_ => (200, response, null));
        MsLoginDecisionException ex = Assert.Throws<MsLoginDecisionException>(static () => MsLoginSteps.EntitlementsOk("MCAT"));
        Assert.Equal(MsLoginDecision.EntitlementMissing, ex.Decision);
        Assert.Equal("你尚未购买正版 Minecraft，或者 Xbox Game Pass 已到期。", ex.Text);   //旧 :947 文案逐字
        Assert.Equal(("购买 Minecraft", "取消", null, false), (ex.Button1, ex.Button2, ex.Button3, ex.IsWarn));
    }

    [Fact]
    public void Entitlements_non_empty_object_items_count_as_held()
    {
        //旧 :946 的 .Any 按子项计数：非空 JSON 对象亦视为持有——锁定 C# 与该真值一致
        UseEndpoints(static _ => (200, """{"items":{"sku":"GAME"}}""", null));
        Assert.True(MsLoginSteps.EntitlementsOk("MCAT"));
    }

    [Fact]
    public void Entitlements_non_json_rejected_before_parse()
    {
        //旧 RequireJson:=True：非 JSON 在请求面被拦（FormatException），不经 :953 的 Warn 包装
        UseEndpoints(static _ => (200, "not json", null));
        Assert.Throws<FormatException>(static () => MsLoginSteps.EntitlementsOk("MCAT"));
    }

    #endregion

    #region Profile（旧 Step6 :959-989：429/404 分诊与档案解析）

    [Fact]
    public void Profile_parses_id_name_and_keeps_raw_json()
    {
        const string raw = """{"id":"E3B7C9D2A1B4F6058972C4D1E0A3B5C7","name":"Steve"}""";
        string? authorization = null;
        UseEndpoints(req =>
        {
            authorization = req.Headers["Authorization"];
            return (200, raw, null);
        });
        MsProfile profile = MsLoginSteps.Profile("MCAT");
        Assert.Equal("Bearer MCAT", authorization);
        Assert.Equal(("E3B7C9D2A1B4F6058972C4D1E0A3B5C7", "Steve", raw), (profile.Uuid, profile.UserName, profile.ProfileJson));
    }

    [Fact]
    public void Profile_404_throws_create_profile_decision()
    {
        UseEndpoints(static _ => (404, """{"title":"NotFound"}""", null));
        MsLoginDecisionException ex = Assert.Throws<MsLoginDecisionException>(static () => MsLoginSteps.Profile("MCAT"));
        Assert.Equal(MsLoginDecision.ProfileMissing, ex.Decision);
        Assert.Equal("请先创建 Minecraft 玩家档案，然后再重新登录。", ex.Text);   //旧 :975 文案逐字
        Assert.Equal("登录失败", ex.Title);
        Assert.Equal(("创建档案", "取消", null, false), (ex.Button1, ex.Button2, ex.Button3, ex.IsWarn));
    }

    [Fact]
    public void Profile_429_throws_verbatim_red_text()
    {
        UseEndpoints(static _ => (429, "too many", null));
        Exception ex = Assert.Throws<Exception>(static () => MsLoginSteps.Profile("MCAT"));
        Assert.Equal("$登录尝试太过频繁，请等待几分钟后再试！", ex.Message);   //旧 :970 逐字
    }

    [Fact]
    public void Profile_other_error_rethrows()
    {
        UseEndpoints(static _ => (500, "server error", null));
        Assert.ThrowsAny<HttpRequestCodeException>(static () => MsLoginSteps.Profile("MCAT"));   //旧 :982 Case Else Throw
    }

    #endregion
}