using MeloongCore;
using PCLCS.Validate;
using System.Collections.ObjectModel;
using static PCLCS.Validate.Validate;
using ValidateRule = PCLCS.Validate.Validate;

namespace PCLCS.Tests.Validate;

/// <summary>
/// 输入验证类族测试（PCLCS.Validate：基类 + 14 派生类 + 聚合静态，自 ModValidate.vb:8-424 逐字移植）。
/// 错误消息与 VB 原文逐字对照；怪癖（Len(Nothing)=0、ErrorMessage 自愈副作用、8.3 正则左端点整串捕获、
/// FolderPath 子段保留名大小写敏感、盘根冒号保留尾斜线等）均以断言锁定，防止后续"顺手修复"。
/// 消费形态抽样覆盖 47 处消费点（VB 代码构造 + XAML 无参构造/属性赋值 + 聚合调用）。
/// </summary>
public class ValidateTests : IDisposable
{
    private readonly string _root;

    public ValidateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclcs.validate." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        //清理临时目录；失败（占用/权限）仅放弃清理，不影响测试结果
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>取一个当前已就绪的盘符（形如 C:\），FolderPath 盘符语义测试用。</summary>
    private static string ExistingDrive()
        => Environment.GetLogicalDrives().FirstOrDefault(DirectoryUtils.Exists) ?? throw new InvalidOperationException("无已就绪盘符");

    #region 聚合入口（旧 ModValidate 模块函数 :8-16）

    [Fact]
    public void Agg_EmptyRules_Pass()
        => Assert.Equal("", ValidateAll("x", []));

    [Fact]
    public void Agg_AllPass_ReturnsEmpty()
    {
        var rules = new Collection<ValidateRule> { new ValidateNullable(), new ValidateLength(1, 5) };
        Assert.Equal("", ValidateAll("abc", rules));
    }

    [Fact]
    public void Agg_FirstFail_StopsAndReturns() //失败即停，后续规则不再执行
    {
        var reached = false;
        var rules = new Collection<ValidateRule>
        {
            new ValidateFunc(static _ => "错"),
            new ValidateFunc(_ => { reached = true; return ""; })
        };
        Assert.Equal("错", ValidateAll("x", rules));
        Assert.False(reached);
    }

    [Fact]
    public void Agg_NullReturn_InterruptsAndPasses() //规则返回 Nothing → 直接通过
        => Assert.Equal("", ValidateAll("x", [new ValidateFunc(static _ => null!)]));

    [Fact]
    public void Agg_NullText_LengthRuleUsesLenSemantics() //Len(Nothing)=0 → 走"至少"分支（VB 语义 1:1）
        => Assert.Equal("长度至少需 1 个字符！", ValidateAll(null!, [new ValidateLength(1, 5)]));

    [Fact]
    public void Agg_NullRules_Throws() //For Each Nothing → NRE（VB 同）
        => Assert.Throws<NullReferenceException>(static () => ValidateAll("x", null!));

    #endregion

    #region 消费形态抽样（47 处消费点）

    [Fact]
    public void Consumption_PageSelectLeft_MixedRules() //PageSelectLeft.xaml.vb:160/:371 形态
    {
        var rules = new Collection<ValidateRule> { new ValidateNullOrWhiteSpace(), new ValidateLength(1, 30), new ValidateExcept([">", "|"]) };
        Assert.Equal("输入内容不能为空！", ValidateAll(" ", rules));
        Assert.Equal("长度最长为 30 个字符！", ValidateAll(new string('a', 31), rules));
        Assert.Equal("输入内容不能包含 >", ValidateAll("a>b", rules));
        Assert.Equal("输入内容不能包含 |", ValidateAll("a|b", rules));
        Assert.Equal("", ValidateAll("正常名称", rules));
    }

    [Fact]
    public void Consumption_PageSelectLeft_SameRule() //PageSelectLeft.xaml.vb:315 形态（命名实参 IgnoreCase）
    {
        var rules = new Collection<ValidateRule> { new ValidateSame("Potato", "请输入 “Potato”", ignoreCase: true) };
        Assert.Equal("", ValidateAll("PoTatO", rules));
        Assert.Equal("请输入 “Potato”", ValidateAll("Tomato", rules));
    }

