using PCLCS.Minecraft.Download;

namespace PCLCS.Tests;

public class OptiFineListParserTests {
    private static string BuildOfficialHtml(int count = 10) {
        var rows = Enumerable.Range(0, count).Select(i =>
            $"<td class='colForge'>Forge 14.23.5.{2859 + i}</td>" +
            $"<td class='colDate'>2.15.2024</td>" +
            $"<td class='colMirror'><a href=\"downloads/OptiFine_1.12.2_HD_U_G{i}.jar\">(Mirror)</a></td>");
        return "<html>" + string.Join("", rows) + "</html>" + new string('x', 200);
    }

    [Fact]
    public void OfficialHtmlBuildsDisplayAndInstanceNames() {
        var entries = OptiFineListParser.ParseOfficialHtml(BuildOfficialHtml());
        Assert.Equal(10, entries.Count);
        Assert.Equal("1.12.2 G0", entries[0].DisplayName);
        Assert.Equal("1.12.2-OptiFine_HD_U_G0", entries[0].InstanceName);
        Assert.Equal("1.12.2", entries[0].Inherit);
        Assert.Equal("2024/15/2", entries[0].ReleaseTime);
        Assert.Equal("14.23.5.2859", entries[0].RequiredForgeVersion);
        Assert.False(entries[0].IsPreview);
    }

    [Fact]
    public void OfficialHtmlMarksPreviewEntries() {
        var html = BuildOfficialHtml().Replace("OptiFine_1.12.2_HD_U_G0", "OptiFine_preview_1.12.2_HD_U_G0");
        var entries = OptiFineListParser.ParseOfficialHtml(html);
        Assert.True(entries[0].IsPreview);
        Assert.StartsWith("preview_OptiFine_", entries[0].FileName);
    }

    [Fact]
    public void OfficialHtmlRejectsMismatchedColumns() {
        var ex = Assert.Throws<DownloadSourceException>(() => OptiFineListParser.ParseOfficialHtml(BuildOfficialHtml().Replace("colDate'>2.15.2024</td>", "")));
        Assert.Equal(DownloadFailureKind.InvalidPayload, ex.Kind);
    }

    [Fact]
    public void BmclapiJsonMapsFields() {
        var json = "[{\"mcversion\":\"1.12.2\",\"type\":\"HD_U\",\"patch\":\"G5\",\"filename\":\"OptiFine_1.12.2_HD_U_G5.jar\",\"forge\":\"Forge 14.23.5.2859\"}]";
        var entry = OptiFineListParser.ParseBmclapiJson(json).Single();
        Assert.Equal("1.12.2 G5", entry.DisplayName);
        Assert.Equal("1.12.2-OptiFine_HD_U_G5", entry.InstanceName);
        Assert.Equal("14.23.5.2859", entry.RequiredForgeVersion);
    }

    [Fact]
    public void RealOfficialPageParses() {
        var html = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "optifine-official.html")));
        var entries = OptiFineListParser.ParseOfficialHtml(html);
        Assert.NotEmpty(entries);
        Assert.All(entries, entry => Assert.Matches("^\\d+\\.", entry.Inherit));
        Assert.All(entries, entry => Assert.Matches("^\\d{4}/\\d{1,2}/\\d{1,2}$", entry.ReleaseTime));
    }

    [Fact]
    public void RealBmclapiListParses() {
        var json = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "optifine-bmclapi.json")));
        Assert.NotEmpty(OptiFineListParser.ParseBmclapiJson(json));
    }
}
