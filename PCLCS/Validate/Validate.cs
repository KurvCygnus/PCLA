using System.Collections.ObjectModel;

namespace PCLCS.Validate;

/// <summary>
/// 输入验证规则族（自宿主层 ModValidate.vb:23-424 逐字移植，一文件多类按旧文件布局）。
/// 聚合入口 <see cref="ValidateAll"/>（旧模块函数 :8-16；VB 侧函数名 Validate——C# 成员名不得与封闭类型同名 CS0542，故作此别记）随迁为本静态，
/// 宿主层模块壳同名转发，消费点零改动。
/// 验证语义：符合返回空字符串；不符合返回错误原因；返回 null（VB Nothing）表示中断检查并直接通过。
/// RegexCheck/RegexSeek/StartsWithF/EndsWithF/IndexOfF/Lower/Upper 与 DirectoryUtils/PathUtils/Logger 均为 MeloongCore 已共享面。
/// </summary>
public abstract class Validate
{
    /// <summary>
    /// 进行输入验证，并返回错误原因。
    /// 如果没有错误则返回空字符串。
    /// </summary>
    //! 逐字移植豁免：旧 :9-15 逐字——Check 声明返回 string 但运行时可返回 Nothing（VB 规则语义），空判不可删
    public static string ValidateAll(string text, IEnumerable<Validate> validateRules)
    {
        var result = "";
        foreach (var validateRule in validateRules)
        {
            result = validateRule.Check(text);
            if (result is null) return "";
            if (result != "") return result;
        }
        return result;
    }

    /// <summary>
    /// 验证某字符串是否符合验证要求。若符合，返回空字符串；若不符合，返回错误原因；若需要中断检查并直接通过，返回 Nothing。
    /// </summary>
    //! 逐字移植豁免：旧 :27 MustOverride Function Validate(Str)——C# CS0542 禁止成员与封闭类型同名，虚方法改名 Check；
    //! 各派生类以非虚转发保留旧公共名 Validate（见各类同名方法），聚合与重写走 Check。
    public abstract string Check(string str);
}

/// <summary>
/// 若为空则直接通过检查。
/// </summary>
public class ValidateNullable : Validate
{
    //! 逐字移植豁免：旧 :35 显式无参构造（公共面逐字）
    public ValidateNullable() { }
    /// <summary>旧 :37 公共方法名 Validate（基类虚方法因 CS0542 改名 Check，本转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：VB Return Nothing（中断检查直接通过）——null 与空串均触发
    public override string Check(string str)
    {
        if (str is null || string.IsNullOrEmpty(str)) return null!;
        return "";
    }
}

/// <summary>
/// 不能为 Nothing 或空字符串（不包括全空格检查）。
/// </summary>
public class ValidateNullOrEmpty : Validate
{
    //! 逐字移植豁免：旧 :48 显式无参构造（公共面逐字）
    public ValidateNullOrEmpty() { }
    /// <summary>旧 :50 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：旧 :51-53 逐字——VB 允许 Nothing 传入，空判运行时可达
    public override string Check(string str)
    {
        if (str is null || string.IsNullOrEmpty(str)) return "输入内容不能为空！";
        return "";
    }
}

/// <summary>
/// 不能为 Nothing 或空字符串（包括全空格检查）。
/// </summary>
public class ValidateNullOrWhiteSpace : Validate
{
    //! 逐字移植豁免：旧 :61 显式无参构造（公共面逐字）
    public ValidateNullOrWhiteSpace() { }
    /// <summary>旧 :63 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：旧 :64-66 逐字——VB 允许 Nothing 传入，空判运行时可达
    public override string Check(string str)
    {
        if (str is null || string.IsNullOrWhiteSpace(str)) return "输入内容不能为空！";
        return "";
    }
}

/// <summary>
/// 自定义的 Validate。
/// </summary>
public class ValidateFunc : Validate
{
    private readonly Func<string, string> _func;
    public ValidateFunc(Func<string, string> f)
    {
        _func = f;
    }
    /// <summary>旧 :78 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：旧 :78-80 逐字（块体形状）
    public override string Check(string str)
    {
        return _func(str);
    }
}

