using MeloongCore.Extensions;
using PCLCS.Crash;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace PCLCS.Tests.Crash;

/// <summary>
/// 崩溃导出核测试（ModCrash.vb 旧 :861-920 的文件操作面）：重命名表逐字（:894-903 全条目）、
/// 脱敏调用语义（旧 :906：启动脚本吃 'F'、其余吃 '*'，provider 未接线时按原文）、读取编码实参逐字、
/// zip 内容平铺断言与错误路径（吞异常留 Report，逐字）。
/// </summary>
public class CrashExportTests : IDisposable
{
    private readonly string _root;
    private int _analyzerSeq;

    public CrashExportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclcs.crashexport." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>每个导出器独立 TaskTemp 目录（形状同宿主层 RequestTaskTempFolder 的返回：以 \ 结尾）。</summary>
    private CrashAnalyzer CreateAnalyzer()
    {
        var seq = _analyzerSeq++;
        return new CrashAnalyzer(() => _root + "TaskTemp-" + seq + "\\");
    }

    /// <summary>写入一份源文件并返回路径（OutputFiles 条目形状）。</summary>
    private static string WriteSource(string caseRoot, string fileName, string content)
    {
        Directory.CreateDirectory(caseRoot);
        var path = Path.Combine(caseRoot, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// 脱敏测试桩：按宿主层 ModMinecraft.vb:1445/:1463 的真实语义实现（登录态打码依赖 McLoginLoader，桩不涉及），
    /// 组合形状同 ModWatcher.vb:13 的接线：AccessToken 过滤吃 filterChar，用户名过滤固定 '*'。
    /// </summary>
    private static string StubFilters(string raw, char filterChar)
    {
        if (raw.Contains("accessToken "))
        {
            foreach (var token in raw.RegexSearch("(?<=accessToken ([^ ]{5}))[^ ]+(?=[^ ]{5})"))
            {
                raw = raw.Replace(token, new string(filterChar, token.Length)); //长度语义同旧 Token.Count
            }
        }
        if (raw.Contains(@":\Users\"))
        {
            raw = Regex.Replace(raw, @"(?<=:\\Users\\)[^\\]+", static m => new string('*', m.Length));
        }
        if (raw.Contains(":/Users/"))
        {
            raw = Regex.Replace(raw, @"(?<=:/Users/)[^/]+", static m => new string('*', m.Length));
        }
        return raw;
    }

    /// <summary>解包 zip 并按条目名返回内容文本（导出核经 FileUtils.Write 恒以 UTF-8 无 BOM 字节写出）。</summary>
    private static Dictionary<string, string> ReadZipText(string zipPath)
    {
        using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(static e => e.FullName, static e => new StreamReader(e.Open(), Encoding.UTF8).ReadToEnd());
    }

    [Fact]
    public void Rename_table_maps_all_entries_verbatim()
    {
        //重命名表（旧 :894-903）逐条对照：三条改名 + 未命中保持原名；zip 条目平铺于根（CreateZipFromDirectory 语义）
        var caseRoot = Path.Combine(_root, "rename");
        var analyzer = CreateAnalyzer();
        analyzer.OutputFiles.AddRange(
        [
            WriteSource(caseRoot, "LatestLaunch.bat", "java -Xmx1024M -jar minecraft.jar"),
            WriteSource(caseRoot, "Log1.txt", "PCL 启动器日志内容"),
            WriteSource(caseRoot, "RawOutput.log", "游戏实时输出内容"),
            WriteSource(caseRoot, "plain.log", "未命中重命名表的日志")
        ]);

        var exported = CrashExport.BuildExportArchive(analyzer, null, _root + "out.zip");

        Assert.Equal(_root + "out.zip", exported);
        var entries = ReadZipText(_root + "out.zip");
        Assert.Equal(new[] { "启动脚本.bat", "PCL 启动器日志.txt", "游戏崩溃前的输出.txt", "plain.log" }.OrderBy(static s => s, StringComparer.Ordinal),
            entries.Keys.OrderBy(static s => s, StringComparer.Ordinal)); //zip 条目序随目录枚举，不属契约；断言按序集对照
        Assert.Equal("java -Xmx1024M -jar minecraft.jar", entries["启动脚本.bat"]);
        Assert.Equal("PCL 启动器日志内容", entries["PCL 启动器日志.txt"]);
        Assert.Equal("游戏实时输出内容", entries["游戏崩溃前的输出.txt"]);
        Assert.Equal("未命中重命名表的日志", entries["plain.log"]);
        //成功后 Report 工作目录已清（旧 :912）
        Assert.False(Directory.Exists(analyzer.TempFolder + "Report"));
    }

    [Fact]
    public void Desensitize_uses_F_for_bat_and_star_for_others()
    {
        //脱敏组合（旧 :906）：FilterChar 按重命名后的文件名判定（启动脚本→'F'，其余→'*'），用户名固定 '*'（ModWatcher.vb:13 组合）。
        //16 位 token 的宿主层语义：首 5 位入 lookbehind 不打码，正则捕获中段 6 位打码，尾 5 位原样（同登录态打码 5+中+5 形状）
        var saved = CrashAnalyzer.DesensitizeFilters;
        CrashAnalyzer.DesensitizeFilters = StubFilters;
        try
        {
            var caseRoot = Path.Combine(_root, "desensitize");
            var analyzer = CreateAnalyzer();
            analyzer.OutputFiles.AddRange(
            [
                WriteSource(caseRoot, "LatestLaunch.bat", @"accessToken 1234567890abcdef C:\Users\Lenovo\minecraft"),
                WriteSource(caseRoot, "latest.log", @"accessToken 1234567890abcdef C:\Users\Lenovo\minecraft")
            ]);

            CrashExport.BuildExportArchive(analyzer, null, _root + "out.zip");

            var entries = ReadZipText(_root + "out.zip");
            Assert.Equal(@"accessToken 12345FFFFFFbcdef C:\Users\******\minecraft", entries["启动脚本.bat"]);
            Assert.Equal(@"accessToken 12345******bcdef C:\Users\******\minecraft", entries["latest.log"]);
        }
        finally
        {
            CrashAnalyzer.DesensitizeFilters = saved;
        }
    }

    [Fact]
    public void Desensitize_not_wired_writes_content_verbatim()
    {
        //静态 provider 未接线（测试直用语义，空值分支）时按原文写出
        var saved = CrashAnalyzer.DesensitizeFilters;
        CrashAnalyzer.DesensitizeFilters = null;
        try
        {
            var caseRoot = Path.Combine(_root, "unwired");
            var analyzer = CreateAnalyzer();
            analyzer.OutputFiles.Add(WriteSource(caseRoot, "latest.log", @"accessToken 1234567890abcdef C:\Users\Lenovo\minecraft"));

            CrashExport.BuildExportArchive(analyzer, null, _root + "out.zip");

            Assert.Equal(@"accessToken 1234567890abcdef C:\Users\Lenovo\minecraft", ReadZipText(_root + "out.zip")["latest.log"]);
        }
        finally
        {
            CrashAnalyzer.DesensitizeFilters = saved;
        }
    }

    [Fact]
    public void Read_encoding_argument_passes_through_verbatim()
    {
        //读取编码实参逐字（旧 :905）：Log1.txt/RawOutput.log 显式 UTF8，其余 Nothing→BOM 探测 + GB18030 回退。
        //GB18030 字节在显式 UTF8 读取下出替换符、在 Nothing 读取下经回退正确解码——两条读取路径各锁定一处
        var caseRoot = Path.Combine(_root, "encoding");
        Directory.CreateDirectory(caseRoot);
        var analyzer = CreateAnalyzer();
        var gbBytes = Encoding.GetEncoding("GB18030").GetBytes("中文日志");
        File.WriteAllBytes(Path.Combine(caseRoot, "Log1.txt"), gbBytes);
        File.WriteAllBytes(Path.Combine(caseRoot, "plain.log"), gbBytes);
        analyzer.OutputFiles.AddRange([Path.Combine(caseRoot, "Log1.txt"), Path.Combine(caseRoot, "plain.log")]);

        CrashExport.BuildExportArchive(analyzer, null, _root + "out.zip");

        var entries = ReadZipText(_root + "out.zip");
        Assert.Contains("\uFFFD", entries["PCL 启动器日志.txt"], StringComparison.Ordinal);
        Assert.Equal("中文日志", entries["plain.log"]);
    }

    [Fact]
    public void Missing_source_files_are_skipped()
    {
        //存在性过滤（旧 :904）：列于 OutputFiles 但磁盘缺失的文件静默跳过，不中断导出
        var caseRoot = Path.Combine(_root, "missing");
        var analyzer = CreateAnalyzer();
        analyzer.OutputFiles.AddRange([WriteSource(caseRoot, "exists.log", "在"), Path.Combine(caseRoot, "ghost.log")]);

        var exported = CrashExport.BuildExportArchive(analyzer, null, _root + "out.zip");

        Assert.NotNull(exported);
        Assert.Equal(["exists.log"], ReadZipText(exported).Keys.ToArray()); //单条目，序无涉
    }

    [Fact]
    public void ExtraFiles_collected_into_shared_list_and_packed()
    {
        //ExtraFiles 收集（旧 :890）：并入共享 OutputFiles 后进包；共享列表被永久追加是逐字保留的副作用（再次导出会重复并入，同旧行为）
        var caseRoot = Path.Combine(_root, "extra");
        var analyzer = CreateAnalyzer();
        var mainFile = WriteSource(caseRoot, "main.log", "main");
        var extraFile = WriteSource(caseRoot, "extra.log", "extra");
        analyzer.OutputFiles.Add(mainFile);

        var exported = CrashExport.BuildExportArchive(analyzer, [extraFile], _root + "out.zip");

        Assert.NotNull(exported);
        Assert.Equal(new[] { "main.log", "extra.log" }.OrderBy(static s => s, StringComparer.Ordinal),
            ReadZipText(exported).Keys.OrderBy(static s => s, StringComparer.Ordinal)); //zip 条目序不属契约
        Assert.Collection(analyzer.OutputFiles,
            f => Assert.Equal(mainFile, f),
            f => Assert.Equal(extraFile, f));
    }

    [Fact]
    public void Export_failure_returns_null_and_keeps_report()
    {
        //错误路径逐字（旧 :915-917）：写 Report 抛异常 → 记日志吞异常返回 null；zip 未生成（旧 :911 未达）、Report 不清（旧 :912 未达）
        var caseRoot = Path.Combine(_root, "failure");
        var analyzer = CreateAnalyzer();
        analyzer.OutputFiles.Add(WriteSource(caseRoot, "good.log", "内容"));
        Directory.CreateDirectory(analyzer.TempFolder + "Report\\good.log"); //写入目标为目录 → FileUtils.Write 抛异常

        var exported = CrashExport.BuildExportArchive(analyzer, null, _root + "out.zip");

        Assert.Null(exported);
        Assert.False(File.Exists(_root + "out.zip"));
        Assert.True(Directory.Exists(analyzer.TempFolder + "Report\\good.log"));
    }
}