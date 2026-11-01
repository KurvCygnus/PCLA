namespace PCLCS.Net;

/// <summary>
/// 网络请求的可选项（ModNet.vb SendRequest 参数组收敛为选项对象）。
/// 前五个成员为后续任务（R3+）依赖的锁定签名；其余成员服务于 PCLA 既有调用面的逐字转发。
/// </summary>
public sealed class NetRequestOptions
{
    /// <summary>Accept 头；null 表示不设置（对应 VB 传 Nothing 的行为）。</summary>
    public string? Accept { get; set; }
    /// <summary>要求返回结果为完整 JSON（ModNet.vb:23-30 的检查）。</summary>
    public bool RequireJson { get; set; }
    /// <summary>单次请求的超时毫秒数。VB 侧转发会显式传入各自的默认值。</summary>
    public int TimeoutMs { get; set; } = 15000;
    /// <summary>重试次数，预留给后续下载任务；当前 SendRequest 沿用 VB 的固定内部重试，不消费此值。</summary>
    //? ModNet.vb:254 的 Retrier.Attempt 未传 maxAttempts（库默认共 2 次尝试）；若改由本值驱动会让 C# 便利入口比 VB 多重试，行为回退，故暂不接线
    public int Retries { get; set; } = 3;
    /// <summary>UI 线程判定回调（ModNet.vb:219 的 RunInUi 守卫经此注入；null 视为非 UI 线程）。</summary>
    public Func<bool>? RunInUi { get; set; }

    /// <summary>请求方法；null 等同 GET（VB 调用方在转发前已完成同样的归一化）。</summary>
    public HttpMethod? Method { get; set; }
    /// <summary>请求体：支持 HttpContent（克隆发送）、byte[]，其余类型按字符串经 Encoding 编码（ModNet.vb:235-243）。</summary>
    public object? Content { get; set; }
    /// <summary>请求体类型；写入 Content-Type 头（ModNet.vb:18 由调用方合并的语义收敛至 SendRequest）。</summary>
    public string? ContentType { get; set; }
    /// <summary>附加请求头（ModNet.vb:17 的 String(,) 二维数组在转发层转为字典）。</summary>
    public Dictionary<string, string>? Headers { get; set; }
    /// <summary>响应体解码编码；null 时按 BOM 探测（GetString 扩展语义）。</summary>
    public Encoding? Encoding { get; set; }
    /// <summary>模拟浏览器的 UA 与 Referer 选择（传递给 HeadersSigner 提供者，ModNet.vb:251）。</summary>
    public bool SimulateBrowserHeaders { get; set; }
    /// <summary>是否记录请求日志（ModNet.vb:15/:35 的 MakeLog）。</summary>
    public bool MakeLog { get; set; } = true;

    /// <summary>浅拷贝（NetRequestByClientRetry 每次尝试需改写 TimeoutMs，避免污染调用方的选项对象）。</summary>
    internal NetRequestOptions Clone() => new()
    {
        Accept = Accept,
        RequireJson = RequireJson,
        TimeoutMs = TimeoutMs,
        Retries = Retries,
        RunInUi = RunInUi,
        Method = Method,
        Content = Content,
        ContentType = ContentType,
        Headers = Headers is null ? null : new Dictionary<string, string>(Headers),
        Encoding = Encoding,
        SimulateBrowserHeaders = SimulateBrowserHeaders,
        MakeLog = MakeLog
    };
}

/// <summary>
/// SendRequest 的返回载体：状态码、响应字节、签名后实际请求的 URL，以及重试期间被吞掉的异常列表。
/// </summary>
public sealed class NetResult
{
    /// <summary>HTTP 状态码。</summary>
    public int StatusCode { get; set; }
    /// <summary>响应字节。</summary>
    public byte[] Bytes { get; set; } = [];
    /// <summary>经 CdnSigner 签名后实际请求的 URL。</summary>
    public string Url { get; set; } = "";
    /// <summary>重试（Retrier）期间被吞掉的异常；仅承载诊断信息，不影响异常语义。</summary>
    public List<Exception> Errors { get; } = [];
}

/// <summary>
/// 下载进度标示（C# 下载引擎的种子模型；VB 侧 ModNet.vb:373-406 的同名枚举继续供下载引擎使用）。
/// </summary>
public enum NetState
{
    /// <summary>尚未开始。</summary>
    WaitForConnect = 0,
    /// <summary>正在下载。</summary>
    Downloading,
    /// <summary>连接成功。</summary>
    LinkSuccess,
    /// <summary>连接失败。</summary>
    LinkFailure,
    /// <summary>等待重试。</summary>
    WaitRetry,
    /// <summary>下载源已失败。</summary>
    SourceFailed,
    /// <summary>已完成。</summary>
    Complete,
    /// <summary>已取消。</summary>
    Cancelled
}

/// <summary>
/// 下载源（C# 下载引擎的种子模型；VB 侧 ModNet.vb:356-369 的同名类继续供下载引擎使用）。
/// </summary>
public sealed class NetSource
{
    public string Id = "";
    public string Url = "";
    public int FailCount;
    public Exception? Ex;
    /// <summary>该下载源是否正在进行强制单线程下载。</summary>
    public bool SingleThread;
    public bool IsFailed;

    public override string ToString() => Url;
}