using PCLCS.Net;
using System.Net;
using System.Text;

namespace PCLCS.Tests.Net;

/// <summary>本机回环 HTTP fixture：随机端口、可编程响应、并发处理请求；仅测试程序集使用。</summary>
public sealed class LoopbackServer : IDisposable
{
    private readonly HttpListener _listener = new();
    public string UrlPrefix { get; }
    public List<string> ReceivedPaths { get; } = [];
    public Func<HttpListenerRequest, (int Status, string Body, Dictionary<string, string>? Headers)>? Responder { get; set; }

    /// <summary>指定二进制响应体；请求携带 <c>Range: bytes=N-</c> 时返回 206 分片（对照 NetFile 多线程分块下载）。</summary>
    public byte[]? RangeBody { get; set; }

    /// <summary>响应体按 64KB 分块写出时的块间延迟毫秒数：模拟慢速源，为下载引擎的自驱调度留出追加 Range 线程的时间窗。</summary>
    public int BodyChunkDelayMs { get; set; }

    public LoopbackServer()
    {
        var port = NetRequest.FindFreePort();
        UrlPrefix = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(UrlPrefix);
        _listener.Start();
        var thread = new Thread(AcceptLoop) { IsBackground = true };
        thread.Start();
    }

    private void AcceptLoop()
    {
        try
        {
            while (_listener.IsListening)
            {
                var ctx = _listener.GetContext();
                //并发处理：分块下载的线程只读取自己的 Range 窗口即停止读取，写出余量的处理线程会因此阻塞，
                //串行处理会卡死后续请求（真实服务器不会），故每个请求独立线程承载
                var worker = new Thread(HandleContext) { IsBackground = true };
                worker.Start(ctx);
            }
        } catch
        {
            // Dispose 触发 Stop() 时，阻塞中的 GetContext 会抛出异常；监听线程随进程退出即可，无需处理
        }
    }

    private void HandleContext(object contextObj)
    {
        var ctx = (HttpListenerContext) contextObj;
        try
        {
            ReceivedPaths.Add(ctx.Request.Url!.PathAndQuery);
            int status;
            byte[] bytes;
            Dictionary<string, string>? headers = null;
            if (RangeBody != null)
            {
                var range = ctx.Request.Headers["Range"];
                if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes=") &&
                    long.TryParse(range[6..].TrimEnd('-'), out var start) && start < RangeBody.Length)
                {
                    status = 206; //Range: bytes=N- → 返回 [N, 末尾) 分片，ContentLength 即分片长度
                    bytes = new byte[RangeBody.Length - start]; //net48 无 Range 切片运行时支持，显式拷贝
                    Array.Copy(RangeBody, start, bytes, 0, bytes.Length);
                } else
                {
                    status = 200;
                    bytes = RangeBody;
                }
            } else
            {
                var (statusCode, body, responseHeaders) = Responder?.Invoke(ctx.Request) ?? (200, "ok", null);
                status = statusCode;
                bytes = Encoding.UTF8.GetBytes(body);
                headers = responseHeaders;
            }
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            if (headers != null)
                foreach (var h in headers)
                    ctx.Response.Headers[h.Key] = h.Value;
            ctx.Response.ContentLength64 = bytes.Length;
            if (BodyChunkDelayMs > 0 && bytes.Length > 65536)
            {
                for (var offset = 0; offset < bytes.Length; offset += 65536)
                {
                    var count = Math.Min(65536, bytes.Length - offset);
                    ctx.Response.OutputStream.Write(bytes, offset, count);
                    if (offset + count < bytes.Length) Thread.Sleep(BodyChunkDelayMs);
                }
            } else
            {
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            ctx.Response.Close();
        } catch
        {
            //客户端已停止读取（分块下载线程只取自己的窗口）或连接中断：静默放弃余量
        }
    }

    public void Dispose()
    {
        try { _listener.Stop(); } catch
        {
            // 监听线程可能已在退出过程中，Stop 的失败可忽略
        }
    }
}