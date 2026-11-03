using PCLCS.Net;
using System.Text.Json.Nodes;

namespace PCLCS.Launch;

/// <summary>轮询回调返回态（旧 WaitingMyMsgBox 弹窗 Converter.Result 的三态收敛：令牌对 / RestartException / 异常）。</summary>
public enum MsPollOutcome
{
    /// <summary>令牌已取得（旧 Result 为 (token0, token1) 数组）。</summary>
    Success,
    /// <summary>重开设备码流（旧 Result 为 RestartException → 宿主密码登录引导弹窗选「重新登录」）。</summary>
    Retry,
    /// <summary>中止登录（旧 Result 为 Exception，含用户取消的 OperationCanceledException）。</summary>
    Failure
}

/// <summary>轮询回调的返回（旧 :785-802 弹窗终态）；由宿主轮询弹窗构造。</summary>
public sealed class MsPollResult
{
    public MsPollOutcome Outcome;
    /// <summary>Success 态的 OAuth 令牌对。</summary>
    public string? AccessToken;
    public string? RefreshToken;
    /// <summary>Failure 态的原样异常（旧 :798-799 TypeOf Exception → 原样抛出）。</summary>
    public Exception? Error;

    public static MsPollResult Succeed(string accessToken, string refreshToken) => new() { Outcome = MsPollOutcome.Success, AccessToken = accessToken, RefreshToken = refreshToken };
    public static MsPollResult Retry() => new() { Outcome = MsPollOutcome.Retry };
    public static MsPollResult Fail(Exception error) => new() { Outcome = MsPollOutcome.Failure, Error = error };
}

/// <summary>设备码流产出（旧 :801 的 (Result(0), Result(1)) 元组收敛）。</summary>
public sealed class MsDeviceCode
{
    public string OAuthAccessToken = "";
    public string OAuthRefreshToken = "";
}

/// <summary>微软登录令牌载体（旧各步返回元组的收敛；空串字段表示该步不产出）。</summary>
public sealed class MsTokens
{
    /// <summary>Step1Refresh 为 OAuth AccessToken（Relogin 信号原样占用此字段，宿主 McLoginMsStart :576 判定不变）；Step2 为 XBLToken；Step3 为 XSTSToken。</summary>
    public string AccessToken = "";
    /// <summary>Step1Refresh 为 OAuth RefreshToken；其余步骤为空串。</summary>
    public string RefreshToken = "";
    /// <summary>Step3 为 UHS（旧 :903 的 DisplayClaims.xui(0).uhs）；其余步骤为空串。</summary>
    public string Uhs = "";
}

/// <summary>玩家档案产出（旧 :988 的 (UUID, UserName, ProfileJson) 元组收敛）。</summary>
public sealed class MsProfile
{
    public string Uuid = "";
    public string UserName = "";
    /// <summary>profile 响应原文（宿主写入 CacheMsV2ProfileJson）。</summary>
    public string ProfileJson = "";
}

/// <summary>需要宿主弹窗的登录决策（旧各步 MyMsgBox 分支收敛）：宿主按枚举映射弹窗并在弹后抛出取消（旧分支语义）。</summary>
public enum MsLoginDecision
{
    /// <summary>Step1Refresh：账号安全问题（旧 :822-824）。</summary>
    SecurityInterrupt,
    /// <summary>Step1Refresh：账号被微软封禁（旧 :825-827）。</summary>
    ServiceAbuse,
    /// <summary>Step3：账号被微软封禁（旧 :872-874，Whoa 2148916227）。</summary>
    XstsBanned,
    /// <summary>Step3：未注册 Xbox 账户（旧 :875-879，Whoa 2148916233）。</summary>
    XstsNotRegistered,
    /// <summary>Step3：网络所在国家或地区不可用（旧 :880-882，Whoa 2148916235）。</summary>
    XstsRegionUnavailable,
    /// <summary>Step3：年龄不足需改出生日期（旧 :883-894，Whoa 2148916238）。</summary>
    XstsAgeInsufficient,
    /// <summary>Step4：账号被封禁（旧 :925-927，ACCOUNT_SUSPENDED，#8655）。</summary>
    AccountSuspended,
    /// <summary>Step5：未购买 Minecraft 或 XGP 已到期（旧 :946-951）。</summary>
    EntitlementMissing,
    /// <summary>Step6：玩家档案不存在（旧 :971-980，404）。</summary>
    ProfileMissing
}

