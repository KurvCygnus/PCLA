using Microsoft.Win32;

namespace PCLCS.Startup;

/// <summary>
/// 文件系统的最小抽象，便于在测试中模拟复制失败。
/// </summary>
public interface IFileSystem {
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CreateDirectory(string path);
    IEnumerable<string> EnumerateFiles(string path);
    void Copy(string source, string destination);
    void WriteAllText(string path, string text);
}

/// <summary>
/// 当前用户注册表的最小抽象，路径均为 <c>HKCU</c> 下的相对路径。
/// </summary>
public interface IRegistry {
    bool KeyExists(string path);
    bool ValueExists(string path, string name);
    object? GetValue(string path, string name);
    void SetValue(string path, string name, object value);
    IEnumerable<string> GetValueNames(string path);
}

/// <summary>
/// 旧数据导入的路径、名称与排除规则。默认值来自 <see cref="LauncherIdentity"/>。
/// </summary>
public sealed class MigrationOptions {
    public string AppDataRoot { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public string BaseDirectory { get; init; } = AppDomain.CurrentDomain.BaseDirectory;
    public string OldFolderName { get; init; } = "PCL";
    public string NewFolderName { get; init; } = LauncherIdentity.FolderName;
    public string OldRegistryPath { get; init; } = @"Software\PCLDebug";
    public string NewRegistryPath { get; init; } = @"Software\" + LauncherIdentity.RegistryFolder;
    public string RegistryMarker { get; init; } = "SystemPclaImportRegistry";
    public string FilesMarker { get; init; } = ".pcla-import-files";
    public string PortableMarker { get; init; } = ".pcla-import-portable";

    /// <summary>不导入的文件名：轮换日志、更新器与旧版主程序。</summary>
    public HashSet<string> ExcludedNames { get; init; } = new(StringComparer.OrdinalIgnoreCase) {
        "Log1.txt", "Log2.txt", "Log3.txt", "Log4.txt", "Log5.txt",
        "Latest.exe", "Plain Craft Launcher 2.exe",
    };

    /// <summary>不导入的目录名：旧版缓存（含下载缓存与临时文件）。</summary>
    public HashSet<string> ExcludedDirectories { get; init; } = new(StringComparer.OrdinalIgnoreCase) { "Cache" };
}

/// <summary>
/// 把旧版 PCL 的用户数据、便携数据与注册表一次性导入 PCLA。
/// 只补缺失项：目标已存在的文件或值一律保留；源数据只读，绝不被删除或覆盖。
/// </summary>
public static class UserDataMigration {
    /// <summary>
    /// 使用真实文件系统、当前用户注册表与默认选项执行迁移。
    /// </summary>
    /// <returns>每个导入失败的区块对应一条中文警告；无失败时为空。</returns>
    public static IReadOnlyList<string> RunDefault() {
        return Run(new PhysicalFileSystem(), new RegistryStore(), new MigrationOptions());
    }

    /// <summary>
    /// 执行迁移。三个区块（用户数据、便携数据、注册表）互相独立，
    /// 任一区块抛出异常只会记录一条警告，其余区块继续执行。
    /// </summary>
    /// <returns>每个导入失败的区块对应一条中文警告；无失败时为空。</returns>
    public static IReadOnlyList<string> Run(IFileSystem fs, IRegistry registry, MigrationOptions options) {
        var warnings = new List<string>();
        Try(() => MigrateDirectory(fs, Path.Combine(options.AppDataRoot, options.OldFolderName),
            Path.Combine(options.AppDataRoot, options.NewFolderName), options.FilesMarker, options), "用户数据", warnings);
        Try(() => MigrateDirectory(fs, Path.Combine(options.BaseDirectory, options.OldFolderName),
            Path.Combine(options.BaseDirectory, options.NewFolderName), options.PortableMarker, options), "便携数据", warnings);
        Try(() => MigrateRegistry(registry, options), "注册表", warnings);
        return warnings;
    }

    private static void Try(Action action, string label, List<string> warnings) {
        try {
            action();
        }
        catch (Exception ex) {
            warnings.Add($"{label}导入失败：{ex.Message}");
        }
    }

    private static void MigrateDirectory(IFileSystem fs, string source, string target, string marker,
        MigrationOptions options) {
        var markerPath = Path.Combine(target, marker);
        // 标记文件代表该区块已经处理过；它只在整块成功后写入，因此失败的区块会在下次启动重试。
        if (fs.FileExists(markerPath)) return;
        fs.CreateDirectory(target);
        if (!fs.DirectoryExists(source)) {
            fs.WriteAllText(markerPath, DateTime.UtcNow.ToString("O"));
            return;
        }

        foreach (var file in fs.EnumerateFiles(source)) {
            var relative = file.Substring(source.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (ShouldSkip(relative, options)) continue;
            var destination = Path.Combine(target, relative);
            if (fs.FileExists(destination)) continue;
            fs.CreateDirectory(Path.GetDirectoryName(destination)!);
            fs.Copy(file, destination);
        }
        fs.WriteAllText(markerPath, DateTime.UtcNow.ToString("O"));
    }

    private static bool ShouldSkip(string relative, MigrationOptions options) {
        var segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return true;
        if (segments.Any(segment => options.ExcludedDirectories.Contains(segment))) return true;
        return options.ExcludedNames.Contains(segments[segments.Length - 1]);
    }

    private static void MigrateRegistry(IRegistry registry, MigrationOptions options) {
        // 注册表只有一个标记值；没有 LastVersionReg 才不会把旧版更新状态带进新版。
        if (registry.ValueExists(options.NewRegistryPath, options.RegistryMarker)) return;
        if (registry.KeyExists(options.OldRegistryPath)) {
            foreach (var name in registry.GetValueNames(options.OldRegistryPath)) {
                if (name == "SystemLastVersionReg") continue;
                if (registry.ValueExists(options.NewRegistryPath, name)) continue;
                var value = registry.GetValue(options.OldRegistryPath, name);
                if (value != null) registry.SetValue(options.NewRegistryPath, name, value);
            }
        }
        registry.SetValue(options.NewRegistryPath, options.RegistryMarker, 1);
    }
}

/// <summary>
/// <see cref="IFileSystem"/> 的真实实现。
/// </summary>
public sealed class PhysicalFileSystem : IFileSystem {
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public IEnumerable<string> EnumerateFiles(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
    public void Copy(string source, string destination) => File.Copy(source, destination, false);
    public void WriteAllText(string path, string text) => File.WriteAllText(path, text);
}

/// <summary>
/// <see cref="IRegistry"/> 的真实实现，操作当前用户的注册表。
/// </summary>
public sealed class RegistryStore : IRegistry {
    public bool KeyExists(string path) {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key != null;
    }

    public bool ValueExists(string path, string name) {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name) != null;
    }

    public object? GetValue(string path, string name) {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name);
    }

    public void SetValue(string path, string name, object value) {
        using var key = Registry.CurrentUser.CreateSubKey(path)
            ?? throw new InvalidOperationException($@"无法创建注册表项 HKCU\{path}。");
        key.SetValue(name, value);
    }

    public IEnumerable<string> GetValueNames(string path) {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValueNames() ?? Array.Empty<string>();
    }
}
