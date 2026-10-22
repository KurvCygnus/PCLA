namespace PCLCS.Minecraft.Download;

/// <summary>下载清单的失败分类。</summary>
public enum DownloadFailureKind {
    NotFound,
    InvalidPayload,
    TooFewEntries,
    MissingField,
    TargetVersionMissing,
    Timeout,
    Unexpected,
}