/// <summary>
/// 微软登录决策异常：网络核在错误分诊表命中弹窗分支时抛出，弹窗面留宿主
/// （文案、标题、按钮与 IsWarn 逐字随迁，宿主按 Decision 映射 MyMsgBox 后抛出取消）。
/// </summary>
public sealed class MsLoginDecisionException : Exception
{
    /// <summary>决策枚举（宿主 Select Case 的映射键）。</summary>
    public MsLoginDecision Decision { get; }
    /// <summary>弹窗正文（旧 MyMsgBox 文案逐字）。</summary>
    public string Text { get; }
    /// <summary>弹窗标题（旧 MyMsgBox 标题逐字）。</summary>
    public string Title { get; }
    /// <summary>弹窗按钮（旧 MyMsgBox 按钮组逐字；单按钮弹窗仅 Button1）。</summary>
    public string Button1 { get; }
    public string? Button2 { get; }
    public string? Button3 { get; }
    /// <summary>是否警告样式（旧 IsWarn）。</summary>
    public bool IsWarn { get; }

    public MsLoginDecisionException(MsLoginDecision decision, string text, string title,
        string button1 = "我知道了", string? button2 = null, string? button3 = null, bool isWarn = false)
        : base(text)
    {
        Decision = decision;
        Text = text;
        Title = title;
        Button1 = button1;
        Button2 = button2;
        Button3 = button3;
        IsWarn = isWarn;
    }
}

/// <summary>
/// 微软登录六步网络核（自宿主层 ModLaunch.vb 移植）：
/// 设备码流（旧 :773-803）、refresh_token 交换（旧 :805-837）、XBL（旧 :839-853）、XSTS（旧 :855-905）、
/// MTS 令牌（旧 :907-937）、entitlements 校验（旧 :939-957）与档案查询（旧 :959-989）。
/// 编排壳（McLoginMsStart :547-614）与 CacheMsV2* 缓存读写、WaitingMyMsgBox 轮询弹窗、错误 MyMsgBox 分支留在宿主层；
/// 本核只产出令牌与决策（<see cref="MsLoginDecisionException"/>），弹窗文案随迁供宿主显示。
/// </summary>
public static class MsLoginSteps
{

    /// <summary>OAuth ClientId 提供者（旧 :780/:811 的 OAuthClientId，宿主层接线 ModSecret.OAuthClientId）。null 时在取值处抛出未接线异常。</summary>
    public static Func<string>? OAuthClientIdProvider { get; set; }

    /// <summary>
    /// UI 线程判定回调（旧 VB 转发面恒传 AddressOf RunInUi，ModNet.vb:63/:76；
    /// NetRequest.SendRequest 以此在 UI 线程发真实网络请求时快速失败，ModNet.vb:219）。null 视为非 UI 线程（测试环境）。宿主层接线。
    /// </summary>
    public static Func<bool>? RunInUiProvider { get; set; }

    //* 测试注入面：旧代码硬编码微软/Mojang 端点，回环测试改指本机假服务器（LoginCores.MojangApiBase 同模式）
    internal static string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";   //旧 :779
    internal static string RefreshUrl = "https://login.live.com/oauth20_token.srf";   //旧 :810
    internal static string XblUrl = "https://user.auth.xboxlive.com/user/authenticate";   //旧 :851
    internal static string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";   //旧 :868
    internal static string McLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";   //旧 :913
    internal static string EntitlementsUrl = "https://api.minecraftservices.com/entitlements/mcstore";   //旧 :942
    internal static string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";   //旧 :964

