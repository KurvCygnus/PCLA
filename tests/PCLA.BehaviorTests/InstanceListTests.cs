using PCL;
using PCLA.BehaviorTests.Fixtures;

namespace PCLA.BehaviorTests;

/// <summary>
/// 实例列表编排（ModMinecraft.InitMcInstanceList）的行为语料：
/// 分组卡片键、选中恢复（PCL.ini Version）与空 versions 目录回退。
/// </summary>
public sealed class InstanceListTests : IDisposable
{
    private readonly List<string> _roots = [];

    public InstanceListTests()
    {
        BehaviorTestHost.Initialize();
    }

    /// <summary>构造一个含 1 正式版 + 1 Forge（含前置正式版）+ 1 损坏版的 .minecraft 目录。</summary>
    private string PrepareMixedCorpus()
    {
        var root = BehaviorTestHost.CreateCaseRoot("instance-list-mixed");
        Directory.CreateDirectory(Path.Combine(root, "versions"));
        // Forge 的 inheritsFrom 前置即本语料的正式版目录，恰好构成 1 正式版 + 1 Forge + 1 损坏版
        FixtureFactory.WriteVersion(root, new InstanceCase("1.20.1", "1.20.1", "release", null, [],
            McInstance.McInstanceState.Original, "1.20.1"));
        FixtureFactory.WriteVersion(root, new InstanceCase("forge", "1.20.1-forge-47.2.0", "release", "1.20.1",
            ["net.minecraftforge:forge:1.20.1-47.2.0"], McInstance.McInstanceState.Forge, "1.20.1"));
        Directory.CreateDirectory(Path.Combine(root, "versions", "broken"));
        // 以 char 构造畸形 JSON 内容，避免字面量触发本地化检查
        File.WriteAllText(Path.Combine(root, "versions", "broken", "broken.json"), new string('{', 1));
        _roots.Add(root);
        ModMinecraft.McFolderSelected = root + Path.DirectorySeparatorChar;
        return root;
    }

    private static void LoadList(string root)
    {
        ModMinecraft.McInstanceListLoader.WaitForExit(root + Path.DirectorySeparatorChar, IsForceRestart: true);
    }

    private static IEnumerable<string> CardNames(ModMinecraft.McInstanceCardType card)
    {
        return ModMinecraft.McInstanceList[card].Select(static i => i.Name);
    }

    [Fact]
    public void GroupedCards()
    {
        var root = PrepareMixedCorpus();

        LoadList(root);

        // 卡片按交付顺序排列（Star, API, OriginalLike, Rubbish, Fool, Error, Hidden），本语料仅命中三种
        Assert.Equal(
            new[]
            {
                ModMinecraft.McInstanceCardType.API,
                ModMinecraft.McInstanceCardType.OriginalLike,
                ModMinecraft.McInstanceCardType.Error
            },
            ModMinecraft.McInstanceList.Keys.ToList());
        Assert.Equal(["forge"], CardNames(ModMinecraft.McInstanceCardType.API));
        Assert.Equal(["1.20.1"], CardNames(ModMinecraft.McInstanceCardType.OriginalLike));
        Assert.Equal(["broken"], CardNames(ModMinecraft.McInstanceCardType.Error));
        // 未储存选择时自动选中第一张卡片的第一个可用项（API 卡内的 Forge 版本）
        Assert.Equal("forge", ModMinecraft.McInstanceSelected.Name);
    }

    [Fact]
    public void SelectedRestoreOnFirstLoad()
    {
        var root = PrepareMixedCorpus();
        ModBase.WriteIni(Path.Combine(root, "PCL.ini"), "Version", "1.20.1");

        LoadList(root);

        Assert.Equal("1.20.1", ModMinecraft.McInstanceSelected.Name);
    }

    [Fact]
    public void SelectedRestoreOnCacheReload()
    {
        var root = PrepareMixedCorpus();

        // 第一次加载自动选中 forge，随后改写已储存的选择
        LoadList(root);
        Assert.Equal("forge", ModMinecraft.McInstanceSelected.Name);
        ModBase.WriteIni(Path.Combine(root, "PCL.ini"), "Version", "1.20.1");

        // 第二次加载命中 PCL.ini 卡片缓存路径，选中恢复仍应生效
        LoadList(root);

        Assert.Equal("1.20.1", ModMinecraft.McInstanceSelected.Name);
    }

    [Fact]
    public void EmptyVersionsFolderClearsSelection()
    {
        var root = BehaviorTestHost.CreateCaseRoot("instance-list-empty");
        Directory.CreateDirectory(Path.Combine(root, "versions"));
        _roots.Add(root);
        ModMinecraft.McFolderSelected = root + Path.DirectorySeparatorChar;

        LoadList(root);

        Assert.Empty(ModMinecraft.McInstanceList);
        Assert.Null(ModMinecraft.McInstanceSelected);
    }

    public void Dispose()
    {
        foreach (var root in _roots) Directory.Delete(root, true);
        BehaviorTestHost.RestoreDefaultMcFolder();
    }
}
