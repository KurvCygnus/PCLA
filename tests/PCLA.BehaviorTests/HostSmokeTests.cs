using System;
using System.IO;
using PCL;

namespace PCLA.BehaviorTests;

public sealed class HostSmokeTests : IDisposable
{
    private readonly string _root;

    public HostSmokeTests()
    {
        BehaviorTestHost.Initialize();
        _root = BehaviorTestHost.CreateCaseRoot("host-smoke");
    }

    [Fact]
    public void CanLoadVanillaInstance()
    {
        WriteVanilla(_root, "1.20.1");

        ModMinecraft.McFolderSelected = _root;
        var instance = new McInstance("1.20.1");

        Assert.True(instance.Check());
        var loaded = instance.Load();

        Assert.Equal(McInstance.McInstanceState.Original, loaded.State);
        Assert.Equal("1.20.1", loaded.Version.VanillaName);
    }

    [Fact]
    public void HostDefaultFolderExists()
    {
        Assert.Equal(BehaviorTestHost.DefaultMcFolder, ModMinecraft.McFolderSelected);
        Assert.True(Directory.Exists(BehaviorTestHost.DefaultMcFolder));
    }

    [Fact]
    public void RestoredDefaultFolderStaysUsableAfterCaseRemoval()
    {
        var caseRoot = BehaviorTestHost.CreateCaseRoot("restore-default");
        ModMinecraft.McFolderSelected = caseRoot;
        Directory.Delete(caseRoot, true);

        BehaviorTestHost.RestoreDefaultMcFolder();

        Assert.Equal(BehaviorTestHost.DefaultMcFolder, ModMinecraft.McFolderSelected);
        Assert.True(Directory.Exists(ModMinecraft.McFolderSelected));
    }

    private static void WriteVanilla(string root, string name)
    {
        var dir = Path.Combine(root, "versions", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".json"), """
        {
          "id": "1.20.1",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "releaseTime": "2023-06-12T10:00:00+00:00",
          "time": "2023-06-12T10:00:00+00:00",
          "assets": "5",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        BehaviorTestHost.RestoreDefaultMcFolder();
    }
}