    #region 微软登录步骤 1：设备码流 / refresh_token 交换

    /// <summary>
    /// 微软登录步骤 1，原始登录（旧 :773-803 决策面逐行）：
    /// 获取 DeviceCode 并把轮询参数交给宿主弹窗（<paramref name="pollCallback"/> 注入 WaitingMyMsgBox 轮询面），
    /// 按弹窗终态收敛：令牌对返回、异常原样抛出、重开流（旧 GoTo Retry）。
    /// </summary>
    /// <param name="pollCallback">轮询弹窗回调：入参为 devicecode 响应 JSON（旧 :785 的 Converter.Content = PrepareJson），
    /// 返回弹窗终态。宿主侧覆盖弹窗展示、令牌轮询与密码登录引导（RestartException 面）。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    public static MsDeviceCode BeginDeviceCodeFlow(Func<string, MsPollResult> pollCallback, Action<string>? log = null)
    {
        while (true)   //旧 :777 GoTo Retry 环
        {
            log?.Invoke("开始微软登录步骤 1/6（原始登录）");   //旧 :778 逐字
            JsonNode prepareJson = JsonNode.Parse(NetRequest.NetRequestByClientRetry(DeviceCodeUrl, new NetRequestOptions
            {
                Method = HttpMethod.Post,
                Content = $"client_id={OAuthClientIdProvider!()}&tenant=/consumers&scope=XboxLive.signin%20offline_access",   //旧 :780 逐字
                ContentType = "application/x-www-form-urlencoded",
                Accept = "*/*",   //旧 VB NetRequestByClient 转发面默认 Accept="*/*"
                RequireJson = true,
                RunInUi = RunInUiProvider   //旧 VB 转发面恒传 AddressOf RunInUi（UI 线程守卫）
            }))!;   //旧 :781 DeserializeJson；响应非 JSON 已被 RequireJson 拦截
            //! 逐字移植豁免：旧 :782 的显式 .ToString() 在字符串拼接下冗余（object 重载同样经虚 ToString 求值），故省去显式调用
            log?.Invoke("网页登录地址：" + prepareJson["verification_uri"]!);   //旧 :782 逐字
            MsPollResult poll = pollCallback(prepareJson.ToJsonString());   //轮询参数产出：devicecode 响应整体交宿主弹窗
            switch (poll.Outcome)
            {
            case MsPollOutcome.Success:
                return new MsDeviceCode { OAuthAccessToken = poll.AccessToken!, OAuthRefreshToken = poll.RefreshToken! };   //旧 :801
            case MsPollOutcome.Failure:
                throw poll.Error!;   //旧 :798-799：弹窗异常（含取消 OCE）原样抛出
            default:
                continue;   //旧 :794：宿主密码登录引导选「重新登录」→ 重开设备码流
            }
        }
    }

