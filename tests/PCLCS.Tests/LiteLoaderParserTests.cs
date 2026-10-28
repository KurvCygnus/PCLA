using PCLCS.Minecraft.Download;

namespace PCLCS.Tests;

public class LiteLoaderParserTests
{
    private const string Json = """
    {
      "versions": {
        "1.5.2": {"artefacts": {"com.mumfrey:liteloader": {"latest": {"timestamp": 1, "md5": "skip15", "stream": "release"}}}},
        "1.6.4": {"artefacts": {"com.mumfrey:liteloader": {"latest": {"timestamp": 2, "md5": "skip16", "stream": "release"}}}},
        "1.7.10": {"snapshots": {"com.mumfrey:liteloader": {"latest": {"timestamp": 1500000000, "md5": "legacy", "stream": "snapshot"}}}},
        "1.8": {"artefacts": {"com.mumfrey:liteloader": {"latest": {"timestamp": 1600000000, "md5": "v18", "stream": "release"}}}},
        "1.9": {"artefacts": {"com.mumfrey:liteloader": {"latest": {"timestamp": 1700000000, "md5": "v19", "stream": "snapshot"}}}}
      }
    }
    """;

    [Fact]
    public void ParseSkipsOneFiveAndOneSix()
    {
        var entries = LiteLoaderParser.Parse(Json);
        Assert.Equal(3, entries.Count);
        Assert.DoesNotContain(entries, entry => entry.Inherit is "1.5.2" or "1.6.4");
    }

    [Fact]
    public void ParseMapsLegacyEntryAndDoubleLocalTime()
    {
        var entry = LiteLoaderParser.Parse(Json).Single(item => item.Inherit == "1.7.10");
        Assert.Equal("liteloader-installer-1.7.10-00-SNAPSHOT.jar", entry.FileName);
        Assert.True(entry.IsLegacy);
        Assert.True(entry.IsPreview);
        Assert.Equal("legacy", entry.MD5);
        Assert.Equal(ExpectedDoubleLocalTime(1500000000), entry.ReleaseTime);
        Assert.Equal("1500000000", entry.JsonToken["timestamp"]?.ToString());
    }

    [Fact]
    public void ParseAddsDotZeroToEightAndNineFileNames()
    {
        var entries = LiteLoaderParser.Parse(Json);
        var v18 = entries.Single(item => item.Inherit == "1.8");
        Assert.Equal("liteloader-installer-1.8.0-00-SNAPSHOT.jar", v18.FileName);
        Assert.False(v18.IsLegacy);
        Assert.False(v18.IsPreview);

        var v19 = entries.Single(item => item.Inherit == "1.9");
        Assert.Equal("liteloader-installer-1.9.0-00-SNAPSHOT.jar", v19.FileName);
        Assert.False(v19.IsLegacy);
        Assert.True(v19.IsPreview);
    }

    //* 旧实现把 Unix 时间先按本地时区解析，再把该值当作 UTC 转本地，共叠加两次本地偏移。
    private static string ExpectedDoubleLocalTime(long timestamp)
    {
        var utc = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        var local = utc.ToLocalTime();
        return local.Add(TimeZoneInfo.Local.GetUtcOffset(utc)).ToString("yyyy/MM/dd HH:mm");
    }

    [Fact]
    public void RealLiteLoaderListParses()
    {
        var json = File.ReadAllText(TestPaths.Fixture(Path.Combine("Download", "liteloader.json")));
        var entries = LiteLoaderParser.Parse(json);
        Assert.NotEmpty(entries);
        Assert.DoesNotContain(entries, entry => entry.Inherit.StartsWith("1.5") || entry.Inherit.StartsWith("1.6"));
        Assert.All(entries, entry => Assert.Equal(
            "liteloader-installer-" + entry.Inherit + (entry.Inherit is "1.8" or "1.9" ? ".0" : "") + "-00-SNAPSHOT.jar",
            entry.FileName));
    }
}