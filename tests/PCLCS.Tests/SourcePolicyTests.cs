using PCLCS.Minecraft.Download;

namespace PCLCS.Tests;

public class SourcePolicyTests {
    [Theory]
    [InlineData(0, false, 30, 90)]
    [InlineData(1, true, 5, 35)]
    [InlineData(2, true, 60, 120)]
    [InlineData(9, true, 60, 120)]
    public void PlanMatchesLegacyTimeoutTable(int version, bool officialFirst, int first, int second) {
        var plan = SourcePolicy.GetPlan(version);
        Assert.Equal(officialFirst, plan.OfficialFirst);
        Assert.Equal(first, plan.FirstSeconds);
        Assert.Equal(second, plan.SecondSeconds);
    }

    [Theory]
    [InlineData(2, false, true)]
    [InlineData(1, true, true)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    public void VersionListPrefersOfficialOnExplicitSettingOrFastMirror(int version, bool downloadPrefer, bool expected) {
        Assert.Equal(expected, SourcePolicy.PreferMojangForVersionList(version, downloadPrefer));
    }
}