    /// <summary>
    /// 微软登录步骤 1，刷新登录（旧 :805-837 决策面逐行）：
    /// 以 refresh_token 换取 OAuth 令牌对（单 URL 双线程竞速，先成功者胜出）；
    /// 错误分诊表逐字——Relogin 信号原样占用 <see cref="MsTokens.AccessToken"/>（宿主 GoTo Relogin 判定不变），
    /// 安全问题 / 封禁抛 <see cref="MsLoginDecisionException"/> 由宿主弹窗。
    /// </summary>
    /// <param name="refreshToken">缓存的 OAuth Code 或 OAuth RefreshToken（旧 :805 的 Code）。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    public static MsTokens RefreshTokens(string refreshToken, Action<string>? log = null)
    {
        log?.Invoke("开始微软登录步骤 1/6（刷新登录）");   //旧 :806 逐字
        string result;
        try
        {
            result = NetRequest.NetRequestByClientMultiple(RefreshUrl, 2, new NetRequestOptions   //旧 :810-814：ThreadCount:=2
            {
                Method = HttpMethod.Post,
                Content = $"client_id={OAuthClientIdProvider!()}&refresh_token={StringUtils.FormUrlEscape(refreshToken)}&grant_type=refresh_token&scope=XboxLive.signin%20offline_access",   //旧 :811 逐字
                ContentType = "application/x-www-form-urlencoded",
                Accept = "*/*",   //旧 VB NetRequestByClient 转发面默认
                Headers = new Dictionary<string, string>
                {
                    ["Accept-Language"] = "en-US,en;q=0.5",   //旧 :813 逐字
                    ["X-Requested-With"] = "XMLHttpRequest"
                },
                TimeoutMs = 25000,   //旧 VB NetRequestByClientMultiple 转发面默认 Timeout=25000
                RunInUi = RunInUiProvider
            });
        } catch (HttpRequestCodeException ex)
        {
            var response = ex.Response!;   //旧 :817：SendRequest 恒给非空 Response（空体为空串），无守卫直接解引用
            //旧 :818-820 的 Relogin 判定链逐字（修改错误列表时，同时检查宿主 MyMsgLogin.xaml.vb 中的对应代码——该联动留弹窗侧）
            if (response.ContainsIgnoreCase("must sign in again") || response.ContainsIgnoreCase("password expired") ||
                (response.Contains("refresh_token") && response.ContainsIgnoreCase("is not valid")) ||   //#269
                response.ContainsIgnoreCase("expired"))   //#8611
                return new MsTokens { AccessToken = "Relogin" };   //旧 :821：("Relogin", "")
            if (response.Contains("Account security interrupt"))
                throw new MsLoginDecisionException(MsLoginDecision.SecurityInterrupt,
                    "该账号由于安全问题无法登陆，请前往微软账户页获取更多信息。", "登录失败", isWarn: true);   //旧 :822-824 文案逐字
            if (response.Contains("service abuse"))
                throw new MsLoginDecisionException(MsLoginDecision.ServiceAbuse,
                    "非常抱歉，该账号已被微软封禁，无法登录。", "登录失败", isWarn: true);   //旧 :825-827 文案逐字
            throw;   //旧 :829 Else Throw
        }
        JsonObject resultJson = JsonNode.Parse(result)!.AsObject();   //旧 :833 DeserializeJson
        return new MsTokens
        {
            AccessToken = resultJson["access_token"]!.ToString(),   //旧 :834
            RefreshToken = resultJson["refresh_token"]!.ToString()   //旧 :835
        };
    }

    #endregion

    #region 微软登录步骤 2-3：XBL / XSTS

    /// <summary>
    /// 微软登录步骤 2（旧 :839-853 逐行）：以 OAuth AccessToken 换取 XBLToken。
    /// RpsTicket 对不以 "d=" 开头的令牌补 "d=" 前缀（旧 :846）。
    /// </summary>
    /// <param name="accessToken">OAuth AccessToken。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    public static string XblAuthenticate(string accessToken, Action<string>? log = null)
    {
        log?.Invoke("开始微软登录步骤 2/6");   //旧 :840 逐字
        var request = $$"""
            {
               "Properties": {
                   "AuthMethod": "RPS",
                   "SiteName": "user.auth.xboxlive.com",
                   "RpsTicket": "{{(accessToken.StartsWithF("d=") ? "" : "d=")}}{{accessToken}}"
               },
               "RelyingParty": "http://auth.xboxlive.com",
               "TokenType": "JWT"
            }
            """;
        var result = NetRequest.NetRequestByClientMultiple(XblUrl, 3, new NetRequestOptions   //旧 :851：ThreadCount 默认 3
        {
            Method = HttpMethod.Post,
            Content = request,   //旧 :842-850 逐字
            ContentType = "application/json",
            Accept = "*/*",   //旧 VB NetRequestByClient 转发面默认
            RequireJson = true,
            TimeoutMs = 25000,   //旧 VB NetRequestByClientMultiple 转发面默认
            RunInUi = RunInUiProvider
        });
        return JsonNode.Parse(result)!["Token"]!.ToString();   //旧 :852
    }

