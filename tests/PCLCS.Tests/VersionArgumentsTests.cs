using Newtonsoft.Json.Linq;
using PCLCS.Minecraft.Launch;

namespace PCLCS.Tests;

public class VersionArgumentsTests
{
    private static readonly RuleEnvironment Windows10 = new("Microsoft Windows NT 10.0.19045.0");

    private static JObject Load(string name) => JObject.Parse(File.ReadAllText(TestPaths.Fixture(name)));

    [Fact]
    public void Jvm_ModernJson()
    {
        var version = Load("modern.json");
        var result = VersionArguments.CollectJvm(version, _ => null, Windows10);
        Assert.Equal(
            new[]
            {
                "-Djava.library.path=${natives_directory}",
                "\"-Dos.name=Windows 10\"",
                "-cp",
                "${classpath}"
            },
            result);
    }

    [Fact]
    public void Jvm_LegacyFallback()
    {
        var version = Load("legacy.json");
        var result = VersionArguments.CollectJvm(version, _ => null, Windows10);
        Assert.Equal(
            new[]
            {
                "-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump",
                "-Djava.library.path=${natives_directory}",
                "-cp",
                "${classpath}"
            },
            result);
    }

    [Fact]
    public void Jvm_WalksParentsInChildFirstOrder()
    {
        var version = Load("child.json");
        var result = VersionArguments.CollectJvm(version, name => name == "parent" ? Load("parent.json") : null, Windows10);
        Assert.Equal(new[] { "-Dchild=1", "-Dparent=1" }, result);
    }

    [Fact]
    public void Game_ModernJson()
    {
        var version = Load("modern.json");
        var result = VersionArguments.CollectGame(version, _ => null, Windows10);
        Assert.Equal(" --username ${auth_player_name} --width ${resolution_width}", result);
    }

    [Fact]
    public void Game_LegacyJson()
    {
        var version = Load("legacy.json");
        var result = VersionArguments.CollectGame(version, _ => null, Windows10);
        Assert.Equal(
            " --username ${auth_player_name} --version ${version_name} --height ${resolution_height} --width ${resolution_width}",
            result);
    }

    [Fact]
    public void Game_WalksParentsInChildFirstOrder()
    {
        var version = Load("child.json");
        var result = VersionArguments.CollectGame(version, name => name == "parent" ? Load("parent.json") : null, Windows10);
        Assert.Equal(" --child C --parent P", result);
    }

    [Fact]
    public void Game_QuotesValuesWithSpaces()
    {
        var version = JObject.Parse("{\"arguments\":{\"game\":[\"arg with space\"]}}");
        Assert.Equal(" \"arg with space\"", VersionArguments.CollectGame(version, _ => null, Windows10));
    }
}