/// <summary>
/// 必须满足正则表达式。
/// </summary>
public class ValidateRegex : Validate
{
    public string? Regex { get; set; }
    public string ErrorDescription { get; set; } = "正则检查失败！";
    //用于 XAML 初始化
    public ValidateRegex() { }
    public ValidateRegex(string regex, string errorDescription = "正则检查失败！")
    {
        Regex = regex;
        ErrorDescription = errorDescription;
    }
    /// <summary>旧 :96 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    public override string Check(string str)
    {
        //! 逐字移植豁免：Regex 允许为 Nothing（无参构造未赋值），RegexCheck 内部捕获异常返回 False——VB 同语义；if-return 形状逐字
        if (!str.RegexCheck(Regex!)) return ErrorDescription;
        return "";
    }
}

/// <summary>
/// 必须是一个完整网址。
/// </summary>
public class ValidateHttp : Validate
{
    //用于 XAML 初始化
    //! 逐字移植豁免：旧 :107-108 显式无参构造（公共面逐字）
    public ValidateHttp() { }
    /// <summary>旧 :109 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：旧 :110-113 逐字（Substring 与 if-return 形状）
    public override string Check(string str)
    {
        if (str.EndsWithF("/")) str = str.Substring(0, str.Length - 1);
        if (!str.RegexCheck("^(http[s]?)\\://")) return "输入的网址无效！";
        return "";
    }
}

/// <summary>
/// 必须为整数。
/// </summary>
public class ValidateInteger : Validate
{
    public int Min { get; set; }
    public int Max { get; set; } = int.MaxValue;
    //用于 XAML 初始化
    //! 逐字移植豁免：旧 :123-124 显式无参构造（公共面逐字）
    public ValidateInteger() { }
    public ValidateInteger(int min, int max)
    {
        Min = min;
        Max = max;
    }
    /// <summary>旧 :129 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：旧 :130-135 逐字（早退 if 形状）
    public override string Check(string str)
    {
        if (str.Length > 9) return "请输入一个大小合理的数字！";
        if (!int.TryParse(str, out var valed)) return "请输入一个整数！";
        if (valed > Max) return "不可超过 " + Max + "！";
        if (valed < Min) return "不可低于 " + Min + "！";
        return "";
    }
}

/// <summary>
/// 长度限制。
/// </summary>
public class ValidateLength : Validate
{
    //! 逐字移植豁免：旧 :144 显式默认值（与缺省相同，保留对照）
    public int Min { get; set; } = 0;
    public int Max { get; set; } = int.MaxValue;
    //用于 XAML 初始化
    //! 逐字移植豁免：旧 :146-147 显式无参构造（公共面逐字）
    public ValidateLength() { }
    public ValidateLength(int min, int max = int.MaxValue)
    {
        Min = min;
        Max = max;
    }
    /// <summary>旧 :152 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：VB 侧运行库 Len(Nothing)=0，以 ?. ?? 0 对齐；旧 :153-156 if-return 形状逐字
    public override string Check(string str)
    {
        var len = str?.Length ?? 0;
        if (len != Max && Max == Min) return $"长度必须为 {Max} 个字符！";
        if (len > Max) return $"长度最长为 {Max} 个字符！";
        if (len < Min) return $"长度至少需 {Min} 个字符！";
        return "";
    }
}

/// <summary>
/// 不能包含某些特定字符串。忽略大小写。
/// </summary>
public class ValidateExcept : Validate
{
    public Collection<string> Excepts { get; set; } = new();
    public string? ErrorMessage { get; set; }
    //用于 XAML 初始化
    //! 逐字移植豁免：旧 :167-169 显式无参构造（公共面逐字）
    public ValidateExcept()
    {
        ErrorMessage = "输入内容不能包含 %";
    }
    public ValidateExcept(Collection<string> excepts, string errorMessage = "输入内容不能包含 %")
    {
        Excepts = excepts;
        ErrorMessage = errorMessage;
    }
    public ValidateExcept(IEnumerable<string> excepts, string errorMessage = "输入内容不能包含 %")
        : this(new Collection<string>(new List<string>(excepts)), errorMessage)
    {
    }
    public ValidateExcept(IEnumerable<char> excepts, string errorMessage = "输入内容不能包含 %")
        : this(new Collection<string>(excepts.Select(static c => c.ToString()).ToList()), errorMessage)
    {
    }
    /// <summary>旧 :184 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：旧 :184-192 逐字——循环、反转 if 与自愈副作用形状
    public override string Check(string str)
    {
        foreach (var ch in Excepts)
        {
            if (str.IndexOfF(ch, true) >= 0)
            {
                //! 逐字移植豁免：ErrorMessage 为 Nothing 时就地赋 ""（自愈副作用保留）——VB 语义 1:1
                if (ErrorMessage is null) ErrorMessage = "";
                return ErrorMessage.Replace("%", ch);
            }
        }
        return "";
    }
}

