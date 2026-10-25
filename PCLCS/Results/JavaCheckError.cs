namespace PCLCS.Results;

public enum JavaFailureReason {
    MissingExe,
    ProcessFailed,
    UnsupportedVersion,
    Not64Bit,
    OutputUnreadable,
    Timeout,
    Unexpected,
}

/// <summary>Java 探测失败；OutputSnippet 截断到 500 字符，避免日志膨胀。</summary>
public sealed class JavaCheckError : PclaError {
    public JavaCheckError(JavaFailureReason reason, string message, string? javaFolder = null,
        int? exitCode = null, string? outputSnippet = null, Exception? cause = null)
        : base(CodeFor(reason), message,
            reason == JavaFailureReason.Unexpected ? PclaErrorKind.Unexpected : PclaErrorKind.Expected,
            cause) {
        Reason = reason;
        JavaFolder = javaFolder;
        ExitCode = exitCode;
        OutputSnippet = outputSnippet is null
            ? null
            : outputSnippet.Length <= 500 ? outputSnippet : outputSnippet[..500];
    }

    public JavaFailureReason Reason { get; }
    public string? JavaFolder { get; }
    public int? ExitCode { get; }
    public string? OutputSnippet { get; }

    private static string CodeFor(JavaFailureReason reason) => reason switch {
        JavaFailureReason.MissingExe => "java.missing-exe",
        JavaFailureReason.ProcessFailed => "java.process-failed",
        JavaFailureReason.UnsupportedVersion => "java.unsupported-version",
        JavaFailureReason.Not64Bit => "java.not-64-bit",
        JavaFailureReason.OutputUnreadable => "java.output-unreadable",
        JavaFailureReason.Timeout => "java.timeout",
        JavaFailureReason.Unexpected => "java.unexpected",
        _ => "java.unknown",
    };
}
