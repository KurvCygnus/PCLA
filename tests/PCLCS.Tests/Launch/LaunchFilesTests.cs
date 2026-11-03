using MeloongCore;
using PCLCS.Launch;
using PCLCS.Startup;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace PCLCS.Tests.Launch;

public class LaunchFilesTests : IDisposable
{
    private readonly string _root;

    public LaunchFilesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclcs.launchfiles." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>构建 Natives zip：按创建顺序写入条目（ZipArchive 保持目录序）。</summary>
    private static void WriteZip(string zipPath, params (string EntryName, byte[] Content)[] entries)
    {
        using var stream = new FileStream(zipPath, FileMode.Create);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name);
            using var entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }
    }

    [Fact]
    public void ExtractNatives_extracts_dll_only_and_cleans_extra_files()
    {
        //ModLaunch.vb McLaunchNatives（:1332-1393 逐行）：仅解压 dll、长度一致跳过、多余文件删除
        var natives = _root + "natives" + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(natives);
        File.WriteAllText(natives + "stale.dll", "old-stale"); //上一局残留，应被清理

        var libA = _root + "libA.jar";
        var dllA1 = Encoding.ASCII.GetBytes("native-a1");
        var dllA2 = Encoding.ASCII.GetBytes("native-a2");
        WriteZip(libA, ("lwjgl/nested.dll", dllA1), ("test2.dll", dllA2), ("readme.txt", Encoding.ASCII.GetBytes("skip")));

        //长度一致的既有文件：跳过解压，内容保持不变
        var sameLength = natives + "test2.dll";
        File.WriteAllBytes(sameLength, Encoding.ASCII.GetBytes("XXXXXXXXX")); //与 dllA2 等长但内容不同

        var logs = new List<string>();
        LaunchFiles.ExtractNatives(natives, [libA], progress: null, log: logs.Add, debug: false);

        Assert.Equal("native-a1", File.ReadAllText(natives + "lwjgl" + Path.DirectorySeparatorChar + "nested.dll")); //子目录条目照常解出
        Assert.Equal("XXXXXXXXX", File.ReadAllText(sameLength)); //长度一致 → 不覆盖
        Assert.False(File.Exists(natives + "readme.txt")); //非 dll 不解压
        Assert.False(File.Exists(natives + "stale.dll")); //多余文件被删除
        Assert.Contains(logs, line => line.Contains("已解压"));
        Assert.Contains(logs, line => line.Contains("删除：") && line.Contains("stale.dll"));
    }

    [Fact]
    public void ExtractNatives_skips_locked_dll_and_breaks_current_zip()
    {
        //:1364-1370：删除原 dll 访问被拒绝 → 记日志并跳过当前 zip 的剩余条目
        var natives = _root + "natives" + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(natives);

        var lib = _root + "libB.jar";
        var dll1 = Encoding.ASCII.GetBytes("native-b1");
        var dll2 = Encoding.ASCII.GetBytes("native-b2");
        WriteZip(lib, ("a.dll", dll1), ("b.dll", dll2));

        //用已映射为可执行映像的 DLL 占用 a.dll：File.Delete 抛 UnauthorizedAccessException
        //（与运行中 MC 加载 dll 的场景一致；只读属性会被 FileUtils.Delete 先行清除，无法触发）
        var lockedPath = natives + "a.dll";
        File.Copy(Path.Combine(Environment.SystemDirectory, "version.dll"), lockedPath);
        var lockedMark = File.ReadAllBytes(lockedPath);
        var library = LoadLibrary(lockedPath);
        Assert.NotEqual(IntPtr.Zero, library);
        try
        {
            var logs = new List<string>();
            LaunchFiles.ExtractNatives(natives, [lib], progress: null, log: logs.Add, debug: false);

            Assert.Contains(logs, line => line.Contains("删除原 dll 访问被拒绝"));
            Assert.Equal(lockedMark, File.ReadAllBytes(lockedPath)); //未被覆盖
            Assert.False(File.Exists(natives + "b.dll")); //Exit For：当前 zip 剩余条目不再解压
        } finally
        {
            FreeLibrary(library); //解除占用以便清理
        }
    }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string fileName);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr library);

    [Fact]
    public void ExtractNatives_throws_and_deletes_on_corrupted_zip()
    {
        //:1343-1350：损坏 zip → 删除源文件并抛出可读错误
        var natives = _root + "natives" + Path.DirectorySeparatorChar;
        var corrupt = _root + "corrupt.jar";
        Directory.CreateDirectory(natives);
        File.WriteAllBytes(corrupt, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00]); //zip 头 + 截断内容

        var logs = new List<string>();
        var exception = Assert.Throws<Exception>(() =>
            LaunchFiles.ExtractNatives(natives, [corrupt], progress: null, log: logs.Add, debug: false));

        Assert.Contains("无法打开 Natives 文件", exception.Message);
        Assert.Contains(corrupt, exception.Message);
        Assert.False(File.Exists(corrupt)); //删除损坏源文件后抛出
    }

    [Theory]
    [InlineData(true, "中文版")]
    [InlineData(false, "ascii-name")]
    public void GetNativesFolder_keeps_version_folder_when_safe(bool isGbkEncoding, string instanceName)
    {
        //:1397-1408：GBK 编码或纯 ASCII 结果 → 不回退，使用版本文件夹
        var versionFolder = _root + "versions" + Path.DirectorySeparatorChar + "1.19.4" + Path.DirectorySeparatorChar;
        var result = LaunchFiles.GetNativesFolder(versionFolder, instanceName, isGbkEncoding, osDrive: _root, appData: _root);

        Assert.EndsWith(instanceName + "-natives" + Path.DirectorySeparatorChar, result);
        Assert.StartsWith(versionFolder, result);
        Assert.True(Directory.Exists(result)); //提前创建
    }

    [Fact]
    public void GetNativesFolder_falls_back_to_appdata_for_non_ascii()
    {
        //:1400-1401：非 GBK 且含非 ASCII → AppData 回退
        var result = LaunchFiles.GetNativesFolder(_root + "versions" + Path.DirectorySeparatorChar + "1.19.4" + Path.DirectorySeparatorChar,
            "中文版", isGbkEncoding: false, osDrive: _root, appData: _root);

        Assert.EndsWith(".minecraft" + Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar + "natives" + Path.DirectorySeparatorChar, result);
        Assert.True(Directory.Exists(result));
    }

    [Fact]
    public void GetNativesFolder_falls_back_to_programdata_for_non_ascii_appdata()
    {
        //:1402-1404：AppData 亦含非 ASCII → ProgramData 回退（LauncherIdentity.FolderName）
        var nonAsciiAppData = _root + "中文用户" + Path.DirectorySeparatorChar;
        var result = LaunchFiles.GetNativesFolder(_root + "versions" + Path.DirectorySeparatorChar + "1.19.4" + Path.DirectorySeparatorChar,
            "中文版", isGbkEncoding: false, osDrive: _root, appData: nonAsciiAppData);

        Assert.Equal(_root + "ProgramData" + Path.DirectorySeparatorChar + LauncherIdentity.FolderName + Path.DirectorySeparatorChar + "natives" + Path.DirectorySeparatorChar, result);
        Assert.True(Directory.Exists(result));
    }

    /// <summary>模拟宿主层 McFolderLauncherProfilesJsonCreate（ModMinecraft.vb:152）：文件已存在时直接返回。</summary>
    private int _createCalls;

    private void CreateIfMissing(string folder)
    {
        _createCalls++;
        if (File.Exists(folder + "launcher_profiles.json")) return;
        File.WriteAllText(folder + "launcher_profiles.json", "{}");
    }

    [Fact]
    public void MergeLauncherProfiles_merges_existing_file()
    {
        //:1439-1468：读现有 → 合并 → GB18030 写回
        var folder = _root + "merge" + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(folder);
        File.WriteAllText(folder + "launcher_profiles.json",
            "{\"profiles\":{\"local\":{\"name\":\"local-profile\"}},\"clientToken\":\"old-token\",\"settings\":{\"keep\":true}}");
        var logs = new List<string>();

        LaunchFiles.MergeLauncherProfiles(folder, "玩家\"Name\"", "client-uuid-token", CreateIfMissing, log: logs.Add);

        Assert.Equal(1, _createCalls); //无条件调用创建回调，文件已存在时由回调自身短路（VB 同）
        using var stream = new FileStream(folder + "launcher_profiles.json", FileMode.Open, FileAccess.Read);
        using var reader = new StreamReader(stream, Encoding.GetEncoding("GB18030"));
        var json = System.Text.Json.Nodes.JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        //authenticationDatabase 按固定键合入，username 中的引号替换为 -
        var account = json["authenticationDatabase"]!["00000111112222233333444445555566"]!;
        Assert.Equal("玩家-Name-", account["username"]!.ToString());
        Assert.Equal("玩家\"Name\"", account["profiles"]!["66666555554444433333222221111100"]!["displayName"]!.ToString());
        Assert.Equal("client-uuid-token", json["clientToken"]!.ToString()); //覆盖
        Assert.Equal("local-profile", json["profiles"]!["local"]!["name"]!.ToString()); //既有 profiles 保留
        Assert.True(json["settings"]!["keep"]!.GetValue<bool>()); //其余字段保留
        Assert.Equal("00000111112222233333444445555566", json["selectedUser"]!["account"]!.ToString());
        Assert.Contains(logs, line => line.Contains("已更新 launcher_profiles.json"));
    }

    [Theory]
    [InlineData("{invalid json")] //损坏 JSON
    [InlineData("   \r\n\t ")] //纯空白：旧 Newtonsoft 解析为 Nothing → 合并处 NRE 进容忍链
    [InlineData("[1,2]")] //根非对象：旧 JObject 直转失败进容忍链
    public void MergeLauncherProfiles_recovers_from_corruption(string content)
    {
        //:1469-1501：合并失败 → 删除后经创建回调重建并重试
        var folder = _root + "corrupt" + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(folder);
        File.WriteAllText(folder + "launcher_profiles.json", content);
        var logs = new List<string>();

        LaunchFiles.MergeLauncherProfiles(folder, "玩家", "token-1", CreateIfMissing, log: logs.Add);

        Assert.Equal(2, _createCalls); //首次（文件已存在短路）+ 重试路径（删除后重建）
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(folder + "launcher_profiles.json"))!.AsObject();
        Assert.Equal("token-1", json["clientToken"]!.ToString());
        Assert.Contains(logs, line => line.Contains("已在删除后更新 launcher_profiles.json"));
    }

    [Fact]
    public void MergeLauncherProfiles_preserves_comments_and_trailing_commas()
    {
        //宽松解析对齐旧 Newtonsoft（LegacyJsonExtensions：跳过注释、允许尾逗号）——
        //此类文件不得触发容忍链清掉重建（否则用户自定义 launcher_profiles 被清除，数据丢失形偏差）
        var folder = _root + "lenient" + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(folder);
        File.WriteAllText(folder + "launcher_profiles.json",
            "// 自定义注释\r\n{\"profiles\":{\"custom\":{\"name\":\"keep-me\"}},}\r\n");

        LaunchFiles.MergeLauncherProfiles(folder, "玩家", "token-2", CreateIfMissing, log: null);

        Assert.Equal(1, _createCalls); //文件已存在，回调短路，未走重建
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(folder + "launcher_profiles.json"))!.AsObject();
        Assert.Equal("keep-me", json["profiles"]!["custom"]!["name"]!.ToString()); //用户自定义保留
        Assert.Equal("token-2", json["clientToken"]!.ToString()); //合并照常生效
    }

    [Fact]
    public void MergeLauncherProfiles_swallows_unrecoverable_failure()
    {
        //:1499-1501：重试仍失败 → 仅记错误日志，不向上抛
        //（FileUtils.Delete 对不存在的文件为 no-op 返回 null，属核心库保证；首删后文件不存在，重试链在创建回调处失败）
        var folder = _root + "unrecoverable" + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(folder);

        LaunchFiles.MergeLauncherProfiles(folder, "玩家", "token-1", createProfilesJson: _ => throw new IOException("模拟创建失败"), log: null);

        Assert.False(File.Exists(folder + "launcher_profiles.json"));
    }
}