using System.Diagnostics;

namespace PCLCS.Common;

/// <summary>
/// 进程启动（ModBase.vb:878-890 逐字，ToShortPath 长路径处理语义）。
/// NetRequest.cs:470-496 的 GetResultWithTimeout 同批提升为 Common.GetResultWithTimeout，本类型只承载进程启动面。
/// </summary>
public static class ProcessStarter
{

    /// <summary>
    /// 启动进程并返回进程实例，若失败则抛出异常。会自动进行长路径处理。
    /// </summary>
    public static Process Start(string fileName, string arguments = "")
    {
        return Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
        });
    }

    /// <summary>
    /// 启动进程并返回进程实例，若失败则抛出异常。会自动进行长路径处理。
    /// </summary>
    public static Process Start(ProcessStartInfo startInfo)
    {
        startInfo.FileName = PathUtils.ToShortPath(startInfo.FileName);
        if (!string.IsNullOrEmpty(startInfo.WorkingDirectory))
        {
            startInfo.WorkingDirectory = PathUtils.ToShortPath(startInfo.WorkingDirectory);
        }
        Logger.Info($"启动进程：{startInfo.FileName} {startInfo.Arguments}");
        //? net8 注解将返回值标为可空（启动失败时抛异常而非返回 null，与 VB 语义一致），此处收紧
        return Process.Start(startInfo)!;
    }

}