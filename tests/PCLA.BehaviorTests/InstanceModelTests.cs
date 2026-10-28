using PCL;
using PCLA.BehaviorTests.Fixtures;
using System;
using System.IO;

namespace PCLA.BehaviorTests;

public sealed class InstanceModelTests : IDisposable
{
    private string _root = "";

    public InstanceModelTests()
    {
        BehaviorTestHost.Initialize();
    }

    [Theory]
    [MemberData(nameof(InstanceCases.All), MemberType = typeof(InstanceCases))]
    public void MatchesCorpus(InstanceCase c)
    {
        _root = FixtureFactory.PrepareInstance(c);
        ModMinecraft.McFolderSelected = _root;

        var instance = new McInstance(c.Name);
        Assert.Equal(c.CheckExpected, instance.Check());

        var loaded = instance.Load();
        Assert.Equal(c.ExpectedState, loaded.State);
        Assert.Equal(c.Name, loaded.Name);

        if (c.ExpectedVanilla.Length > 0)
        {
            Assert.Equal(c.ExpectedVanilla, loaded.Version.VanillaName);
        }
        if (c.ExpectedModable is not null)
        {
            Assert.Equal(c.ExpectedModable.Value, loaded.Modable);
        }
        if (c.Isolated)
        {
            Assert.Equal(Path.Combine(_root, "versions", c.Name) + "\\", loaded.PathIndie);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        BehaviorTestHost.RestoreDefaultMcFolder();
    }
}