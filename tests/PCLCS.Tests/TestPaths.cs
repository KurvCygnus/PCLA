namespace PCLCS.Tests;

internal static class TestPaths
{
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>设置目录对拍快照（tests\PCLCS.Tests\Settings\snapshot.txt，随仓库提交）。</summary>
    public static string SettingsSnapshot
        => Path.Combine(RepoRoot, "tests", "PCLCS.Tests", "Settings", "snapshot.txt");

    public static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "tools", "version.ps1"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("找不到仓库根目录（tools/version.ps1）");
        }
    }
}