using PCLCS.Settings;
using System.Text;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Settings;

public class SettingsStoreTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "pclcs.store." + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_root, "Settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void Load_missing_file_returns_empty_document()
    {
        var doc = new SettingsStore(FilePath).Load();
        Assert.Empty(doc.Values);
        Assert.Empty(doc.Encrypted);
        Assert.False(doc.RegistryImported);
    }

    [Fact]
    public void Save_then_Load_roundtrips_sections()
    {
        var store = new SettingsStore(FilePath);
        var doc = store.Load();
        doc.Values["WindowWidth"] = 1024;
        doc.Values["UiLogoText"] = "中文";
        doc.Encrypted["CacheMsAccess"] =
            new JsonObject { ["$enc"] = "v1", ["data"] = "QUJD" };
        doc.RegistryImported = true;
        doc.Extra = new JsonObject { ["UnknownFutureKey"] = "keep" };
        store.Save(doc);

        var loaded = new SettingsStore(FilePath).Load();
        Assert.Equal(1024, loaded.Values["WindowWidth"]!.GetValue<int>());
        Assert.Equal("中文", loaded.Values["UiLogoText"]!.GetValue<string>());
        Assert.Equal("QUJD", loaded.Encrypted["CacheMsAccess"]!["data"]!.GetValue<string>());
        Assert.True(loaded.RegistryImported);
        Assert.Equal("keep", loaded.Extra!["UnknownFutureKey"]!.GetValue<string>());
    }

    [Fact]
    public void Save_is_atomic_and_keeps_backup()
    {
        var store = new SettingsStore(FilePath);
        var doc = store.Load();
        doc.Values["WindowWidth"] = 900;
        store.Save(doc);
        doc.Values["WindowWidth"] = 1024;
        store.Save(doc);

        Assert.True(File.Exists(SettingsStore.BackupPathFor(FilePath)));
        var bak = JsonNode.Parse(File.ReadAllText(SettingsStore.BackupPathFor(FilePath)))!;
        Assert.Equal(900, bak["values"]!["WindowWidth"]!.GetValue<int>());  // 备份是上一版
        var cur = JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Equal(1024, cur["values"]!["WindowWidth"]!.GetValue<int>());
    }

    [Fact]
    public void Save_uses_two_space_indented_utf8_no_bom()
    {
        var store = new SettingsStore(FilePath);
        var doc = store.Load();
        doc.Values["K"] = 1;
        store.Save(doc);
        var bytes = File.ReadAllBytes(FilePath);
        Assert.NotEqual(0xEF, bytes[0]);                       // 无 BOM
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\n  \"values\"", text);               // 缩进排版稳定
        Assert.Contains("\"schemaVersion\": 1", text);
    }

    [Fact]
    public void Load_tolerates_corrupted_json()
    {
        File.WriteAllText(FilePath, "{ broken");
        var doc = new SettingsStore(FilePath).Load();
        Assert.Empty(doc.Values);   // 损坏 → 空文档 + 备份仍可用（人工恢复路径），不抛出
    }
}