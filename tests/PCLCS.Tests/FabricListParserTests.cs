using PCLCS.Minecraft.Download;
using PCLCS.Results;
using System.Text.Json.Nodes;

namespace PCLCS.Tests;

public class FabricListParserTests
{
    [Fact]
    public void ParseReturnsRootWithAllSections()
    {
        const string json = """
        {"game":[{"version":"1.21.1"}],"loader":[{"version":"0.16.0"}],"installer":[{"version":"1.0.1"}]}
        """;
        var result = FabricListParser.Parse(json);

        Assert.True(result.IsSuccess);
        var root = result.Value;
        Assert.Equal("1.21.1", root["game"]!.AsArray()[0]!["version"]!.ToString());
    }

    [Theory]
    [InlineData("""{"loader":[],"installer":[]}""")]
    [InlineData("""{"game":[],"installer":[]}""")]
    [InlineData("""{"game":[],"loader":[]}""")]
    public void ParseRejectsMissingSection(string json)
    {
        var result = FabricListParser.Parse(json);

        Assert.True(result.IsFailure);
        Assert.Equal(DownloadFailureKind.MissingField, result.Error.FailureKind);
        Assert.Equal("download.missing-field", result.Error.Code);
        Assert.Equal("Fabric", result.Error.Source);
    }

    [Fact]
    public void ParseRejectsInvalidJson()
    {
        var result = FabricListParser.Parse("{");

        Assert.True(result.IsFailure);
        Assert.Equal(DownloadFailureKind.InvalidPayload, result.Error.FailureKind);
        Assert.Equal("download.invalid-payload", result.Error.Code);
        Assert.NotNull(result.Error.Diagnostic);
    }

    [Fact]
    public void ParseOrThrowThrowsPclaExceptionWithDownloadError()
    {
        var ex = Assert.Throws<PclaException>(() => FabricListParser.ParseOrThrow("{"));
        var error = Assert.IsType<DownloadError>(ex.Error);
        Assert.Equal(DownloadFailureKind.InvalidPayload, error.FailureKind);
    }

    [Fact]
    public void RealFabricListParses()
    {
        var json = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "fabric.json")));
        var result = FabricListParser.Parse(json);

        Assert.True(result.IsSuccess);
        var root = result.Value;
        Assert.NotNull(root["loader"]);
        Assert.True(root["loader"]!.AsArray().Count > 0);
        Assert.True(root["game"]!.AsArray().Count > 0);
        Assert.True(root["installer"]!.AsArray().Count > 0);
        Assert.All(root["loader"]!.AsArray(), entry => Assert.NotNull(entry!["version"]));
    }
}