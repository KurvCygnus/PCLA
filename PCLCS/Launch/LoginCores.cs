using PCLCS.Common;
using PCLCS.Instances;
using PCLCS.Net;
using PCLCS.Settings;
using System.Globalization;
using System.Text.Json.Nodes;

namespace PCLCS.Launch;

/// <summary>
/// 登录核（自宿主层 ModLaunch.vb 移植）：
/// 离线 UUID 算法（旧 :1167-1170）、离线皮肤调整（旧 :1105-1142）、正版 UUID 查询与 ini 缓存（旧 :1144-1166）、
/// authlib/Nide 三段请求核 validate/refresh/login（旧 :710-883）。
/// 编排壳（McLoginServerStart :615-688、McLoginLegacyStart :689-707）与 UI 弹窗面留在宿主层。
/// </summary>
public static class LoginCores
{

    /// <summary>Mojang UUID 缓存目录提供者（宿主层接线 PathTemp &amp; "Cache\Uuid"；测试注入临时目录）。null 时在取值处抛出未接线异常。</summary>
    public static Func<string>? MojangUuidCacheFolder { get; set; }

    /// <summary>
    /// 已选实例的原版版本主版本号提供者（旧 :1127 的 McInstanceSelected.Version.Vanilla.Major；快照/未知版本为 9999）。
    /// 返回 null 或未接线表示未选择实例（旧 McInstanceSelected Is Nothing）。宿主层接线。
    /// </summary>
    public static Func<int?>? SelectedVanillaMajor { get; set; }

    /// <summary>
    /// UI 线程判定回调（旧 VB NetRequestByClientRetry 转发面恒传 AddressOf RunInUi，ModNet.vb:63；
    /// NetRequest.SendRequest 以此在 UI 线程发真实网络请求时快速失败，ModNet.vb:219）。null 视为非 UI 线程（测试环境）。宿主层接线。
    /// </summary>
    public static Func<bool>? RunInUiProvider { get; set; }

    //* 测试注入面：旧 :1151 硬编码 "https://api.mojang.com/users/profiles/minecraft/"，回环测试改指本机假服务器
    internal static string MojangApiBase = "https://api.mojang.com";

    /// <summary>请求共同头（旧 :723/:746/:776 的 Accept-Language；Accept 为旧 VB NetRequestByClient 的默认 "*/*"）。</summary>
    private static Dictionary<string, string> RequestHeaders() => new() { ["Accept-Language"] = "zh-CN" };

    #region 离线 UUID

    /// <summary>
    /// 根据用户名返回对应 UUID（旧 :1167-1170 逐字移植，纯函数）。
    /// 长度与稳定哈希各占 16 位十六进制，并把第 13 位改为 '3'（版本）、第 17 位改为 '9'（变体）。
    /// </summary>
    public static string LegacyUuid(string userName)
    {
        var fullUuid = userName.Length.ToString("X").EnsureLength('0', 16) + userName.GetStableHashCode().ToString("X").EnsureLength('0', 16);
        return fullUuid[..12] + "3" + fullUuid.Substring(13, 3) + "9" + fullUuid.Substring(17, 15);
    }

    /// <summary>
    /// 返回符合离线皮肤设置的 UUID（旧 :1105-1142 决策面移植）。
    /// 旧代码在此处不涉及文件与 Zip 操作（皮肤文件的下载/解压面在皮肤加载器，未随本函数迁移）；
    /// 皮肤性别判定经 W1 SkinProfile.GetModel（旧 McSkinSex :1101-1103 的转发目标）。
    /// </summary>
    /// <param name="userName">登录用户名。</param>
    /// <param name="skinType">皮肤种类（旧 McLoginLegacy.SkinType：0 默认 / 1 Steve / 2 Alex / 3 正版用户名 / 4 自定义）。</param>
    /// <param name="skinName">若采用正版皮肤，则为该皮肤名。</param>
    public static string LegacyUuidWithCustomSkin(string userName, int skinType, string? skinName)
    {
        var uuid = LegacyUuid(userName);
        //根据离线皮肤获取实际使用的 Uuid
        switch (skinType)
        {
        case 0:
            //默认，不需要处理
            break;
        case 1:
            //Steve
            uuid = BumpUntilModel(uuid, "Steve");   //旧 :1113-1116
            break;
        case 2:
            //Alex
            uuid = BumpUntilModel(uuid, "Alex");   //旧 :1119-1122
            break;
        case 3:
            //使用正版用户名
            try
            {
                int? vanillaMajor = SelectedVanillaMajor?.Invoke();   //旧 :1127：McInstanceSelected Is Nothing 时整体不成立；快照/未知版本为 9999
                if ((skinName ?? "") != "" && vanillaMajor is < 20)   //1.20+ 或快照版不能使用该项（#3746）
                {
                    Logger.Info($"由于离线皮肤设置，使用正版 UUID：{skinName}");
                    uuid = MojangUuid(skinName!, false);
                }
            } catch (Exception ex)
            {
                Logger.Error(ex, "皮肤信息获取失败，游戏将以无皮肤的方式启动", LogBehavior.Toast);   //旧 :1132 逐字（Toast 级，非弹窗）
            }
            break;
        case 4:
            //自定义（旧 :1136 的 If(LaunchSkinSlim, "Alex", "Steve") 目标经设置面读取；循环期间设置不可变，求值一次语义相同）
            uuid = BumpUntilModel(uuid, SettingsService.Get<bool>("LaunchSkinSlim") ? "Alex" : "Steve");
            break;
        }
        return uuid;
    }

