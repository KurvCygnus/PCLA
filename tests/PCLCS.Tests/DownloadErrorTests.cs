using PCLCS.Minecraft.Download;

namespace PCLCS.Tests;

public class DownloadErrorTests {
    [Fact]
    public void ExceptionCarriesKindAndDiagnostic() {
        var ex = new DownloadSourceException(
            DownloadFailureKind.InvalidPayload, "OptiFine 页面缺少版本表", "OptiFine 官方源", "长度 42；开头：<html>");
        Assert.Equal(DownloadFailureKind.InvalidPayload, ex.Kind);
        Assert.Equal("OptiFine 官方源", ex.Source);
        Assert.Equal("长度 42；开头：<html>", ex.Diagnostic);
        Assert.DoesNotContain("<html>", ex.Message);
    }

    [Fact]
    public void NotFoundAndTargetMissingAreUnavailable() {
        Assert.True(new DownloadSourceException(DownloadFailureKind.NotFound, "无").IsUnavailable);
        Assert.True(new DownloadSourceException(DownloadFailureKind.TargetVersionMissing, "无").IsUnavailable);
        Assert.False(new DownloadSourceException(DownloadFailureKind.Timeout, "超时").IsUnavailable);
    }

    [Fact]
    public void SnippetTruncatesLongPayload() {
        var text = new string('a', 600);
        var snippet = DownloadDiagnostics.Snippet(text, 10);
        Assert.Contains("长度 600", snippet);
        Assert.Contains(new string('a', 10), snippet);
        Assert.DoesNotContain(new string('a', 11), snippet);
    }

    [Fact]
    public void SnippetHandlesEmptyAndNullPayload() {
        Assert.Equal("响应为空", DownloadDiagnostics.Snippet(""));
        Assert.Equal("响应为空", DownloadDiagnostics.Snippet(null));
    }

    [Fact]
    public void SnippetDefaultsTo512Characters() {
        var text = new string('a', 600);
        var snippet = DownloadDiagnostics.Snippet(text);
        Assert.Contains("长度 600", snippet);
        Assert.Contains(new string('a', 512), snippet);
        Assert.DoesNotContain(new string('a', 513), snippet);
    }

    [Theory]
    [InlineData(DownloadFailureKind.InvalidPayload)]
    [InlineData(DownloadFailureKind.TooFewEntries)]
    [InlineData(DownloadFailureKind.MissingField)]
    [InlineData(DownloadFailureKind.Unexpected)]
    public void PayloadFailuresAreNotMarkedUnavailable(DownloadFailureKind kind) {
        Assert.False(new DownloadSourceException(kind, "无").IsUnavailable);
    }
}
