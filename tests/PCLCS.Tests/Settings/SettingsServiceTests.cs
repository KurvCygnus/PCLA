using PCLCS.Settings;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Settings;

//* 服务是静态单例，与 SettingsImportTests 共享状态：必须放进同一测试集合串行执行
[Collection("PCLCS.Settings")]
public class SettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pclcs.svc." + Guid.NewGuid().ToString("N"));
    private string InstanceKey => Path.Combine(_root, "versions", "t\\");

    public SettingsServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "versions", "t"));
        SettingsService.Configure(_root);   // 全局 Settings.json 落在临时目录
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void Get_returns_default_when_unset()
        => Assert.Equal(900, SettingsService.Get<int>("WindowWidth"));

    [Fact]
    public void Set_then_Get_persists_across_reconfigure()
    {
        SettingsService.Set("WindowWidth", 1024);
        SettingsService.Configure(_root);   // 模拟重启：重载磁盘
        Assert.Equal(1024, SettingsService.Get<int>("WindowWidth"));
        Assert.True(SettingsService.HasSaved("WindowWidth"));
    }

    [Fact]
    public void Set_same_value_skips_write_and_event()
    {
        var fired = 0;
        SettingsService.SettingChanged += (_, _, _) => fired++;
        SettingsService.Set("WindowWidth", 1024);
        SettingsService.Set("WindowWidth", 1024);
        Assert.Equal(1, fired);             // 与 VB Set 的“值相同直接 Return”一致
    }

    [Fact]
    public void Set_coerces_string_to_entry_type()
    {
        SettingsService.Set("WindowWidth", "1024");     // CTypeDynamic 语义
        Assert.Equal(1024, SettingsService.Get<int>("WindowWidth"));
        SettingsService.Set("LaunchArgumentIndie", "2");
        Assert.Equal(2, SettingsService.Get<int>("LaunchArgumentIndie"));
    }

    [Fact]
    public void Reset_restores_default_and_removes_persisted_key()
    {
        SettingsService.Set("WindowWidth", 1024);
        SettingsService.Reset("WindowWidth");
        Assert.Equal(900, SettingsService.Get<int>("WindowWidth"));
        Assert.False(SettingsService.HasSaved("WindowWidth"));
    }

    [Fact]
    public void Instance_settings_are_isolated_by_key()
    {
        var other = Path.Combine(_root, "versions", "u\\");
        Directory.CreateDirectory(other);
        SettingsService.Set("VersionArgumentIndieV2", true, InstanceKey);
        SettingsService.Set("VersionArgumentIndieV2", false, other);
        Assert.True(SettingsService.Get<bool>("VersionArgumentIndieV2", InstanceKey));
        Assert.False(SettingsService.Get<bool>("VersionArgumentIndieV2", other));
    }

    [Fact]
    public void Instance_file_lives_under_pcla_folder()
    {
        SettingsService.Set("VersionRamType", 1, InstanceKey);
        Assert.True(File.Exists(Path.Combine(InstanceKey, ".pcla", "Settings.json")));
    }

    [Fact]
    public void Forced_keys_always_return_true()
    {
        Assert.True(SettingsService.Get<bool>("UiHiddenPageLink"));
        Assert.True(SettingsService.Get<bool>("UiHiddenSetupLink"));
        Assert.True(SettingsService.HasSaved("UiHiddenPageLink"));
    }

    [Fact]
    public void Encrypted_value_stored_as_v1_marker_and_roundtrips()
    {
        SettingsService.Set("CacheMsAccess", "token 中文字符");
        var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "Settings.json")))!;
        var node = raw["encrypted"]!["CacheMsAccess"]!;
        Assert.Equal("v1", node["$enc"]!.GetValue<string>());
        var plain = node["data"]!.GetValue<string>();
        Assert.NotEqual("token 中文字符", plain);
        Assert.Equal("token 中文字符", SettingsService.Get<string>("CacheMsAccess"));
    }

    [Fact]
    public void SetSafe_and_GetSafe_reject_encrypted_keys()
    {
        Assert.Throws<InvalidOperationException>(static () => SettingsService.SetSafe("CacheMsAccess", "x"));
        Assert.Throws<InvalidOperationException>(static () => SettingsService.GetSafe("CacheMsAccess"));
    }

    [Fact]
    public void Unknown_key_throws_key_not_found()
        => Assert.Throws<KeyNotFoundException>(static () => SettingsService.Get("NoSuchKey"));

    [Fact]
    public void ResetInstanceCache_clears_only_that_instance()
    {
        SettingsService.Set("VersionRamType", 1, InstanceKey);
        SettingsService.ResetInstanceCache(InstanceKey);
        Assert.Equal(2, SettingsService.Get<int>("VersionRamType", InstanceKey));   // 默认值回落
    }

    [Fact]
    public void GetDefault_returns_raw_default()
        => Assert.Equal(42, SettingsService.GetDefault("ToolDownloadSpeed"));
}