    [Fact]
    public void Consumption_PageLinkMain_IntegerRule() //PageLinkMain.xaml.vb:98 形态
        => Assert.Equal("不可低于 1024！", ValidateAll("1023", [new ValidateInteger(1024, 65535)]));

    [Fact]
    public void Consumption_FormMain_HttpRule() //FormMain.xaml.vb:582、PageLoginAuth:83、PageLoginAuthSkin:82 形态
    {
        Assert.Equal("", new ValidateHttp().Validate("https://authlib.example.com/yggdrasil"));
        Assert.NotEqual("", new ValidateHttp().Validate("authlib.example.com"));
    }

    [Fact]
    public void Consumption_ModModpack_FolderName() //ModModpack.vb:129 等 6 处形态
    {
        var folder = Directory.CreateDirectory(_root + "consumption-versions").FullName;
        Assert.Equal("", new ValidateFolderName(folder).Validate("新版本名"));
    }

    [Fact]
    public void Consumption_PageDownloadModify_IgnoreList() //PageDownloadModify.vb:319、PageInstanceOverall:115 形态（命名实参 IgnoreList）
    {
        var folder = Directory.CreateDirectory(_root + "consumption-rename").FullName;
        Directory.CreateDirectory(Path.Combine(folder, "OldName"));
        Directory.CreateDirectory(Path.Combine(folder, "Sub"));
        var rule = new ValidateFolderName(folder, ignoreList: ["OldName"]);
        Assert.Equal("", ValidateAll("OldName", [rule])); //忽略列表内不查重
        Assert.Equal("不可与现有文件夹重名！", ValidateAll("SUB", [rule])); //忽略大小写查重命中
    }

    [Fact]
    public void Consumption_PageInstanceSetup_FuncRule() //PageInstanceSetup.xaml.vb:24 形态
    {
        var rules = new Collection<ValidateRule> { new ValidateFunc(static str => string.IsNullOrWhiteSpace(str) ? "不能为空" : "") };
        Assert.Equal("不能为空", ValidateAll(" ", rules));
        Assert.Equal("", ValidateAll(">=17", rules));
    }

    [Fact]
    public void Consumption_XamlElementShapes() //XAML 形态：无参构造 + 属性赋值（PageSetupLaunch/PageSetupUI/PageInstanceSetup/PageSetupSystem）
    {
        //! 豁免：XAML 属性元素要求真实 Collection 实例（经属性 setter 添加子项），集合表达式替换会改变被测面
        Assert.Equal("输入内容不能包含 \"", new ValidateExcept { Excepts = new Collection<string> { "\"", "“", "”" } }.Validate("a\"b"));
        Assert.Equal("", new ValidateInteger { Min = 1, Max = 99999 }.Validate("99999"));
        Assert.Equal("不可超过 99999！", new ValidateInteger { Min = 1, Max = 99999 }.Validate("100000"));
        Assert.Equal("长度必须为 32 个字符！", new ValidateLength { Min = 32, Max = 32 }.Validate("short"));
        Assert.Null(new ValidateNullable().Validate("")); //空 → 返回 Nothing（中断通过），由聚合放行
        Assert.Equal("", new ValidateHttp().Validate("http://x.y/"));
        Assert.Equal("", new ValidateFolderPath { UseMinecraftCharCheck = false }.Validate(ExistingDrive() + "正常路径"));
    }

    [Fact]
    public void Consumption_MyTextBox_AggregateCall() //MyTextBox.vb:226 形态：ModValidate.Validate(Text, ValidateRules)
    {
        var rules = new Collection<ValidateRule> { new ValidateNullOrWhiteSpace(), new ValidateLength(1, 30) };
        Assert.Equal("", ValidateAll("合法输入", rules));
        Assert.Equal("输入内容不能为空！", ValidateAll("", rules));
    }

    #endregion

    #region ValidateNullable / ValidateNullOrEmpty / ValidateNullOrWhiteSpace

    [Theory]
    [InlineData(null, null)]  //null → 返回 Nothing（中断检查直接通过）
    [InlineData("", null)]    //空串 → Nothing
    [InlineData(" ", "")]     //仅空格非空 → 正常通过
    [InlineData("a", "")]
    public void ValidateNullable_Verbatim(string? input, string? expected)
        => Assert.Equal(expected, new ValidateNullable().Validate(input!));

