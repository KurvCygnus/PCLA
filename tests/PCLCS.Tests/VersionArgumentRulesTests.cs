using System.Text.Json.Nodes;
using PCLCS.Minecraft.Launch;

namespace PCLCS.Tests;

public class VersionArgumentRulesTests
{
    private static readonly RuleEnvironment Windows10 = new("Microsoft Windows NT 10.0.19045.0");

    [Fact]
    public void NullRulesAllow() => Assert.True(VersionArgumentRules.IsAllowed(null, Windows10));

    [Theory]
    [InlineData("windows", true)]
    [InlineData("osx", false)]
    [InlineData("unknown", true)]
    public void OsName(string name, bool expected)
    {
        var rules = JsonNode.Parse($"{{\"action\":\"allow\",\"os\":{{\"name\":\"{name}\"}}}}");
        Assert.Equal(expected, VersionArgumentRules.IsAllowed(rules, Windows10));
    }

    [Fact]
    public void OsVersionMatchesRegex()
    {
        var rules = JsonNode.Parse("{\"action\":\"allow\",\"os\":{\"name\":\"windows\",\"version\":\"^10\\\\.\"}}");
        Assert.True(VersionArgumentRules.IsAllowed(rules, Windows10));
        Assert.False(VersionArgumentRules.IsAllowed(rules, new RuleEnvironment("Microsoft Windows NT 6.1.7601.0")));
    }

    [Fact]
    public void InvalidRegexWarnsAndDenies()
    {
        var warnings = new List<string>();
        var rules = JsonNode.Parse("{\"action\":\"allow\",\"os\":{\"name\":\"windows\",\"version\":\"[\"}}");
        Assert.False(VersionArgumentRules.IsAllowed(rules, Windows10, warnings.Add));
        Assert.Contains("规则正则无效：[", warnings[0]);
    }

    [Theory]
    [InlineData("x86", false)]
    [InlineData("arm64", false)]
    [InlineData("x86_64", true)]
    public void OsArch(string arch, bool expected)
    {
        var rules = JsonNode.Parse($"{{\"action\":\"allow\",\"os\":{{\"arch\":\"{arch}\"}}}}");
        Assert.Equal(expected, VersionArgumentRules.IsAllowed(rules, Windows10));
    }

    [Theory]
    [InlineData("{\"action\":\"allow\"}", true)]
    [InlineData("{\"action\":\"disallow\"}", false)]
    [InlineData("{\"action\":\"allow\",\"features\":{}}", true)]
    [InlineData("{\"action\":\"allow\",\"features\":{\"is_demo_user\":true}}", false)]
    [InlineData("{\"action\":\"allow\",\"features\":{\"has_custom_resolution\":true}}", true)]
    [InlineData("{\"action\":\"allow\",\"features\":{\"quick_play_singleplayer\":true}}", false)]
    public void FeaturesAndAction(string json, bool expected)
    {
        Assert.Equal(expected, VersionArgumentRules.IsAllowed(JsonNode.Parse(json), Windows10));
    }

    [Fact]
    public void DisallowOverridesEarlierAllow()
    {
        var rules = JsonNode.Parse(
            "[{\"action\":\"allow\",\"os\":{\"name\":\"windows\"}},{\"action\":\"disallow\",\"os\":{\"name\":\"windows\"}}]");
        Assert.False(VersionArgumentRules.IsAllowed(rules, Windows10));
    }
}
