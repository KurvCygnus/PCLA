using PCLCS.Json;
using PCLCS.Minecraft.Launch;
using System.Text.Json.Nodes;

namespace PCLCS.Tests;

public class MergedJsonTests
{
    private static readonly RuleEnvironment Windows10 = new("Microsoft Windows NT 10.0.19045.0");

    private static JsonObject Load(string name) => JsonNodeUtils.ParseObject(File.ReadAllText(TestPaths.Fixture(name)));

    private static JsonObject? ResolveParent(string name) => name == "merged-parent" ? Load("merged-parent.json") : null;

    [Fact]
    public void Jvm_MergedJsonThenParent_KeepsFirstOccurrence()
    {
        var version = Load("merged-child.json");
        var result = ArgumentLine.Deduplicate(
            VersionArguments.CollectJvm(version, ResolveParent, Windows10),
            true);
        // VB 先输出选中版本合并 JSON 的 jvm（内部祖先→子），再沿 InheritName 追加父级；
        // JVM 去重保留首次出现，因此父级追加的重复项被丢弃，子级的 -Dmerged.shared=child 保留。
        Assert.Equal(
            new[]
            {
                "-Dmerged.parent=1",
                "-Dmerged.shared=parent",
                "-Dmerged.child=1",
                "-Dmerged.shared=child"
            },
            result);
    }

    [Fact]
    public void Game_MergedJsonThenParent_ParentValueWins()
    {
        var version = Load("merged-child.json");
        var result = ArgumentLine.Deduplicate(
            ArgumentLine.Split(VersionArguments.CollectGame(version, ResolveParent, Windows10)),
            false);
        // VB 先输出合并 JSON 的 game（内部祖先→子），再追加父级；游戏参数去重时后者覆盖前者。
        // 合并 JSON 内子级把 --username/--width 改为 ChildUser/1200，最后父级参数再次追加，
        // 因此最终值回到父级的 ParentUser/800。
        Assert.Equal(
            new[] { "--username", "ParentUser", "--width", "800", "--childFlag", "C" },
            result);
    }
}