    /// <summary>
    /// 微软登录步骤 3（旧 :855-905 决策面逐行）：从 XBLToken 获取 XSTSToken 与 UHS。
    /// Whoa 错误码分诊表逐字（214891623x），命中弹窗分支时抛 <see cref="MsLoginDecisionException"/>，弹窗与取消由宿主承接。
    /// </summary>
    /// <param name="xblToken">步骤 2 取得的 XBLToken。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    public static MsTokens XstsAuthorize(string xblToken, Action<string>? log = null)
    {
        log?.Invoke("开始微软登录步骤 3/6");   //旧 :856 逐字
        var request = $$"""
            {
               "Properties": {
                   "SandboxId": "RETAIL",
                   "UserTokens": ["{{xblToken}}"]
               },
               "RelyingParty": "rp://api.minecraftservices.com/",
               "TokenType": "JWT"
            }
            """;
        string result;
        try
        {
            result = NetRequest.NetRequestByClientMultiple(XstsUrl, 3, new NetRequestOptions   //旧 :868：ThreadCount 默认 3
            {
                Method = HttpMethod.Post,
                Content = request,   //旧 :858-865 逐字
                ContentType = "application/json",
                Accept = "*/*",   //旧 VB NetRequestByClient 转发面默认
                RequireJson = true,
                TimeoutMs = 25000,   //旧 VB NetRequestByClientMultiple 转发面默认
                RunInUi = RunInUiProvider
            });
        } catch (HttpRequestCodeException ex)
        {
            //参考 https://github.com/PrismarineJS/prismarine-auth/blob/master/src/common/Constants.js
            var response = ex.Response!;   //旧 :870 同上：无守卫直接解引用
            if (response.Contains("2148916227"))
                throw new MsLoginDecisionException(MsLoginDecision.XstsBanned,
                    "该账号似乎已被微软封禁，无法登录。", "登录失败", isWarn: true);   //旧 :872-874 文案逐字
            if (response.Contains("2148916233"))
                throw new MsLoginDecisionException(MsLoginDecision.XstsNotRegistered,
                    "你尚未注册 Xbox 账户，请在注册后再登录。", "登录提示", "注册", "取消");   //旧 :875-879 文案逐字
            if (response.Contains("2148916235"))
                throw new MsLoginDecisionException(MsLoginDecision.XstsRegionUnavailable,
                    "你的网络所在的国家或地区无法登录微软账号。\r\n请使用加速器或 VPN，然后再试。", "登录失败");   //旧 :880-882 文案逐字（vbCrLf）
            if (response.Contains("2148916238"))
                throw new MsLoginDecisionException(MsLoginDecision.XstsAgeInsufficient,
                    "该账号年龄不足，你需要先修改出生日期，然后才能登录。\r\n该账号目前填写的年龄是否在 13 岁以上？", "登录提示",
                    "13 岁以上", "12 岁以下", "我不知道");   //旧 :883-894 文案逐字（vbCrLf）；改年龄引导弹窗与网页跳转由宿主映射
            throw;   //旧 :896 Else Throw
        }
        JsonNode resultJson = JsonNode.Parse(result)!;   //旧 :900 DeserializeJson
        return new MsTokens
        {
            AccessToken = resultJson["Token"]!.ToString(),   //旧 :902 XSTSToken
            Uhs = resultJson["DisplayClaims"]!["xui"]![0]!["uhs"]!.ToString()   //旧 :903 UHS
        };
    }

    #endregion

    #region 微软登录步骤 4-6：MTS 令牌 / entitlements / 档案

