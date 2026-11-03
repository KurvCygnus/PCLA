using MeloongCore;
using PCLCS.Launch;
using PCLCS.Settings;
using System.Diagnostics;
using Xunit;

namespace PCLCS.Tests.Launch;

//* 触及 SettingsService（LaunchAdvanceRun/LaunchAdvanceRunWait/LaunchArgumentPriority），与设置类测试共享串行集合
[Collection("PCLCS.Settings")]
public class ProcessRunnerTests : IDisposable
{
    private readonly string _root;

    public ProcessRunnerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclcs.procrunner." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_root + "minecraft"); //自定义命令进程的工作目录（原 McFolderSelected）
        SettingsService.Configure(_root);   // 全局 Settings.json 落在临时目录
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }

    private CustomInputs NewCustomInputs(string? saveBatch = null) =>
        new()
        {
            InstanceName = "测试版本",
            InstanceIndiePath = _root + "instance" + Path.DirectorySeparatorChar,
            McFolderSelected = _root + "minecraft" + Path.DirectorySeparatorChar,
            JavaExePath = "javaw.exe",
            Arguments = "--demo --arg",
            Encoding = System.Text.Encoding.UTF8,
            IsGbkEncoding = false,
            SaveBatch = saveBatch,
            InstanceSettingsKey = "instance-key",
            ArgumentReplace = static text => text,
            FilterAccessToken = static (text, _) => text,
        };

    [Fact]
    public void RunGameProcess_starts_redirects_and_streams()
    {
        //ModLaunch.vb McLaunchRun（:1764-1812 决策面）：启动、重定向、stdout/stderr 行回调、退出码
        var stdout = new List<string>();
        var stderr = new List<string>();
        var logs = new List<string>();

        var process = ProcessRunner.RunGameProcess(
            new GameProcessInputs
            {
                JavaExePath = "cmd.exe",
                JavaFolder = @"C:\fake\java",
                McFolderSelected = _root + "minecraft" + Path.DirectorySeparatorChar,
                WorkingDirectory = _root,
                Arguments = "/c echo hello-stdout & echo hello-stderr 1>&2 & exit 3",
                Encoding = System.Text.Encoding.UTF8,
            },
            new ProcessCallbacks
            {
                Log = logs.Add,
                StdoutLine = stdout.Add,
                StderrLine = stderr.Add,
            });

        Assert.NotNull(process);
        Assert.True(process!.WaitForExit(30000));
        process.WaitForExit(); //等待异步输出读尽
        //cmd echo 会带上 & 前的空格，按 Trim 后比对
        Assert.True(stdout.Any(line => line.Trim() == "hello-stdout"), $"stdout 实际：[{string.Join("|", stdout)}]");
        Assert.True(stderr.Any(line => line.Trim() == "hello-stderr"), $"stderr 实际：[{string.Join("|", stderr)}]");
        Assert.Equal(3, process.ExitCode);
        Assert.Contains(logs, line => line.Contains("已启动游戏进程"));
    }

    [Fact]
    public void RunGameProcess_sets_environment_variables()
    {
        //:1769-1775：Path 前插 Java 文件夹并去重、appdata 指向 MC 文件夹（短路径语义）
        var javaFolder = _root + "java" + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(javaFolder);
        var mcFolder = _root + "minecraft" + Path.DirectorySeparatorChar;
        var stdout = new List<string>();

        var process = ProcessRunner.RunGameProcess(
            new GameProcessInputs
            {
                JavaExePath = "cmd.exe",
                JavaFolder = javaFolder,
                McFolderSelected = mcFolder,
                WorkingDirectory = _root,
                Arguments = "/c echo %appdata% & echo %Path:;=&echo %",
                Encoding = System.Text.Encoding.UTF8,
            },
            new ProcessCallbacks { StdoutLine = stdout.Add });

        Assert.NotNull(process);
        Assert.True(process!.WaitForExit(30000));
        process.WaitForExit();
        //appdata 环境变量保留结尾分隔符（ToShortPath 短路径快速返回不剥离），整值比对
        Assert.True(stdout.Any(line => string.Equals(line.Trim(), mcFolder, StringComparison.OrdinalIgnoreCase)),
            $"stdout 实际：[{string.Join("|", stdout)}]");
        Assert.True(stdout.Any(line => line.Contains(PathUtils.RemoveSlashSuffix(javaFolder))),
            $"stdout 实际：[{string.Join("|", stdout)}]");
    }

    [Fact]
    public void RunGameProcess_kills_on_cancel()
    {
        //#1631（:1790-1794）：取消后 Kill 并不返回句柄（Loader.Output 不赋值）
        var watch = Stopwatch.StartNew();
        var process = ProcessRunner.RunGameProcess(
            new GameProcessInputs
            {
                JavaExePath = "cmd.exe",
                JavaFolder = @"C:\fake\java",
                McFolderSelected = _root + "minecraft" + Path.DirectorySeparatorChar,
                WorkingDirectory = _root,
                Arguments = "/c ping -n 30 127.0.0.1 > nul",
                Encoding = System.Text.Encoding.UTF8,
            },
            new ProcessCallbacks { IsCanceled = () => true });

        watch.Stop();
        Assert.Null(process);
        Assert.True(watch.ElapsedMilliseconds < 20000, $"取消后应立即 Kill，实际耗时 {watch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void RunGameProcess_applies_priority_setting()
    {
        //:1798-1810：PriorityBoostEnabled + LaunchArgumentPriority（0=高 → AboveNormal）
        SettingsService.Set("LaunchArgumentPriority", 0);
        var process = ProcessRunner.RunGameProcess(
            new GameProcessInputs
            {
                JavaExePath = "cmd.exe",
                JavaFolder = @"C:\fake\java",
                McFolderSelected = _root + "minecraft" + Path.DirectorySeparatorChar,
                WorkingDirectory = _root,
                Arguments = "/c ping -n 10 127.0.0.1 > nul",
                Encoding = System.Text.Encoding.UTF8,
            },
            new ProcessCallbacks());

        try
        {
            Assert.NotNull(process);
            Assert.True(process!.PriorityBoostEnabled);
            Assert.Equal(ProcessPriorityClass.AboveNormal, process.PriorityClass);
        } finally
        {
            try { process!.Kill(); } catch (InvalidOperationException) { }
            SettingsService.Reset("LaunchArgumentPriority");
        }
    }

    [Fact]
    public void Custom_commands_execute_and_wait()
    {
        //:1711-1736：执行全局自定义命令并等待（LaunchAdvanceRunWait=true 默认）
        var marker = _root + "marker-global.txt";
        SettingsService.Set("LaunchAdvanceRun", "echo custom-ok> \"" + marker + "\"");

        var logs = new List<string>();
        var result = ProcessRunner.RunCustomCommands(NewCustomInputs(), new ProcessCallbacks
        {
            Log = logs.Add,
        });

        Assert.True(result);
        Assert.True(File.Exists(marker), "等待退出后自定义命令应已执行完成");
        Assert.Contains("custom-ok", File.ReadAllText(marker));
        Assert.Contains(logs, line => line.Contains("正在执行全局自定义命令"));

        //版本命令同样执行（VersionAdvanceRunWait 默认 true）
        var versionMarker = _root + "marker-version.txt";
        SettingsService.Set("VersionAdvanceRun", "echo version-ok> \"" + versionMarker + "\"", "instance-key");
        ProcessRunner.RunCustomCommands(NewCustomInputs(), new ProcessCallbacks());
        Assert.True(File.Exists(versionMarker));
    }

    [Fact]
    public void Custom_commands_kill_on_cancel()
    {
        //#1183（:1730-1734）：取消启动时强制结束自定义命令 CMD 进程
        SettingsService.Set("LaunchAdvanceRun", "ping -n 30 127.0.0.1 > nul");

        var watch = Stopwatch.StartNew();
        var logs = new List<string>();
        var result = ProcessRunner.RunCustomCommands(NewCustomInputs(), new ProcessCallbacks
        {
            Log = logs.Add,
            IsCanceled = () => true,
        });

        watch.Stop();
        Assert.True(result);
        Assert.Contains(logs, line => line.Contains("已强制结束自定义命令 CMD 进程"));
        Assert.True(watch.ElapsedMilliseconds < 20000, $"取消后应立即 Kill，实际耗时 {watch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Custom_commands_save_batch_writes_and_cancels()
    {
        //:1695-1705：SaveBatch 分支——写 bat、上抛取消、不执行命令
        var saveBatch = _root + "LatestLaunch.bat";
        string? opened = null;
        var cancelInvoked = false;
        var hintInvoked = false;

        var result = ProcessRunner.RunCustomCommands(NewCustomInputs(saveBatch), new ProcessCallbacks
        {
            OpenExplorer = path => opened = path,
            CancelLaunch = () => cancelInvoked = true,
            SaveBatchHint = () => hintInvoked = true,
        });

        Assert.False(result);
        Assert.True(File.Exists(saveBatch));
        var content = File.ReadAllText(saveBatch);
        Assert.Contains("chcp 65001>nul", content);
        Assert.Contains("title 启动 - 测试版本", content);
        Assert.Contains("javaw.exe\" --demo --arg", content);
        Assert.EndsWith("pause", content);
        Assert.Equal(saveBatch, opened);
        Assert.True(cancelInvoked);
        Assert.True(hintInvoked);
    }
}