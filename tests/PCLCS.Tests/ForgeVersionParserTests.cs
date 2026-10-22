using PCLCS.Minecraft.Download;

namespace PCLCS.Tests;

public class ForgeVersionParserTests {
    private static string Html(int count) {
        var links = Enumerable.Range(0, count).Select(i => $"<a href=\"index_1.{i}.2.html\">1.{i}.2</a>");
        return "<html>" + string.Join("", links) + "</html>" + new string('x', 300);
    }

    [Fact]
    public void OfficialHtmlAddsLegacyOneTwoFour() {
        var names = ForgeVersionParser.ParseOfficialHtml(Html(12));
        Assert.Equal(13, names.Count);
        Assert.Equal("1.2.4", names[names.Count - 1]);
    }

    [Fact]
    public void OfficialHtmlRejectsShortLists() {
        var ex = Assert.Throws<DownloadSourceException>(() => ForgeVersionParser.ParseOfficialHtml(Html(5)));
        Assert.Equal(DownloadFailureKind.TooFewEntries, ex.Kind);
    }

    [Fact]
    public void BmclapiReadsStringArray() {
        var json = "[\"1.21.1\",\"1.20.1\",\"1.19.2\",\"1.18.2\",\"1.17.1\",\"1.16.5\",\"1.15.2\",\"1.14.4\",\"1.12.2\",\"1.7.10\"]";
        Assert.Equal(10, ForgeVersionParser.ParseBmclapiJson(json).Count);
    }

    [Fact]
    public void RealForgeIndexParses() {
        var html = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "forge-index.html")));
        Assert.Contains("1.2.4", ForgeVersionParser.ParseOfficialHtml(html));
    }
}