/// <summary>
/// 不能与某些特定字符串相同。
/// </summary>
public class ValidateExceptSame : Validate
{
    public Collection<string> Excepts { get; set; } = new();
    public string? ErrorMessage { get; set; }
    //! 逐字移植豁免：旧 :203 显式默认值（与缺省相同，保留对照）
    public bool IgnoreCase { get; set; } = false;
    //! 逐字移植豁免：旧 :204-205 显式无参构造（公共面逐字）
    public ValidateExceptSame() { }
    public ValidateExceptSame(Collection<string> excepts, string errorMessage = "输入内容不能为 %", bool ignoreCase = false)
    {
        Excepts = excepts;
        ErrorMessage = errorMessage;
        IgnoreCase = ignoreCase;
    }
    public ValidateExceptSame(IEnumerable<string> excepts, string errorMessage = "输入内容不能为 %", bool ignoreCase = false)
        : this(new Collection<string>(new List<string>(excepts)), errorMessage, ignoreCase)
    {
    }
    public ValidateExceptSame(string except, string errorMessage = "输入内容不能为 %", bool ignoreCase = false)
        : this(new Collection<string> { except }, errorMessage, ignoreCase)
    {
    }
    /// <summary>旧 :221 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：ErrorMessage 允许为 Nothing（无参构造），此处 Replace 触发 NRE——VB 同语义；str 空判运行时可达（VB 允许 Nothing 传入），循环形状逐字
    public override string Check(string str)
    {
        if (str is null) return ErrorMessage!.Replace("%", "null");
        foreach (var ch in Excepts)
        {
            if (IgnoreCase)
            {
                if (str.Lower() == ch.Lower()) return ErrorMessage!.Replace("%", ch);
            }
            else
            {
                if (str.Equals(ch)) return ErrorMessage!.Replace("%", ch); //使用 = 不确定是否会忽略大小写
            }
        }
        return "";
    }
}

/// <summary>
/// 必须为特定字符串。
/// </summary>
public class ValidateSame : Validate
{
    public string? Required { get; set; }
    public string? ErrorMessage { get; set; }
    //! 逐字移植豁免：旧 :242 显式默认值（与缺省相同，保留对照）
    public bool IgnoreCase { get; set; } = false;
    //! 逐字移植豁免：旧 :243-244 显式无参构造（公共面逐字）
    public ValidateSame() { }
    public ValidateSame(string required, string errorMessage = "输入内容有误", bool ignoreCase = false)
    {
        Required = required;
        ErrorMessage = errorMessage;
        IgnoreCase = ignoreCase;
    }
    /// <summary>旧 :250 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：ErrorMessage 允许为 Nothing → 返回 Nothing 由聚合放行——VB 同语义
    public override string Check(string str)
    {
        return string.Compare(str, Required, IgnoreCase) == 0 ? "" : ErrorMessage!;
    }
}

