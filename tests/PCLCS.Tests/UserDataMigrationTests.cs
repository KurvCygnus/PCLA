using PCLCS.Startup;

namespace PCLCS.Tests;

public class UserDataMigrationTests {
    private sealed class InMemoryRegistry : IRegistry {
        private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, object> Key(string path) {
            if (!_keys.TryGetValue(path, out var key)) {
                key = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                _keys[path] = key;
            }
            return key;
        }

        public bool KeyExists(string path) => _keys.ContainsKey(path);
        public bool ValueExists(string path, string name) => _keys.TryGetValue(path, out var key) && key.ContainsKey(name);
        public object? GetValue(string path, string name) =>
            _keys.TryGetValue(path, out var key) && key.TryGetValue(name, out var value) ? value : null;
        public void SetValue(string path, string name, object value) => Key(path)[name] = value;
        public IEnumerable<string> GetValueNames(string path) =>
            _keys.TryGetValue(path, out var key) ? key.Keys.ToArray() : Array.Empty<string>();
    }

    /// <summary>
    /// 真实文件系统，但复制时可以按源路径前缀模拟失败；不传前缀则所有复制都失败。
    /// </summary>
    private sealed class FailingFileSystem : IFileSystem {
        private readonly PhysicalFileSystem _inner = new();
        private readonly string? _failUnder;

        public FailingFileSystem(string? failUnder = null) => _failUnder = failUnder;

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public IEnumerable<string> EnumerateFiles(string path) => _inner.EnumerateFiles(path);
        public IEnumerable<string> EnumerateDirectories(string path) => _inner.EnumerateDirectories(path);

        public void Copy(string source, string destination) {
            if (_failUnder == null || source.StartsWith(_failUnder, StringComparison.OrdinalIgnoreCase)) {
                throw new IOException("模拟复制失败");
            }
            _inner.Copy(source, destination);
        }

        public void Move(string source, string destination) => _inner.Move(source, destination);
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void WriteAllText(string path, string text) => _inner.WriteAllText(path, text);
    }

    /// <summary>
    /// 模拟复制到一半就中断：向暂存路径写入残缺内容后抛异常。
    /// </summary>
    private sealed class TornCopyFileSystem : IFileSystem {
        private readonly PhysicalFileSystem _inner = new();

        public bool Fail { get; set; } = true;
        public bool SawExistingStaging { get; private set; }

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public IEnumerable<string> EnumerateFiles(string path) => _inner.EnumerateFiles(path);
        public IEnumerable<string> EnumerateDirectories(string path) => _inner.EnumerateDirectories(path);

        public void Copy(string source, string destination) {
            SawExistingStaging |= File.Exists(destination);
            if (Fail) {
                File.WriteAllText(destination, "torn");
                throw new IOException("模拟复制中断");
            }
            _inner.Copy(source, destination);
        }

        public void Move(string source, string destination) => _inner.Move(source, destination);
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void WriteAllText(string path, string text) => _inner.WriteAllText(path, text);
    }

    /// <summary>
    /// 复制成功但改名失败，用于验证标记不会在目标文件真正落盘前写入。
    /// </summary>
    private sealed class MoveFailingFileSystem : IFileSystem {
        private readonly PhysicalFileSystem _inner = new();

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public IEnumerable<string> EnumerateFiles(string path) => _inner.EnumerateFiles(path);
        public IEnumerable<string> EnumerateDirectories(string path) => _inner.EnumerateDirectories(path);
        public void Copy(string source, string destination) => _inner.Copy(source, destination);
        public void Move(string source, string destination) => throw new IOException("模拟移动失败");
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void WriteAllText(string path, string text) => _inner.WriteAllText(path, text);
    }

    /// <summary>
    /// 一旦枚举到 Cache 目录就抛异常，证明排除目录在进入前已经被剪掉。
    /// </summary>
    private sealed class CacheTripwireFileSystem : IFileSystem {
        private readonly PhysicalFileSystem _inner = new();

