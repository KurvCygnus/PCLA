using PCLCS.Json;
using PCLCS.Minecraft.Download;
using System.Text.Json.Nodes;

namespace PCLCS.Tests;

public class ClientListParserTests
{
    private static string Manifest(params string[] ids)
    {
        var versions = string.Join(",", ids.Select(id => $"{{\"id\":\"{id}\",\"url\":\"https://x/{id}.json\"}}"));
        return $"{{\"latest\":{{\"release\":\"1.21.1\"}},\"versions\":[{versions}]}}";
    }

    [Fact]
    public void RejectsListsBelowTwoHundredEntries()
    {
        var ex = Assert.Throws<DownloadSourceException>(() => ClientListParser.Parse(Manifest("1.20.1"), "Mojang 官方源"));
        Assert.Equal(DownloadFailureKind.TooFewEntries, ex.Kind);
    }

    [Fact]
    public void AcceptsRealSizedListAndKeepsJson()
    {
        var ids = Enumerable.Range(0, 200).Select(i => $"1.{i}.0").ToArray();
        var result = ClientListParser.Parse(Manifest(ids), "Mojang 官方源");
        Assert.Equal(200, result["versions"]!.AsArray().Count);
    }

    [Fact]
    public void DropsSkipSnapshotsAndUnknownDrops()
    {
        //* 快照 "1.21-pre1" 含连字符会被跳过，其 Drop 210 与正式版不同；
        //* "unknown" 经 NameToVersion 落到 9999.0.0，VersionToDrop 返回 209，会被排除。
        var json = JsonNodeUtils.ParseObject(Manifest("1.12.2", "1.20.1", "1.20.1", "1.21-pre1", "unknown"));
        var drops = ClientListParser.ComputeDrops(json);
        Assert.Equal(new[] { 200, 120 }, drops);
    }

    [Fact]
    public void NormalizeIdFixesUnderscoresAndTrailingZero()
    {
        Assert.Equal("1.7.10-pre4", ClientListParser.NormalizeId("1.7.10_pre4"));
        Assert.Equal("1.8", ClientListParser.NormalizeId("1.8.0"));
        Assert.Equal("1.0", ClientListParser.NormalizeId("1.0"));
    }

    [Fact]
    public void ContainsVersionChecksIdExactly()
    {
        var json = JsonNodeUtils.ParseObject(Manifest("1.20.1", "1.21"));
        Assert.True(ClientListParser.ContainsVersion(json, "1.21"));
        Assert.False(ClientListParser.ContainsVersion(json, "1.21.0"));
    }

    [Fact]
    public void RealMojangManifestParses()
    {
        var json = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "mojang-manifest.json")));
        var root = ClientListParser.Parse(json, "Mojang 官方源");
        Assert.True(root["versions"]!.AsArray().Count >= 200);
    }

    [Fact]
    public void RealBmclapiManifestParses()
    {
        var json = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "bmclapi-manifest.json")));
        var root = ClientListParser.Parse(json, "BMCLAPI");
        Assert.NotEmpty(ClientListParser.ComputeDrops(root));
        Assert.Equal("26.3", root["latest"]!["release"]!.ToString());
        var versions = root["versions"]!.AsArray();
        Assert.Equal("26.4-snapshot-5", versions[0]!["id"]!.ToString());
        Assert.Equal("https://piston-meta.mojang.com/v1/packages/2ee2d20f6e6a37aae39238fff3286ab879a99ce6/26.4-snapshot-5.json",
            versions[0]!["url"]!.ToString());
        Assert.Equal("rd-132211", versions[versions.Count - 1]!["id"]!.ToString());
        var release = versions.Single(v => v!["id"]!.ToString() == "1.20.1")!;
        Assert.Equal("release", release["type"]!.ToString());
        Assert.Equal("https://piston-meta.mojang.com/v1/packages/38d412af3b247e485c7f7e50ac25febc4e79ab1d/1.20.1.json",
            release["url"]!.ToString());
    }
}