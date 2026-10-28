using PCLCS.Minecraft.Download;

namespace PCLCS.Results;

/// <summary>下载清单解析/源切换失败；保留旧异常的诊断字段。</summary>
public sealed class DownloadError : PclaError
{
    public DownloadError(DownloadFailureKind failureKind, string message, string? source = null,
        string? diagnostic = null, Exception? cause = null)
        : base(CodeFor(failureKind), message,
            failureKind == DownloadFailureKind.Unexpected ? PclaErrorKind.Unexpected : PclaErrorKind.Expected,
            cause)
    {
        FailureKind = failureKind;
        Source = source;
        Diagnostic = diagnostic;
    }

    public DownloadFailureKind FailureKind { get; }
    public string? Source { get; }
    public string? Diagnostic { get; }
    public bool IsUnavailable => FailureKind is DownloadFailureKind.NotFound or DownloadFailureKind.TargetVersionMissing;

    public static DownloadError From(DownloadSourceException exception) =>
        new(exception.Kind, exception.Message, exception.Source, exception.Diagnostic, exception);

    private static string CodeFor(DownloadFailureKind kind) => kind switch
    {
        DownloadFailureKind.NotFound => "download.not-found",
        DownloadFailureKind.InvalidPayload => "download.invalid-payload",
        DownloadFailureKind.TooFewEntries => "download.too-few-entries",
        DownloadFailureKind.MissingField => "download.missing-field",
        DownloadFailureKind.TargetVersionMissing => "download.target-version-missing",
        DownloadFailureKind.Timeout => "download.timeout",
        DownloadFailureKind.Unexpected => "download.unexpected",
        _ => "download.unknown",
    };
}