        private static void Guard(string path) {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(name, "Cache", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException("不应枚举旧版 Cache 目录");
            }
        }

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public IEnumerable<string> EnumerateFiles(string path) {
            Guard(path);
            return _inner.EnumerateFiles(path);
        }

        public IEnumerable<string> EnumerateDirectories(string path) {
            Guard(path);
            return _inner.EnumerateDirectories(path);
        }

        public void Copy(string source, string destination) => _inner.Copy(source, destination);
        public void Move(string source, string destination) => _inner.Move(source, destination);
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void WriteAllText(string path, string text) => _inner.WriteAllText(path, text);
    }

    private static (string Root, MigrationOptions Options) CreateRoot() {
        var root = Path.Combine(Path.GetTempPath(), "pcla-migration-" + Guid.NewGuid().ToString("N"));
        var appData = Path.Combine(root, "AppData");
        var baseDir = Path.Combine(root, "Base");
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(baseDir);
        return (root, new MigrationOptions { AppDataRoot = appData, BaseDirectory = baseDir });
    }

    private static void Cleanup(string root) {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public void CopiesMissingFilesAndWritesMarker() {
        var (root, options) = CreateRoot();
        try {
            var source = Path.Combine(options.AppDataRoot, "PCL");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "options.txt"), "old");
            var target = Path.Combine(options.AppDataRoot, "PCLA");

            var warnings = UserDataMigration.Run(new PhysicalFileSystem(), new InMemoryRegistry(), options);

            Assert.Empty(warnings);
            Assert.Equal("old", File.ReadAllText(Path.Combine(target, "options.txt")));
            Assert.True(File.Exists(Path.Combine(target, ".pcla-import-files")));
            Assert.True(File.Exists(Path.Combine(source, "options.txt")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void DoesNotOverwriteExistingTarget() {
        var (root, options) = CreateRoot();
        try {
            var source = Path.Combine(options.AppDataRoot, "PCL");
            var target = Path.Combine(options.AppDataRoot, "PCLA");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, "options.txt"), "old");
            File.WriteAllText(Path.Combine(target, "options.txt"), "new");

            UserDataMigration.Run(new PhysicalFileSystem(), new InMemoryRegistry(), options);

            Assert.Equal("new", File.ReadAllText(Path.Combine(target, "options.txt")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void SkipsLogsCacheOldExeAndLatest() {
        var (root, options) = CreateRoot();
        try {
            var source = Path.Combine(options.AppDataRoot, "PCL");
            Directory.CreateDirectory(Path.Combine(source, "Cache"));
            File.WriteAllText(Path.Combine(source, "Log1.txt"), "log");
            File.WriteAllText(Path.Combine(source, "Cache", "a.dat"), "cache");
            File.WriteAllText(Path.Combine(source, "Latest.exe"), "latest");
            File.WriteAllText(Path.Combine(source, "Plain Craft Launcher 2.exe"), "exe");
            File.WriteAllText(Path.Combine(source, "options.txt"), "old");

            UserDataMigration.Run(new PhysicalFileSystem(), new InMemoryRegistry(), options);

            var target = Path.Combine(options.AppDataRoot, "PCLA");
            // 标记文件由迁移本身写入，不属于旧数据；其余文件一个都不该多。
            Assert.Equal(new[] { "options.txt" },
                Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetFileName(path))
                    .Where(name => name != options.FilesMarker)
                    .OrderBy(name => name));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void NestedCacheIsPrunedBeforeEnumeration() {
        var (root, options) = CreateRoot();
        try {
            var source = Path.Combine(options.AppDataRoot, "PCL");
            Directory.CreateDirectory(Path.Combine(source, "Cache"));
            Directory.CreateDirectory(Path.Combine(source, "Sub", "Cache"));
            File.WriteAllText(Path.Combine(source, "Cache", "a.dat"), "cache");
            File.WriteAllText(Path.Combine(source, "Sub", "Cache", "b.dat"), "cache");
            File.WriteAllText(Path.Combine(source, "Sub", "keep.txt"), "keep");
            File.WriteAllText(Path.Combine(source, "options.txt"), "old");

            var warnings = UserDataMigration.Run(new CacheTripwireFileSystem(), new InMemoryRegistry(), options);

            Assert.Empty(warnings);
            var target = Path.Combine(options.AppDataRoot, "PCLA");
            Assert.Equal(new[] { "keep.txt", "options.txt" },
                Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetFileName(path))
                    .Where(name => name != options.FilesMarker)
                    .OrderBy(name => name));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void RetryAfterTornCopyProducesCompleteTarget() {
        var (root, options) = CreateRoot();
        try {
            var source = Path.Combine(options.AppDataRoot, "PCL");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "options.txt"), "old");
            var target = Path.Combine(options.AppDataRoot, "PCLA");
            var staging = Path.Combine(target, "options.txt.pcla-tmp");

            var warnings = UserDataMigration.Run(new TornCopyFileSystem(), new InMemoryRegistry(), options);

            Assert.Contains(warnings, warning => warning.Contains("用户数据"));
            Assert.False(File.Exists(Path.Combine(target, ".pcla-import-files")));
            Assert.False(File.Exists(Path.Combine(target, "options.txt")));
            Assert.False(File.Exists(staging));

            // 模拟进程被强制终止、来不及清理暂存文件的情况。
            File.WriteAllText(staging, "torn");
            var retry = new TornCopyFileSystem { Fail = false };

            var secondWarnings = UserDataMigration.Run(retry, new InMemoryRegistry(), options);

            Assert.Empty(secondWarnings);
            Assert.False(retry.SawExistingStaging);
            Assert.Equal("old", File.ReadAllText(Path.Combine(target, "options.txt")));
            Assert.False(File.Exists(staging));
            Assert.True(File.Exists(Path.Combine(target, ".pcla-import-files")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void MoveFailureDoesNotWriteMarkerAndRetries() {
        var (root, options) = CreateRoot();
        try {
            var source = Path.Combine(options.AppDataRoot, "PCL");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "options.txt"), "old");
            var target = Path.Combine(options.AppDataRoot, "PCLA");
            var staging = Path.Combine(target, "options.txt.pcla-tmp");

            var warnings = UserDataMigration.Run(new MoveFailingFileSystem(), new InMemoryRegistry(), options);

            Assert.Contains(warnings, warning => warning.Contains("用户数据"));
            Assert.False(File.Exists(Path.Combine(target, ".pcla-import-files")));
            Assert.False(File.Exists(Path.Combine(target, "options.txt")));
            Assert.False(File.Exists(staging));

            var secondWarnings = UserDataMigration.Run(new PhysicalFileSystem(), new InMemoryRegistry(), options);

            Assert.Empty(secondWarnings);
            Assert.Equal("old", File.ReadAllText(Path.Combine(target, "options.txt")));
            Assert.True(File.Exists(Path.Combine(target, ".pcla-import-files")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void SecondRunDoesNotCopyNewSourceData() {
        var (root, options) = CreateRoot();
        try {
            var source = Path.Combine(options.AppDataRoot, "PCL");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "options.txt"), "old");
            var target = Path.Combine(options.AppDataRoot, "PCLA");

            UserDataMigration.Run(new PhysicalFileSystem(), new InMemoryRegistry(), options);
            File.WriteAllText(Path.Combine(source, "options.txt"), "changed");
            File.WriteAllText(Path.Combine(source, "added.txt"), "added");

            var warnings = UserDataMigration.Run(new PhysicalFileSystem(), new InMemoryRegistry(), options);

            Assert.Empty(warnings);
            Assert.Equal("old", File.ReadAllText(Path.Combine(target, "options.txt")));
            Assert.False(File.Exists(Path.Combine(target, "added.txt")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void OneBlockFailureDoesNotAbortOtherBlocks() {
        var (root, options) = CreateRoot();
        try {
            var source = Path.Combine(options.AppDataRoot, "PCL");
            var portable = Path.Combine(options.BaseDirectory, "PCL");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(portable);
            File.WriteAllText(Path.Combine(source, "options.txt"), "old");
            File.WriteAllText(Path.Combine(portable, "portable.txt"), "portable");
            var registry = new InMemoryRegistry();
            registry.SetValue(options.OldRegistryPath, "A", "1");

            var warnings = UserDataMigration.Run(new FailingFileSystem(source), registry, options);

            Assert.Single(warnings);
            Assert.Contains("用户数据", warnings[0]);
            Assert.False(File.Exists(Path.Combine(options.AppDataRoot, "PCLA", ".pcla-import-files")));
            Assert.True(File.Exists(Path.Combine(options.BaseDirectory, "PCLA", ".pcla-import-portable")));
            Assert.Equal("portable", File.ReadAllText(Path.Combine(options.BaseDirectory, "PCLA", "portable.txt")));
            Assert.Equal("1", registry.GetValue(options.NewRegistryPath, "A"));
            Assert.Equal(1, registry.GetValue(options.NewRegistryPath, options.RegistryMarker));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void MissingSourceStillWritesMarker() {
        var (root, options) = CreateRoot();
        try {
            UserDataMigration.Run(new PhysicalFileSystem(), new InMemoryRegistry(), options);
            Assert.True(File.Exists(Path.Combine(options.AppDataRoot, "PCLA", ".pcla-import-files")));
            Assert.True(File.Exists(Path.Combine(options.BaseDirectory, "PCLA", ".pcla-import-portable")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void CopiesRegistryValuesButSkipsLastVersion() {
        var (root, options) = CreateRoot();
        try {
            var registry = new InMemoryRegistry();
            registry.SetValue(options.OldRegistryPath, "A", "1");
            registry.SetValue(options.OldRegistryPath, "SystemLastVersionReg", 413);

            var warnings = UserDataMigration.Run(new PhysicalFileSystem(), registry, options);

            Assert.Empty(warnings);
            Assert.Equal("1", registry.GetValue(options.NewRegistryPath, "A"));
            Assert.False(registry.ValueExists(options.NewRegistryPath, "SystemLastVersionReg"));
            Assert.Equal(1, registry.GetValue(options.NewRegistryPath, options.RegistryMarker));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void SkipsLastVersionRegardlessOfCase() {
        var (root, options) = CreateRoot();
        try {
            var registry = new InMemoryRegistry();
            registry.SetValue(options.OldRegistryPath, "SYSTEMLASTVERSIONREG", 413);

            var warnings = UserDataMigration.Run(new PhysicalFileSystem(), registry, options);

            Assert.Empty(warnings);
            Assert.False(registry.ValueExists(options.NewRegistryPath, "SystemLastVersionReg"));
            Assert.Equal(1, registry.GetValue(options.NewRegistryPath, options.RegistryMarker));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void DoesNotOverwriteExistingRegistryValue() {
        var (root, options) = CreateRoot();
        try {
            var registry = new InMemoryRegistry();
            registry.SetValue(options.OldRegistryPath, "A", "old");
            registry.SetValue(options.NewRegistryPath, "A", "new");

            UserDataMigration.Run(new PhysicalFileSystem(), registry, options);

            Assert.Equal("new", registry.GetValue(options.NewRegistryPath, "A"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void FailureReturnsWarningAndDoesNotWriteMarker() {
        var (root, options) = CreateRoot();
        try {
            var source = Path.Combine(options.AppDataRoot, "PCL");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "options.txt"), "old");

            var warnings = UserDataMigration.Run(new FailingFileSystem(), new InMemoryRegistry(), options);

            Assert.Contains(warnings, warning => warning.Contains("用户数据"));
            Assert.False(File.Exists(Path.Combine(options.AppDataRoot, "PCLA", ".pcla-import-files")));
            Assert.False(File.Exists(Path.Combine(options.AppDataRoot, "PCLA", "options.txt.pcla-tmp")));
        }
        finally { Cleanup(root); }
    }
}
