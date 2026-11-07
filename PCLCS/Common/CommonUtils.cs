using System.Diagnostics;
using System.Globalization;

namespace PCLCS.Common;

/// <summary>
/// 基础工具族（自宿主层 ModBase.vb 收敛：ini 家族 :466-564、权限 :571-585、Val/引号 :602-616、
/// 搜索排序 :627-755 与 :959-970、Task 超时 :764-782、UUID/时间 :817-848、随机 :1182-1208）。
/// VB 侧经 ModBase 壳转发保持调用点零改动；W2/W3 私有端口（NetRequest/ProcessRunner/LoginCores）改指本文件消重。
/// </summary>
public static class CommonUtils
{

    #region ini

    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> IniCache = new();

    /// <summary>
    /// 清除某 ini 文件的运行时缓存。
    /// </summary>
    /// <param name="fileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
    public static void IniClearCache(string fileName)
    {
        if (!fileName.Contains(":\\"))
        {
            fileName = $"{Paths.BaseThenName}{fileName}.ini";
        }
        IniCache.Remove(fileName);
    }

    /// <summary>
    /// 获取 ini 文件缓存。如果没有，则新读取 ini 文件内容。
    /// 在文件不存在或读取失败时返回 null。
    /// </summary>
    /// <param name="fileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
    private static ConcurrentDictionary<string, string>? IniGetContent(string fileName)
    {
        try
        {
            //还原文件路径
            if (!fileName.Contains(":\\"))
            {
                fileName = $"{Paths.BaseThenName}{fileName}.ini";
            }
            //检索缓存
            if (IniCache.TryGetValue(fileName, out var cache))
            {
                return cache;
            }
            //读取文件
            if (!FileUtils.Exists(fileName))
            {
                return null;
            }
            var ini = new ConcurrentDictionary<string, string>();
            foreach (var line in FileUtils.ReadAsLines(fileName, true))
            {
                var index = line.IndexOfF(":");
                if (index > 0)
                {
                    ini[line[..index]] = line[(index + 1)..]; //可能会有重复键，见 #3616
                }
            }
            IniCache[fileName] = ini;
            return ini;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"生成 ini 文件缓存失败（{fileName}）", LogBehavior.Toast);
            return null;
        }
    }

    /// <summary>
    /// 读取 ini 文件。这可能会使用到缓存。
    /// </summary>
    /// <param name="fileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
    /// <param name="key">键。</param>
    /// <param name="defaultValue">没有找到键时返回的默认值。</param>
    public static string ReadIni(string fileName, string key, string defaultValue = "")
    {
        var content = IniGetContent(fileName);
        if (content is null)
        {
            return defaultValue;
        }
        return content.GetOrDefault(key, defaultValue)!;   //值经 IniGetContent 构造必非 null
    }

    /// <summary>
    /// 判断 ini 文件中是否包含某个键。这可能会使用到缓存。
    /// </summary>
    public static bool HasIniKey(string fileName, string key)
    {
        var content = IniGetContent(fileName);
        return content is not null && content.ContainsKey(key);
    }

    /// <summary>
    /// 从 ini 文件中移除某个键。这会更新缓存。
    /// </summary>
    public static void DeleteIniKey(string fileName, string key)
    {
        WriteIni(fileName, key, null);
    }

    /// <summary>
    /// 写入 ini 文件，这会更新缓存。
    /// 若 value 为 null，则删除该键。
    /// </summary>
    /// <param name="fileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
    /// <param name="key">键。</param>
    /// <param name="value">值。</param>
    public static void WriteIni(string fileName, string key, string? value)
    {
        try
        {
            //预处理
            if (key.Contains(":"))
            {
                throw new Exception($"尝试写入 ini 文件 {fileName} 的键名中包含了冒号：{key}");
            }
            key = key.ReplaceLineEndings("");
            value = value?.ReplaceLineEndings("");
            //获取目前文件
            var content = IniGetContent(fileName);
            if (content is null)
            {
                content = new ConcurrentDictionary<string, string>();
            }
            //更新值
            if (value is null)
            {
                if (!content.Remove(key))
                {
                    return; //无需处理
                }
            }
            else
            {
                if (content.TryGetValue(key, out var existingValue) && existingValue == value)
                {
                    return; //无需处理
                }
                content[key] = value;
            }
            //写入文件
            var fileContent = new StringBuilder();
            foreach (var pair in content)
            {
                fileContent.Append(pair.Key);
                fileContent.Append(":");
                fileContent.Append(pair.Value);
                fileContent.Append("\r\n");
            }
            if (!fileName.Contains(":\\"))
            {
                fileName = $"{Paths.BaseThenName}{fileName}.ini";
            }
            //处理相对路径
            fileName = fileName.Contains(":\\") ? fileName : Paths.Base + fileName;
            FileUtils.Write(fileName, fileContent.ToString());
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"写入文件失败（{fileName} → {key}:{value}）", LogBehavior.Toast);
        }
    }

    #endregion

    #region 权限检查

    /// <summary>
    /// 检查是否拥有某一文件夹的 I/O 权限。如果文件夹不存在，会返回 False。
    /// </summary>
    public static bool CheckPermission(string folder)
    {
        try
        {
            if (string.IsNullOrEmpty(folder))
            {
                return false;
            }
            folder = PathUtils.AddSlashSuffix(folder);
            if (folder.EndsWithF(":\\System Volume Information\\", true) || folder.EndsWithF(":\\$RECYCLE.BIN\\", true))
            {
                return false;
            }
            if (!DirectoryUtils.Exists(folder))
            {
                return false;
            }
            var testFilePath = $"{folder}CheckPermission{GetUuid()}.txt";
            FileUtils.Write(testFilePath, "临时文件，用于检测该文件夹的权限是否正常。");
            FileUtils.Delete(testFilePath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"没有对文件夹 {folder} 的权限，请尝试以管理员权限运行 PCL");
            return false;
        }
    }

    #endregion

    #region 文本

    /// <summary>中文弯引号（旧 vbLQ，ModBase.vb:602）。</summary>
    public const char VbLQ = (char)8220;

    /// <summary>中文弯引号（旧 vbRQ，ModBase.vb:603）。</summary>
    public const char VbRQ = (char)8221;

    /// <summary>
    /// 不会报错的 Val。如果输入有误，返回 0。
    /// </summary>
    //! 逐字移植豁免：PCLCS 不得引用 VB 运行时，Conversion.Val 的扫描器语义（.NET Framework 反编译逐行）在此手工移植：
    //! 空白集为空格/制表/换行/回车/全角空格（前后与内部均跳过）、前导 0 不入累积器、D 与 E 同为指数、
    //! 尾随 %&!@ 为 VB6 类型后缀、小数部分以“整数累积 ÷ 10^小数长”还原、&H/&O 按有效位数回绕。
    public static double Val(object? input)
    {
        if (input is null)
        {
            return 0;
        }
        try
        {
            if (input is string text && text == "&")
            {
                return 0; //旧 :612 特判（Conversion.Val("&") 会越界抛错）
            }
            return input switch
            {
                string value => ValString(value),
                char value => value is >= '1' and <= '9' ? value - '0' : 0,   //Conversion.Val(Char)：仅数字字符（'0' 亦为 0）
                bool value => value ? -1 : 0,   //IsNumeric(Boolean)=True → Conversions.ToDouble
                byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                    => Convert.ToDouble(input, CultureInfo.InvariantCulture),   //数值直转（文化无关）
                IConvertible => ValString(Convert.ToString(input, CultureInfo.CurrentCulture) ?? ""),   //Date 等经文化字符串回扫描
                _ => throw new ArgumentException("Val 输入类型不支持转换。"),   //非 IConvertible（数组等）→ VB 错误 438 → 包装归 0
            };
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Conversion.Val 的十进制扫描器（含 &amp;H/&amp;O 分派；语义见 <see cref="Val"/> 备注）。</summary>
    private static double ValString(string inputStr)
    {
        var length = inputStr.Length;
        var i = 0;
        while (i < length)
        {
            switch (inputStr[i])
            {
            case '\t':
            case '\n':
            case '\r':
            case ' ':
            case '\u3000':
                i++;
                continue;
            }
            break;
        }
        if (i >= length)
        {
            return 0.0;
        }
        var c = inputStr[i];
        if (c == '&')
        {
            return ValHexOrOct(inputStr, i + 1);   //"&" 位于末尾时与 VB 一致越界抛错
        }
        var negative = false;
        var seenDot = false;
        var seenExp = false;
        double mantissa = 0.0;
        c = inputStr[i];
        switch (c)
        {
        case '-':
            negative = true;
            i++;
            break;
        case '+':
            i++;
            break;
        }
        int digitCount = 0, fracStart = 0;
        while (i < length)
        {
            c = inputStr[i];
            switch (c)
            {
            case '\t':
            case '\n':
            case '\r':
            case ' ':
            case '\u3000':
                i++;
                continue;
            case '0':
                if (digitCount != 0 || seenDot)
                {
                    mantissa = mantissa * 10.0 + c - 48.0;   //前导 0 不入累积器
                    i++;
                    digitCount++;
                }
                else
                {
                    i++;
                }
                continue;
            case >= '1' and <= '9':
                mantissa = mantissa * 10.0 + c - 48.0;
                i++;
                digitCount++;
                continue;
            }
            switch (c)
            {
            case '.':
                i++;
                if (!seenDot)
                {
                    seenDot = true;
                    fracStart = digitCount;
                    continue;
                }
                break;
            case 'D':
            case 'E':
            case 'd':
            case 'e':
                seenExp = true;
                i++;
                break;
            }
            break;
        }
        var fracLen = 0;
        if (seenDot)
        {
            fracLen = digitCount - fracStart;
        }
        if (seenExp)
        {
            var expSignSeen = false;
            var expNegative = false;
            double exponent = 0.0;
            while (i < length)
            {
                c = inputStr[i];
                switch (c)
                {
                case '\t':
                case '\n':
                case '\r':
                case ' ':
                case '\u3000':
                    i++;
                    continue;
                case >= '0' and <= '9':
                    exponent = exponent * 10.0 + c - 48.0;
                    i++;
                    continue;
                }
                switch (c)
                {
                case '+':
                    if (!expSignSeen)
                    {
                        expSignSeen = true;
                        i++;
                        continue;
                    }
                    break;
                case '-':
                    if (!expSignSeen)
                    {
                        expSignSeen = true;
                        expNegative = true;
                        i++;
                        continue;
                    }
                    break;
                }
                break;
            }
            if (expNegative)
            {
                exponent += fracLen;
                mantissa *= Math.Pow(10.0, 0.0 - exponent);
            }
            else
            {
                exponent -= fracLen;
                mantissa *= Math.Pow(10.0, exponent);
            }
        }
        else if (seenDot && fracLen != 0)
        {
            mantissa /= Math.Pow(10.0, fracLen);
        }
        if (double.IsInfinity(mantissa))
        {
            throw new OverflowException();   //VB 错误 6
        }
        if (negative)
        {
            mantissa = 0.0 - mantissa;
        }
        switch (c)
        {
        case '%':
            if (fracLen > 0)
            {
                throw new InvalidCastException();   //VB 错误 13
            }
            mantissa = checked((short)Math.Round(mantissa));
            break;
        case '&':
            if (fracLen > 0)
            {
                throw new InvalidCastException();
            }
            mantissa = checked((int)Math.Round(mantissa));
            break;
        case '!':
            mantissa = (float)mantissa;
            break;
        case '@':
            mantissa = Convert.ToDouble(new decimal(mantissa));
            break;
        }
        return mantissa;
    }

    /// <summary>Conversion.Val 的 &amp;H/&amp;O 十六进制与八进制分支（有效位数回绕与 VB 字面量规则一致）。</summary>
    private static double ValHexOrOct(string inputStr, int i)
    {
        var length = inputStr.Length;
        var c = inputStr[i];
        checked
        {
            i++;
            long value = 0;
            var digits = 0;
            if (c == 'H' || c == 'h')
            {
                while (i < length && digits < 17)
                {
                    c = inputStr[i];
                    i++;
                    if (c is '\t' or '\n' or '\r' or ' ' or '\u3000')
                    {
                        continue;
                    }
                    int digit;
                    if (c == '0')
                    {
                        if (digits == 0)
                        {
                            continue;   //前导 0 不计位数
                        }
                        digit = 0;
                    }
                    else if (c is >= '1' and <= '9')
                    {
                        digit = c - '0';
                    }
                    else if (c is >= 'A' and <= 'F')
                    {
                        digit = c - '7';
                    }
                    else if (c is >= 'a' and <= 'f')
                    {
                        digit = c - 'W';
                    }
                    else
                    {
                        break;
                    }
                    if (digits == 15 && value > 576460752303423487L)
                    {
                        value = (value & 0x7FFFFFFFFFFFFFFL) * 16;   //第 16 位起按 Long 回绕
                        value |= long.MinValue;
                    }
                    else
                    {
                        value *= 16;
                    }
                    value += digit;
                    digits++;
                }
                if (digits == 16)
                {
                    i++;
                    if (i < length)
                    {
                        c = inputStr[i];   //恰 16 位时预读类型后缀
                    }
                }
                if (digits <= 8)
                {
                    if (digits > 4 || c == '&')
                    {
                        if (value > int.MaxValue)
                        {
                            value = int.MinValue + (value & 0x7FFFFFFF);   //≤8 位按 Integer 回绕
                        }
                    }
                    else if ((digits > 2 || c == '%') && value > short.MaxValue)
                    {
                        value = short.MinValue + (value & 0x7FFF);   //≤4 位按 Short 回绕
                    }
                }
                switch (c)
                {
                case '%':
                    value = unchecked((short)value);   //VB 整型窄化为截断（conv.i2，不检溢出）
                    break;
                case '&':
                    value = unchecked((int)value);
                    break;
                }
                return value;
            }
            if (c == 'O' || c == 'o')
            {
                while (i < length && digits < 22)
                {
                    c = inputStr[i];
                    i++;
                    if (c is '\t' or '\n' or '\r' or ' ' or '\u3000')
                    {
                        continue;
                    }
                    int digit;
                    if (c == '0')
                    {
                        if (digits == 0)
                        {
                            continue;
                        }
                        digit = 0;
                    }
                    else if (c is >= '1' and <= '7')
                    {
                        digit = c - '0';
                    }
                    else
                    {
                        break;
                    }
                    if (value >= 1152921504606846976L)   //≥2^60：60 位累加器回绕并恢复第 60 位
                    {
                        value = (value & 0xFFFFFFFFFFFFFFFL) * 8;
                        value |= 0x1000000000000000L;
                    }
                    else
                    {
                        value *= 8;
                    }
                    value += digit;
                    digits++;
                }
                if (digits == 22)
                {
                    i++;
                    if (i < length)
                    {
                        c = inputStr[i];
                    }
                }
                if (value <= 4294967296L)
                {
                    if (value > 65535 || c == '&')
                    {
                        if (value > int.MaxValue)
                        {
                            value = int.MinValue + (value & 0x7FFFFFFF);
                        }
                    }
                    else if ((value > 255 || c == '%') && value > short.MaxValue)
                    {
                        value = short.MinValue + (value & 0x7FFF);
                    }
                }
                switch (c)
                {
                case '%':
                    value = unchecked((short)value);
                    break;
                case '&':
                    value = unchecked((int)value);
                    break;
                }
                return value;
            }
            return 0.0;
        }
    }

    #endregion

    #region 搜索

    /// <summary>
    /// 获取搜索文本的相似度。
    /// </summary>
    /// <param name="source">被搜索的长内容。</param>
    /// <param name="query">用户输入的搜索文本。</param>
    private static double SearchSimilarity(string source, string query)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(query))
        {
            return 0;
        }
        int qp = 0;
        double lenSum = 0;
        var str = new StringBuilder(source.Length);
        str.Append(source.Lower().Replace(" ", ""));
        query = query.Lower().Replace(" ", "");
        var sourceLength = str.Length;
        var queryLength = query.Length; //用于计算最后因数的长度缓存
        if (queryLength == 0)
        {
            return 0;
        }
        while (qp < queryLength)
        {
            //对 qp 作为开始位置计算
            int sp = 0, lenMax = 0, spMax = 0;
            var currentSourceLength = str.Length;
            while (sp < currentSourceLength)
            {
                var len = 0;
                while (qp + len < queryLength && sp + len < currentSourceLength && str[sp + len] == query[qp + len])
                {
                    len += 1;
                }
                //存储 len
                if (len > lenMax)
                {
                    lenMax = len;
                    spMax = sp;
                }
                //根据结果增加 sp
                sp += len > 0 ? len : 1;
            }
            if (lenMax > 0)
            {
                str.Remove(spMax, lenMax); //将源中的对应字段移除
                //存储 lenSum
                var incWeight = Math.Pow(1.4, 3 + lenMax) - 3.6; //根据长度加成
                incWeight *= 1 + 0.3 * Math.Max(0, 3 - Math.Abs(qp - spMax)); //根据位置加成
                lenSum += incWeight;
            }
            qp += lenMax > 0 ? lenMax : 1;
        }
        //计算结果：重复字段量 × 源长度影响比例
        return lenSum / queryLength * (3 / Math.Sqrt(sourceLength + 15)) * (queryLength <= 2 ? 3 - queryLength : 1);
    }

    /// <summary>
    /// 获取多段文本加权后的相似度。
    /// </summary>
    private static double SearchSimilarityWeighted(IEnumerable<SearchSource> source, string query)
    {
        double totalWeight = 0;
        double sum = 0;
        foreach (var pair in source)
        {
            if (pair.Aliases.Any())
            {
                sum += pair.Aliases.Max(alias => SearchSimilarity(alias, query)) * pair.Weight;
            }
            totalWeight += pair.Weight;
        }
        return sum / totalWeight;
    }

    /// <summary>
    /// 进行多段文本加权搜索，获取相似度较高的数项结果。
    /// 在搜索时，会对每个文本源单独加权，但单个文本源内的多个别名只取最高的一个的相似度。
    /// 这会修改 entries 中每项的 Similarity 与 AbsoluteRight 字段。
    /// </summary>
    /// <param name="entries">候选项目。</param>
    /// <param name="query">用户输入的搜索文本。</param>
    /// <param name="maxBlurCount">返回的最大模糊结果数。</param>
    /// <param name="minBlurSimilarity">返回结果要求的最低相似度。</param>
    public static List<SearchEntry<T>> Search<T>(List<SearchEntry<T>> entries, string query, int maxBlurCount = 5, double minBlurSimilarity = 0.1)
    {
        //初始化
        var resultList = new List<SearchEntry<T>>();
        if (!entries.Any())
        {
            return resultList;
        }
        //进行搜索，获取相似信息
        var queryParts = query.Split(" ", true);
        var candidates = new List<SearchEntry<T>>();
        foreach (var entry in entries)
        {
            entry.Similarity = SearchSimilarityWeighted(entry.SearchSource, query);
            entry.AbsoluteRight =
                queryParts.All( //对于按空格分割的每一段
                    queryPart => entry.SearchSource.Any( //若与任意一个搜索源完全匹配，则标记为完全匹配项
                        source => source.Aliases.Any(
                            alias => alias.Replace(" ", "").ContainsIgnoreCase(queryPart))));
            if (entry.AbsoluteRight || entry.Similarity >= minBlurSimilarity)
            {
                candidates.Add(entry);
            }
        }
        //按照相似度进行排序
        candidates = candidates.SortByComparison(static (left, right) => left.AbsoluteRight != right.AbsoluteRight ? left.AbsoluteRight : left.Similarity > right.Similarity);
        //返回结果
        var blurCount = 0;
        foreach (var entry in candidates)
        {
            if (entry.AbsoluteRight)
            {
                resultList.Add(entry); //完全匹配，直接加入
            }
            else
            {
                if (blurCount == maxBlurCount)
                {
                    break;
                }
                resultList.Add(entry);
                blurCount += 1; //模糊结果计数
            }
        }
        return resultList;
    }

    #endregion

    #region Task 超时

    /// <summary>为 Task 设置超时，在超时时抛出 TimeoutException（ModBase.vb:764 扩展，自 NetRequest.cs 私有等价实现提升为公共版）。</summary>
    //* 既定例外：包装既有 C# 异步工具的阻塞等待（Task.WhenAny + GetAwaiter().GetResult()），不新引入异步面
    public static T GetResultWithTimeout<T>(this Task<T> targetTask, CancellationTokenSource tokenSource, int timeoutMs)
    {
        var delayTask = Task.Delay(timeoutMs);
        if (Task.WhenAny(targetTask, delayTask).ConfigureAwait(false).GetAwaiter().GetResult() != delayTask)
        {
            return targetTask.GetAwaiter().GetResult(); //VB 的 TargetTask.Run()
        }
        tokenSource.Cancel();
        throw new TimeoutException($"任务超时（{timeoutMs} ms）");
    }

    /// <summary>为 Task 设置超时，在超时时抛出 TimeoutException（ModBase.vb:775 扩展，自 NetRequest.cs 私有等价实现提升为公共版）。</summary>
    //* 既定例外：同上
    public static void GetResultWithTimeout(this Task targetTask, CancellationTokenSource tokenSource, int timeoutMs)
    {
        var delayTask = Task.Delay(timeoutMs);
        if (Task.WhenAny(targetTask, delayTask).ConfigureAwait(false).GetAwaiter().GetResult() != delayTask)
        {
            targetTask.GetAwaiter().GetResult();
            return;
        }
        tokenSource.Cancel();
        throw new TimeoutException($"任务超时（{timeoutMs} ms）");
    }

    #endregion

    #region UUID 与时间

    private static int uuid = 1;
    private static readonly object UuidLock = new();

    /// <summary>
    /// 获取一个全程序内不会重复的数字（伪 Uuid）。
    /// </summary>
    public static int GetUuid()
    {
        lock (UuidLock)
        {
            return ++uuid;   //全局计数器：自 1 起步，首个取值为 2（旧 :817-828）
        }
    }

    /// <summary>
    /// 获取一个单调递增时间值（毫秒）。
    /// </summary>
    public static long GetTimeMs()
    {
        return Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000L);
    }

    /// <summary>
    /// 获取十进制 Unix 时间戳。
    /// </summary>
    public static long GetUnixTimestampUtc()
    {
        return Convert.ToInt64((double)(DateTime.Now.ToUniversalTime().Ticks - 621355968000000000) / 10000000.0);   //VB “/”为浮点除，返回经 CLng 银行家舍入
    }

    #endregion

    #region 随机

    private static readonly Random Random = new();

    /// <summary>
    /// 随机选择其一。
    /// </summary>
    public static T RandomOne<T>(ICollection<T> objects)
    {
        return objects.ElementAt(RandomInteger(0, objects.Count - 1));   //VB 经后期绑定索引器，实际调用方均为 IList
    }

    /// <summary>
    /// 取随机整数（包含）。
    /// </summary>
    public static int RandomInteger(int min, int max)
    {
        return (int)(Math.Floor((max - min + 1) * Random.NextDouble()) + min);
    }

    /// <summary>
    /// 将数组随机打乱。
    /// </summary>
    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> raw)
    {
        var rawCopy = new List<T>(raw);
        while (rawCopy.Any())
        {
            var index = RandomInteger(0, rawCopy.Count - 1);   //惰性求值，与 VB 迭代器一致
            yield return rawCopy[index];
            rawCopy.RemoveAt(index);
        }
    }

    #endregion

    #region 排序

    /// <summary>
    /// 按照既定的函数进行选择排序。
    /// 返回第一个对象是否应该排在前面（a &gt; b）。
    /// 注意：与旧实现一致，会逐个 Remove 排空传入列表。
    /// </summary>
    public static List<T> SortByComparison<T>(this IList<T> list, Func<T, T, bool> sortRule)
    {
        var newList = new List<T>();
        while (list.Any())
        {
            var highest = list[0];
            for (var i = 1; i <= list.Count - 1; i++)
            {
                if (sortRule(list[i], highest))
                {
                    highest = list[i];
                }
            }
            list.Remove(highest);
            newList.Add(highest);
        }
        return newList;
    }

    #endregion

}