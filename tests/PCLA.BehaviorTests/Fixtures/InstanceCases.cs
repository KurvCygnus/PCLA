using PCL;

namespace PCLA.BehaviorTests.Fixtures;

public sealed class InstanceCase
{
    public string Name { get; }
    public string Id { get; }
    public string Type { get; }
    public string? InheritsFrom { get; }
    public string[] Libraries { get; }
    public McInstance.McInstanceState ExpectedState { get; }
    public bool CheckExpected { get; }
    public bool Isolated { get; }
    public bool MissingJson { get; }
    public bool BrokenJson { get; }
    public bool? ExpectedModable { get; }
    public string ExpectedVanilla { get; }

    public InstanceCase(
        string name,
        string id,
        string type,
        string? inheritsFrom,
        string[] libraries,
        McInstance.McInstanceState expectedState,
        string expectedVanilla,
        bool checkExpected = true,
        bool isolated = false,
        bool missingJson = false,
        bool brokenJson = false,
        bool? expectedModable = null)
    {
        Name = name;
        Id = id;
        Type = type;
        InheritsFrom = inheritsFrom;
        Libraries = libraries;
        ExpectedState = expectedState;
        ExpectedVanilla = expectedVanilla;
        CheckExpected = checkExpected;
        Isolated = isolated;
        MissingJson = missingJson;
        BrokenJson = brokenJson;
        ExpectedModable = expectedModable;
    }
}

internal static class InstanceCases
{
    public static TheoryData<InstanceCase> All { get; } = new()
    {
        new("vanilla", "1.20.1", "release", null, [], McInstance.McInstanceState.Original, "1.20.1", expectedModable: false),
        new("forge", "1.20.1-forge-47.2.0", "release", "1.20.1",
            ["net.minecraftforge:forge:1.20.1-47.2.0"], McInstance.McInstanceState.Forge, "1.20.1", expectedModable: true),
        new("neoforge", "1.20.1-neoforge-20.4.237", "release", "1.20.1",
            ["net.neoforged:neoforge:20.4.237"], McInstance.McInstanceState.NeoForge, "1.20.1", expectedModable: true),
        new("fabric", "1.20.1-fabric-0.15.0", "release", "1.20.1",
            ["net.fabricmc:fabric-loader:0.15.0"], McInstance.McInstanceState.Fabric, "1.20.1", expectedModable: true),
        new("optifine", "1.20.1-OptiFine_HD_U_I6", "release", "1.20.1",
            ["optifine:OptiFine:1.20.1_HD_U_I6"], McInstance.McInstanceState.OptiFine, "1.20.1"),
        new("liteloader", "1.12.2-LiteLoader", "release", "1.12.2",
            ["com.mumfrey:liteloader:1.12.2"], McInstance.McInstanceState.LiteLoader, "1.12.2"),
        new("missing-json", "missing-json", "release", null, [], McInstance.McInstanceState.Error, "",
            checkExpected: false, missingJson: true),
        new("broken-json", "broken-json", "release", null, [], McInstance.McInstanceState.Error, "",
            checkExpected: false, brokenJson: true),
        new("isolated", "1.20.1", "release", null, [], McInstance.McInstanceState.Original, "1.20.1", isolated: true),
        //* 目录名与 JSON id 故意不同：Name 取目录名，版本号取 JSON。
        new("name-mismatch", "1.20.1", "release", null, [], McInstance.McInstanceState.Original, "1.20.1")
    };
}