    /// <summary>末 5 位十六进制递增直至皮肤性别匹配（旧 :1113-1116/:1119-1122/:1136-1139 三处逐字相同的循环体收敛）。</summary>
    private static string BumpUntilModel(string uuid, string targetModel)
    {
        while (SkinProfile.GetModel(uuid) != targetModel)
        {
            if (uuid.EndsWithF("FFFFF")) uuid = uuid[..27] + "00000";   //旧：FFFFF 时先归零，再统一 +1（实际落点为 00001）
            uuid = uuid[..27] + (long.Parse(uuid[27..], NumberStyles.AllowHexSpecifier) + 1).ToString("X").PadLeft(5, '0');
        }
        return uuid;
    }

    /// <summary>根据用户名返回对应正版 UUID（旧 :1144-1166 逐行移植）。</summary>
    /// <param name="name">正版用户名。</param>
    /// <param name="throwOnNotFound">玩家档案不存在（响应体可解析为空）时是否抛出；为 false 时回落离线 UUID。</param>
    public static string MojangUuid(string name, bool throwOnNotFound)
    {
        if (name.Trim().Length == 0) return new string('0', 32);
        var mojangIni = Path.Combine(MojangUuidCacheFolder!(), "Mojang.ini");   //旧 PathTemp & "Cache\Uuid\Mojang.ini"
        //从缓存获取
        var uuid = CommonUtils.ReadIni(mojangIni, name);   //ini 读写已收敛至 Common 公共版（含全局缓存，旧单文件收敛版消重）
        if (uuid.Length == 32) return uuid;
        //从官网获取
        try
        {
            var body = NetRequest.NetRequestByClientRetry(MojangApiBase + "/users/profiles/minecraft/" + name,
                accept: "*/*", requireJson: true, runInUi: RunInUiProvider);   //旧 VB 转发面恒传 AddressOf RunInUi（ModNet.vb:63，UI 线程守卫）
            var gotJson = string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);   //旧 DeserializeJson：空白返回 Nothing（:1152）
            if (gotJson is null) throw new FileNotFoundException("正版玩家档案不存在（" + name + "）");
            uuid = gotJson["id"]?.ToString() ?? "";   //旧 If(GotJson("id"), "")：缺失或 JSON null 均得空串
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"从官网获取正版 Uuid 失败（{name}）");
            //! 逐字移植豁免：旧 :1156 以类型名字符串判定（ex.GetType.Name = "FileNotFoundException"），保留该判定方式
            if (!throwOnNotFound && ex.GetType().Name == "FileNotFoundException")
            {
                uuid = LegacyUuid(name);   //玩家档案不存在
            } else
            {
                throw new Exception("从官网获取正版 Uuid 失败", ex);
            }
        }
        //写入缓存
        if (uuid.Length != 32) throw new Exception("获取的正版 Uuid 长度不足（" + uuid + "）");
        CommonUtils.WriteIni(mojangIni, name, uuid);
        return uuid;
    }

    #endregion

    //ini 缓存（旧 ModBase.ReadIni/WriteIni 的 Mojang.ini 单文件收敛版）已消重改指 CommonUtils 公共版：
    //Mojang.ini 仅本核读写，公共版的全局缓存与旧现读现写在文件内容语义上一致，失败语义（Toast + 吞掉）逐字相同

    #region Server 登录三段请求核

    /// <summary>
    /// 验证登录请求核（旧 McLoginRequestValidate :710-733 逐行移植）。
    /// 使用缓存中的令牌向 &lt;BaseUrl&gt;/validate 发起无返回值请求，成功后原样输出缓存；不更改缓存。
    /// </summary>
    /// <param name="input">登录输入（读取 BaseUrl/Token；Token 决定缓存键前缀，只可能为 "Auth" 或 "Nide"）。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    public static McLoginResult RequestValidate(McLoginServer input, Action<string>? log = null)
    {
        log?.Invoke($"验证登录开始（Validate, {input.Token}）");
        //提前缓存信息，否则如果在登录请求过程中退出登录，设置项目会被清空，导致输出存在空值
        var accessToken = SettingsService.Get<string>("Cache" + input.Token + "Access");
        var clientToken = SettingsService.Get<string>("Cache" + input.Token + "Client");
        var uuid = SettingsService.Get<string>("Cache" + input.Token + "Uuid");
        var name = SettingsService.Get<string>("Cache" + input.Token + "Name");
        //发送登录请求
        NetRequest.NetRequestByClientRetry(input.BaseUrl + "/validate", new NetRequestOptions
        {
            Method = HttpMethod.Post,
            Content = new JsonObject
            {
                ["accessToken"] = accessToken,
                ["clientToken"] = clientToken,
                ["requestUser"] = true,
            }.ToJsonString(),   //旧 ToString(Formatting.None)
            Headers = RequestHeaders(),
            ContentType = "application/json; charset=utf-8",
            Accept = "*/*",   //旧 VB NetRequestByClient 默认 Accept="*/*"
            RunInUi = RunInUiProvider,   //旧 VB 转发面恒传 AddressOf RunInUi（ModNet.vb:63，UI 线程守卫）
        });   //没有返回值的
        //将登录结果输出
        var result = new McLoginResult
        {
            AccessToken = accessToken,
            ClientToken = clientToken,
            Uuid = uuid,
            Name = name,
            Type = input.Token,
        };
        //不更改缓存，直接结束
        log?.Invoke($"验证登录成功（Validate, {input.Token}）");
        return result;
    }

    /// <summary>
    /// 刷新登录请求核（旧 McLoginRequestRefresh :734-763 逐行移植）。
    /// 使用缓存中的角色与令牌请求 &lt;BaseUrl&gt;/refresh，输出新结果并回写全部缓存。
    /// </summary>
    /// <param name="input">登录输入（读取 BaseUrl/Token/UserName/Password；后两者写入缓存键 Cache{Token}Username/Pass）。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    public static McLoginResult RequestRefresh(McLoginServer input, Action<string>? log = null)
    {
        log?.Invoke($"刷新登录开始（Refresh, {input.Token}）");
        var loginJson = JsonNode.Parse(NetRequest.NetRequestByClientRetry(input.BaseUrl + "/refresh", new NetRequestOptions
        {
            Method = HttpMethod.Post,
            Content = new JsonObject
            {
                ["selectedProfile"] = new JsonObject
                {
                    ["name"] = SettingsService.Get<string>($"Cache{input.Token}Name"),
                    ["id"] = SettingsService.Get<string>($"Cache{input.Token}Uuid"),
                },
                ["accessToken"] = SettingsService.Get<string>($"Cache{input.Token}Access"),
                ["requestUser"] = true,
            }.ToJsonString(),
            Headers = RequestHeaders(),
            ContentType = "application/json; charset=utf-8",
            Accept = "*/*",
            RequireJson = true,
            RunInUi = RunInUiProvider,   //旧 VB 转发面恒传 AddressOf RunInUi（ModNet.vb:76，UI 线程守卫）
        }))!;   //旧 DeserializeJson 后必为对象；响应非 JSON 已被 RequireJson 拦截
        //将登录结果输出
        if (loginJson["selectedProfile"] is null) throw new Exception("选择的角色 " + SettingsService.Get<string>("Cache" + input.Token + "Name") + " 无效！");
        //? 旧 JValue 对 JSON null 的 ToString 为空串；STJ 将 JSON null 与缺失归并为 null（!.ToString() 抛 NRE），病态输入差异记 TECHDEBT
        var result = new McLoginResult
        {
            AccessToken = loginJson["accessToken"]!.ToString(),
            ClientToken = loginJson["clientToken"]!.ToString(),
            Uuid = loginJson["selectedProfile"]!["id"]!.ToString(),
            Name = loginJson["selectedProfile"]!["name"]!.ToString(),
            Type = input.Token,
        };
        //保存缓存
        SettingsService.Set("Cache" + input.Token + "Access", result.AccessToken);
        SettingsService.Set("Cache" + input.Token + "Client", result.ClientToken);
        SettingsService.Set("Cache" + input.Token + "Uuid", result.Uuid);
        SettingsService.Set("Cache" + input.Token + "Name", result.Name);
        SettingsService.Set("Cache" + input.Token + "Username", input.UserName);
        SettingsService.Set("Cache" + input.Token + "Pass", input.Password);
        log?.Invoke($"刷新登录成功（Refresh, {input.Token}）");
        return result;
    }

    /// <summary>
    /// 普通登录请求核（旧 McLoginRequestLogin :764-883 逐行移植）。
    /// 请求 &lt;BaseUrl&gt;/authenticate 并按多角色/缓存/强制重选三态收敛角色，输出结果并回写全部缓存；
    /// 失败时按错误分诊表逐字抛出。返回是否需要刷新登录（旧 NeedRefresh，供编排壳决定是否进行二轮刷新）。
    /// </summary>
    /// <param name="input">登录输入（读取全部字段；Type 参与 403 分诊与密码错误的退出登录路由）。</param>
    /// <param name="roleSelector">
    /// 角色选择回调（旧 :801-812 的 RunInUiWait + MyMsgBoxSelect）：入参为候选角色名列表，
    /// 返回所选下标；取消时抛出异常（宿主弹窗的取消语义原样进入错误分诊）。
    /// </param>
    /// <param name="result">登录成功时输出结果；失败时为 default（旧 Data.Output 失败路径不被触碰）。</param>
    /// <param name="log">启动日志（原 McLaunchLog 默认级；null 时静默）。</param>
    /// <param name="hint">红字提示（原 Hint(…, HintType.Red)；null 时静默）。</param>
    /// <param name="onPasswordError">密码错误时的退出登录回调（旧 :849-854 按 Type 经 RunInUi 调用 PageLoginAuth/NideSkin.ExitLogin；UI 面留宿主）。</param>
    public static bool RequestLogin(McLoginServer input, Func<IReadOnlyList<string>, int> roleSelector,
        out McLoginResult result, Action<string>? log = null, Action<string>? hint = null, Action<McLoginType>? onPasswordError = null)
    {
        result = default;   //失败路径输出保持空（旧 Data.Output 不被触碰）
        var needRefresh = false;
        try
        {
            log?.Invoke($"登录开始（Login, {input.Token}）");
            var loginJson = JsonNode.Parse(NetRequest.NetRequestByClientRetry(input.BaseUrl + "/authenticate", new NetRequestOptions
            {
                Method = HttpMethod.Post,
                Content = new JsonObject
                {
                    ["agent"] = new JsonObject
                    {
                        ["name"] = "Minecraft",
                        ["version"] = 1,
                    },
                    ["username"] = input.UserName,
                    ["password"] = input.Password,
                    ["requestUser"] = true,
                }.ToJsonString(),
                Headers = RequestHeaders(),
                ContentType = "application/json; charset=utf-8",
                Accept = "*/*",
                RequireJson = true,
                RunInUi = RunInUiProvider,   //旧 VB 转发面恒传 AddressOf RunInUi（ModNet.vb:76，UI 线程守卫）
            }))!;
            //检查登录结果（旧 :779：availableProfiles 缺失时在其 Count 处抛 NRE）
            var availableProfiles = loginJson["availableProfiles"]!.AsArray();
            if (availableProfiles.Count == 0)
            {
                if (input.ForceReselectProfile) hint?.Invoke("你还没有创建角色，无法更换！");
                throw new Exception("$你还没有创建角色，请在创建角色后再试！");
            } else if (input.ForceReselectProfile && availableProfiles.Count == 1)   //旧 IsSingle
            {
                hint?.Invoke("你的账户中只有一个角色，无法更换！");
            }
            string? selectedName = null;
            string? selectedId = null;
            if ((loginJson["selectedProfile"] is null || input.ForceReselectProfile) && availableProfiles.Count > 1)
            {
                //要求选择档案；优先从缓存读取
                needRefresh = true;
                var cacheId = SettingsService.Get<string>("Cache" + input.Token + "Uuid");
                foreach (var profile in availableProfiles)
                {
                    if (profile!["id"]!.ToString() == cacheId)
                    {
                        selectedName = profile["name"]!.ToString();
                        selectedId = profile["id"]!.ToString();
                        log?.Invoke("根据缓存选择的角色：" + selectedName);
                    }
                }
                //缓存无效，要求玩家选择
                if (selectedName is null)
                {
                    log?.Invoke("要求玩家选择角色");
                    //旧 SelectionControl/SelectionJson 双列表收敛为 (name, id) 序列；MyMsgBoxSelect 返回下标
                    var profileNames = availableProfiles.Select(profile => profile!["name"]!.ToString()).ToList();
                    var profileIds = availableProfiles.Select(profile => profile!["id"]!.ToString()).ToList();
                    var selectedIndex = roleSelector(profileNames);
                    selectedName = profileNames[selectedIndex];
                    selectedId = profileIds[selectedIndex];
                    log?.Invoke("玩家选择的角色：" + selectedName);
                }
            } else
            {
                selectedName = loginJson["selectedProfile"]!["name"]!.ToString();
                selectedId = loginJson["selectedProfile"]!["id"]!.ToString();
            }
            //将登录结果输出
            result = new McLoginResult
            {
                AccessToken = loginJson["accessToken"]!.ToString(),
                ClientToken = loginJson["clientToken"]!.ToString(),
                Name = selectedName,
                Uuid = selectedId,
                Type = input.Token,
            };
            //保存缓存
            SettingsService.Set("Cache" + input.Token + "Access", result.AccessToken);
            SettingsService.Set("Cache" + input.Token + "Client", result.ClientToken);
            SettingsService.Set("Cache" + input.Token + "Uuid", result.Uuid);
            SettingsService.Set("Cache" + input.Token + "Name", result.Name);
            SettingsService.Set("Cache" + input.Token + "Username", input.UserName);
            SettingsService.Set("Cache" + input.Token + "Pass", input.Password);
            log?.Invoke($"登录成功（Login, {input.Token}）");
            return needRefresh;
        } catch (Exception ex)
        {
            var allMessage = ex.GetDisplay(false);
            Logger.Info(ex, "登录失败原始错误信息");
            //读取服务器返回的错误
            if (ex is HttpRequestCodeException)
            {
                string? errorMessage = null;
                try
                {
                    var response = ((HttpRequestCodeException) ex).Response;
                    if (response is not null && response.ContainsIgnoreCase("errorMessage"))
                        errorMessage = JsonNode.Parse(response)?["errorMessage"]?.ToString();   //旧：解析失败走空 Catch；JSON null 得空串
                } catch
                {
                    //旧 :843-844 空吞：errorMessage 提取失败时回落通用关键字检测
                }
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    var message = errorMessage!;   //net48 引用程序集无 NotNullWhen 注解，以局部别名消解空引用告警
                    if (message.Contains("密码错误") || message.ContainsIgnoreCase("Incorrect username or password"))
                    {
                        //密码错误，退出登录 (#5090)
                        log?.Invoke("密码错误，退出登录");
                        //旧 :849-854 的 Select Case：Auth→PageLoginAuthSkin.ExitLogin、Nide→PageLoginNideSkin.ExitLogin，由宿主回调按类型路由
                        onPasswordError?.Invoke(input.Type);
                    }
                    throw new Exception("$登录失败：" + message);
                }
            }
            //通用关键字检测
            if (allMessage.Contains("(403)"))
            {
                switch (input.Type)
                {
                case McLoginType.Auth:
                    throw new Exception("$登录失败，以下为可能的原因：" + "\r\n" +
                                        " - 输入的账号或密码错误。" + "\r\n" +
                                        " - 登录尝试过于频繁，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。" + "\r\n" +
                                        " - 只注册了账号，但没有在皮肤站新建角色。");
                case McLoginType.Nide:
                    //! 逐字移植豁免：旧 :871 直接 Data.Input.UserName.Contains("@")（Nothing 时 NRE 由 catch 外抛），此处同样不解引用保护
                    throw new Exception("$登录失败，以下为可能的原因：" + "\r\n" +
                                        " - 输入的账号或密码错误。" + "\r\n" +
                                        " - 密码错误次数过多，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。" + "\r\n" +
                                        (input.UserName!.Contains('@') ? "" : " - 登录账号应为邮箱或统一通行证账号，而非游戏角色 ID。" + "\r\n") +
                                        " - 只注册了账号，但没有加入对应服务器。");
                }
            } else if (ex.IsBadNetwork())
            {
                throw new Exception("$登录失败：你的网络环境不佳，导致难以连接到海外服务器。" + "\r\n" + "请稍后再试，或使用加速器、VPN 改善网络环境。");
            } else if (ex.Message.StartsWithF("$"))
            {
                throw;
            } else
            {
                throw new Exception("登录失败：" + ex.Message, ex);
            }
            return false;   //旧 :881：仅 403 且类型非 Auth/Nide 时 Select 无匹配落到此处
        }
    }

    #endregion

}