using PCLCS.Settings;

namespace PCLCS.Tests.Settings;

/// <summary>
/// 目录对拍：PCLCS.Settings.SettingsCatalog 必须与 VB Settings.vb:5-188 的机器可读快照逐行一致。
/// 快照由重写前的 Settings.vb 一次性机械生成（生成脚本见 task-3 报告），随仓库提交，
/// 是 182 键迁移的唯一权威对账物。
/// </summary>
public class SettingsCatalogParityTests
{
    public static IEnumerable<object[]> SnapshotRows() =>
        File.ReadAllLines(TestPaths.SettingsSnapshot) // tests\PCLCS.Tests\Settings\snapshot.txt，随仓库提交
            .Where(static l => l.Length > 0 && !l.StartsWith("#"))
            .Select(static l => new object[] { l });

    [Theory]
    [MemberData(nameof(SnapshotRows))]
    public void Catalog_matches_legacy_snapshot(string row)
    {
        var parts = row.Split('|'); // key|default|source|encrypted|type
        var entry = SettingsCatalog.Get(parts[0]);
        Assert.Equal(parts[1], entry.DefaultValue.ToString());
        Assert.Equal(parts[2], entry.Source.ToString());
        Assert.Equal(bool.Parse(parts[3]), entry.Encrypted);
        Assert.Equal(parts[4], entry.ValueType.Name);
    }
}