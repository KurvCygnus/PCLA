using PCLCS.Minecraft.Download;
using PCLCS.Results;

namespace PCLCS.Tests;

public class DomainErrorTests
{
    [Fact]
    public void DownloadErrorFromPreservesFields()
    {
        var exception = new DownloadSourceException(
            DownloadFailureKind.NotFound, "没有找到版本", "BMCLAPI", "HTTP 404");
        var error = DownloadError.From(exception);

        Assert.Equal("download.not-found", error.Code);
        Assert.Equal(DownloadFailureKind.NotFound, error.FailureKind);
        Assert.Equal("BMCLAPI", error.Source);
        Assert.Equal("HTTP 404", error.Diagnostic);
        Assert.True(error.IsUnavailable);
        Assert.Same(exception, error.Cause);
        Assert.Equal(PclaErrorKind.Expected, error.Kind);
    }

    [Fact]
    public void DownloadErrorMapsEveryKind()
    {
        var expected = new Dictionary<DownloadFailureKind, string>
        {
            [DownloadFailureKind.NotFound] = "download.not-found",
            [DownloadFailureKind.InvalidPayload] = "download.invalid-payload",
            [DownloadFailureKind.TooFewEntries] = "download.too-few-entries",
            [DownloadFailureKind.MissingField] = "download.missing-field",
            [DownloadFailureKind.TargetVersionMissing] = "download.target-version-missing",
            [DownloadFailureKind.Timeout] = "download.timeout",
            [DownloadFailureKind.Unexpected] = "download.unexpected",
        };

        foreach (var pair in expected)
        {
            var error = new DownloadError(pair.Key, "消息");
            Assert.Equal(pair.Value, error.Code);
            Assert.Equal(
                pair.Key == DownloadFailureKind.Unexpected ? PclaErrorKind.Unexpected : PclaErrorKind.Expected,
                error.Kind);
        }

        Assert.True(new DownloadError(DownloadFailureKind.TargetVersionMissing, "消息").IsUnavailable);
        Assert.False(new DownloadError(DownloadFailureKind.Timeout, "消息").IsUnavailable);
    }

    [Fact]
    public void JavaErrorTruncatesOutput()
    {
        var output = new string('x', 600);
        var error = new JavaCheckError(
            JavaFailureReason.OutputUnreadable, "无法读取版本", @"C:\Java\", 1, output);

        Assert.Equal("java.output-unreadable", error.Code);
        Assert.Equal(JavaFailureReason.OutputUnreadable, error.Reason);
        Assert.Equal(500, error.OutputSnippet!.Length);
        Assert.Equal(PclaErrorKind.Expected, error.Kind);
    }

    [Fact]
    public void JavaUnexpectedIsUnexpectedKind()
    {
        var error = new JavaCheckError(JavaFailureReason.Unexpected, "未预期");
        Assert.Equal("java.unexpected", error.Code);
        Assert.Equal(PclaErrorKind.Unexpected, error.Kind);
    }

    [Fact]
    public void MigrationErrorMapsSectionCodes()
    {
        Assert.Equal("migration.files",
            new MigrationError(MigrationSection.Files, "path", "失败").Code);
        Assert.Equal("migration.portable",
            new MigrationError(MigrationSection.Portable, "path", "失败").Code);
        Assert.Equal("migration.registry",
            new MigrationError(MigrationSection.Registry, "path", "失败").Code);
        Assert.Equal("migration.startup",
            new MigrationError(MigrationSection.Startup, null, "失败").Code);
    }
}