/// <summary>
/// 对文件夹名的粗略的特化检测。
/// </summary>
public class ValidateFolderName : Validate
{
    public string? Folder { get; set; }
    public bool UseMinecraftCharCheck { get; set; } = true;
    private readonly IEnumerable<string>? _pathIgnore;
    private readonly List<string>? _ignoreList;
    public ValidateFolderName() { }
    public ValidateFolderName(string folder, bool useMinecraftCharCheck = true, List<string>? ignoreList = null)
    {
        Folder = folder;
        UseMinecraftCharCheck = useMinecraftCharCheck;
        //! 逐字移植豁免：旧 :270 On Error Resume Next——枚举失败时 _pathIgnore 保持 Nothing
        try
        {
            _pathIgnore = DirectoryUtils.EnumerateDirectories(folder);
        }
        catch
        {
            //忽略（旧 :270 On Error Resume Next）
        }
        _ignoreList = ignoreList;
    }
    /// <summary>旧 :274 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：旧 :274-311 逐字——早退 if、空判与查重循环形状
    public override string Check(string str)
    {
        try
        {
            //检查是否为空
            var lengthCheck = new ValidateNullOrWhiteSpace().Validate(str);
            if (lengthCheck != "") return lengthCheck;
            //检查空格
            if (str.StartsWithF(" ")) return "文件夹名不能以空格开头！";
            if (str.EndsWithF(" ")) return "文件夹名不能以空格结尾！";
            //检查长度
            lengthCheck = new ValidateLength(1, 100).Validate(str);
            if (lengthCheck != "") return lengthCheck;
            //检查尾部小数点
            if (str.EndsWithF(".")) return "文件夹名不能以小数点结尾！";
            //检查特殊字符
            var charactCheck = new ValidateExcept(new string(Path.GetInvalidFileNameChars()) + (UseMinecraftCharCheck ? "!;" : ""), "文件夹名不可包含 % 字符！").Validate(str);
            if (charactCheck != "") return charactCheck;
            //检查特殊字符串
            var invalidStrCheck = new ValidateExceptSame(["CON", "PRN", "AUX", "CLOCK$", "NUL", "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"], "文件夹名不可为 %！", true).Validate(str);
            if (invalidStrCheck != "") return invalidStrCheck;
            //检查 NTFS 8.3 文件名（#4505）
            if (str.RegexCheck(".{2,}~\\d")) return "文件夹名不能包含这一特殊格式！";
            //检查文件夹重名
            var arr = new List<string>();
            if (_pathIgnore != null)
            {
                foreach (var folderPath in _pathIgnore)
                {
                    var folderName = PathUtils.GetLastPart(folderPath);
                    if (_ignoreList == null || !_ignoreList.Contains(folderName, StringComparer.OrdinalIgnoreCase)) arr.Add(folderName);
                }
            }
            var sameNameCheck = new ValidateExceptSame(arr, "不可与现有文件夹重名！", true).Validate(str);
            if (sameNameCheck != "") return sameNameCheck;
            return "";
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "检查文件夹名出错");
            return "错误：" + ex.Message;
        }
    }
}

/// <summary>
/// 对文件名的粗略的特化检测。
/// </summary>
public class ValidateFileName : Validate
{
    public string? Name { get; set; }
    public bool UseMinecraftCharCheck { get; set; } = true;
    public bool IgnoreCase { get; set; } = true;
    public string? ParentFolder { get; set; } = null;
    public bool RequireParentFolderExists { get; set; } = true;
    public ValidateFileName() { }
    public ValidateFileName(string name, bool useMinecraftCharCheck = true, bool ignoreCase = true)
    {
        Name = name;
        IgnoreCase = ignoreCase;
        UseMinecraftCharCheck = useMinecraftCharCheck;
    }
    /// <summary>旧 :331 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：旧 :331-368 逐字——早退 if、空判与查重循环形状
    public override string Check(string str)
    {
        try
        {
            //检查是否为空
            var lengthCheck = new ValidateNullOrWhiteSpace().Validate(str);
            if (lengthCheck != "") return lengthCheck;
            //检查空格
            if (str.StartsWithF(" ")) return "文件名不能以空格开头！";
            if (str.EndsWithF(" ")) return "文件名不能以空格结尾！";
            //检查长度
            //! 逐字移植豁免：VB & 对 Nothing 视为空串（Str & If(ParentFolder, "")）；Str 此时必非 Nothing（空检查已先行返回），ParentFolder 可为 Nothing
            lengthCheck = new ValidateLength(1, 253).Validate(str + (ParentFolder ?? ""));
            if (lengthCheck != "") return lengthCheck;
            //检查尾部小数点
            if (str.EndsWithF(".")) return "文件名不能以小数点结尾！";
            //检查特殊字符
            var charactCheck = new ValidateExcept(new string(Path.GetInvalidFileNameChars()) + (UseMinecraftCharCheck ? "!;" : ""), "文件名不可包含 % 字符！").Validate(str);
            if (charactCheck != "") return charactCheck;
            //检查特殊字符串
            var invalidStrCheck = new ValidateExceptSame(["CON", "PRN", "AUX", "CLOCK$", "NUL", "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"], "文件名不可为 %！", true).Validate(str);
            if (invalidStrCheck != "") return invalidStrCheck;
            //检查 NTFS 8.3 文件名（#4505）
            if (str.RegexCheck(".{2,}~\\d")) return "文件名不能包含这一特殊格式！";
            //检查文件重名
            if (ParentFolder != null)
            {
                var dirInfo = DirectoryUtils.GetInfo(ParentFolder);
                if (dirInfo.Exists)
                {
                    var sameNameCheck = new ValidateExceptSame(dirInfo.EnumerateFiles("*").Select(static f => f.Name), "不可与现有文件重名！", IgnoreCase).Validate(str);
                    if (sameNameCheck != "") return sameNameCheck;
                }
                else
                {
                    if (RequireParentFolderExists) return $"父文件夹不存在：{ParentFolder}";
                }
            }
            return "";
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "检查文件名出错");
            return "错误：" + ex.Message;
        }
    }
}

