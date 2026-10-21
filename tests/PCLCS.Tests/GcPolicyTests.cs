using PCLCS.Minecraft.Launch;

namespace PCLCS.Tests;

public class GcPolicyTests {
    private static readonly Version Windows10 = new(10, 0, 19045);
    private static readonly Version Windows81 = new(6, 3, 9600);

    private static string[] Build(int setupType, int javaMajor, bool debug = false, Version? windows = null) {
        return GcPolicy.Build(setupType, javaMajor, windows ?? Windows10, debug).ToArray();
    }

    [Fact]
    public void CustomSetupProducesNothing() {
        Assert.Empty(Build(3, 21));
    }

    [Fact]
    public void DefaultOnModernJavaUsesZgc() {
        Assert.Equal(
            new[] { "-XX:+UnlockExperimentalVMOptions", "-XX:+UseZGC" },
            Build(0, 17));
    }

    [Fact]
    public void DefaultOnJava21AddsGenerationalZgc() {
        Assert.Equal(
            new[] { "-XX:+UnlockExperimentalVMOptions", "-XX:+UseZGC", "-XX:+ZGenerational" },
            Build(0, 21));
    }

    [Fact]
    public void DefaultOnJava14FallsBackToG1() {
        Assert.Equal(
            new[]
            {
                "-XX:+UnlockExperimentalVMOptions",
                "-XX:+UseG1GC",
                "-XX:G1NewSizePercent=20",
                "-XX:G1ReservePercent=20",
                "-XX:G1HeapRegionSize=32M",
                "-XX:MaxGCPauseMillis=50"
            },
            Build(0, 14));
    }

    [Fact]
    public void OptimizedG1OnJava8() {
        Assert.Equal(
            new[]
            {
                "-XX:+UnlockExperimentalVMOptions",
                "-XX:+UseG1GC",
                "-XX:G1NewSizePercent=20",
                "-XX:G1ReservePercent=20",
                "-XX:G1HeapRegionSize=32M",
                "-XX:MaxGCPauseMillis=50",
                "-XX:+PerfDisableSharedMem",
                "-XX:+ParallelRefProcEnabled"
            },
            Build(4, 8));
        // PermGen 在 Java 8 已被移除，VB 只在 Java 7 及以下添加该参数。
        Assert.DoesNotContain("-XX:MaxPermSize=512m", Build(4, 8));
    }

    [Fact]
    public void OptimizedG1OnJava7AddsMaxPermSize() {
        var result = Build(4, 7);
        Assert.Contains("-XX:MaxPermSize=512m", result);
        Assert.DoesNotContain("-XX:+ParallelRefProcEnabled", result);
    }

    [Fact]
    public void OptimizedG1OnJava17AddsHeapRatios() {
        var result = Build(4, 17);
        Assert.Contains("-XX:MinHeapFreeRatio=25", result);
        Assert.Contains("-XX:MaxHeapFreeRatio=40", result);
        Assert.Contains("-XX:+PerfDisableSharedMem", result);
        Assert.DoesNotContain("-XX:MaxPermSize=512m", result);
    }

    [Fact]
    public void DebugDropsPerfDisableSharedMem() {
        Assert.DoesNotContain("-XX:+PerfDisableSharedMem", Build(4, 17, debug: true));
    }

    [Fact]
    public void OldWindowsForcesG1() {
        var result = Build(0, 21, windows: Windows81);
        Assert.Contains("-XX:+UseG1GC", result);
        Assert.DoesNotContain("-XX:+UseZGC", result);
    }

    [Fact]
    public void UnknownSetupTypeFallsThroughToZgc() {
        Assert.Equal(
            new[] { "-XX:+UnlockExperimentalVMOptions", "-XX:+UseZGC" },
            Build(5, 17));
    }

    [Fact]
    public void Java24AddsCompactObjectHeaders() {
        Assert.Contains("-XX:+UseCompactObjectHeaders", Build(0, 24));
    }

    [Theory]
    [InlineData("-XX:+UseG1GC", true)]
    [InlineData("-XX:+UseZGC", true)]
    [InlineData("-XX:+ZGenerational", true)]
    [InlineData("-XX:+UseCompactObjectHeaders", true)]
    [InlineData("-XX:G1NewSizePercent=20", true)]
    [InlineData("-XX:MaxGCPauseMillis=50", true)]
    [InlineData("-XX:+UnlockExperimentalVMOptions", false)]
    [InlineData("-Xmx2048m", false)]
    public void IsGcArgument(string argument, bool expected) {
        Assert.Equal(expected, GcPolicy.IsGcArgument(argument));
    }
}
