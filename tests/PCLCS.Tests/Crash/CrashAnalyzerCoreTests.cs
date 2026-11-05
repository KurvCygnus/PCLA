using MeloongCore;
using PCLCS.Crash;
using System.IO.Compression;
using System.Text;

namespace PCLCS.Tests.Crash;

/// <summary>
/// 崩溃分析核测试：语料 8 例自 BehaviorTests 契约面复制意图（写 latest.log → Import → Prepare → Analyze →
/// GetAnalyzeResult(false)），另覆盖 Mixin 规则、五类分流与截断魔数、堆栈关键词三处缺口。
/// TaskTemp 申请留宿主层（RequestTaskTempFolder），经构造回调注入临时目录工厂。
/// </summary>
public class CrashAnalyzerCoreTests : IDisposable
{
    private readonly string _root;
    private int _analyzerSeq;

    public CrashAnalyzerCoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclcs.crash." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>每个分析器独立 TaskTemp 目录（形状同宿主层 RequestTaskTempFolder 的返回：以 \ 结尾）。</summary>
    private CrashAnalyzer CreateAnalyzer()
    {
        var seq = _analyzerSeq++;
        return new CrashAnalyzer(() => _root + "TaskTemp-" + seq + "\\");
    }

    /// <summary>写一份日志文件并返回路径（语料与 Crit2/堆栈测试共用的 Import 输入形状）。</summary>
    private static string WriteCaseLog(string caseRoot, string fileName, string content)
    {
        var path = Path.Combine(caseRoot, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData("out-of-memory", "java.lang.OutOfMemoryError: Java heap space", "内存不足")]
    [InlineData("night-config", "com.electronwill.nightconfig.core.io.ParsingException: Not enough data available", "Night Config")]
    [InlineData("duplicate-mods", "DuplicateModsFoundException: Found a duplicate mod example.jar", "重复")]
    [InlineData("missing-dependency",
        "java.lang.RuntimeException: Mod resolution encountered an incompatible mod set!\n" +
        "Missing or unsupported mandatory dependencies:\n" +
        "\tMod ID: 'examplemod', Requested by: 'othermod', Expected range: '[1.0,)', Actual version: '[MISSING]'",
        "前置")]
    [InlineData("java-too-new", "Unsupported class file major version 65", "Java")]
    [InlineData("java-32bit", "Invalid maximum heap size: -Xmx4G\nThe specified size exceeds the maximum representable size.", "32 位")]
    [InlineData("opengl", "The driver does not appear to support OpenGL", "显卡驱动")]
    [InlineData("unknown",
        "[12:00:00] [main/INFO]: Loading Minecraft 1.20.1\n" +
        "[12:00:01] [main/INFO]: Preparing spawn area\n" +
        "[12:00:02] [Render thread/WARN]: Something unexpected happened\n" +
        "java.lang.RuntimeException: something unexpected happened",
        "很抱歉")]
    public void Corpus_cases_classify_identically_to_baseline(string caseName, string log, string expectedText)
    {
        //语料 8 例：与 PCLA.BehaviorTests CrashCases 同日志、同期望（契约面 = Import/Prepare/Analyze/GetAnalyzeResult）
        var logDir = Path.Combine(_root, "corpus-" + caseName, "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "latest.log");
        File.WriteAllText(logPath, log);

        var analyzer = CreateAnalyzer();
        analyzer.Import(logPath);
        Assert.True(analyzer.Prepare());
        analyzer.Analyze();

        var text = analyzer.GetAnalyzeResult(false);
        Assert.Contains(expectedText, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Crit2_mixin_rules_fire()
    {
        //Crit2 Mixin 规则（旧 :589-645）：Mod 名称匹配（:600-605）与 JSON 名称回退（:607-611）两路
        var caseRoot = Path.Combine(_root, "crit2-mixin");
        Directory.CreateDirectory(caseRoot);

        //构造 1：「from mod X] from」形状命中 Mod 名（:600），TryAnalyzeModName 无反查源时原样回退（:843-847）
        var analyzer = CreateAnalyzer();
        analyzer.Import(WriteCaseLog(caseRoot, "latest.log",
            "[12:00:00] [main/INFO]: Launching Minecraft\n" +
            "[12:00:01] [main/ERROR] [mixin/]: Mixin apply failed from mod examplemod] from MixinProcessor\n" +
            "org.spongepowered.asm.mixin.transformer.MixinApplyError: bad target\n" +
            "[12:00:02] [main/INFO]: Stopping"));
        Assert.True(analyzer.Prepare());
        analyzer.Analyze();
        Assert.True(analyzer.CrashReasons.ContainsKey(CrashReason.ModMixin失败));
        Assert.Equal(["examplemod"], analyzer.CrashReasons[CrashReason.ModMixin失败]);
        Assert.Contains("examplemod", analyzer.GetAnalyzeResult(false), StringComparison.Ordinal);

        //构造 2：无 Mod 名命中时的 mixin 配置名反查（:607-609 的 mixins→mixin 归一化）
        var analyzer2 = CreateAnalyzer();
        analyzer2.Import(WriteCaseLog(caseRoot, "latest.log",
            "[12:00:00] [main/INFO]: Loading Minecraft\n" +
            "[mixin/]: Mixin prepare failed (examplemod.mixins.json)\n" +
            "[12:00:02] [main/INFO]: Stopping"));
        Assert.True(analyzer2.Prepare());
        analyzer2.Analyze();
        Assert.True(analyzer2.CrashReasons.ContainsKey(CrashReason.ModMixin失败));
        Assert.Equal(["examplemod"], analyzer2.CrashReasons[CrashReason.ModMixin失败]);
    }

    [Fact]
    public void Prepare_classifies_five_file_kinds_with_truncation()
    {
        //五类分流（旧 :139-302）与截断魔数（200/100、300/700、1500/500、1000/0）逐字
        var analyzer = CreateAnalyzer();
        var zipPath = _root + "crash-input.zip";
        WriteZip(zipPath,
            ("hs_err_pid1.log", Lines("hs", 350)),
            ("crash-report.txt", Lines("crash", 1200)),
            ("latest.log", Lines("mc", 2500)),
            ("debug.log", Lines("debug", 1500)),
            ("other.log", Lines("other", 30)),
            ("notes.txt", Lines("notes", 5)));
        analyzer.Import(zipPath);
        //解压产物时间戳同刻：显式区分，令 HsErr/CrashReport 的「最新文件」判定（旧 :201-211）可断言
        File.SetLastWriteTime(analyzer.TempFolder + "Temp\\hs_err_pid1.log", new DateTime(2026, 1, 1, 12, 0, 1));
        File.SetLastWriteTime(analyzer.TempFolder + "Temp\\crash-report.txt", new DateTime(2026, 1, 1, 12, 0, 2));

        Assert.True(analyzer.Prepare());

        //分桶：五类各有归宿，互不串桶
        Assert.NotNull(analyzer.LogMc);
        Assert.NotNull(analyzer.LogMcDebug);
        Assert.NotNull(analyzer.LogHs);
        Assert.NotNull(analyzer.LogCrash);
        Assert.DoesNotContain("debug-", analyzer.LogMc);
        Assert.DoesNotContain("other-", analyzer.LogMc);
        Assert.DoesNotContain("notes-", analyzer.LogMc);
        Assert.DoesNotContain("mc-", analyzer.LogMcDebug);
        Assert.DoesNotContain("crash-", analyzer.LogHs);
        Assert.DoesNotContain("hs-", analyzer.LogCrash);

        //截断魔数逐字：hs 200/100、crash 300/700、mc 1500/500、debug 1000/0
        AssertHeadTail(analyzer.LogMc!, "mc", 2500, 1500, 500);
        AssertHeadTail(analyzer.LogHs!, "hs", 350, 200, 100);
        AssertHeadTail(analyzer.LogCrash!, "crash", 1200, 300, 700);
        AssertHeadTailZeroTail(analyzer.LogMcDebug!, "debug", 1500, 1000);

        //OutputFiles 收齐五份；ExtraReportFile（notes.txt）按旧 :189 的 SelectType 遍历缺口不入 OutputFiles——逐字保留
        Assert.Equal(["crash-report.txt", "debug.log", "hs_err_pid1.log", "latest.log", "other.log"],
            analyzer.OutputFiles.Select(PathUtils.GetLastPart).OrderBy(static s => s).ToArray());

        //仅额外日志时全部视作 Minecraft 日志（旧 :183-186 的重投影）
        var analyzer2 = CreateAnalyzer();
        WriteZip(_root + "extra-only.zip", ("other.log", Lines("only", 10)));
        analyzer2.Import(_root + "extra-only.zip");
        Assert.True(analyzer2.Prepare());
        Assert.Contains("only-0000", analyzer2.LogMc, StringComparison.Ordinal);
        Assert.Null(analyzer2.LogMcDebug);
        Assert.Null(analyzer2.LogHs);
        Assert.Null(analyzer2.LogCrash);
    }

    [Fact]
    public void StackKeyword_analysis_extracts_mod_id()
    {
        //堆栈分析（旧 :674-733）：前缀黑名单（:687-692）与词黑名单（:711-719）两态 + 关键词回退（:425-433）。
        //! /FATAL] 捕获正则（:411）的 . 不跨行，模态文本须与真实日志同形：报错类路径在 FATAL 行内，紧随其后为「[」开头的行
        const string fatalLog =
            "[12:00:00] [main/INFO]: Loading Minecraft 1.20.1\n" +
            "[12:00:01] [main/FATAL] [mixin/]: com.example.coolmod.client.RenderTickHandler.onRender crashed at com.mumfrey.liteloader.core.LiteLoader.init in net.minecraftforge.common.ForgeVersion.checkVersion\n" +
            "[12:00:02] [main/INFO]: Exiting";

        //态 1：无崩溃报告/Debug 可反查 —— 关键词原样回退；com.mumfrey/net.minecraftforge 前缀被剔除
        var caseRoot = Path.Combine(_root, "stack-keyword");
        Directory.CreateDirectory(caseRoot);
        var analyzer = CreateAnalyzer();
        analyzer.Import(WriteCaseLog(caseRoot, "latest.log", fatalLog));
        Assert.True(analyzer.Prepare());
        analyzer.Analyze();
        Assert.True(analyzer.CrashReasons.ContainsKey(CrashReason.堆栈分析发现关键字));
        Assert.Equal(["example", "coolmod"], analyzer.CrashReasons[CrashReason.堆栈分析发现关键字]);

        //态 2：debug.log 提供「valid mod file」行 —— 关键词反查为 Mod 文件名（旧 :800-826）
        var analyzer2 = CreateAnalyzer();
        WriteZip(_root + "stack-with-debug.zip",
            ("latest.log", fatalLog),
            ("debug.log", "Forge: Found valid mod file CoolMod-1.0.jar with {coolmod} mods - versions {1.0}"));
        analyzer2.Import(_root + "stack-with-debug.zip");
        Assert.True(analyzer2.Prepare());
        analyzer2.Analyze();
        Assert.True(analyzer2.CrashReasons.ContainsKey(CrashReason.堆栈分析发现Mod名称));
        Assert.Equal(["CoolMod-1.0.jar"], analyzer2.CrashReasons[CrashReason.堆栈分析发现Mod名称]);
    }

    /// <summary>生成 i → "{prefix}-{i:D4}" 的 n 行唯一内容（去重/截断形状可精确断言）。</summary>
    private static string Lines(string prefix, int count)
        => string.Join("\n", Enumerable.Range(0, count).Select(i => $"{prefix}-{i:D4}"));

    /// <summary>构建 zip（条目按序写入）；Import 的压缩包分支（旧 :93-99）以此构造输入。</summary>
    private static void WriteZip(string zipPath, params (string EntryName, string Content)[] entries)
    {
        using var stream = new FileStream(zipPath, FileMode.Create);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name);
            using var entryStream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            entryStream.Write(bytes, 0, bytes.Length);
        }
    }

    /// <summary>校验 GetHeadTailLines（旧 :306-330）的头/尾截断形状：头 HeadLines 行 + 尾 TailLines 行，行尾统一 \n。</summary>
    private static void AssertHeadTail(string log, string prefix, int total, int head, int tail)
    {
        var lines = log.Split('\n');
        Assert.Equal(head + tail + 1, lines.Length); //每行补 \n，结尾多一个空段
        Assert.Equal($"{prefix}-0000", lines[0]);
        Assert.Equal($"{prefix}-{head - 1:D4}", lines[head - 1]);
        Assert.Equal($"{prefix}-{total - tail:D4}", lines[head]);
        Assert.Equal($"{prefix}-{total - 1:D4}", lines[head + tail - 1]);
    }

    /// <summary>校验 Tail=0 的逐字行为（旧 :316-322）：尾循环在检查阈值前先插入一行，收尾多出一行原文末行。</summary>
    private static void AssertHeadTailZeroTail(string log, string prefix, int total, int head)
    {
        var lines = log.Split('\n');
        Assert.Equal(head + 2, lines.Length); //头 head 行 + 尾循环先插的原文末行 + 结尾空段
        Assert.Equal($"{prefix}-{head - 1:D4}", lines[head - 1]);
        Assert.Equal($"{prefix}-{total - 1:D4}", lines[head]);
    }
}