    [Theory]
    [InlineData(null, "输入内容不能为空！")]
    [InlineData("", "输入内容不能为空！")]
    [InlineData(" ", "")] //不包括全空格检查（逐字）
    [InlineData("a", "")]
    public void ValidateNullOrEmpty_Verbatim(string? input, string? expected)
        => Assert.Equal(expected, new ValidateNullOrEmpty().Validate(input!));

    [Theory]
    [InlineData(null, "输入内容不能为空！")]
    [InlineData("", "输入内容不能为空！")]
    [InlineData(" \t ", "输入内容不能为空！")]
    [InlineData("a", "")]
    public void ValidateNullOrWhiteSpace_Verbatim(string? input, string? expected)
        => Assert.Equal(expected, new ValidateNullOrWhiteSpace().Validate(input!));

    #endregion

    #region ValidateFunc

    [Fact]
    public void ValidateFunc_ReturnsFuncResultVerbatim()
    {
        var rule = new ValidateFunc(static str => str.Length > 2 ? "太长" : "");
        Assert.Equal("", rule.Validate("ab"));
        Assert.Equal("太长", rule.Validate("abc"));
    }

    [Fact]
    public void ValidateFunc_NullResultPassthrough()
        => Assert.Null(new ValidateFunc(static _ => null!).Validate("x"));

    #endregion

    #region ValidateRegex

    [Theory]
    [InlineData("abc123", "")]
    [InlineData("abc", "正则检查失败！")]
    public void ValidateRegex_Default(string input, string expected)
        => Assert.Equal(expected, new ValidateRegex("\\d+").Validate(input));

    [Fact]
    public void ValidateRegex_CustomDescription()
        => Assert.Equal("必须为数字", new ValidateRegex("\\d+", "必须为数字").Validate("abc"));

    [Fact]
    public void ValidateRegex_XamlShape() //无参构造 + 属性赋值（XAML 形态）
        => Assert.Equal("", new ValidateRegex { Regex = "^a+$" }.Validate("aaa"));

    [Fact]
    public void ValidateRegex_InvalidPattern_SwallowsToFalse() //RegexCheck 捕获异常返回 False → 报告描述
        => Assert.Equal("正则检查失败！", new ValidateRegex("[").Validate("abc"));

    [Fact]
    public void ValidateRegex_NullRegexProperty_SwallowsToFalse() //Regex 属性为 Nothing → RegexCheck 内部捕获 → False
        => Assert.Equal("正则检查失败！", new ValidateRegex().Validate("abc"));

    #endregion

    #region ValidateHttp

    [Theory]
    [InlineData("http://pcl2.cn", "")]
    [InlineData("https://x.y", "")]
    [InlineData("https://x.y/", "")] //尾部斜线先裁一根
    [InlineData("https://x.y//", "")] //裁一根后仍以 :// 前缀命中（正则只查前缀，逐字）
    [InlineData("HTTP://x.y", "输入的网址无效！")] //正则大小写敏感（逐字）
    [InlineData("ftp://x", "输入的网址无效！")]
    [InlineData("", "输入的网址无效！")]
    [InlineData("httpxxx", "输入的网址无效！")]
    [InlineData(null, "输入的网址无效！")] //EndsWithF 对 Nothing 安全；RegexCheck 捕获 → False
    public void ValidateHttp_Verbatim(string? input, string expected)
        => Assert.Equal(expected, new ValidateHttp().Validate(input!));

    #endregion

    #region ValidateInteger

    [Theory]
    [InlineData("5", "")]
    [InlineData("10", "")] //边界：= Max 通过
    [InlineData("1", "")] //边界：= Min 通过
    [InlineData("11", "不可超过 10！")]
    [InlineData("0", "不可低于 1！")]
    [InlineData("-5", "不可低于 1！")]
    [InlineData("abc", "请输入一个整数！")]
    [InlineData("", "请输入一个整数！")]
    [InlineData("123456789", "不可超过 10！")] //9 位可解析
    [InlineData("1234567890", "请输入一个大小合理的数字！")] //超 9 位先拦截
    [InlineData("abcdefghijkl", "请输入一个大小合理的数字！")] //长度检查先于解析（顺序锁定）
    public void ValidateInteger_Range(string input, string expected)
        => Assert.Equal(expected, new ValidateInteger(1, 10).Validate(input));

