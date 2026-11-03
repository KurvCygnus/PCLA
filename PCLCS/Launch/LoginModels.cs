namespace PCLCS.Launch;

/// <summary>
/// 登录模型（自宿主层 ModLaunch.vb:300-408 "主登录模块" 随迁段逐字移植）。
/// Equals 语义依赖 <see cref="MeloongCore.Extensions.StringExtensions.GetStableHashCode"/>，与 VB 侧共享同一实现。
/// </summary>
public enum McLoginType
{
    Legacy = 0,
    Nide = 2,
    Auth = 3,
    Ms = 5,
}

//! 逐字移植豁免：旧 :309-317 的 McLoginData 只重写 Equals、不重写 GetHashCode（各子类自行重写），基类沿袭该形态，故压制 CS0659
#pragma warning disable CS0659
/// <summary>各个登录方式的对应数据（旧 :309-317）。</summary>
public abstract class McLoginData
{
    /// <summary>
    /// 登录方式。
    /// </summary>
    public McLoginType Type;

    //! 逐字移植豁免：旧 :314-316 的 Equals 只比较双方 GetHashCode、不做类型判定（哈希相同即视为相等），保留该语义
    public override bool Equals(object? obj) => obj is not null && obj.GetHashCode() == GetHashCode();
}
#pragma warning restore CS0659

/// <summary>Server 登录（Authlib-Injector / Nide）输入（旧 :318-353）。</summary>
public class McLoginServer : McLoginData
{
    /// <summary>
    /// 登录用户名。
    /// </summary>
    public string? UserName;

    /// <summary>
    /// 登录密码。
    /// </summary>
    public string? Password;

    /// <summary>
    /// 登录服务器基础地址。
    /// </summary>
    public string? BaseUrl;

    /// <summary>
    /// 登录所使用的标识符，目前只可能为 “Auth” 或 “Nide”，用于存储缓存等。
    /// </summary>
    public string? Token;

    /// <summary>
    /// 登录方式的描述字符串，如 “正版”、“统一通行证”。
    /// </summary>
    public string? Description;

    /// <summary>
    /// 是否在本次登录中强制要求玩家重新选择角色，目前仅对 Authlib-Injector 生效。
    /// </summary>
    public bool ForceReselectProfile = false;

    public McLoginServer(McLoginType type) => Type = type;

    //! 逐字移植豁免：旧 :350 的 (UserName & Password & BaseUrl & Token & Type)——VB 的 & 对 Nothing 按空串拼接，枚举拼接取名称；
    //! 旧 :350 为 Mod Integer.MaxValue 后再转 Integer，须先取模再收窄（C# 强转优先级高于 %，须显式加括号）
    public override int GetHashCode()
        => unchecked((int) (((UserName ?? "") + (Password ?? "") + (BaseUrl ?? "") + (Token ?? "") + Type)
            .GetStableHashCode() % int.MaxValue));
}

/// <summary>微软登录输入（旧 :354-373；本 Task 仅随迁模型，消费核属 Task 4）。</summary>
public class McLoginMs : McLoginData
{
    /// <summary>
    /// 缓存的 OAuth Refresh Token。若没有则为空字符串。
    /// </summary>
    public string OAuthRefreshToken = "";
    public string AccessToken = "";
    public string Uuid = "";
    public string UserName = "";
    public string ProfileJson = "";

    public McLoginMs() => Type = McLoginType.Ms;

    //! 逐字移植豁免：旧 :369-372 不能使用全部信息，这俩就足以在登录输入时确保是同一个用户了（先取模再收窄，同上）
    public override int GetHashCode()
        => unchecked((int) (((OAuthRefreshToken ?? "") + (UserName ?? "")).GetStableHashCode() % int.MaxValue));
}

/// <summary>离线登录输入（旧 :374-395）。</summary>
public class McLoginLegacy : McLoginData
{
    /// <summary>
    /// 登录用户名。
    /// </summary>
    public string? UserName;

    /// <summary>
    /// 皮肤种类。
    /// </summary>
    public int SkinType;

    /// <summary>
    /// 若采用正版皮肤，则为该皮肤名。
    /// </summary>
    public string? SkinName;

    public McLoginLegacy() => Type = McLoginType.Legacy;

    //! 逐字移植豁免：旧 :392-394 的 (UserName & SkinType & SkinName & Type)——VB 的 & 把整数按十进制拼入字符串（先取模再收窄，同上）
    public override int GetHashCode()
        => unchecked((int) (((UserName ?? "") + SkinType + (SkinName ?? "") + Type)
            .GetStableHashCode() % int.MaxValue));
}

/// <summary>登录返回结果（旧 :398-408，Structure 值类型）。</summary>
public struct McLoginResult
{
    public string? Name;
    public string? Uuid;
    public string? AccessToken;
    public string? Type;
    public string? ClientToken;

    /// <summary>
    /// 进行微软登录时返回的 profile 信息。
    /// </summary>
    public string? ProfileJson;
}