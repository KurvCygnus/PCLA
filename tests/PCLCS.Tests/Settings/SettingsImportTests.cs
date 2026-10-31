using PCLCS.Settings;

namespace PCLCS.Tests.Settings;

//* 服务是静态单例，与 SettingsServiceTests 共享状态：必须放进同一测试集合串行执行
[Collection("PCLCS.Settings")]
public class SettingsImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pclcs.imp." + Guid.NewGuid().ToString("N"));

    public SettingsImportTests()
    {
        Directory.CreateDirectory(_root);
        SettingsService.Configure(_root);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void Setup_ini_values_are_imported_once()
    {
        // 旧 Normal 源文件：%APPDATA%\PCLA\Setup.ini（ModBase.WriteIni 简写规则）
        File.WriteAllText(Path.Combine(_root, "Setup.ini"), "WindowWidth:1234\r\nToolDownloadThread:31\r\n");
        SettingsImport.EnsureImported();
        Assert.Equal(1234, SettingsService.Get<int>("WindowWidth"));
        // 已导入标记后，外部改动不再进
        File.WriteAllText(Path.Combine(_root, "Setup.ini"), "WindowWidth:555\r\n");
        SettingsImport.EnsureImported();
        Assert.Equal(1234, SettingsService.Get<int>("WindowWidth"));
    }

    [Fact]
    public void Existing_new_value_wins_over_import()
    {
        SettingsService.Set("WindowWidth", 777);
        File.WriteAllText(Path.Combine(_root, "Setup.ini"), "WindowWidth:1234\r\n");
        SettingsImport.EnsureImported();
        Assert.Equal(777, SettingsService.Get<int>("WindowWidth"));   // 只补缺失
    }

    [Fact]
    public void Instance_ini_imported_lazily()
    {
        var inst = Path.Combine(_root, "versions", "t\\");
        Directory.CreateDirectory(Path.Combine(inst, "PCL"));
        File.WriteAllText(Path.Combine(inst, "PCL", "Setup.ini"), "VersionRamType:1\r\nVersionArgumentTitle:自定义\r\n");
        SettingsImport.EnsureInstanceImported(inst);
        Assert.Equal(1, SettingsService.Get<int>("VersionRamType", inst));
        Assert.Equal("自定义", SettingsService.Get<string>("VersionArgumentTitle", inst));
        Assert.True(File.Exists(Path.Combine(inst, ".pcla", "Settings.json")));   // 标记在新文件里
    }
}