    [Fact]
    public void ValidateInteger_NullStr_Nre() //怪癖锁定：长度检查先于 TryParse，Str.Length 对 Nothing → NRE（VB 同）
        => Assert.Throws<NullReferenceException>(static () => new ValidateInteger(1, 10).Validate(null!));

    #endregion

    #region ValidateLength

    [Theory]
    [InlineData("abc", "")]
    [InlineData("ab", "长度必须为 3 个字符！")] //Min=Max 分支先于超长判断（顺序锁定）
    [InlineData("abcd", "长度必须为 3 个字符！")] //超长也报"必须为"
    [InlineData(null, "长度必须为 3 个字符！")] //Len(Nothing)=0（VB 语义 1:1）
    public void ValidateLength_ExactRange(string? input, string expected)
        => Assert.Equal(expected, new ValidateLength(3, 3).Validate(input!));

    [Theory]
    [InlineData("abcdef", "长度最长为 5 个字符！")]
    [InlineData("", "长度至少需 1 个字符！")]
    [InlineData("abc", "")]
    public void ValidateLength_Range(string input, string expected)
        => Assert.Equal(expected, new ValidateLength(1, 5).Validate(input));

    [Fact]
    public void ValidateLength_DefaultMax()
        => Assert.Equal("长度至少需 2 个字符！", new ValidateLength(2).Validate("a"));

    #endregion

    #region ValidateExcept

    [Theory]
    [InlineData("xa", "输入内容不能包含 a")]
    [InlineData("xb", "输入内容不能包含 b")]
    [InlineData("xyz", "")]
    [InlineData("", "")]
    public void ValidateExcept_FirstHitWins(string input, string expected)
        => Assert.Equal(expected, new ValidateExcept(new[] { "a", "b" }).Validate(input));

    [Fact]
    public void ValidateExcept_IgnoreCase() //IndexOfF(..., True)
        => Assert.Equal("输入内容不能包含 a", new ValidateExcept(["a"]).Validate("ABC"));

    [Fact]
    public void ValidateExcept_CharEnumerableCtor() //无效字符数组 & "!;" 形态 → 字符串走 IEnumerable(Of Char) 重载
        => Assert.Equal("文件夹名不可包含 | 字符！",
            new ValidateExcept(new string(Path.GetInvalidFileNameChars()) + "!;", "文件夹名不可包含 % 字符！").Validate("a|b"));

    [Fact]
    public void ValidateExcept_CollectionCtor() //Collection 重载本身即被测面，不换集合表达式
    {
        var rule = new ValidateExcept(new Collection<string> { "x" }, "含 %");
        Assert.Equal("含 x", rule.Validate("ax"));
        Assert.Equal("", rule.Validate("ay"));
    }

    [Fact]
    public void ValidateExcept_NullErrorMessage_SelfHeals() //怪癖锁定：ErrorMessage 为 Nothing 时就地赋 "" 并返回 ""；属性被副作用改写
    {
        var rule = new ValidateExcept(["x"]) { ErrorMessage = null! };
        Assert.Equal("", rule.Validate("ax"));
        Assert.Equal("", rule.ErrorMessage); //自愈副作用（VB 语义 1:1）
    }

    #endregion

    #region ValidateExceptSame

    [Theory]
    [InlineData("potato", "输入内容不能为 potato")]
    [InlineData("potatoX", "")]
    public void ValidateExceptSame_Single(string input, string expected)
        => Assert.Equal(expected, new ValidateExceptSame("potato").Validate(input));

    [Theory]
    [InlineData("POTATO", "输入内容不能为 potato")] //消息中 % 替换为 Excepts 条目（非输入值）
    [InlineData("potato", "输入内容不能为 potato")]
    public void ValidateExceptSame_IgnoreCase(string input, string expected)
        => Assert.Equal(expected, new ValidateExceptSame("potato", "输入内容不能为 %", true).Validate(input));

    [Fact]
    public void ValidateExceptSame_CaseSensitiveDefault() //默认 IgnoreCase=False（逐字）
        => Assert.Equal("", new ValidateExceptSame("potato").Validate("POTATO"));

