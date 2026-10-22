using PCLCS.Minecraft.Download;

namespace PCLCS.Tests;

public class NeoForgeParserTests {
    private const string Latest = "{\"versions\":[\"20.4.30-beta\",\"21.1.0\",\"26.1.0.0-alpha.1+snapshot-1\"]}";
    private const string Legacy = "{\"versions\":[\"1.20.1-47.1.99\",\"47.1.82\",\"0.25w14craftmine.3-beta\"]}";

    [Fact]
    public void ParsesBothFeedsAndSortsNewestFirst() {
        var entries = NeoForgeParser.Parse(Latest, Legacy);
        Assert.Equal(5, entries.Count);
        Assert.Equal("26.1.0.0-alpha.1+snapshot-1", entries[0].ApiName);
        Assert.DoesNotContain(entries, e => e.ApiName == "47.1.82");
    }

    [Fact]
    public void LegacyOneTwentyOneLiftsMajorToNineteen() {
        var entry = NeoForgeParser.Parse(Latest, Legacy).Single(e => e.ApiName == "1.20.1-47.1.99");
        Assert.Equal("19.47.1.99", entry.Version.ToString());
        Assert.Equal("1.20.1", entry.Inherit);
        Assert.Equal("47.1.99", entry.VersionName);
    }

    [Fact]
    public void SnapshotSuffixBecomesInheritSuffix() {
        var entry = NeoForgeParser.Parse(Latest, "{\"versions\":[]}").Single(e => e.ApiName.StartsWith("26.1.0.0"));
        Assert.Equal("26.1-snapshot-1", entry.Inherit);
    }

    [Fact]
    public void EmptyFeedsThrowNotFound() {
        var ex = Assert.Throws<DownloadSourceException>(() => NeoForgeParser.Parse("{\"versions\":[]}", "{\"versions\":[]}"));
        Assert.Equal(DownloadFailureKind.NotFound, ex.Kind);
    }

    [Fact]
    public void UnparsableApiNameThrowsInvalidPayload() {
        //* 该名称能通过正则（第 4 段可缺省），但映射 Legacy 前缀后是 5 段，Version 构造会抛。
        var ex = Assert.Throws<DownloadSourceException>(
            () => NeoForgeParser.Parse("{\"versions\":[\"1.20.1-47.1.99.5\"]}", "{\"versions\":[]}"));
        Assert.Equal(DownloadFailureKind.InvalidPayload, ex.Kind);
        Assert.Equal("NeoForge", ex.Source);
        Assert.Contains("1.20.1-47.1.99.5", ex.Message);
        Assert.NotNull(ex.Diagnostic);
    }

    [Fact]
    public void SortsFullOrderNewestFirst() {
        var entries = NeoForgeParser.Parse(Latest, Legacy);
        Assert.Equal(new[] {
            "26.1.0.0-alpha.1+snapshot-1",
            "21.1.0",
            "20.4.30-beta",
            "1.20.1-47.1.99",
            "0.25w14craftmine.3-beta",
        }, entries.Select(e => e.ApiName));
    }

    [Fact]
    public void SameVersionBreaksTieByVersionName() {
        //* 三个名称的 Version 都是 21.1.0，顺序完全由 McUtils.CompareVersion 决定：
        //* beta.10 与 beta.2 按数字段比较（10 > 2，Ordinal 字符串比较会得出相反结果）；
        //* "beta" 与缺失段都归 0 后走 Ordinal 回退，字母大于数字，故 beta 系排在 21.1.0 之前。
        var latest = "{\"versions\":[\"21.1.0-beta.2\",\"21.1.0-beta.10\",\"21.1.0\"]}";
        var entries = NeoForgeParser.Parse(latest, "{\"versions\":[]}");
        Assert.Equal(new[] { "21.1.0-beta.10", "21.1.0-beta.2", "21.1.0" }, entries.Select(e => e.ApiName));
    }

    [Fact(Skip = "maven.neoforged.net 在本机不可达，待网络可用后补录真实响应")]
    public void RealNeoForgeFeedsParse() {
        //* 真实夹具从未录制成功（maven.neoforged.net:443 被墙），本测试在录制成功前保持跳过。
        var latestPath = TestPaths.Fixture(Path.Combine("Download", "neoforge-latest.json"));
        var legacyPath = TestPaths.Fixture(Path.Combine("Download", "neoforge-legacy.json"));
        var latest = File.ReadAllText(latestPath);
        var legacy = File.ReadAllText(legacyPath);
        Assert.NotEmpty(NeoForgeParser.Parse(latest, legacy));
    }

    [Fact]
    public void MinimalFixturesParseExcludeAndSort() {
        //* 合成夹具（neoforge-*.min.json）：真实响应因 maven.neoforged.net 不可达未录制，
        //* 用最小可解析形状锁住字段映射与排序，不依赖网络。
        var latest = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "neoforge-latest.min.json")));
        var legacy = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "neoforge-legacy.min.json")));
        var entries = NeoForgeParser.Parse(latest, legacy);
        Assert.Equal(new[] {
            "26.1.44",
            "21.0.2",
            "21.0.0-beta",
            "20.2.1",
            "1.20.1-47.2.3",
            "1.20.1-47.1.83",
        }, entries.Select(e => e.ApiName));
        Assert.DoesNotContain(entries, e => e.ApiName == "47.1.82");
        Assert.Equal(6, entries.Count);
    }

    [Fact]
    public void MinimalFixturesMapFields() {
        var latest = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "neoforge-latest.min.json")));
        var legacy = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "neoforge-legacy.min.json")));
        var entries = NeoForgeParser.Parse(latest, legacy);
        var lifted = entries.Single(e => e.ApiName == "1.20.1-47.1.83");
        Assert.Equal("19.47.1.83", lifted.Version.ToString());
        Assert.Equal("1.20.1", lifted.Inherit);
        Assert.Equal("47.1.83", lifted.VersionName);
        Assert.False(lifted.IsBeta);
        var plain = entries.Single(e => e.ApiName == "21.0.2");
        Assert.Equal(new Version(21, 0, 2), plain.Version);
        Assert.Equal("1.21", plain.Inherit);
        var beta = entries.Single(e => e.ApiName == "21.0.0-beta");
        Assert.Equal(new Version(21, 0, 0), beta.Version);
        Assert.Equal("1.21", beta.Inherit);
        Assert.True(beta.IsBeta);
        var snapshot = entries.Single(e => e.ApiName == "26.1.44");
        Assert.Equal(new Version(26, 1, 44), snapshot.Version);
        Assert.Equal("26.1.44", snapshot.Inherit);
    }
}
