using PCLCS.Settings;
using System.Diagnostics;

namespace PCLCS.Launch;

/// <summary>
/// 进程输出与取消的回调面（原宿主层读取收敛：<see cref="IsCanceled"/> 对应 Loader.IsCanceled，
/// <see cref="Log"/> 对应 McLaunchLog 默认级；输出侧行回调供 Watcher 对接，
/// SaveBatch 相关回调保留宿主层 UI/加载器依赖）。
/// </summary>
public sealed class ProcessCallbacks
{
    /// <summary>取消检查（原 Loader.IsCanceled；VB 宿主绑定）。</summary>
    public Func<bool>? IsCanceled;
    /// <summary>启动日志（原 McLaunchLog 默认级；null 时静默）。</summary>
    public Action<string>? Log;
    /// <summary>stdout 行回调（原 Watcher 对 GameProcess.OutputDataReceived 的接线；null 时 C# 侧不接线）。</summary>
    public Action<string>? StdoutLine;
    /// <summary>stderr 行回调（原 Watcher 对 GameProcess.ErrorDataReceived 的接线；null 时 C# 侧不接线）。</summary>
    public Action<string>? StderrLine;
    /// <summary>进程退出回调（原宿主层轮询 HasExited；接线时才启用 EnableRaisingEvents）。</summary>
    public Action? Exited;
    /// <summary>导出脚本完成提示（原 CanceledHint = "导出启动脚本成功！"；宿主模块状态留宿主层）。</summary>
    public Action? SaveBatchHint;
    /// <summary>打开导出脚本（原 OpenExplorer，UI 留宿主层）。</summary>
    public Action<string>? OpenExplorer;
    /// <summary>强制结束启动过程（原 Loader.Parent.Cancel()，宿主层绑定）。</summary>
    public Action? CancelLaunch;
}

/// <summary>
/// 游戏进程启动（ModLaunch.vb:1764-1812 决策面）的宿主读取面。
/// </summary>
public sealed class GameProcessInputs
{
    /// <summary>选定的 Java 可执行文件（原 McLaunchJavaSelected.JavaExePath；使用 javaw.exe 会导致 #6263）。</summary>
    public string JavaExePath = null!;
    /// <summary>选定的 Java 文件夹（原 McLaunchJavaSelected.Folder；Path 环境变量前插）。</summary>
    public string JavaFolder = null!;
    /// <summary>MC 文件夹（原 McFolderSelected；appdata 环境变量）。</summary>
    public string McFolderSelected = null!;
    /// <summary>工作目录（原 McInstanceSelected.PathIndie）。</summary>
    public string WorkingDirectory = null!;
    /// <summary>启动参数串（原 McLaunchArgument，经宿主转发属性传入）。</summary>
    public string Arguments = null!;
    /// <summary>输出编码（原 McLaunchEncoding，经宿主转发属性传入）。</summary>
    public Encoding Encoding = null!;
}

/// <summary>
/// 自定义命令执行（ModLaunch.vb:1674-1763 决策面）的宿主读取面。
/// </summary>
public sealed class CustomInputs
{
    /// <summary>实例名（原 McInstanceSelected.Name；bat 标题行）。</summary>
    public string InstanceName = null!;
    /// <summary>实例目录（原 McInstanceSelected.PathIndie；bat cd /D 行）。</summary>
    public string InstanceIndiePath = null!;
    /// <summary>MC 文件夹（原 McFolderSelected；命令进程工作目录）。</summary>
    public string McFolderSelected = null!;
    /// <summary>选定的 Java 可执行文件（原 McLaunchJavaSelected.JavaExePath；bat 启动行）。</summary>
    public string JavaExePath = null!;
    /// <summary>启动参数串（原 McLaunchArgument；bat 启动行）。</summary>
    public string Arguments = null!;
    /// <summary>bat 编码（原 McLaunchEncoding，经宿主转发属性传入；chcp 行同源）。</summary>
    public Encoding Encoding = null!;
    /// <summary>系统是否 GBK（原 IsGBKEncoding，宿主层 ModBase.vb:70；chcp 54936→936 回退判定）。</summary>
    public bool IsGbkEncoding;
    /// <summary>启动脚本保存地址（原 CurrentLaunchOptions.SaveBatch；null 时写 LatestLaunch.bat 但继续启动）。</summary>
    public string? SaveBatch;
    /// <summary>实例设置键（W1 面，原 Settings.Get(…, Instance:=McInstanceSelected)）。</summary>
    public string InstanceSettingsKey = null!;
    /// <summary>自定义标记替换（原 ArgumentReplace，宿主层 ModMain.vb:819，留宿主层）。</summary>
    public Func<string, string> ArgumentReplace = null!;
    /// <summary>令牌过滤（原 FilterAccessToken，宿主层 ModMinecraft.vb:1445，留宿主层）。</summary>
    public Func<string, char, string> FilterAccessToken = null!;
}

