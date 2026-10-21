using PCLCS.Minecraft.Launch;

namespace PCLCS.Tests;

public class ArgumentLineTests
{
    [Fact]
    public void Split_NullOrBlank_ReturnsEmpty()
    {
        Assert.Empty(ArgumentLine.Split(null));
        Assert.Empty(ArgumentLine.Split("   "));
    }

    [Fact]
    public void Split_SimpleLine()
    {
        Assert.Equal(
            new[] { "--username", "Steve", "--width", "800" },
            ArgumentLine.Split("--username Steve --width 800"));
    }

    [Fact]
    public void Split_KeepsQuotesAndInnerSpaces()
    {
        Assert.Equal(
            new[] { "\"-Dos.name=Windows 10\"", "-cp", "a" },
            ArgumentLine.Split("\"-Dos.name=Windows 10\" -cp a"));
    }

    [Fact]
    public void Split_MergesLineEndings()
    {
        Assert.Equal(new[] { "a", "b", "c" }, ArgumentLine.Split("a\r\nb\n\nc"));
    }

    [Fact]
    public void Split_EscapedQuotesStayInOneToken()
    {
        var result = ArgumentLine.Split("\"a \\\"b\\\" c\"");
        Assert.Single(result);
        Assert.Equal("\"a \\\"b\\\" c\"", result[0]);
    }

    [Fact]
    public void Deduplicate_SingleParameter()
    {
        var diagnostics = new List<string>();
        var result = ArgumentLine.Deduplicate(new[] { "-Xmx2G", "-Xmx2G" }, true, diagnostics);
        Assert.Equal(new[] { "-Xmx2G" }, result);
        Assert.Equal(new[] { "已去重单个 JVM 参数：-Xmx2G" }, diagnostics);
    }

    [Fact]
    public void Deduplicate_GamePairOverridesEarlierValue()
    {
        var diagnostics = new List<string>();
        var result = ArgumentLine.Deduplicate(new[] { "--width", "800", "--width", "1200" }, false, diagnostics);
        Assert.Equal(new[] { "--width", "1200" }, result);
        Assert.Equal(new[] { "已覆盖重复的游戏参数：--width 800 → --width 1200" }, diagnostics);
    }

    [Fact]
    public void Deduplicate_JvmPairKeepsDifferentValues()
    {
        var diagnostics = new List<string>();
        var result = ArgumentLine.Deduplicate(new[] { "--width", "800", "--width", "1200" }, true, diagnostics);
        Assert.Equal(new[] { "--width", "800", "--width", "1200" }, result);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Deduplicate_JvmPairDropsExactDuplicate()
    {
        var diagnostics = new List<string>();
        var result = ArgumentLine.Deduplicate(new[] { "--width", "800", "--width", "800" }, true, diagnostics);
        Assert.Equal(new[] { "--width", "800" }, result);
        Assert.Equal(new[] { "已去重 JVM 参数对：--width 800" }, diagnostics);
    }

    [Fact]
    public void Deduplicate_NegativeValueIsNotTreatedAsKey()
    {
        Assert.Equal(
            new[] { "-xPos", "-50" },
            ArgumentLine.Deduplicate(new[] { "-xPos", "23", "-xPos", "-50" }, false));
        Assert.Equal(
            new[] { "-xPos", "23", "-xPos", "-50" },
            ArgumentLine.Deduplicate(new[] { "-xPos", "23", "-xPos", "-50" }, true));
    }

    [Fact]
    public void Deduplicate_TweakClassIsNotOverridden()
    {
        Assert.Equal(
            new[] { "--tweakClass", "a", "--tweakClass", "b" },
            ArgumentLine.Deduplicate(new[] { "--tweakClass", "a", "--tweakClass", "b" }, false));
    }

    [Fact]
    public void Deduplicate_RepeatedSingleValue()
    {
        var diagnostics = new List<string>();
        var result = ArgumentLine.Deduplicate(new[] { "a", "b", "a" }, false, diagnostics);
        Assert.Equal(new[] { "a", "b" }, result);
        Assert.Equal(new[] { "已去重单个游戏参数：a" }, diagnostics);
    }
}
