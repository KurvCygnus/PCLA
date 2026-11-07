using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.VisualBasic;   //* 差分对照基线：仅测试工程引用，PCLCS 源保持零 VB 运行时依赖
using PCLCS.Common;

namespace PCLCS.Tests.Common;

/// <summary>
/// CommonUtils 基础工具族测试：ini 回合 trip（含 #3616 重复键与冒号键）、UUID 计数器连续性、
/// 随机/洗牌/排序等值保序（W2 实证）、搜索相似度排序、Val 边界（与 net48 VB 运行时差分对照）、
/// Task 超时两态与进程启动。
/// </summary>
public class CommonUtilsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pclcs.common." + Guid.NewGuid().ToString("N"));

    public CommonUtilsTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    #region ini 家族

    [Fact]
    public void Ini_roundtrip_write_read_delete()
    {
        var ini = Path.Combine(_root, "round.ini");
        CommonUtils.WriteIni(ini, "Name", "PCL");
        Assert.Equal("PCL", CommonUtils.ReadIni(ini, "Name"));
        Assert.True(CommonUtils.HasIniKey(ini, "Name"));
        Assert.False(CommonUtils.HasIniKey(ini, "Missing"));
        Assert.Equal("默认", CommonUtils.ReadIni(ini, "Missing", "默认"));
        CommonUtils.DeleteIniKey(ini, "Name");
        Assert.False(CommonUtils.HasIniKey(ini, "Name"));
        Assert.Equal("", CommonUtils.ReadIni(ini, "Name"));
        Assert.True(File.Exists(ini));   //删除键后仍全量重写（空内容文件）
        Assert.Equal("", File.ReadAllText(ini));
    }

    [Fact]
    public void Ini_duplicate_key_last_wins()
    {
        var ini = Path.Combine(_root, "dup.ini");
        File.WriteAllText(ini, "a:1\r\nb:2\r\na:3\r\n");
        CommonUtils.IniClearCache(ini);
        Assert.Equal("3", CommonUtils.ReadIni(ini, "a"));   //#3616：重复键后值胜
        Assert.Equal("2", CommonUtils.ReadIni(ini, "b"));
        CommonUtils.WriteIni(ini, "a", "4");
        Assert.Equal("4", CommonUtils.ReadIni(ini, "a"));   //写后收敛为单键
    }

    [Fact]
    public void Ini_cache_is_busted_only_by_clear()
    {
        var ini = Path.Combine(_root, "cache.ini");
        File.WriteAllText(ini, "k:v1\r\n");
        Assert.Equal("v1", CommonUtils.ReadIni(ini, "k"));
        File.WriteAllText(ini, "k:v2\r\n");   //外部改动，未清缓存 → 读旧值
        Assert.Equal("v1", CommonUtils.ReadIni(ini, "k"));
        CommonUtils.IniClearCache(ini);
        Assert.Equal("v2", CommonUtils.ReadIni(ini, "k"));
    }

    [Fact]
    public void Ini_colon_key_throws_is_swallowed()
    {
        var ini = Path.Combine(_root, "colon.ini");
        CommonUtils.WriteIni(ini, "a:b", "v");   //键含冒号抛错被吞（旧 :535/:561-563）
        Assert.False(File.Exists(ini));
        Assert.False(CommonUtils.HasIniKey(ini, "a:b"));
    }

    [Fact]
    public void Ini_write_strips_line_endings()
    {
        var ini = Path.Combine(_root, "newline.ini");
        CommonUtils.WriteIni(ini, "a\r\nb", "v\r\nw");   //键与值内换行符均被移除（旧 :536-537）
        Assert.Equal("vw", CommonUtils.ReadIni(ini, "ab"));
        Assert.Equal("", CommonUtils.ReadIni(ini, "a\r\nb"));   //读取面不剥离换行，原键名查询落空
    }

    [Fact]
    public void Ini_write_same_value_keeps_file_untouched()
    {
        var ini = Path.Combine(_root, "noop.ini");
        File.WriteAllText(ini, "k:v1\r\nEXTRA\r\n");
        Assert.Equal("v1", CommonUtils.ReadIni(ini, "k"));   //入缓存
        CommonUtils.WriteIni(ini, "k", "v1");                 //同值 → 不重写（旧 :545-546）
        Assert.Equal("k:v1\r\nEXTRA\r\n", File.ReadAllText(ini));   //无冒号杂行原样保留
        CommonUtils.WriteIni(ini, "k", "v2");                 //改值 → 全量重写
        Assert.Equal("k:v2\r\n", File.ReadAllText(ini));
    }

    [Fact]
    public void Ini_missing_file_uses_default_and_write_creates()
    {
        var ini = Path.Combine(_root, "missing.ini");
        Assert.Equal("", CommonUtils.ReadIni(ini, "k"));
        Assert.False(CommonUtils.HasIniKey(ini, "k"));
        CommonUtils.WriteIni(ini, "k", "v");
        Assert.Equal("v", CommonUtils.ReadIni(ini, "k"));
    }

    [Fact]
    public void Ini_shorthand_resolves_to_base_folder()
    {
        //简写名解析到 Paths.BaseThenName{名字}.ini（不存在 → 默认值；读取面不落盘）
        Assert.Equal("", CommonUtils.ReadIni("pclcs-tests-nonexistent", "k"));
        Assert.False(CommonUtils.HasIniKey("pclcs-tests-nonexistent", "k"));
        CommonUtils.IniClearCache("pclcs-tests-nonexistent");   //简写与全路径解析为同键（旧 :471-474）
    }

    #endregion

    #region UUID 与时间

    [Fact]
    public void GetUuid_counter_is_continuous_and_threadsafe()
    {
        var first = CommonUtils.GetUuid();
        Assert.Equal(first + 1, CommonUtils.GetUuid());
        var all = new ConcurrentBag<int>();
        Parallel.For(0, 8, _ =>
        {
            for (var j = 0; j < 500; j++) all.Add(CommonUtils.GetUuid());
        });
        Assert.Equal(4000, all.Distinct().Count());   //全局计数器：并发下仍不重复
    }

    [Fact]
    public void GetTimeMs_is_monotonic()
    {
        var t1 = CommonUtils.GetTimeMs();
        Thread.Sleep(20);
        Assert.True(CommonUtils.GetTimeMs() > t1);
    }

    [Fact]
    public void GetUnixTimestampUtc_matches_clock()
    {
        Assert.True(Math.Abs(CommonUtils.GetUnixTimestampUtc() - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) <= 5);
    }

    #endregion

    #region 随机与排序

    [Fact]
    public void RandomInteger_is_inclusive_on_both_ends()
    {
        for (var i = 0; i < 2000; i++)
        {
            var value = CommonUtils.RandomInteger(-2, 3);
            Assert.InRange(value, -2, 3);
        }
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(5, CommonUtils.RandomInteger(5, 5));
            Assert.Equal(-3, CommonUtils.RandomInteger(-3, -3));
        }
        var seen = new HashSet<int>();
        for (var i = 0; i < 1000; i++) seen.Add(CommonUtils.RandomInteger(0, 3));
        Assert.Equal([0, 1, 2, 3], seen.OrderBy(static x => x));   //两端均可取到
    }

    [Fact]
    public void Shuffle_keeps_multiset_and_source_intact()
    {
        var source = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };
        var shuffled = source.Shuffle().ToList();
        Assert.Equal(8, shuffled.Count);
        Assert.Equal(source.OrderBy(static x => x), shuffled.OrderBy(static x => x));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], source);   //原列表不变
    }

    [Fact]
    public void RandomOne_returns_member()
    {
        var list = new List<string> { "a", "b", "c" };
        Assert.Contains(CommonUtils.RandomOne(list), list);
    }

    [Fact]
    public void SortByComparison_keeps_equal_order_and_drains_source()
    {
        var list = new List<(int Key, string Id)> { (1, "a"), (2, "b1"), (2, "b2"), (3, "c") };
        var sorted = list.SortByComparison(static (x, y) => x.Key > y.Key);
        Assert.Equal(["c", "b1", "b2", "a"], sorted.Select(static x => x.Id));   //等值保序（W2 实证）
        Assert.Empty(list);   //旧实现逐个 Remove 排空原列表
    }

    #endregion

    #region 搜索

    private static SearchEntry<string> Entry(string item, params string[] aliases)
        => new() { Item = item, SearchSource = aliases.Select(static alias => new SearchSource(alias)).ToList() };

    [Fact]
    public void Search_orders_absolute_right_first_then_similarity()
    {
        var entries = new List<SearchEntry<string>> { Entry("A", "Alpha"), Entry("B", "Alpha Beta"), Entry("C", "Gamma") };
        var result = CommonUtils.Search(entries, "alpha", minBlurSimilarity: 0.5);
        Assert.Equal(["A", "B"], result.Select(static x => x.Item));   //完全匹配按相似度排序，C 相似度不足剔除
        Assert.All(result, static x => Assert.True(x.AbsoluteRight));
    }

    [Fact]
    public void Search_caps_blur_results_and_keeps_tie_order()
    {
        var entries = new List<SearchEntry<string>>
        {
            Entry("A", "Alpha"), Entry("D1", "AlpXxx"), Entry("D2", "AlpYyy"), Entry("D3", "AlpZzz"),
        };
        var result = CommonUtils.Search(entries, "alpha", maxBlurCount: 2, minBlurSimilarity: 0.5);
        Assert.Equal(["A", "D1", "D2"], result.Select(static x => x.Item));   //完全匹配 + 2 条模糊；同相似度保持原序
    }

    [Fact]
    public void Search_empty_entries_returns_empty()
    {
        Assert.Empty(CommonUtils.Search(new List<SearchEntry<string>>(), "alpha"));
    }

    #endregion

    #region Val

    [Theory]
    [InlineData("123", 123)]
    [InlineData(" 12 34", 1234)]        //内部空白跳过
    [InlineData("1e3", 1000)]
    [InlineData("1E-3", 0.001)]
    [InlineData("1d3", 1000)]           //D 指数（VB 双精度字面量风格）
    [InlineData("1D3", 1000)]
    [InlineData("1d-3", 0.001)]
    [InlineData(".5", 0.5)]
    [InlineData("-.5", -0.5)]
    [InlineData("+5", 5)]
    [InlineData("123abc", 123)]
    [InlineData("abc", 0)]
    [InlineData("", 0)]
    [InlineData(" ", 0)]
    [InlineData("\t", 0)]
    [InlineData("&H10", 16)]
    [InlineData("&hff", 255)]
    [InlineData("&H0", 0)]
    [InlineData("&O17", 15)]
    [InlineData("&o7", 7)]
    [InlineData("&", 0)]                //ModBase 包装特判
    [InlineData("&H", 0)]
    [InlineData("&O", 0)]
    [InlineData("&HZZ", 0)]
    [InlineData("0x10", 0)]
    [InlineData("1.5e", 1.5)]           //悬空指数丢弃
    [InlineData("1e+", 1)]
    [InlineData("1e", 1)]
    [InlineData("--1", 0)]
    [InlineData("+-1", 0)]
    [InlineData("1..2", 1)]
    [InlineData("5.5.5", 5.5)]
    [InlineData("12-", 12)]
    [InlineData("5-", 5)]
    [InlineData("-0", 0)]
    [InlineData("+ -.", 0)]
    [InlineData(".", 0)]
    [InlineData("1.", 1)]
    [InlineData(".e1", 0)]
    [InlineData("e1", 0)]
    [InlineData("E", 0)]
    [InlineData("1 234", 1234)]
    [InlineData("3.14", 3.14)]
    [InlineData("1,234", 1)]            //逗号截断
    [InlineData("١٢٣", 0)]              //非 ASCII 数字不解析
    [InlineData("&H1.5", 1)]            //&H 后遇非十六进制终止
    [InlineData("& 10", 0)]
    [InlineData("&H 10", 16)]           //&H 后空白跳过
    [InlineData("1e309", 0)]            //溢出异常被包装吞掉
    [InlineData("-1e309", 0)]
    [InlineData("1e-400", 0)]           //下溢为 0
    [InlineData("5%", 5)]               //VB6 类型后缀
    [InlineData("5&", 5)]
    [InlineData("12!", 12)]
    [InlineData("12@", 12)]
    [InlineData("1.5&", 0)]             //带小数取整后缀 → 类型不匹配异常 → 0
    [InlineData("1.5%", 0)]
    [InlineData("5&x", 5)]              //后缀字符即终止符
    public void Val_known_cases(string input, double expected)
    {
        Assert.Equal(expected, CommonUtils.Val(input), 12);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(true, -1)]
    [InlineData(false, 0)]
    [InlineData(5, 5)]
    [InlineData(-5, -5)]
    [InlineData(2.5, 2.5)]
    [InlineData(0.5, 0.5)]
    [InlineData('5', 5)]
    [InlineData('x', 0)]
    [InlineData('0', 0)]        //Val(Char)：'0' 亦为 0（旧 Conversion.Val(Char) 语义）
    [InlineData("42", 42)]
    public void Val_object_cases(object? input, double expected)
    {
        Assert.Equal(expected, CommonUtils.Val(input), 12);
    }

    [Fact]
    public void Val_hex_octal_wrap_semantics()
    {
        Assert.Equal(-1, CommonUtils.Val("&HFFFFFFFF"));                    //8 位十六进制按 Integer 回绕
        Assert.Equal(-2147483648, CommonUtils.Val("&H80000000"));
        Assert.Equal(4294967296, CommonUtils.Val("&H100000000"));           //9 位起按 Long
        Assert.Equal(9.223372036854776E18, CommonUtils.Val("&H7FFFFFFFFFFFFFFF"));
        Assert.Equal(-1, CommonUtils.Val("&HFFFFFFFFFFFFFFFF"));            //16 位按 Long 回绕
        Assert.Equal(-1, CommonUtils.Val("&H" + new string('F', 17)));      //17 位全 F 仍回绕
        Assert.Equal(0, CommonUtils.Val("&H1FFFFFFFFFFFFFFFF"));            //17 位非全 F 溢出 → 0
        Assert.Equal(-15, CommonUtils.Val("&HFFFFFFFFFFFFFFF1"));
        Assert.Equal(1073741823, CommonUtils.Val("&O7777777777"));
        Assert.Equal(8589934591, CommonUtils.Val("&O77777777777"));
        Assert.Equal(9.223372036854776E18, CommonUtils.Val("&O" + new string('7', 21)));
        Assert.Equal(9.223372036854776E18, CommonUtils.Val("&O" + new string('7', 27)));   //溢出饱和
        Assert.Equal(1.152921504606847E18, CommonUtils.Val("&O1" + new string('0', 21)));  //2^63 经 60 位累加器落点
        Assert.Equal(1073741823, CommonUtils.Val("&O7777777777&"));         //后缀 & 强制 Integer
    }

    [Fact]
    public void Val_matches_VB_runtime_on_random_inputs()
    {
        const string alphabet = "0123456789.,eEdD+-&HhOo \t\r%&!@xZ_";
        var rng = new Random(20261107);
        for (var round = 0; round < 50000; round++)
        {
            var length = rng.Next(0, 13);
            var chars = new char[length];
            for (var j = 0; j < length; j++) chars[j] = alphabet[rng.Next(alphabet.Length)];
            var input = new string(chars);
            double expected;
            try
            {
                expected = Conversion.Val(input);
            }
            catch
            {
                expected = 0;   //ModBase.Val 包装语义：异常归 0
            }
            var actual = CommonUtils.Val(input);
            Assert.True(expected.Equals(actual), $"'{input}': expected {expected:R}, got {actual:R}");
        }
    }

    #endregion

    #region 权限与进程

    [Fact]
    public void CheckPermission_distinguishes_writable_and_missing_folders()
    {
        Assert.True(CommonUtils.CheckPermission(_root));
        Assert.False(CommonUtils.CheckPermission(Path.Combine(_root, "not-exist")));
        Assert.False(CommonUtils.CheckPermission(""));
    }

    [Fact]
    public void ProcessStarter_starts_process_and_returns_handle()
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c exit 0",
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var process = ProcessStarter.Start(startInfo);
        process.WaitForExit(10000);
        Assert.Equal(0, process.ExitCode);
    }

    #endregion

    #region Task 超时（对照 NetRequest.cs:470-496 语义）

    [Fact]
    public void GetResultWithTimeout_returns_result_on_success()
    {
        using var cts = new CancellationTokenSource();
        Assert.Equal(42, CommonUtils.GetResultWithTimeout(Task.FromResult(42), cts, 5000));
        CommonUtils.GetResultWithTimeout(Task.CompletedTask, cts, 5000);   //无返回值重载
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public void GetResultWithTimeout_throws_and_cancels_on_timeout()
    {
        using var cts = new CancellationTokenSource();
        var pending = new TaskCompletionSource<int>();
        Assert.Throws<TimeoutException>(() => CommonUtils.GetResultWithTimeout(pending.Task, cts, 50));
        Assert.True(cts.IsCancellationRequested);   //超时同时取消令牌（旧 :767）
    }

    #endregion
}