/// <summary>
/// 要求输入一个可用的文件夹路径。
/// </summary>
public class ValidateFolderPath : Validate
{
    public bool UseMinecraftCharCheck { get; set; } = true;
    //! 逐字移植豁免：旧 :377-378 显式无参构造（公共面逐字）
    public ValidateFolderPath() { }
    public ValidateFolderPath(bool useMinecraftCharCheck)
    {
        UseMinecraftCharCheck = useMinecraftCharCheck;
    }
    /// <summary>旧 :382 公共方法名 Validate（转发保留旧公共名）。</summary>
    public string Validate(string str) => Check(str);
    //! 逐字移植豁免：旧 :382-423 逐字——GoTo/标签、逐次 Split 与反转 if 形状
    public override string Check(string str)
    {
        //去除尾部斜线，统一为 \
        str = str.Replace("/", "\\");
        //! 逐字移植豁免：以冒号结尾（盘根）时保留原串不去尾斜线——两次 RemoveSlashSuffix 与旧 :385 逐字对照
        if (!PathUtils.RemoveSlashSuffix(str).EndsWithF(":")) str = PathUtils.RemoveSlashSuffix(str);
        //检查是否为空
        var lengthCheck = new ValidateNullOrWhiteSpace().Validate(str);
        if (lengthCheck != "") return lengthCheck;
        //检查长度
        lengthCheck = new ValidateLength(1, 254).Validate(str);
        if (lengthCheck != "") return lengthCheck;
        //检查 NTFS 8.3 文件名（#4505）
        var match = str.RegexSeek(".{2,}~\\d");
        if (match != null) return $"文件夹名不能包含 {match} 这一特殊格式！";
        //检查开头
        if (str.StartsWithF("\\\\Mac\\")) goto Fin;
        foreach (var drive in Environment.GetLogicalDrives().Where(static p => DirectoryUtils.Exists(p)))
        {
            var driveName = drive.Upper().First() + ":\\";
            if (str.Upper() == driveName) return "";
            if (str.StartsWithF(driveName, true)) goto Fin;
        }
        return "文件夹路径头存在错误！";
    //对首层以外的路径检查
    Fin:
        for (var i = str.StartsWithF("\\\\Mac\\") ? 2 : 1; i <= str.Split('\\').Length - 1; i++)
        {
            var subStr = str.Split('\\')[i];
            //检查是否为空
            var subLengthCheck = new ValidateNullOrWhiteSpace().Validate(subStr);
            if (subLengthCheck != "") return "文件夹路径存在错误！";
            //检查特殊字符
            var charactCheck = new ValidateExcept(new string(Path.GetInvalidFileNameChars()) + (UseMinecraftCharCheck ? "!;" : ""), "路径中存在无效字符！").Validate(subStr);
            if (charactCheck != "") return charactCheck;
            //检查头部空格
            if (subStr.StartsWithF(" ")) return "文件夹名不能以空格开头！";
            if (subStr.EndsWithF(" ")) return "文件夹名不能以空格结尾！";
            //检查尾部小数点
            if (subStr.EndsWithF(".")) return "文件夹名不能以小数点结尾！";
            //检查特殊字符串
            var invalidStrCheck = new ValidateExceptSame(["CON", "PRN", "AUX", "CLOCK$", "NUL", "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"], "文件夹名不可为 %！").Validate(subStr);
            if (invalidStrCheck != "") return invalidStrCheck;
        }
        return "";
    }
}