    [Fact]
    public void ValidateExceptSame_EnumerableCtor()
        => Assert.Equal("输入内容不能为 CON", new ValidateExceptSame(new[] { "CON", "NUL" }).Validate("CON"));

    [Fact]
    public void ValidateExceptSame_NullStr_ReportsNull() //Nothing → Replace("%", "null")
        => Assert.Equal("输入内容不能为 null", new ValidateExceptSame("x").Validate(null!));

    [Fact]
    public void ValidateExceptSame_ParameterlessNullStr_Nre() //怪癖锁定：无参构造 + Nothing 实参 → ErrorMessage 为 Nothing → NRE（VB 同）
        => Assert.Throws<NullReferenceException>(static () => new ValidateExceptSame().Validate(null!));

    #endregion

    #region ValidateSame

    [Theory]
    [InlineData("Required", "")]
    [InlineData("Other", "输入内容有误")]
    public void ValidateSame_Default(string input, string expected)
        => Assert.Equal(expected, new ValidateSame("Required").Validate(input));

    [Fact]
    public void ValidateSame_IgnoreCase() //String.Compare(..., True) = 0
        => Assert.Equal("", new ValidateSame("Required", "输入内容有误", true).Validate("rEqUiReD"));

    [Fact]
    public void ValidateSame_NullErrorMessage_ReturnsNothing() //返回 Nothing 由聚合放行（MyMsgBoxInput 消费形态锁定）
        => Assert.Null(new ValidateSame("y") { ErrorMessage = null! }.Validate("x"));

    #endregion

    #region ValidateFolderName

    [Fact]
    public void FolderName_DuplicateCheck_IgnoreCase() //ctor 枚举目录查重
    {
        var folder = Directory.CreateDirectory(_root + "fn-dup").FullName;
        Directory.CreateDirectory(Path.Combine(folder, "Existing"));
        var rule = new ValidateFolderName(folder);
        Assert.Equal("不可与现有文件夹重名！", rule.Validate("Existing"));
        Assert.Equal("不可与现有文件夹重名！", rule.Validate("EXISTING")); //ValidateExceptSame IgnoreCase=True
        Assert.Equal("", rule.Validate("NewName"));
    }

    [Fact]
    public void FolderName_IgnoreList() //ctor 第三参
    {
        var folder = Directory.CreateDirectory(_root + "fn-ignore").FullName;
        Directory.CreateDirectory(Path.Combine(folder, "Keep"));
        Directory.CreateDirectory(Path.Combine(folder, "Sub"));
        var rule = new ValidateFolderName(folder, true, ["Keep"]);
        Assert.Equal("", rule.Validate("Keep"));
        Assert.Equal("不可与现有文件夹重名！", rule.Validate("SUB"));
    }

    [Fact]
    public void FolderName_NonexistentFolder_NoDuplicates() //文件夹不存在 → 枚举为空 → 不查重
        => Assert.Equal("", new ValidateFolderName(_root + "fn-not-exists").Validate("x"));

    [Fact]
    public void FolderName_Parameterless_NoPathIgnore() //无参构造：PathIgnore 为 Nothing → 不查重
        => Assert.Equal("", new ValidateFolderName().Validate("Existing"));

    [Theory]
    [InlineData(null, "输入内容不能为空！")]
    [InlineData("", "输入内容不能为空！")]
    [InlineData(" x", "文件夹名不能以空格开头！")]
    [InlineData("x ", "文件夹名不能以空格结尾！")]
    [InlineData("x.", "文件夹名不能以小数点结尾！")]
    [InlineData("a|b", "文件夹名不可包含 | 字符！")]
    [InlineData("a!b", "文件夹名不可包含 ! 字符！")] //MC 字符检查默认开
    [InlineData("CON", "文件夹名不可为 CON！")]
    [InlineData("con", "文件夹名不可为 CON！")] //保留字忽略大小写
    [InlineData("COM0", "文件夹名不可为 COM0！")]
    [InlineData("CONX", "")]
    [InlineData("ab~1", "文件夹名不能包含这一特殊格式！")] //NTFS 8.3（#4505）
    [InlineData("a~1", "")] //正则要求 ~ 前至少 2 字符
    [InlineData("ab~x", "")]
    public void FolderName_Battery(string? input, string expected)
        => Assert.Equal(expected, new ValidateFolderName(_root + "fn-battery").Validate(input!));