/// <summary>
/// 进程启动核（自宿主层 ModLaunch.vb McLaunchRun/McLaunchCustom :1764-1812/:1674-1763 移植）。
/// StartProcess 长路径处理（ModBase.vb:878-890）随迁为私有实现；输出侧经回调（Watcher 以事件对接）。
/// </summary>
public static class ProcessRunner
{

    /// <summary>
    /// 游戏进程启动核（:1764-1812 决策面）：StartProcess ToShortPath 语义、环境变量（Path/appdata）、
    /// stdout/stderr 重定向与编码、取消 Kill（#1631）、进程优先级（PriorityBoostEnabled + LaunchArgumentPriority）。
    /// </summary>
    /// <returns>游戏进程句柄；取消路径（:1790-1794 Return）为 null，此时宿主层不赋 Loader.Output。</returns>
    public static Process? RunGameProcess(GameProcessInputs inputs, ProcessCallbacks callbacks)
    {
        var log = callbacks.Log;

        //启动信息
        var startInfo = new ProcessStartInfo(inputs.JavaExePath); //使用 javaw.exe 会导致 #6263

        //设置环境变量
        //? net8 差异（编码注）：net8 下 UseShellExecute=true 时访问 EnvironmentVariables 抛
        //? InvalidOperationException；运行目标为 net48（与 VB 行为 1:1），net8 仅 AOT 冒烟
        var pathEnv = startInfo.EnvironmentVariables["Path"];
        var paths = new List<string>();
        if (!string.IsNullOrEmpty(pathEnv)) paths.AddRange(pathEnv.Split(';'));
        paths.Add(PathUtils.ToShortPath(inputs.JavaFolder));
        startInfo.EnvironmentVariables["Path"] = string.Join(";", paths.Distinct());
        startInfo.EnvironmentVariables["appdata"] = PathUtils.ToShortPath(inputs.McFolderSelected);

        //设置其他参数
        startInfo.WorkingDirectory = inputs.WorkingDirectory;
        startInfo.UseShellExecute = false;
        startInfo.StandardOutputEncoding = inputs.Encoding;
        startInfo.StandardErrorEncoding = inputs.Encoding;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
        startInfo.Arguments = inputs.Arguments;

        //开始进程
        var gameProcess = StartProcess(startInfo);
        log?.Invoke("已启动游戏进程：" + inputs.JavaExePath);
        if (callbacks.IsCanceled?.Invoke() ?? false)
        {
            log?.Invoke("由于取消启动，已强制结束游戏进程"); //#1631
            gameProcess.Kill();
            return null;
        }

        //输出侧回调接线（Watcher 对接；宿主层 Watcher 现状对进程自行接线，未传回调时保持原状）
        if (callbacks.StdoutLine is not null || callbacks.StderrLine is not null)
        {
            if (callbacks.StdoutLine is not null) gameProcess.OutputDataReceived += (_, e) => { if (e.Data is not null) callbacks.StdoutLine(e.Data); };
            if (callbacks.StderrLine is not null) gameProcess.ErrorDataReceived += (_, e) => { if (e.Data is not null) callbacks.StderrLine(e.Data); };
            gameProcess.BeginOutputReadLine();
            gameProcess.BeginErrorReadLine();
        }
        if (callbacks.Exited is not null)
        {
            gameProcess.EnableRaisingEvents = true;
            gameProcess.Exited += (_, _) => callbacks.Exited();
        }

        //进程优先级处理
        try
        {
            gameProcess.PriorityBoostEnabled = true;
            switch (SettingsService.Get<int>("LaunchArgumentPriority"))
            {
            case 0: //高
                gameProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
                break;
            case 2: //低
                gameProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
                break;
            default: //中
                break;
            }
        } catch (Exception ex)
        {
            Logger.Error(ex, "设置进程优先级失败");
        }
        return gameProcess;
    }