    /// <summary>
    /// 微软登录步骤 4（旧 :907-937 决策面逐行）：从 XSTSToken 与 UHS 换取 Minecraft AccessToken 与过期时刻
    /// （提前 1 小时视作过期，旧 :936）。429/403/503 抛红字异常（$ 前缀，加载器原样展示），
    /// ACCOUNT_SUSPENDED（#8655）抛 <see cref="MsLoginDecisionException"/> 由宿主弹窗。
    /// </summary>
    /// <param name="xstsToken">步骤 3 取得的 XSTSToken。</param>
    /// <param name="uhs">步骤 3 取得的 UHS。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    public static (string AccessToken, int ExpiresAt) MinecraftToken(string xstsToken, string uhs, Action<string>? log = null)
    {
        log?.Invoke("开始微软登录步骤 4/6");   //旧 :908 逐字
        string result;
        try
        {
            result = NetRequest.NetRequestByClientRetry(McLoginUrl, new NetRequestOptions   //旧 :913-914：45 秒 3 次尝试面
            {
                Method = HttpMethod.Post,
                Content = new JsonObject { ["identityToken"] = $"XBL3.0 x={uhs};{xstsToken}" }.ToJsonString(),   //旧 :910 ToString(Formatting.None)
                ContentType = "application/json",
                Accept = "*/*",   //旧 VB NetRequestByClient 转发面默认
                RequireJson = true,
                RunInUi = RunInUiProvider
            });
        } catch (HttpRequestCodeException ex)
        {
            switch (ex.StatusCode)
            {
            case (HttpStatusCode) 429:   //旧 :916 Case 429
                Logger.Warn(ex, "微软登录第 4 步汇报 429");
                throw new Exception("$登录尝试太过频繁，请等待几分钟后再试！");
            case HttpStatusCode.Forbidden:   //旧 :919
                Logger.Warn(ex, "微软登录第 4 步汇报 403");
                throw new Exception("$当前 IP 的登录尝试异常。\r\n如果你使用了 VPN 或加速器，请把它们关掉或更换节点后再试！");
            case HttpStatusCode.ServiceUnavailable:   //旧 :922
                Logger.Warn(ex, "微软登录第 4 步汇报 503");
                throw new Exception("$Mojang 服务器出现问题，请稍后再试。\r\n你的网络是正常的，PCL 也是正常的，是 Mojang 出问题了……");
            default:
                if (ex.Response?.Contains("ACCOUNT_SUSPENDED") == true)   //#8655（旧 :925 的 Response?.Contains）
                    throw new MsLoginDecisionException(MsLoginDecision.AccountSuspended,
                        "该账号似乎已被微软封禁，无法登录。", "登录失败", isWarn: true);   //旧 :925-927 文案逐字
                throw;   //旧 :929 Else Throw
            }
        }
        JsonNode resultJson = JsonNode.Parse(result)!;   //旧 :933 DeserializeJson
        //? TECHDEBT：旧 :936 的 .ToObject(Of Integer) 亦能转换字符串型数值，STJ GetValue<int> 仅收数值 JSON；JSON null/缺键同归 NRE 面（病态响应差异）
        return (
            resultJson["access_token"]!.ToString(),   //旧 :935
            (int) (resultJson["expires_in"]!.GetValue<int>() + DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600));   //旧 :936：GetUnixTimestampUtc（ModBase.vb:840，Unix 秒）+ 提前 1 小时
    }

