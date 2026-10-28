namespace PCLCS;

/// <summary>Java 进程探测的注入点；测试用假实现覆盖超时与取消。</summary>
public interface IJavaProcessRunner
{
    Task<JavaProcessOutput> RunAsync(string exePath, string arguments, int timeoutMs, CancellationToken ct);
}

/// <summary>进程输出；TimedOut 由默认 runner 捕获 TimeoutException 后置位。</summary>
public sealed class JavaProcessOutput
{
    public string Output { get; init; } = "";
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
}

/// <summary>包装 TaskUtils.RunProgramAsync 的默认实现。</summary>
public sealed class ProcessJavaRunner : IJavaProcessRunner
{
    public async Task<JavaProcessOutput> RunAsync(string exePath, string arguments, int timeoutMs,
        CancellationToken ct)
    {
        try
        {
            var (output, exitCode) = await TaskUtils.RunProgramAsync(
                exePath, arguments, timeoutMs, cancellationToken: ct).NoCapture();
            return new JavaProcessOutput { Output = output, ExitCode = exitCode };
        } catch (TimeoutException)
        {
            return new JavaProcessOutput { TimedOut = true };
        }
    }
}