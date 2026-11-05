using PCLCS.Settings;

namespace PCLCS.Tests.Settings;

public class SettingsCatalogTests
{
    [Fact]
    public void Entry_count_is_182() => Assert.Equal(182, SettingsCatalog.Entries.Count);

    // 代表性钉子：默认值/Source/Encrypted/Type 与 Settings.vb:5-188 一致（完整一致性由
    // “VB 目录 vs C# 目录”对拍测试兜底）。
    [Theory]
    [InlineData("WindowWidth", 900, SettingsSource.Normal, false)]
    [InlineData("Identify", "", SettingsSource.Registry, false)]
    [InlineData("AprilYear", 0, SettingsSource.Registry, true)]
    [InlineData("CacheMsV2Expires", 0L, SettingsSource.Registry, true)]
    [InlineData("LoginMsJson", "{}", SettingsSource.Registry, true)]
    [InlineData("LaunchArgumentInfo", "PCLA", SettingsSource.Normal, false)]
    [InlineData("VersionArgumentIndieV2", false, SettingsSource.Instance, false)]
    [InlineData("VersionArgumentJavaSelect", "使用全局设置", SettingsSource.Instance, false)]
    [InlineData("UiLauncherTransparent", 600, SettingsSource.Normal, false)]
    [InlineData("SystemSystemTelemetry", false, SettingsSource.Registry, false)]
    public void Representative_entries_match_legacy(string key, object defaultValue, SettingsSource source, bool encrypted)
    {
        var entry = SettingsCatalog.Get(key);
        Assert.Equal(defaultValue, entry.DefaultValue);
        Assert.Equal(source, entry.Source);
        Assert.Equal(encrypted, entry.Encrypted);
    }
}