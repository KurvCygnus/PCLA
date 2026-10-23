namespace PCLCS.Tests;

internal static class TestPaths {
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string RepoRoot {
        get {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null) {
                if (File.Exists(Path.Combine(directory.FullName, "tools", "version.ps1"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("找不到仓库根目录（tools/version.ps1）");
        }
    }
}
