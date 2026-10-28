using System.Diagnostics;

namespace PCLCS.Tests;

public class VersionScriptTests
{
    private static Dictionary<string, string> Run(string channel, string commit)
    {
        var script = Path.Combine(TestPaths.RepoRoot, "tools", "version.ps1");
        var psi = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"-NoProfile -File \"{script}\" -Channel {channel} -Commit {commit}",
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
        Assert.True(process.ExitCode == 0, $"version.ps1 失败：{stderr}");
        return stdout.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Contains('='))
            .Select(line => line.Split(new[] { '=' }, 2))
            .ToDictionary(parts => parts[0], parts => parts[1]);
    }

    [Theory]
    [InlineData("release", "abc1234def", "0.1.0", "0", "abc1234def")]
    [InlineData("nightly", "abc1234def", "0.1.0-nightly-abc1234", "1", "abc1234def")]
    [InlineData("snapshot", "abc1234def", "0.1.0-snapshot", "2", "")]
    public void ProducesChannelVersion(string channel, string commit, string expectedVersion,
        string expectedCode, string expectedCommitHash)
    {
        var map = Run(channel, commit);
        Assert.Equal(expectedVersion, map["VersionFull"]);
        Assert.Equal(expectedCode, map["ChannelCode"]);
        Assert.Equal(expectedCommitHash, map["CommitHash"]);
        Assert.Equal("0.1.0.0", map["AssemblyVersion"]);
        Assert.Equal("1000", map["ProtocolVersion"]);
        Assert.Equal("1", map["HelpVersion"]);
    }

    [Fact]
    public void NightlyRequiresCommit()
    {
        var script = Path.Combine(TestPaths.RepoRoot, "tools", "version.ps1");
        var psi = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"-NoProfile -File \"{script}\" -Channel nightly -Commit \"\"",
        };
        using var process = Process.Start(psi)!;
        _ = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("nightly", stderr, StringComparison.OrdinalIgnoreCase);
    }
}