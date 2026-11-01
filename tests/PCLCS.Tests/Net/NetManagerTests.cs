using PCLCS.Net;
using PCLCS.Settings;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace PCLCS.Tests.Net;

//* 走临时目录设置（ThreadLimit / SpeedLimitHigh 读写 ToolDownloadThread / ToolDownloadSpeed），与设置类测试共享串行集合；
//* 下载类用例不依赖设置取值（线程上限默认 63 足够），TempFolderProvider 每用例注入独立临时目录以隔离首次缓存清理
[Collection("PCLCS.Settings")]
public class NetManagerTests
{
    private static string TempRoot(string name) => Path.Combine(Path.GetTempPath(), "pclcs.mgr." + name + "." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;

    private static string TempPath(string name) => Path.Combine(Path.GetTempPath(), "pclcs.mgr.file." + Guid.NewGuid().ToString("N"), name);

    [Fact]
    public void AllFiles_deduplicates_by_local_path()
    {
        var path = TempPath("dedup.txt");
        var f1 = new NetFile(["https://example.test/z"], path);
        var f2 = new NetFile(["https://example.test/z"], path);
        NetManager.Instance.Register(f1);
        NetManager.Instance.Register(f2);
        Assert.Same(f1, NetManager.Instance.AllFiles[path]); //:1817-1828 去重语义：进行中的文件保留首个，后来的被抛弃
    }

    [Fact]
    public void ThreadLimit_is_settings_plus_one()
    {
        var root = TempRoot("limit");
        SettingsService.Configure(root); //临时目录隔离
        SettingsService.Set("ToolDownloadThread", 5);
        Assert.Equal(6, NetSpeed.ThreadLimit); //Settings ToolDownloadThread + 1
    }

    [Theory]
    [InlineData(0, 104858L)]  //(0+1) * 0.1 * 1024 * 1024 → Convert.ToInt64（CLng 银行家舍入语义，Task 2 已证等价）
    [InlineData(14, 1572864L)]
    [InlineData(15, 2097152L)] //(15-11) * 0.5 * 1024 * 1024
    [InlineData(31, 10485760L)]
    [InlineData(32, 11534336L)] //(32-21) * 1024 * 1024
    [InlineData(41, 20971520L)]
    [InlineData(42, -1L)] //超过 41 档视为不限速
    public void SpeedLimitHigh_follows_three_segment_table(int setting, long expected)
    {
        var root = TempRoot("speed");
        SettingsService.Configure(root);
        SettingsService.Set("ToolDownloadSpeed", setting);
        NetSpeed.UpdateNetTaskSpeedLimitHigh(setting); //Settings.vb 钩子入口：清缓存后按新档位换算
        Assert.Equal(expected, NetSpeed.SpeedLimitHigh);
    }

    [Fact]
    public void Start_replaces_duplicate_file_with_ongoing_one()
    {
        NetManager.TempFolderProvider = static () => TempRoot("cache"); //首次清理落在测试目录，避免触碰并行用例的 %TEMP%\Download
        var path = TempPath("dup.txt");
        var f1 = new NetFile(["https://example.test/dup"], path);
        var f2 = new NetFile(["https://example.test/dup"], path);
        var host1 = new TestTaskHost { Files = new ConcurrentQueue<NetFile>([f1]) };
        var host2 = new TestTaskHost { Files = new ConcurrentQueue<NetFile>([f2]) };
        var newFiles1 = NetManager.Instance.Start(host1);
        var newFiles2 = NetManager.Instance.Start(host2);
        Assert.Same(f1, Assert.Single(newFiles1));
        Assert.Empty(newFiles2); //f2 被进行中的 f1 顶替，不算新文件
        Assert.Same(f1, NetManager.Instance.AllFiles[path]);
        Assert.Same(f1, host2.Files.Single()); //任务队列已改写为进行中的文件（两个任务指向同一个文件）
        Assert.Contains(host1, f1.Tasks);
        Assert.Contains(host2, f1.Tasks);
    }

    [Fact]
    public void Guard_threads_drive_hosted_task_to_completion()
    {
        var root = TempRoot("smoke");
        NetManager.TempFolderProvider = () => root;
        using var server = new LoopbackServer();
        server.Responder = static _ => (200, "manager-smoke", null);
        var path = TempPath("smoke.bin");
        var file = new NetFile([server.UrlPrefix + "smoke.bin"], path);
        var host = new TestTaskHost { Files = new ConcurrentQueue<NetFile>([file]) };
        var newFiles = NetManager.Instance.Start(host);
        Assert.Same(file, Assert.Single(newFiles));
        //宿主层（LoaderDownload.Start）对无校验文件的处置：置等待下载后由管理器守护线程接管调度
        foreach (var newFile in newFiles)
        {
            newFile.State = NetState.WaitingToDownload;
            newFile.IsCopy = false;
        }
        Assert.True(SpinWaitState(file, NetState.Finished, 15_000), $"state={file.State}");
        Assert.Equal("manager-smoke", File.ReadAllText(path));
        //文件曾登记进管理器；完成后管理器会清空登记表（NetManager ThreadStarter id==0 清理），两种状态均视为通过
        Assert.True(!NetManager.Instance.AllFiles.TryGetValue(path, out var registered) || ReferenceEquals(registered, file));
        Assert.Equal(1, host.FinishedCount);
        Assert.False(host.Failed);
    }

    [Fact]
    public void Register_drives_hostless_file_to_completion()
    {
        using var server = new LoopbackServer();
        server.Responder = static _ => (200, "register-smoke", null);
        var path = TempPath("register.bin");
        var file = new NetFile([server.UrlPrefix + "register.bin"], path);
        file.Start(); //无宿主直用入口：注册进管理器，由守护线程承担调度（原过渡自驱 DriveLoop 的替代路径）
        Assert.True(SpinWaitState(file, NetState.Finished, 15_000), $"state={file.State}");
        Assert.Equal("register-smoke", File.ReadAllText(path));
    }

    private static bool SpinWaitState(NetFile file, NetState target, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (file.State == target) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    /// <summary>测试宿主任务：承载 INetTaskHost 全部成员，记录完成/失败回调。</summary>
    private sealed class TestTaskHost : INetTaskHost
    {
        private static int uuidCounter;

        public int Uuid { get; } = Interlocked.Increment(ref uuidCounter);

        public ConcurrentQueue<NetFile> Files { get; set; } = new();

        public int FailCount { get; set; }

        public int FinishedCount;

        public bool Failed;

        public void OnFileFail(NetFile file) => Failed = true;

        public void OnFileFinish(NetFile file) => FinishedCount++;

        public void RefreshStat() { }
    }
}