namespace PCLCS.Minecraft.Launch;

/// <summary>
/// 版本规则求值所需的运行环境。
/// </summary>
public readonly struct RuleEnvironment
{
    public RuleEnvironment(string osVersion)
    {
        OsVersion = osVersion;
    }

    /// <summary>操作系统版本字符串，对应 <c>My.Computer.Info.OSVersion</c>。</summary>
    public string OsVersion { get; }
}