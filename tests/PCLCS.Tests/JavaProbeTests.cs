using PCLCS.Results;

namespace PCLCS.Tests;

public class JavaProbeTests {
    private sealed class FakeRunner : IJavaProcessRunner {
        public JavaProcessOutput Output { get; init; } = new();
        public Exception? Throw { get; init; }
        public int Calls { get; private set; }

        public Task<JavaProcessOutput> RunAsync(string exePath, string arguments, int timeoutMs,
            CancellationToken ct) {
            Calls++;
            if (Throw is not null) throw Throw;
            return Task.FromResult(Output);
        }
    }

    private static string NewJavaFolder() {
        var folder = Path.Combine(Path.GetTempPath(), "pcla-java-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Cleanup(string folder) {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    private static JavaProcessOutput SuccessOutput() => new() {
        ExitCode = 0,
        Output = "openjdk version \"17.0.1\" 2021-10-19\n 64-bit\n    file.encoding = UTF-8\n    native.encoding = UTF-8\n",
    };

    [Fact]
    public async Task CheckAsyncParsesVersionAndEncoding() {
        var folder = NewJavaFolder();
        try {
            var java = new Java(folder);
            var result = await java.CheckAsync(runner: new FakeRunner { Output = SuccessOutput() });

            Assert.True(result.IsSuccess);
            Assert.Same(java, result.Value);
            Assert.Equal(17, java.Version!.Major);
            Assert.Equal("UTF-8", java.FileEncodingName);
            Assert.Equal("UTF-8", java.NativeEncodingName);
            Assert.Null(java.LastError);
        }
        finally { Cleanup(folder); }
    }

    [Fact]
    public async Task CheckAsyncMapsMissingExe() {
        var folder = Path.Combine(Path.GetTempPath(), "pcla-java-missing-" + Guid.NewGuid().ToString("N"));
        var java = new Java(folder);
        var result = await java.CheckAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(JavaFailureReason.MissingExe, result.Error.Reason);
        Assert.Equal("java.missing-exe", result.Error.Code);
        Assert.Same(result.Error, java.LastError);
    }

    [Fact]
    public async Task CheckAsyncMapsNonZeroExit() {
        var folder = NewJavaFolder();
        try {
            var java = new Java(folder);
            var result = await java.CheckAsync(runner: new FakeRunner {
                Output = new JavaProcessOutput { ExitCode = 1, Output = "error" },
            });

            Assert.True(result.IsFailure);
            Assert.Equal(JavaFailureReason.ProcessFailed, result.Error.Reason);
            Assert.Equal(1, result.Error.ExitCode);
        }
        finally { Cleanup(folder); }
    }

    [Fact]
    public async Task CheckAsyncMapsTimeout() {
        var folder = NewJavaFolder();
        try {
            var java = new Java(folder);
            var result = await java.CheckAsync(runner: new FakeRunner {
                Output = new JavaProcessOutput { TimedOut = true, Output = "partial" },
            });

            Assert.True(result.IsFailure);
            Assert.Equal(JavaFailureReason.Timeout, result.Error.Reason);
        }
        finally { Cleanup(folder); }
    }

    [Fact]
    public async Task CheckAsyncMapsUnreadableOutput() {
        var folder = NewJavaFolder();
        try {
            var java = new Java(folder);
            var result = await java.CheckAsync(runner: new FakeRunner {
                Output = new JavaProcessOutput { ExitCode = 0, Output = "hello" },
            });

            Assert.True(result.IsFailure);
            Assert.Equal(JavaFailureReason.OutputUnreadable, result.Error.Reason);
        }
        finally { Cleanup(folder); }
    }

    [Fact]
    public async Task CheckAsyncMaps32BitJava() {
        var folder = NewJavaFolder();
        try {
            var java = new Java(folder);
            var result = await java.CheckAsync(runner: new FakeRunner {
                Output = new JavaProcessOutput {
                    ExitCode = 0,
                    Output = "openjdk version \"17.0.1\" 2021-10-19\n    file.encoding = UTF-8\n",
                },
            });

            Assert.True(result.IsFailure);
            Assert.Equal(JavaFailureReason.Not64Bit, result.Error.Reason);
        }
        finally { Cleanup(folder); }
    }

    [Fact]
    public async Task CheckAsyncRethrowsCancellationAndResetsCache() {
        var folder = NewJavaFolder();
        try {
            var java = new Java(folder);
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await java.CheckAsync(runner: new FakeRunner { Throw = new OperationCanceledException() }));

            Assert.Equal(0, java.isChecked);
            Assert.Null(java.available);
        }
        finally { Cleanup(folder); }
    }

    [Fact]
    public async Task CheckAllAsyncFiltersFailures() {
        var goodFolder = NewJavaFolder();
        var badFolder = Path.Combine(Path.GetTempPath(), "pcla-java-bad-" + Guid.NewGuid().ToString("N"));
        try {
            var good = new Java(goodFolder) { available = true };
            var bad = new Java(badFolder);
            var list = await JavaUtils.CheckAllAsync([good, bad]);

            Assert.Single(list);
            Assert.Same(good, list[0]);
            Assert.NotNull(bad.LastError);
        }
        finally {
            Cleanup(goodFolder);
            Cleanup(badFolder);
        }
    }
}
