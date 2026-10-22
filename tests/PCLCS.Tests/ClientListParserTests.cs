using Newtonsoft.Json.Linq;
using PCLCS.Minecraft.Download;

namespace PCLCS.Tests;

public class ClientListParserTests {
    private static string Manifest(params string[] ids) {
        var versions = string.Join(",", ids.Select(id => $"{{\"id\":\"{id}\",\"url\":\"https://x/{id}.json\"}}"));
        return $"{{\"latest\":{{\"release\":\"1.21.1\"}},\"versions\":[{versions}]}}";
    }

    [Fact]
    public void RejectsListsBelowTwoHundredEntries() {
        var ex = Assert.Throws<DownloadSourceException>(() => ClientListParser.Parse(Manifest("1.20.1"), "Mojang 官方源"));
        Assert.Equal(DownloadFailureKind.TooFewEntries, ex.Kind);
    }

    [Fact]
    public void AcceptsRealSizedListAndKeepsJson() {
        var ids = Enumerable.Range(0, 200).Select(i => $"1.{i}.0").ToArray();
        var result = ClientListParser.Parse(Manifest(ids), "Mojang 官方源");
        Assert.Equal(200, ((JArray)result["versions"]!).Count);
    }

    [Fact]
    public void DropsSkipSnapshotsAndUnknownDrops() {
        var json = JObject.Parse(Manifest("1.20.1", "1.20.2-pre1", "1.21", "1.21"));
        var drops = ClientListParser.ComputeDrops(json);
        Assert.Equal(drops.OrderByDescending(d => d), drops);
        Assert.Equal(drops.Distinct(), drops);
        Assert.DoesNotContain(209, drops);
    }

    [Fact]
    public void NormalizeIdFixesUnderscoresAndTrailingZero() {
        Assert.Equal("1.7.10-pre4", ClientListParser.NormalizeId("1.7.10_pre4"));
        Assert.Equal("1.8", ClientListParser.NormalizeId("1.8.0"));
        Assert.Equal("1.0", ClientListParser.NormalizeId("1.0"));
    }

    [Fact]
    public void ContainsVersionChecksIdExactly() {
        var json = JObject.Parse(Manifest("1.20.1", "1.21"));
        Assert.True(ClientListParser.ContainsVersion(json, "1.21"));
        Assert.False(ClientListParser.ContainsVersion(json, "1.21.0"));
    }
}
