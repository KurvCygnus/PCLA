using System.Text.Json.Nodes;
using PCLCS;

namespace PCLCS.Tests;

public class McVersionTests
{
    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void ReleaseTime_Iso8601_InvariantRoundtrip()
    {
        var version = new McVersion("1.20.1", Parse("""{"id":"1.20.1","libraries":[],"releaseTime":"2023-06-12T13:25:49+00:00"}"""));
        Assert.Equal(new DateTime(2023, 6, 12, 13, 25, 49, DateTimeKind.Utc), version.ReleaseTime.ToUniversalTime());
    }

    [Fact]
    public void ReleaseTime_OffsetsKeepUtcInstant()
    {
        // +08:00 与 +00:00 的同一时刻必须解析为同一 UTC 时间点。
        var utc = new McVersion("test", Parse("""{"id":"test","libraries":[],"releaseTime":"2021-04-01T00:00:00+00:00"}"""));
        var offset = new McVersion("test", Parse("""{"id":"test","libraries":[],"releaseTime":"2021-04-01T08:00:00+08:00"}"""));
        Assert.Equal(utc.ReleaseTime.ToUniversalTime(), offset.ReleaseTime.ToUniversalTime());
    }

    [Fact]
    public void ReleaseTime_NonIso_FallsBackToEpoch()
    {
        // 解析失败必须保留 1970 默认值，不能被 TryParse 的 out 参数覆盖为 MinValue。
        var version = new McVersion("test", Parse("""{"id":"test","libraries":[],"releaseTime":"not a date"}"""));
        Assert.Equal(new DateTime(1970, 1, 1, 15, 0, 0), version.ReleaseTime);
    }

    [Fact]
    public void ReleaseTime_Missing_FallsBackToEpoch()
    {
        var version = new McVersion("test", Parse("""{"id":"test","libraries":[]}"""));
        Assert.Equal(new DateTime(1970, 1, 1, 15, 0, 0), version.ReleaseTime);
    }

    [Fact]
    public void ReleaseTime_IsoWithoutOffset_TreatedAsLocalRoundtrip()
    {
        // 无时区后缀按 RoundtripKind 归为 Unspecified，行为与旧 JToken.ToObject<DateTime>() 一致。
        var version = new McVersion("test", Parse("""{"id":"test","libraries":[],"releaseTime":"2013-06-25T13:25:49"}"""));
        Assert.Equal(new DateTime(2013, 6, 25, 13, 25, 49, DateTimeKind.Unspecified), version.ReleaseTime);
    }

    [Fact]
    public void Type_Pending_IsExperimentalSnapshot()
    {
        var version = new McVersion("test", Parse("""{"type":"pending","libraries":[],"releaseTime":"2020-01-01T00:00:00+00:00"}"""));
        Assert.True(version.IsSnapshot);
        Assert.Equal("pending", version.VanillaName);
    }

    [Fact]
    public void Patches_GameVersion_Wins()
    {
        var version = new McVersion("custom-folder", Parse(
            """{"patches":[{"id":"loader"},{"id":"game","version":"1.12.2"}],"releaseTime":"2017-09-18T00:00:00+00:00"}"""));
        Assert.Equal("1.12.2", version.VanillaName);
        Assert.True(version.Reliable);
    }

    [Fact]
    public void OldReleaseTime_MarksOldVersion()
    {
        var version = new McVersion("beta-folder", Parse("""{"id":"b1.2","libraries":[],"releaseTime":"2010-12-23T00:00:00+00:00"}"""));
        Assert.Equal("Old", version.VanillaName);
    }
}