    /// <summary>
    /// 自定义命令执行核（:1674-1763 决策面）：LatestLaunch.bat 写盘（SaveBatch 分支，取消经回调上抛）、
    /// 全局/版本自定义命令执行（可等待、取消 Kill #1183）。
    /// </summary>
    /// <returns>false：SaveBatch 导出脚本完成、提前结束（原 Return）；true：命令执行段完成。</returns>
    public static bool RunCustomCommands(CustomInputs inputs, ProcessCallbacks callbacks)
    {
        var log = callbacks.Log;

        //获取自定义命令
        var customCommandGlobal = SettingsService.Get<string>("LaunchAdvanceRun");
        if (customCommandGlobal != "") customCommandGlobal = inputs.ArgumentReplace(customCommandGlobal);
        var customCommandVersion = SettingsService.Get<string>("VersionAdvanceRun", inputs.InstanceSettingsKey);
        if (customCommandVersion != "") customCommandVersion = inputs.ArgumentReplace(customCommandVersion);

        //输出 bat
        try
        {
            var encoding = inputs.Encoding;
            var cmdString =
                $"chcp {(encoding.CodePage == 54936 && inputs.IsGbkEncoding ? 936 : encoding.CodePage)}>nul" + "\r\n" +
                "@echo off" + "\r\n" +
                $"title 启动 - {inputs.InstanceName}" + "\r\n" +
                "echo 游戏正在启动，请稍候。" + "\r\n" +
                $"cd /D \"{PathUtils.ToShortPath(inputs.InstanceIndiePath)}\"" + "\r\n" +
                customCommandGlobal + "\r\n" +
                customCommandVersion + "\r\n" +
                $"\"{inputs.JavaExePath}\" {inputs.Arguments}" + "\r\n" +
                "echo 游戏已退出。" + "\r\n" +
                "pause";
            FileUtils.Write(
                filePath: inputs.SaveBatch ?? Paths.BaseThenName + "LatestLaunch.bat",
                text: inputs.FilterAccessToken(cmdString, 'F').Replace("%", "%%"),
                encoding: encoding);
            if (inputs.SaveBatch is not null)
            {
                log?.Invoke("导出启动脚本完成，强制结束启动过程");
                callbacks.SaveBatchHint?.Invoke(); //原 CanceledHint = "导出启动脚本成功！"
                callbacks.OpenExplorer?.Invoke(inputs.SaveBatch);
                callbacks.CancelLaunch?.Invoke(); //原 Loader.Parent.Cancel()
                return false; //导出脚本完成
            }
        } catch (Exception ex)
        {
            Logger.Warn(ex, "输出启动脚本失败");
            if (inputs.SaveBatch is not null) throw; //直接触发启动失败
        }

        //执行自定义命令
        if (customCommandGlobal != "")
        {
            log?.Invoke("正在执行全局自定义命令：" + customCommandGlobal);
            RunCustomCommand(customCommandGlobal, inputs.McFolderSelected,
                SettingsService.Get<bool>("LaunchAdvanceRunWait"), "全局", callbacks);
        }
        if (customCommandVersion != "")
        {
            log?.Invoke("正在执行版本自定义命令：" + customCommandVersion);
            RunCustomCommand(customCommandVersion, inputs.McFolderSelected,
                SettingsService.Get<bool>("VersionAdvanceRunWait", inputs.InstanceSettingsKey), "版本", callbacks);
        }
        return true;
    }

    /// <summary>单条自定义命令的执行与取消 Kill（:1714-1735/:1739-1760，两分支同构抽取，文案 1:1）。</summary>
    private static void RunCustomCommand(string command, string workingDirectory, bool waitForExit, string label, ProcessCallbacks callbacks)
    {
        Process? customProcess = null;
        try
        {
            customProcess = StartProcess(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{command}\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (waitForExit)
            {
                while (!customProcess.HasExited && !(callbacks.IsCanceled?.Invoke() ?? false))
                {
                    Thread.Sleep(10);
                }
            }
        } catch (Exception ex)
        {
            Logger.Error(ex, $"执行{label}自定义命令失败", LogBehavior.Toast);
        } finally
        {
            if (customProcess is not null && !customProcess.HasExited && (callbacks.IsCanceled?.Invoke() ?? false))
            {
                callbacks.Log?.Invoke("由于取消启动，已强制结束自定义命令 CMD 进程"); //#1183
                customProcess.Kill();
            }
        }
    }

    /// <summary>
    /// 启动进程并返回进程实例，若失败则抛出异常。会自动进行长路径处理。
    /// （原宿主层 StartProcess(ProcessStartInfo)，ModBase.vb:885-890，逐字移植豁免。）
    /// </summary>
    private static Process StartProcess(ProcessStartInfo startInfo)
    {
        startInfo.FileName = PathUtils.ToShortPath(startInfo.FileName);
        if (!string.IsNullOrEmpty(startInfo.WorkingDirectory)) startInfo.WorkingDirectory = PathUtils.ToShortPath(startInfo.WorkingDirectory);
        Logger.Info($"启动进程：{startInfo.FileName} {startInfo.Arguments}");
        //? net8 注解将返回值标为可空（启动失败时抛异常而非返回 null，与 VB 语义一致），此处收紧
        return Process.Start(startInfo)!;
    }

}