    /// <summary>
    /// 微软登录步骤 5（旧 :939-957 决策面逐行）：验证微软账号是否持有 Minecraft（这也会刷新 XGP）。
    /// items 缺失/JSON null/空容器抛 <see cref="MsLoginDecisionException"/> 由宿主弹购买引导；响应异常经 Warn 记录原始响应体后原样再抛（旧 :953-956）。
    /// </summary>
    /// <param name="accessToken">步骤 4 取得的 Minecraft AccessToken。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    public static bool EntitlementsOk(string accessToken, Action<string>? log = null)
    {
        log?.Invoke("开始微软登录步骤 5/6");   //旧 :940 逐字
        var result = NetRequest.NetRequestByClientMultiple(EntitlementsUrl, 2, new NetRequestOptions   //旧 :942-943：ThreadCount:=2，GET
        {
            ContentType = "application/json",
            Accept = "*/*",   //旧 VB NetRequestByClient 转发面默认
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + accessToken },   //旧 :943
            RequireJson = true,
            TimeoutMs = 25000,   //旧 VB NetRequestByClientMultiple 转发面默认
            RunInUi = RunInUiProvider
        });
        try
        {
            JsonObject resultJson = JsonNode.Parse(result)!.AsObject();
            JsonNode? items = resultJson["items"];
            //? 旧 :946 为 ContainsKey("items") AndAlso items.Any（AndAlso 短路）：缺键时不再评估 .Any，
            //? Not(False) 即走购买弹窗；JSON null 的 JValue 枚举为空（.Any=False）与空容器同弹窗——旧代码无 NRE 路径。
            //? .Any 按子项计数：非空数组/对象视为持有，标量/空容器/缺键/JSON null 视为未持有
            var itemCount = items switch
            {
                JsonArray array => array.Count,
                JsonObject obj => obj.Count,
                _ => 0
            };
            if (itemCount == 0)
                throw new MsLoginDecisionException(MsLoginDecision.EntitlementMissing,
                    "你尚未购买正版 Minecraft，或者 Xbox Game Pass 已到期。", "登录失败", "购买 Minecraft", "取消");   //旧 :947 文案逐字；商店跳转与取消由宿主映射
            return true;
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"微软登录第 5 步异常：{result}");   //旧 :954 逐字（含原始响应体）
            throw;
        }
    }

    /// <summary>
    /// 微软登录步骤 6（旧 :959-989 决策面逐行）：从 Minecraft AccessToken 获取 UUID、用户名与档案 JSON 原文。
    /// 429 抛红字异常；404 抛 <see cref="MsLoginDecisionException"/> 由宿主弹非阻塞建档引导（旧 RunInNewThread）后取消。
    /// </summary>
    /// <param name="accessToken">步骤 4 取得的 Minecraft AccessToken。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    public static MsProfile Profile(string accessToken, Action<string>? log = null)
    {
        log?.Invoke("开始微软登录步骤 6/6");   //旧 :960 逐字
        string result;
        try
        {
            result = NetRequest.NetRequestByClientMultiple(ProfileUrl, 2, new NetRequestOptions   //旧 :964-965：ThreadCount:=2，GET
            {
                ContentType = "application/json",
                Accept = "*/*",   //旧 VB NetRequestByClient 转发面默认
                Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + accessToken },   //旧 :965
                RequireJson = true,
                TimeoutMs = 25000,   //旧 VB NetRequestByClientMultiple 转发面默认
                RunInUi = RunInUiProvider
            });
        } catch (HttpRequestCodeException ex)
        {
            switch (ex.StatusCode)
            {
            case (HttpStatusCode) 429:   //旧 :968 Case 429
                Logger.Warn(ex, "微软登录第 6 步汇报 429");
                throw new Exception("$登录尝试太过频繁，请等待几分钟后再试！");
            case HttpStatusCode.NotFound:   //旧 :971
                Logger.Warn(ex, "微软登录第 6 步汇报 404");
                throw new MsLoginDecisionException(MsLoginDecision.ProfileMissing,
                    "请先创建 Minecraft 玩家档案，然后再重新登录。", "登录失败", "创建档案", "取消");   //旧 :975 文案逐字；非阻塞弹窗（旧 :973 RunInNewThread）与取消由宿主映射
            default:
                throw;   //旧 :982 Case Else Throw
            }
        }
        JsonNode resultJson = JsonNode.Parse(result)!;   //旧 :985 DeserializeJson
        return new MsProfile
        {
            Uuid = resultJson["id"]!.ToString(),   //旧 :986
            UserName = resultJson["name"]!.ToString(),   //旧 :987
            ProfileJson = result   //旧 :988
        };
    }

    #endregion

}