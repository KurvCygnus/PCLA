namespace PCLCS.Minecraft.Download;

/// <summary>下载清单解析或源访问失败。消息面向用户，诊断片段只进日志。</summary>
public sealed class DownloadSourceException : Exception {
    public DownloadFailureKind Kind { get; }
    //! 有意隐藏 Exception.Source：这里的 Source 指下载源，与异常的抛出位置无关。
    public new string? Source { get; }
    public string? Diagnostic { get; }

    /// <summary>该源是否确定没有可用结果，可以直接切换到下一个源。</summary>
    public bool IsUnavailable => Kind is DownloadFailureKind.NotFound or DownloadFailureKind.TargetVersionMissing;

    public DownloadSourceException(DownloadFailureKind kind, string message, string? source = null,
        string? diagnostic = null, Exception? inner = null) : base(message, inner) {
        Kind = kind;
        Source = source;
        Diagnostic = diagnostic;
    }
}

/// <summary>诊断片段生成。</summary>
public static class DownloadDiagnostics {
    /// <summary>截断原始响应，保留长度与开头，避免把整页 HTML 塞进日志。</summary>
    public static string Snippet(string? payload, int max = 512) {
        if (string.IsNullOrEmpty(payload)) return "响应为空";
        var text = payload!.Length <= max ? payload : payload.Substring(0, max);
        return $"长度 {payload.Length}；开头：{text.Replace("\r", " ").Replace("\n", " ")}";
    }
}
