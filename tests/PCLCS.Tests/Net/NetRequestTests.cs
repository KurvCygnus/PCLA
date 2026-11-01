using PCLCS.Net;

namespace PCLCS.Tests.Net;

public class NetRequestTests
{
    [Fact]
    public void NetRequestByClient_returns_body()
    {
        using var server = new LoopbackServer();
        server.Responder = static _ => (200, """{"ok":true}""", null);
        var body = NetRequest.NetRequestByClient(server.UrlPrefix + "a.json");
        Assert.Equal("""{"ok":true}""", body);
    }

    [Fact]
    public void NetRequestByClientRequireJson_throws_on_non_json()
    {
        using var server = new LoopbackServer();
        server.Responder = static _ => (200, "not json", null);
        Assert.ThrowsAny<Exception>(() => NetRequest.NetRequestByClient(server.UrlPrefix + "a.json", requireJson: true));
    }

    [Fact]
    public void Retry_succeeds_after_transient_503()
    {
        var calls = 0;
        using var server = new LoopbackServer();
        //? brief 原文为 ++calls < 3，但该写法前两次请求都返回 503（成功仅出现在第 3 次），与 Assert.Equal(2, calls)
        //? 及注释"1 次 503 + 重试成功"矛盾；按断言意图取 ++calls < 2（恰一次 503 后重试成功）
        server.Responder = _ => (++calls < 2) ? (503, "busy", null) : (200, """{"ok":1}""", null);
        var body = NetRequest.NetRequestByClientRetry(server.UrlPrefix + "r.json");
        Assert.Equal(2, calls);  // 1 次 503 + 重试成功；503 触发重试而非直接抛（对照 :66-88）
        Assert.Equal("""{"ok":1}""", body);
    }

    [Fact]
    public void Retry_404_throws_immediately()
    {
        using var server = new LoopbackServer();
        server.Responder = static _ => (404, "no", null);
        Assert.ThrowsAny<Exception>(() => NetRequest.NetRequestByClientRetry(server.UrlPrefix + "x.json"));
    }

    [Fact]
    public void Multiple_races_and_returns_all_bodies()
    {
        using var a = new LoopbackServer();
        a.Responder = static _ => (200, "A", null);
        using var b = new LoopbackServer();
        b.Responder = static _ => (200, "B", null);
        var bodies = NetRequest.NetRequestByClientMultiple([a.UrlPrefix + "1", b.UrlPrefix + "2"]);
        Assert.Equal(2, bodies.Count);
    }

    [Fact]
    public void DownloadByClient_writes_file()
    {
        using var server = new LoopbackServer();
        server.Responder = static _ => (200, "file-bytes", null);
        var path = Path.Combine(Path.GetTempPath(), "pclcs.net." + Guid.NewGuid().ToString("N"));
        Assert.True(NetRequest.NetDownloadByClient(server.UrlPrefix + "f.bin", path));
        Assert.Equal("file-bytes", File.ReadAllText(path));
        File.Delete(path);
    }

    [Fact]
    public void CdnSigner_provider_is_applied_to_url()
    {
        using var server = new LoopbackServer();
        server.Responder = static _ => (200, "ok", null);
        NetRequest.CdnSigner = static url => url + "?signed=1";
        try
        {
            NetRequest.NetRequestByClient(server.UrlPrefix + "s.json");
            Assert.Contains("signed=1", server.ReceivedPaths[0]);  // 对照 :233 SecretCdnSign 接线点
        } finally { NetRequest.CdnSigner = null; }
    }

    [Fact]
    public void HeadersSigner_provider_receives_headers_url_and_simulate_flag()
    {
        string? headerSeen = null;
        using var server = new LoopbackServer();
        server.Responder = r => { headerSeen = r.Headers["X-Pclcs-Test"]; return (200, "ok", null); };
        bool? receivedSimulate = null;
        NetRequest.HeadersSigner = (headers, _, simulateBrowserHeaders) =>
        {
            receivedSimulate = simulateBrowserHeaders;
            headers.Add("X-Pclcs-Test", "1");
        };
        try
        {
            NetRequest.NetRequestByClient(server.UrlPrefix + "h.json", new NetRequestOptions { SimulateBrowserHeaders = true });
            Assert.True(receivedSimulate == true);
            Assert.Equal("1", headerSeen);
        } finally { NetRequest.HeadersSigner = null; }
    }

    [Fact]
    public void FindFreePorts_returns_consecutive_ports_not_in_use()
    {
        var ports = NetRequest.FindFreePorts(3);
        Assert.Equal(3, ports.Count);
        Assert.Equal(ports[0] + 1, ports[1]);
        Assert.Equal(ports[0] + 2, ports[2]);
        var used = NetRequest.GetUsedPorts();
        Assert.Empty(ports.Intersect(used));
    }
}