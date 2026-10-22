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
}