    [Fact]
    public void FolderName_Length100()
    {
        var rule = new ValidateFolderName(_root + "fn-len");
        Assert.Equal("", rule.Validate(new string('a', 100)));
        Assert.Equal("长度最长为 100 个字符！", rule.Validate(new string('a', 101)));
    }

    [Fact]
    public void FolderName_UseMinecraftCharCheck_Off() //构造开关
        => Assert.Equal("", new ValidateFolderName(_root + "fn-mc", false).Validate("a!b"));

    [Fact]
    public void FolderName_SpaceBeforeReserved() //检查顺序：空格 → 长度 → 小数点 → 字符 → 保留字 → 8.3 → 查重
        => Assert.Equal("文件夹名不能以空格开头！", new ValidateFolderName(_root + "fn-order").Validate(" CON"));

    [Fact]
    public void FolderName_FolderDeletedAfterCtor_NoError() //ctor 后删除文件夹：懒枚举对已删目录静默为空（DirectoryUtils 共享行为，VB 侧同源同构）→ 不查重直接通过
    {
        var folder = Directory.CreateDirectory(_root + "fn-catch").FullName;
        var rule = new ValidateFolderName(folder);
        Directory.Delete(folder, true);
        Assert.Equal("", rule.Validate("x"));
    }

    #endregion

    #region ValidateFileName

    [Fact]
    public void FileName_DuplicateCheck() //ParentFolder 属性赋值后枚举文件查重（ctor 的 Name 形参为死参怪癖，不参与查重）
    {
        var parent = Directory.CreateDirectory(_root + "fname").FullName;
        File.WriteAllText(Path.Combine(parent, "a.txt"), "");
        Assert.Equal("不可与现有文件重名！", new ValidateFileName { ParentFolder = parent }.Validate("a.txt"));
        Assert.Equal("不可与现有文件重名！", new ValidateFileName { ParentFolder = parent }.Validate("A.TXT")); //默认 IgnoreCase=True
        Assert.Equal("", new ValidateFileName { ParentFolder = parent, IgnoreCase = false }.Validate("A.TXT")); //关忽略大小写
        Assert.Equal("", new ValidateFileName { ParentFolder = parent }.Validate("b.txt"));
    }

    [Fact]
    public void FileName_NoParent_NoDuplicateCheck() //ParentFolder 默认 Nothing → 不查重
        => Assert.Equal("", new ValidateFileName().Validate("a.txt"));

    [Fact]
    public void FileName_ParentMissing_RequireExists() //默认要求父文件夹存在
    {
        var missing = _root + "fname-missing\\";
        var rule = new ValidateFileName { ParentFolder = missing };
        Assert.Equal($"父文件夹不存在：{missing}", rule.Validate("x"));
        rule.RequireParentFolderExists = false;
        Assert.Equal("", rule.Validate("x"));
    }

    [Fact]
    public void FileName_LengthUsesNamePlusParent() //怪癖：长度按 Str & ParentFolder 合并计算
        => Assert.Equal("长度最长为 253 个字符！", new ValidateFileName { ParentFolder = new string('x', 300) }.Validate("a"));

    [Theory]
    [InlineData(null, "输入内容不能为空！")]
    [InlineData(" x", "文件名不能以空格开头！")]
    [InlineData("x ", "文件名不能以空格结尾！")]
    [InlineData("x.", "文件名不能以小数点结尾！")]
    [InlineData("a|b", "文件名不可包含 | 字符！")]
    [InlineData("a!b", "文件名不可包含 ! 字符！")]
    [InlineData("CON", "文件名不可为 CON！")]
    [InlineData("ab~1", "文件名不能包含这一特殊格式！")]
    [InlineData("b.txt", "")]
    public void FileName_Battery(string? input, string expected)
        => Assert.Equal(expected, new ValidateFileName().Validate(input!));

    [Fact]
    public void FileName_UseMinecraftCharCheck_Off() //构造第二参
        => Assert.Equal("", new ValidateFileName("x", false).Validate("a!b"));

    #endregion

    #region ValidateFolderPath

