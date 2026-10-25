using Microsoft.Win32;
using PCLCS.Results;

namespace PCLCS.Startup;

/// <summary>
/// 文件系统的最小抽象，便于在测试中模拟复制、移动与清理失败。
/// 枚举方法只返回当前目录的内容，不递归。
/// </summary>
public interface IFileSystem {
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CreateDirectory(string path);
    IEnumerable<string> EnumerateFiles(string path);
    IEnumerable<string> EnumerateDirectories(string path);
    void Copy(string source, string destination);
    void Move(string source, string destination);
    void DeleteFile(string path);
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
/// <remarks>
/// 每个区块只在第一次成功时导入：标记写入后不再扫描旧目录。因此首次启动 PCLA 前应先关闭旧版 PCL，
/// 首次启动之后才写入旧版 PCL 的数据不会被导入。文件先复制到以目标名开头、带随机串的 <c>.tmp</c>
/// 暂存名，成功后再改名为目标文件，避免把写了一半的内容当成完整数据。进程被强制终止时可能留下
/// 这种随机名暂存文件；迁移不会清理它们，因为无法证明它们属于迁移而不是用户数据。
/// </remarks>
public static class UserDataMigration {

    /// <summary>使用真实文件系统与当前用户注册表执行迁移；初始化失败也返回报告。</summary>
    public static MigrationReport RunDefault() =>
        RunDefault(() => new PhysicalFileSystem(), () => new RegistryStore(), new MigrationOptions());

    internal static MigrationReport RunDefault(Func<IFileSystem> fileSystemFactory,
        Func<IRegistry> registryFactory, MigrationOptions options) {
        try {
            return Run(fileSystemFactory(), registryFactory(), options);
        } catch (Exception ex) {
            return new MigrationReport([
                new MigrationSectionResult(MigrationSection.Startup,
                    Result<Unit, MigrationError>.Err(
                        new MigrationError(MigrationSection.Startup, null, "初始化用户数据导入失败", ex))),
            ]);
        }
    }

    /// <summary>执行三个区块的迁移；区块失败不阻断其他区块。</summary>
    public static MigrationReport Run(IFileSystem fs, IRegistry registry, MigrationOptions options) {
        var filesPath = Path.Combine(options.AppDataRoot, options.NewFolderName);
        var portablePath = Path.Combine(options.BaseDirectory, options.NewFolderName);
        return new MigrationReport([
            RunSection(MigrationSection.Files, filesPath, "用户数据",
                () => MigrateDirectory(fs, Path.Combine(options.AppDataRoot, options.OldFolderName),
                    filesPath, options.FilesMarker, options)),
            RunSection(MigrationSection.Portable, portablePath, "便携数据",
                () => MigrateDirectory(fs, Path.Combine(options.BaseDirectory, options.OldFolderName),
                    portablePath, options.PortableMarker, options)),
            RunSection(MigrationSection.Registry, options.NewRegistryPath, "注册表",
                () => MigrateRegistry(registry, options)),
        ]);
    }

    private static MigrationSectionResult RunSection(MigrationSection section, string path, string label,
        Action action) {
        try {
            action();
            return new MigrationSectionResult(section, Result<Unit, MigrationError>.Ok(Unit.Value));
        } catch (Exception ex) {
            return new MigrationSectionResult(section,
                Result<Unit, MigrationError>.Err(new MigrationError(section, path, $"{label}导入失败", ex)));
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

        MigrateDirectoryContents(fs, source, target, options);
        fs.WriteAllText(markerPath, DateTime.UtcNow.ToString("O"));
    }

    private static void MigrateDirectoryContents(IFileSystem fs, string source, string target,
        MigrationOptions options) {
        foreach (var file in fs.EnumerateFiles(source)) {
            var name = Path.GetFileName(file);
            if (options.ExcludedNames.Contains(name)) continue;
            CopyAtomically(fs, file, Path.Combine(target, name));
        }
        foreach (var directory in fs.EnumerateDirectories(source)) {
            var name = Path.GetFileName(directory);
            // 在进入之前就剪掉排除目录，避免枚举 Cache 这类可能很大的旧缓存。
            if (options.ExcludedDirectories.Contains(name)) continue;
            var childTarget = Path.Combine(target, name);
            fs.CreateDirectory(childTarget);
            MigrateDirectoryContents(fs, directory, childTarget, options);
        }
    }

    private static void CopyAtomically(IFileSystem fs, string source, string destination) {
        // 目标文件已经存在就视为完整数据（包括用户或开发环境预先创建的文件），绝不覆盖。
        if (fs.FileExists(destination)) return;
        fs.CreateDirectory(Path.GetDirectoryName(destination)!);
        // 每次复制使用唯一暂存名：不会与用户文件（包括 foo.pcla-tmp 这类名字）冲突，
        // 也不需要删除任何预先存在的路径。崩溃残留的随机名文件不会影响后续重试。
        var staging = destination + ".pcla-" + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            fs.Copy(source, staging);
            fs.Move(staging, destination);
        }
        catch {
            TryDelete(fs, staging);
            throw;
        }
    }

    private static void TryDelete(IFileSystem fs, string path) {
        try {
            if (fs.FileExists(path)) fs.DeleteFile(path);
        }
        catch (Exception) {
            // 清理失败不能掩盖原始异常；随机名暂存文件不会影响后续重试。
        }
    }

    private static void MigrateRegistry(IRegistry registry, MigrationOptions options) {
        // 注册表只有一个标记值；没有 LastVersionReg 才不会把旧版更新状态带进新版。
        if (registry.ValueExists(options.NewRegistryPath, options.RegistryMarker)) return;
        if (registry.KeyExists(options.OldRegistryPath)) {
            foreach (var name in registry.GetValueNames(options.OldRegistryPath)) {
                // 注册表值名的比较规则与 Windows 一致，不区分大小写。
                if (string.Equals(name, "SystemLastVersionReg", StringComparison.OrdinalIgnoreCase)) continue;
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
    public IEnumerable<string> EnumerateFiles(string path) => Directory.EnumerateFiles(path);
    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);
    public void Copy(string source, string destination) => File.Copy(source, destination, false);
    public void Move(string source, string destination) => File.Move(source, destination);
    public void DeleteFile(string path) => File.Delete(path);
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
