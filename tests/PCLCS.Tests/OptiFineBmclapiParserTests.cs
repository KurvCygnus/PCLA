using PCLCS.Minecraft.Download;

namespace PCLCS.Tests;

public class OptiFineBmclapiParserTests
{
    [Fact]
    public void ParseBmclapiJsonMapsFields()
    {
        const string json = """
        [
            {"mcversion":"1.12.2","type":"HD_U","patch":"G5","filename":"OptiFine_1.12.2_HD_U_G5.jar","forge":"Forge #2859"}
        ]
        """;
        var entry = Assert.Single(OptiFineListParser.ParseBmclapiJson(json));
        Assert.Equal("1.12.2 G5", entry.DisplayName);
        Assert.Equal("OptiFine_1.12.2_HD_U_G5.jar", entry.FileName);
        Assert.Equal("1.12.2-OptiFine_HD_U_G5", entry.InstanceName);
        Assert.False(entry.IsPreview);
        Assert.Equal("1.12.2", entry.Inherit);
        Assert.Equal("", entry.ReleaseTime);
        Assert.Equal("2859", entry.RequiredForgeVersion);
    }

    [Fact]
    public void ParseBmclapiJsonNormalizesInheritAndInstanceName()
    {
        const string json = """
        [
            {"mcversion":"1.9.0","type":"HD_U","patch":"E7","filename":"OptiFine_1.9.0_HD_U_E7.jar","forge":"Forge 11.15.1.2318"},
            {"mcversion":"1.12.2","type":"HD_U","patch":"G5","filename":"OptiFine_1.12.2_HD_U_G5.jar","forge":"Forge #2859"}
        ]
        """;
        var entries = OptiFineListParser.ParseBmclapiJson(json);
        Assert.Equal(2, entries.Count);
        Assert.Equal("1.9 E7", entries[0].DisplayName);
        Assert.Equal("1.9", entries[0].Inherit);
        Assert.Equal("1.9-OptiFine_HD_U_E7", entries[0].InstanceName);
        Assert.Equal("11.15.1.2318", entries[0].RequiredForgeVersion);
        Assert.Equal("1.12.2", entries[1].Inherit);
        Assert.Equal("1.12.2-OptiFine_HD_U_G5", entries[1].InstanceName);
    }

    [Fact]
    public void ParseBmclapiJsonHandlesForgeCompatibility()
    {
        const string json = """
        [
            {"mcversion":"1.12.2","type":"HD_U","patch":"G5","filename":"a.jar","forge":"N/A"},
            {"mcversion":"1.12.2","type":"HD_U","patch":"G5","filename":"b.jar","forge":null},
            {"mcversion":"1.12.2","type":"HD_U","patch":"G5","filename":"c.jar"}
        ]
        """;
        var entries = OptiFineListParser.ParseBmclapiJson(json);
        Assert.Null(entries[0].RequiredForgeVersion);
        Assert.Equal("", entries[1].RequiredForgeVersion);
        Assert.Equal("", entries[2].RequiredForgeVersion);
    }
}