    [Fact]
    public void FolderPath_DriveRoot_WithTrailingSlash() //RemoveSlashSuffix 得 "X:" 以冒号结尾 → 保留原串 → 盘根相等放行（顺序怪癖）
        => Assert.Equal("", new ValidateFolderPath().Validate(ExistingDrive()));

    [Fact]
    public void FolderPath_BareDriveColon_Fails() //"X:"（无尾分隔）→ 驱动器等值与前缀均不中 → 报头错误
        => Assert.Equal("文件夹路径头存在错误！", new ValidateFolderPath().Validate(ExistingDrive().TrimEnd('\\')));

    [Fact]
    public void FolderPath_UnderDrive_Pass()
    {
        var drive = ExistingDrive();
        Assert.Equal("", new ValidateFolderPath().Validate(drive + "正常\\子目录"));
        Assert.Equal("", new ValidateFolderPath().Validate(drive + "正常/子目录")); //斜线统一
        Assert.Equal("", new ValidateFolderPath().Validate(drive + "正常\\子目录\\")); //尾斜线去除
    }

    [Fact]
    public void FolderPath_RelativePath_HeadError()
        => Assert.Equal("文件夹路径头存在错误！", new ValidateFolderPath().Validate("相对路径"));

    [Fact]
    public void FolderPath_MissingDriveLetter_HeadError()
    {
        var used = Environment.GetLogicalDrives().Select(static d => d[0]).ToHashSet();
        var letter = "DEFGHIJKLMNOPQRSTUVWXYZ".First(c => !used.Contains(c));
        Assert.Equal("文件夹路径头存在错误！", new ValidateFolderPath().Validate(letter + ":\\x"));
    }

    [Fact]
    public void FolderPath_MacPrefix_SkipsDriveAndFirstSegment() //\\Mac\ 特例：跳过盘符检查且子段从第 2 段起检
    {
        Assert.Equal("", new ValidateFolderPath().Validate("\\\\Mac\\正常"));
        Assert.Equal("文件夹名不可为 CON！", new ValidateFolderPath().Validate("\\\\Mac\\CON"));
        Assert.Equal("", new ValidateFolderPath().Validate("\\\\Mac\\con")); //子段保留字检查大小写敏感（怪癖锁定）
        Assert.Equal("文件夹路径存在错误！", new ValidateFolderPath().Validate("\\\\Mac\\\\x")); //中段为空
        Assert.Equal("文件夹路径头存在错误！", new ValidateFolderPath().Validate("\\\\Mac\\")); //怪癖锁定：尾斜线先被去除 → 前缀失配 → 报头错误
    }

    [Fact]
    public void FolderPath_EmptySegment_SwappedMessage() //子段为空 → 统一报"文件夹路径存在错误！"（不透传空串原因）
        => Assert.Equal("文件夹路径存在错误！", new ValidateFolderPath().Validate(ExistingDrive() + "\\a"));

    [Theory]
    [InlineData("foo|bar", "路径中存在无效字符！")]
    [InlineData(" foo", "文件夹名不能以空格开头！")] //子段头部空格
    [InlineData("foo ", "文件夹名不能以空格结尾！")] //子段尾部空格
    [InlineData("foo.", "文件夹名不能以小数点结尾！")]
    [InlineData("CON", "文件夹名不可为 CON！")]
    [InlineData("a!b", "路径中存在无效字符！")] //MC 字符检查默认开
    [InlineData("正常", "")]
    public void FolderPath_SegmentBattery(string segment, string expected)
        => Assert.Equal(expected, new ValidateFolderPath().Validate(ExistingDrive() + segment));

    [Fact]
    public void FolderPath_83Check_BeforeDriveCheck_WholeStringInMessage() //8.3 在盘符检查之前；RegexSeek 左端点捕获整串（逐字）
    {
        var input = ExistingDrive() + "ab~1";
        Assert.Equal($"文件夹名不能包含 {input} 这一特殊格式！", new ValidateFolderPath().Validate(input));
    }

    [Fact]
    public void FolderPath_Length254()
        => Assert.Equal("长度最长为 254 个字符！", new ValidateFolderPath().Validate(ExistingDrive() + new string('a', 252)));

    [Fact]
    public void FolderPath_UseMinecraftCharCheck_Off() //构造第一参
        => Assert.Equal("", new ValidateFolderPath(false).Validate(ExistingDrive() + "a!b"));

    #endregion
}