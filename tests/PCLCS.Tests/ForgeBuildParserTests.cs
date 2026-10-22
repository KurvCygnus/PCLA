using PCLCS.Minecraft.Download;

namespace PCLCS.Tests;

public class ForgeBuildParserTests {
    private static string Html(string version, string category = "installer", string md5 = "abc123") {
        var file = category == "installer" ? "installer.jar" : category + ".zip";
        var cls = category == "client" ? "client.zip" : "classifier-" + category + "\"";
        return "<table><td class=\"download-version\"><a href=\"/forge-" + version + "-" + file + "\">" + version + "</a>" +
               "<span class=\"download-time\" title=\"2024-01-02 03:04:05\"></span>" +
               "<a href=\"/" + file + "\" class=\"" + cls + "\">" + file + "</a> MD5:</strong> " + md5 + "\n" +
               "</table>" +
               //! 官方源解析器要求响应至少 1000 字符（与 legacy 一致），真实页面远长于此。
               new string('x', 1000);
    }

    [Fact]
    public void OfficialHtmlReadsInstallerBuild() {
        var entry = ForgeBuildParser.ParseOfficialHtml("<html>" + Html("47.1.0") + "</html>", "1.20.1").Single();
        Assert.Equal("47.1.0", entry.VersionName);
        Assert.Equal("1.20.1", entry.Inherit);
        Assert.Equal("installer", entry.Category);
        Assert.Equal("abc123", entry.Hash);
        var expected = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm");
        Assert.Equal(expected, entry.ReleaseTime);
    }

    [Fact]
    public void OfficialHtmlSkipsBuildsWithoutDownloads() {
        var html = "<html><table><td class=\"download-version\"><a>1.0</a></td></table></html>" + new string('x', 1000);
        var ex = Assert.Throws<DownloadSourceException>(() => ForgeBuildParser.ParseOfficialHtml(html, "1.0"));
        Assert.Equal(DownloadFailureKind.NotFound, ex.Kind);
    }

    [Fact]
    public void OfficialHtmlRejectsShortResponse() {
        var ex = Assert.Throws<DownloadSourceException>(() => ForgeBuildParser.ParseOfficialHtml("<html></html>", "1.0"));
        Assert.Equal(DownloadFailureKind.TooFewEntries, ex.Kind);
    }

    [Fact]
    public void BmclapiPrefersInstallerOverUniversalAndClient() {
        var json = "[{\"version\":\"47.1.0\",\"branch\":null,\"modified\":\"2024-01-02T03:04:05Z\",\"files\":[" +
                   "{\"category\":\"client\",\"format\":\"zip\",\"hash\":\"c\"}," +
                   "{\"category\":\"universal\",\"format\":\"zip\",\"hash\":\"u\"}," +
                   "{\"category\":\"installer\",\"format\":\"jar\",\"hash\":\"i\"}]}]";
        var entry = ForgeBuildParser.ParseBmclapiJson(json, "1.20.1", "47.1.0").Single();
        Assert.Equal("installer", entry.Category);
        Assert.Equal("i", entry.Hash);
        Assert.True(entry.IsRecommended);
        Assert.Equal("47.1.0", entry.FileVersion);
    }

    [Fact]
    public void BmclapiAppliesLegacyBranchFixups() {
        var json = "[{\"version\":\"11.15.1.2318\",\"branch\":null,\"modified\":\"2015-01-02T03:04:05Z\",\"files\":[{\"category\":\"installer\",\"format\":\"jar\",\"hash\":\"i\"}]}]";
        var entry = ForgeBuildParser.ParseBmclapiJson(json, "1.8.9", null).Single();
        Assert.Equal("11.15.1.2318-1.8.9", entry.FileVersion);
    }

    [Fact]
    public void BmclapiInvalidVersionThrowsInvalidPayload() {
        var json = "[{\"version\":\"not-a-version\",\"branch\":null,\"modified\":\"2024-01-02T03:04:05Z\",\"files\":[{\"category\":\"installer\",\"format\":\"jar\",\"hash\":\"i\"}]}]";
        var ex = Assert.Throws<DownloadSourceException>(() => ForgeBuildParser.ParseBmclapiJson(json, "1.20.1", null));
        Assert.Equal(DownloadFailureKind.InvalidPayload, ex.Kind);
        Assert.Equal("BMCLAPI", ex.Source);
        Assert.Contains("not-a-version", ex.Message);
    }

