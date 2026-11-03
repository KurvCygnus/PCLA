namespace PCLCS.Launch;

/// <summary>
/// 启动选项（自宿主层 ModLaunch.vb:6-26 字段逐字移植）。
/// 宿主层经同名转发类保持读写面（Instance 属性仍为 McInstance 类型）。
/// </summary>
public sealed class McLaunchOptions
{
    /// <summary>强制指定在启动后进入的服务器 IP。默认值：null。使用版本设置的值。</summary>
    public string? ServerIp;

    /// <summary>将启动脚本保存到该地址，然后取消启动。这同时会改变启动时的提示等。默认值：null。不保存。</summary>
    public string? SaveBatch;

    /// <summary>强行指定启动的 MC 版本。默认值：null。使用 McInstanceCurrent。</summary>
    //? 实际类型为 VB 侧 McInstance（未迁移），暂以 object 承载；宿主层转发类经属性收窄回 McInstance，
    //? C# 消费经宿主转换（Task 8 FabricApi/OptiFabric 的 object 先例，已裁定）
    public object? Instance;

    /// <summary>额外的启动参数。</summary>
    public List<string> ExtraGameArgs = [];
}
