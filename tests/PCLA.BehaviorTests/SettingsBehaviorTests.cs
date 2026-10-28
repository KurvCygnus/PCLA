using PCL;
using PCLA.BehaviorTests.Fixtures;
using System;
using System.Collections.Generic;
using System.IO;

namespace PCLA.BehaviorTests;

public sealed class SettingsBehaviorTests : IDisposable
{
    private readonly List<string> _roots = [];

    public SettingsBehaviorTests()
    {
        BehaviorTestHost.Initialize();
    }

    private McInstance NewInstance(string name, out string root)
    {
        var c = new InstanceCase(name, "1.20.1", "release", null, [],
            McInstance.McInstanceState.Original, "1.20.1");
        root = FixtureFactory.PrepareInstance(c);
        _roots.Add(root);
        ModMinecraft.McFolderSelected = root;
        return new McInstance(name);
    }

    private static void ResetWindowWidth()
    {
        //* Reset 在缓存已是默认值时直接返回，不会删除已存储的键；先写入非默认值以确保清理落到文件。
        Settings.Set("WindowWidth", 1024);
        Settings.Reset("WindowWidth");
    }

    private static void ResetLaunchArgumentInfo()
    {
        Settings.Set("LaunchArgumentInfo", "");
        Settings.Reset("LaunchArgumentInfo");
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public void DefaultInt()
    {
        Assert.Equal(900, Settings.Get<int>("WindowWidth"));
    }

    [Fact]
    public void SetNormal()
    {
        try
        {
            Settings.Set("WindowWidth", 1024);
            Assert.Equal(1024, Settings.Get<int>("WindowWidth"));
        } finally
        {
            ResetWindowWidth();
        }
    }

    [Fact]
    public void ResetNormal()
    {
        try
        {
            Settings.Set("WindowWidth", 1024);
            Settings.Reset("WindowWidth");
            Assert.Equal(900, Settings.Get<int>("WindowWidth"));
            Assert.False(Settings.HasSaved("WindowWidth"));
        } finally
        {
            ResetWindowWidth();
        }
    }

    [Fact]
    public void InstanceIsolated()
    {
        var a = NewInstance("a", out var rootA);
        var b = NewInstance("b", out var rootB);

        try
        {
            Settings.Set("VersionArgumentIndieV2", true, a);
            Settings.Set("VersionArgumentIndieV2", false, b);

            Assert.True(Settings.Get<bool>("VersionArgumentIndieV2", a));
            Assert.False(Settings.Get<bool>("VersionArgumentIndieV2", b));
        } finally
        {
            DeleteRoot(rootA);
            DeleteRoot(rootB);
            BehaviorTestHost.RestoreDefaultMcFolder();
        }
    }

    [Fact]
    public void InstanceMissing()
    {
        //! 实例设置缺少目标实例时：读取在进入 try 前就解引用 Instance，抛出 NullReferenceException；
        //! 写入的异常被吞掉且不产生值，HasSaved 直接抛出“未提供目标版本”的通用 Exception。
        Assert.Throws<NullReferenceException>(() => Settings.Get<bool>("VersionArgumentIndieV2"));
        Settings.Set("VersionArgumentIndieV2", true);
        var missingInstance = Record.Exception(() => Settings.HasSaved("VersionArgumentIndieV2"));
        var ex = Assert.IsType<Exception>(missingInstance);
        Assert.Contains("未提供目标版本", ex.Message);
    }

    [Fact]
    public void HasSaved()
    {
        try
        {
            Assert.False(Settings.HasSaved("WindowWidth"));
            Settings.Set("WindowWidth", 1024);
            Assert.True(Settings.HasSaved("WindowWidth"));
            Settings.Reset("WindowWidth");
            Assert.False(Settings.HasSaved("WindowWidth"));
        } finally
        {
            ResetWindowWidth();
        }
    }

    [Fact]
    public void GetDefault()
    {
        Assert.Equal("900", Settings.GetDefault("WindowWidth"));
    }

    [Fact]
    public void UnknownKey()
    {
        Assert.Throws<KeyNotFoundException>(() => Settings.Get<int>("NoSuchKey"));
    }

    [Fact]
    public void InvalidType()
    {
        try
        {
            Settings.Set("WindowWidth", "not-a-number");
            Assert.Equal(900, Settings.Get<int>("WindowWidth"));
        } finally
        {
            ResetWindowWidth();
        }
    }

    [Fact]
    public void SetSafeEncrypted()
    {
        Assert.Throws<InvalidOperationException>(() => Settings.SetSafe("SystemCount", 1));
    }

    [Fact]
    public void StringRoundtrip()
    {
        var value = "line1\n\"quoted\"\\backslash";
        try
        {
            Settings.Set("LaunchArgumentInfo", value);
            Assert.Equal(value, Settings.Get<string>("LaunchArgumentInfo"));
        } finally
        {
            ResetLaunchArgumentInfo();
        }
    }

    public void Dispose()
    {
        foreach (var root in _roots) DeleteRoot(root);
        BehaviorTestHost.RestoreDefaultMcFolder();
    }
}