    [Fact]
    public void BmclapiNonStringVersionThrowsInvalidPayload() {
        var json = "[{\"version\":{\"build\":1},\"branch\":null,\"modified\":\"2024-01-02T03:04:05Z\"}]";
        var ex = Assert.Throws<DownloadSourceException>(() => ForgeBuildParser.ParseBmclapiJson(json, "1.20.1", null));
        Assert.Equal(DownloadFailureKind.InvalidPayload, ex.Kind);
        Assert.NotNull(ex.Diagnostic);
    }

    [Fact]
    public void BmclapiInvalidModifiedThrowsInvalidPayload() {
        var json = "[{\"version\":\"47.1.0\",\"branch\":null,\"modified\":\"not-a-date\",\"files\":[]}]";
        var ex = Assert.Throws<DownloadSourceException>(() => ForgeBuildParser.ParseBmclapiJson(json, "1.20.1", null));
        Assert.Equal(DownloadFailureKind.InvalidPayload, ex.Kind);
        Assert.Contains("47.1.0", ex.Message);
    }

    [Fact]
    public void BmclapiPrefersInstallerEvenWhenItComesFirst() {
        var json = "[{\"version\":\"47.1.0\",\"modified\":\"2024-01-02T03:04:05Z\",\"files\":[" +
                   "{\"category\":\"installer\",\"format\":\"jar\",\"hash\":\"i\"}," +
                   "{\"category\":\"client\",\"format\":\"zip\",\"hash\":\"c\"}]}]";
        var entry = ForgeBuildParser.ParseBmclapiJson(json, "1.20.1", null).Single();
        Assert.Equal("installer", entry.Category);
        Assert.Equal("i", entry.Hash);
    }

    [Fact]
    public void BmclapiIgnoresFileWithMismatchedFormat() {
        var json = "[{\"version\":\"47.1.0\",\"modified\":\"2024-01-02T03:04:05Z\",\"files\":[" +
                   "{\"category\":\"installer\",\"format\":\"zip\",\"hash\":\"bad\"}," +
                   "{\"category\":\"universal\",\"format\":\"zip\",\"hash\":\"u\"}]}]";
        var entry = ForgeBuildParser.ParseBmclapiJson(json, "1.20.1", null).Single();
        Assert.Equal("universal", entry.Category);
        Assert.Equal("u", entry.Hash);
    }

    [Fact]
    public void RealForgeBuildsPageParses() {
        var html = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "forge-builds.html")));
        var entries = ForgeBuildParser.ParseOfficialHtml(html, "1.20.1");
        Assert.NotEmpty(entries);
        Assert.All(entries, entry => Assert.Equal("1.20.1", entry.Inherit));
        Assert.Contains(entries, entry => entry.Category is "installer" or "universal" or "client");
        Assert.All(entries, entry => Assert.Matches("^\\d{4}/\\d{2}/\\d{2} \\d{2}:\\d{2}$", entry.ReleaseTime!));
    }

    [Fact]
    public void RealForgeBuildsJsonParses() {
        var json = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "forge-builds-bmclapi.json")));
        var entries = ForgeBuildParser.ParseBmclapiJson(json, "1.20.1", null);
        Assert.NotEmpty(entries);
        //* 夹具按修改时间倒序，首条是最新的 47.4.27；47.0.1 是数组中最早的 1.20.1 构建。
        //* 夹具数组按修改时间升序：47.0.1 在首，47.4.27 在末。
        var newest = entries[entries.Count - 1];
        Assert.Equal("1.20.1", newest.Inherit);
        Assert.Equal("installer", newest.Category);
        Assert.Equal("47.4.27", newest.FileVersion);
        var oldest = entries[0];
        Assert.Equal("47.0.1", oldest.VersionName);
        Assert.Equal("installer", oldest.Category);
        Assert.Equal("2bdc0e68530daf0af01cae44a05e7f84849bdc6e", oldest.Hash);
        Assert.All(entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Hash)));
    }
}
