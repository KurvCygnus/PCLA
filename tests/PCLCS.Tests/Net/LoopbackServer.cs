using PCLCS.Net;
using System.Net;
using System.Text;

namespace PCLCS.Tests.Net;

/// <summary>本机回环 HTTP fixture：随机端口、可编程响应；仅测试程序集使用。</summary>
public sealed class LoopbackServer : IDisposable
{
    private readonly HttpListener _listener = new();
    public string UrlPrefix { get; }
    public List<string> ReceivedPaths { get; } = [];
    public Func<HttpListenerRequest, (int Status, string Body, Dictionary<string, string>? Headers)>? Responder { get; set; }

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
                ReceivedPaths.Add(ctx.Request.Url!.PathAndQuery);
                var (status, body, headers) = Responder?.Invoke(ctx.Request) ?? (200, "ok", null);
                var bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                if (headers != null)
                    foreach (var h in headers)
                        ctx.Response.Headers[h.Key] = h.Value;
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
        } catch
        {
            // Dispose 触发 Stop() 时，阻塞中的 GetContext 会抛出异常；监听线程随进程退出即可，无需处理
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