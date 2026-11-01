using MeloongCore;
using PCLCS.Net;
using System.Diagnostics;
using System.Text;

namespace PCLCS.Tests.Net;

/// <summary>多线程下载引擎（NetFile/NetThread）回环测试：分块、多源降级与校验失败路径。</summary>
public class NetFileTests
{
    private static string TempPath(string name) => Path.Combine(Path.GetTempPath(), "pclcs.netfile." + Guid.NewGuid().ToString("N"), name);

    [Fact]
    public void Single_source_small_file_downloads_and_completes()
    {
        using var server = new LoopbackServer();
        server.Responder = static _ => (200, "hello-world", null);
        var path = TempPath("a.txt");
        //brief 原文为恒真委托 (t, size) => true；FileChecker 实为 MeloongCore 校验类（Hash/ActualSize/MinSize/IsJson 四判据），
        //无判据即等价于恒过（文件存在即可）
        var file = new NetFile([server.UrlPrefix + "a.txt"], path, new FileChecker());
        file.Start();
        Assert.True(SpinWaitSuccess(file, 10_000), $"state={file.State}");
        Assert.Equal("hello-world", File.ReadAllText(path));
    }

    [Fact]
    public void Large_file_uses_range_threads_and_merges()
    {
        var body = new string('x', 2_000_000); //>1MB 触发分块（IsNoSplit 阈值）
        //BodyChunkDelayMs 模拟慢速源，确保自驱调度追加 Range 分块线程与多线程合并路径真实发生
        using var server = new LoopbackServer();
        server.RangeBody = Encoding.UTF8.GetBytes(body);
        server.BodyChunkDelayMs = 10;
        var path = TempPath("big.bin");
        var file = new NetFile([server.UrlPrefix + "big.bin"], path);
        file.Start();
        Assert.True(SpinWaitSuccess(file, 30_000), $"state={file.State}");
        Assert.Equal(body.Length, new FileInfo(path).Length);
        Assert.Equal(body, File.ReadAllText(path));
    }

    [Fact]
    public void Failing_source_falls_back_to_single_thread_retry()
    {
        using var bad = new LoopbackServer();
        bad.Responder = static _ => (500, "boom", null);
        using var good = new LoopbackServer();
        good.RangeBody = Encoding.UTF8.GetBytes(new string('y', 200_000));
        var path = TempPath("c.bin");
        var file = new NetFile([bad.UrlPrefix + "c", good.UrlPrefix + "c"], path);
        file.Start();
        Assert.True(SpinWaitSuccess(file, 30_000), $"state={file.State}"); //首源失败后由后续源接续（SourceFail 降级路径）
        Assert.Equal(200_000, new FileInfo(path).Length);
    }

    [Fact]
    public void Hash_checker_failure_marks_failed()
    {
        using var server = new LoopbackServer();
        server.Responder = static _ => (200, "wrong-content", null);
        var path = TempPath("d.txt");
        //brief 原文为恒败委托 (t, size) => false；以恒不匹配的 SHA256 承载“校验必败”，走 Merge 校验失败→SourceFail 重试→Fail 路径
        var file = new NetFile([server.UrlPrefix + "d.txt"], path, new FileChecker { Hash = new string('0', 64) });
        file.Start();
        Assert.True(SpinWaitFailed(file, 30_000), $"state={file.State}");
        Assert.NotEmpty(file.Exs);
    }

    private static bool SpinWaitSuccess(NetFile f, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (f.State == NetState.Finished) return true; //VB 真值：成功 = Finished（brief 的 NetState.Complete 不存在）
            Thread.Sleep(50);
        }
        return false;
    }

    private static bool SpinWaitFailed(NetFile f, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (f.State == NetState.Canceled) return true; //VB 真值：失败 = Canceled（brief 的 SourceFailed/LinkFailure 不存在）
            Thread.Sleep(50);
        }
        return false;
    }
}