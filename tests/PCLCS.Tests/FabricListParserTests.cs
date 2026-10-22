using Newtonsoft.Json.Linq;
using PCLCS.Minecraft.Download;

namespace PCLCS.Tests;

public class FabricListParserTests {
    [Fact]
    public void ParseReturnsRootWithAllSections() {
        const string json = """
        {"game":[{"version":"1.21.1"}],"loader":[{"version":"0.16.0"}],"installer":[{"version":"1.0.1"}]}
        """;
        var root = FabricListParser.Parse(json);
        Assert.Same(root, root["game"]!.Root);
        Assert.Same(root, root["loader"]!.Root);
        Assert.Same(root, root["installer"]!.Root);
        Assert.Equal("1.21.1", ((JArray)root["game"]!)[0]["version"]!.ToString());
    }

    [Theory]
    [InlineData("""{"loader":[],"installer":[]}""")]
    [InlineData("""{"game":[],"installer":[]}""")]
    [InlineData("""{"game":[],"loader":[]}""")]
    public void ParseRejectsMissingSection(string json) {
        var ex = Assert.Throws<DownloadSourceException>(() => FabricListParser.Parse(json));
        Assert.Equal(DownloadFailureKind.MissingField, ex.Kind);
    }

    [Fact]
    public void ParseRejectsInvalidJson() {
        var ex = Assert.Throws<DownloadSourceException>(() => FabricListParser.Parse("{"));
        Assert.Equal(DownloadFailureKind.InvalidPayload, ex.Kind);
    }
}
