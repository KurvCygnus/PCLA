using PCLCS.Results;

namespace PCLCS.Tests;

public class PclaCrashFallbackTests
{
    [Fact]
    public void WritesCrashFileWithCodeAndException()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pcla-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var ex = new InvalidOperationException("boom");
            var error = PclaError.Unexpected(ex);
            var path = PclaCrashFallback.TryWrite(ex, error, folder);

            Assert.NotNull(path);
            Assert.True(File.Exists(path!));
            var text = File.ReadAllText(path!);
            Assert.Contains("unexpected", text);
            Assert.Contains("boom", text);
            Assert.Contains(nameof(InvalidOperationException), text);
        } finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void InvalidTempPathReturnsNull()
    {
        var path = PclaCrashFallback.TryWrite(
            new InvalidOperationException("boom"), PclaError.Unexpected(new InvalidOperationException("boom")), "\0");
        Assert.Null(path);
    }
}