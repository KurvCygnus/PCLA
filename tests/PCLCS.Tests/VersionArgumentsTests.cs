using System.Text.Json.Nodes;
using PCLCS.Minecraft.Launch;

namespace PCLCS.Tests;

public class VersionArgumentsTests
{
    private static readonly RuleEnvironment Windows10 = new("Microsoft Windows NT 10.0.19045.0");

    private static JsonObject Load(string name) => JsonNode.Parse(File.ReadAllText(TestPaths.Fixture(name)))!.AsObject();

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
    public void Jvm_WalksParentsSelectedVersionFirst()
    {
        var version = Load("child.json");
        var result = VersionArguments.CollectJvm(version, name => name == "parent" ? Load("parent.json") : null, Windows10);
        // VB 先追加选中版本的 jvm，再沿 InheritName 追加父级，因此子级参数在前。
        Assert.Equal(new[] { "-Dchild=1", "-Dparent=1" }, result);
    }

    [Fact]
    public void Jvm_UsesParentSectionWhenChildHasNone()
    {
        var version = JsonNode.Parse("{\"id\":\"child\",\"inheritsFrom\":\"parent\",\"arguments\":{\"game\":[\"--child\"]}}")!.AsObject();
        var result = VersionArguments.CollectJvm(version, name => name == "parent" ? Load("parent.json") : null, Windows10);
        Assert.Equal(new[] { "-Dparent=1" }, result);
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
    public void Game_WalksParentsSelectedVersionFirst()
    {
        var version = Load("child.json");
        var result = VersionArguments.CollectGame(version, name => name == "parent" ? Load("parent.json") : null, Windows10);
        // VB 先追加选中版本的 game，再沿 InheritName 追加父级，因此子级参数在前。
        Assert.Equal(" --child C --parent P", result);
    }

    [Fact]
    public void Game_UsesParentSectionWhenChildHasNone()
    {
        var version = JsonNode.Parse("{\"id\":\"child\",\"inheritsFrom\":\"parent\",\"arguments\":{\"jvm\":[\"-Dchild=1\"]}}")!.AsObject();
        var result = VersionArguments.CollectGame(version, name => name == "parent" ? Load("parent.json") : null, Windows10);
        Assert.Equal(" --parent P", result);
    }

    [Fact]
    public void Game_LegacyFromParentPrecedesModernChild()
    {
        var parent = JsonNode.Parse("{\"minecraftArguments\":\"--legacy\",\"arguments\":{\"game\":[\"--parent\"]}}")!.AsObject();
        var child = JsonNode.Parse("{\"inheritsFrom\":\"parent\",\"arguments\":{\"game\":[\"--child\"]}}")!.AsObject();
        var result = VersionArguments.CollectGame(child, name => name == "parent" ? parent : null, Windows10);
        // minecraftArguments 仍最先输出；arguments.game 按"选中版本 → 父级"追加，子级 --child 在父级 --parent 之前。
        Assert.Equal(" --legacy --height ${resolution_height} --width ${resolution_width} --child --parent", result);
    }

    [Fact]
    public void Game_QuotesValuesWithSpaces()
    {
        var version = JsonNode.Parse("{\"arguments\":{\"game\":[\"arg with space\"]}}")!.AsObject();
        Assert.Equal(" \"arg with space\"", VersionArguments.CollectGame(version, _ => null, Windows10));
    }

    [Fact]
    public void InheritName_OverridesJsonInheritsFrom()
    {
        var version = JsonNode.Parse("{\"id\":\"child\",\"inheritsFrom\":\"wrong\"}")!.AsObject();
        var parents = new Dictionary<string, JsonObject>
        {
            ["parent"] = Load("parent.json"),
            ["wrong"] = JsonNode.Parse("{\"arguments\":{\"jvm\":[\"-Dwrong=1\"],\"game\":[\"--wrong\"]}}")!.AsObject()
        };
        JsonObject? Resolve(string name) => parents.TryGetValue(name, out var parent) ? parent : null;

        Assert.Equal(
            new[] { "-Dparent=1" },
            VersionArguments.CollectJvm(version, Resolve, Windows10, inheritName: "parent"));
        Assert.Equal(" --parent P", VersionArguments.CollectGame(version, Resolve, Windows10, inheritName: "parent"));
    }

    [Fact]
    public void InheritName_EmptySuppressesJsonInheritsFrom()
    {
        var version = JsonNode.Parse("{\"id\":\"hmcl\",\"inheritsFrom\":\"parent\"}")!.AsObject();
        JsonObject Resolve(string name) => throw new InvalidOperationException($"不应解析父级 {name}");

        Assert.Equal(
            new[]
            {
                "-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump",
                "-Djava.library.path=${natives_directory}",
                "-cp",
                "${classpath}"
            },
            VersionArguments.CollectJvm(version, Resolve, Windows10, inheritName: ""));
        Assert.Equal("", VersionArguments.CollectGame(version, Resolve, Windows10, inheritName: ""));
    }

    [Fact]
    public void InheritName_NullKeepsJsonInheritsFrom()
    {
        var version = Load("child.json");
        JsonObject? Resolve(string name) => name == "parent" ? Load("parent.json") : null;

        // null 沿用 JSON 的 inheritsFrom，顺序同样是选中版本 → 父级。
        Assert.Equal(
            new[] { "-Dchild=1", "-Dparent=1" },
            VersionArguments.CollectJvm(version, Resolve, Windows10, inheritName: null));
        Assert.Equal(" --child C --parent P", VersionArguments.CollectGame(version, Resolve, Windows10, inheritName: null));
    }
}
