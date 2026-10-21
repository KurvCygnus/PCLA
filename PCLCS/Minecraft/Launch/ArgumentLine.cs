using System.Globalization;

namespace PCLCS.Minecraft.Launch;

/// <summary>
/// 启动参数行的拆分与去重。
/// </summary>
public static class ArgumentLine {
    /// <summary>
    /// 按空格拆分参数行，保留引号与转义。
    /// </summary>
    public static IReadOnlyList<string> Split(string? raw) {
        var result = new List<string>();
        if (string.IsNullOrEmpty(raw)) return result;
        var text = NormalizeLineEndings(raw!).Trim();
        if (text.Length == 0) return result;
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++) {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '"') {
                current.Append("\\\"");
                i++;
            } else if (c == '"') {
                quoted = !quoted;
                current.Append(c);
            } else if (c == ' ' && !quoted) {
                if (current.Length > 0) {
                    result.Add(current.ToString());
                    current.Clear();
                }
            } else {
                current.Append(c);
            }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    /// <summary>
    /// 去重 JVM/游戏参数，保留原顺序。
    /// 游戏参数里重复出现同名的键会让这一组参数整体失效，因此用后出现的值覆盖先出现的值。
    /// </summary>
    public static IReadOnlyList<string> Deduplicate(
        IReadOnlyList<string> args,
        bool isJvm,
        ICollection<string>? diagnostics = null) {
        var result = new List<string>();
        var index = 0;
        while (index < args.Count) {
            var key = args[index];
            var isPair = key.StartsWith("-", StringComparison.Ordinal)
                         && index + 1 < args.Count
                         && !(args[index + 1].StartsWith("-", StringComparison.Ordinal)
                              && VbVal(args[index + 1].Substring(1)) == 0);
            if (!isPair) {
                index++;
                if (result.Contains(key)) {
                    diagnostics?.Add($"已去重单个{(isJvm ? " JVM " : "游戏")}参数：{key}");
                    continue;
                }
                result.Add(key);
                continue;
            }

            var value = args[index + 1];
            index += 2;
            var handled = false;
            for (var i = 0; i + 1 < result.Count; i++) {
                if (result[i] != key) continue;
                // 游戏对重复的 --width、--uuid 之类参数会整组失效，覆盖成最后一个值比保留多个更安全；--tweakClass 允许出现多次。
                if (!isJvm && key != "--tweakClass") {
                    diagnostics?.Add($"已覆盖重复的{(isJvm ? " JVM " : "游戏")}参数：{key} {result[i + 1]} → {key} {value}");
                    result[i + 1] = value;
                    handled = true;
                    break;
                }
                if (result[i + 1] == value) {
                    diagnostics?.Add($"已去重{(isJvm ? " JVM " : "游戏")}参数对：{key} {value}");
                    handled = true;
                    break;
                }
            }
            if (handled) continue;
            result.Add(key);
            result.Add(value);
        }
        return result;
    }

    private static string NormalizeLineEndings(string raw) {
        var builder = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++) {
            var c = raw[i];
            if (!IsLineEnding(c)) {
                builder.Append(c);
                continue;
            }
            builder.Append(' ');
            // 连续的行尾（包括 \r\n 混用）只产生一个空格，与 ReplaceLineEndings(mergeMultiple:=True) 的合并语义一致。
            while (i + 1 < raw.Length && IsLineEnding(raw[i + 1])) i++;
        }
        return builder.ToString();
    }

    // 字符集与 MeloongCore 的 ReplaceLineEndings 保持一致，例如 \f 与 U+2028 也会被当作换行。
    private static bool IsLineEnding(char c) => c is '\r' or '\n' or '\f' or '\u0085' or '\u2028' or '\u2029';

    // 复刻 VB Val 的数值前缀解析，用于区分 "-xPos -50" 中的负数值。
    // 与 VB 的已知差异：超长的 &H/&O 在 VB 里会抛 OverflowException（足以让启动崩溃），这里只返回有限值；调用方只判断是否为 0。
    private static double VbVal(string text) {
        var s = text.TrimStart();
        // 进制前缀只在无符号时生效：Val("&H10") = 16，而 Val("-&H10") = 0。
        if (s.Length > 1 && s[0] == '&') {
            var radix = s[1] switch {
                'H' or 'h' => 16,
                'O' or 'o' => 8,
                _ => 0
            };
            if (radix == 0) return 0;
            var value = 0d;
            var count = 0;
            for (var i = 2; i < s.Length; i++) {
                var digit = DigitValue(s[i], radix);
                if (digit < 0) break;
                value = value * radix + digit;
                count++;
            }
            return count == 0 ? 0 : value;
        }
        var index = 0;
        var negative = false;
        if (index < s.Length && (s[index] == '+' || s[index] == '-')) {
            negative = s[index] == '-';
            index++;
            // VB 允许符号与数字之间有空白：Val("- 5") = -5。
            while (index < s.Length && char.IsWhiteSpace(s[index])) index++;
        }
        var start = index;
        while (index < s.Length && char.IsDigit(s[index])) index++;
        if (index < s.Length && s[index] == '.') {
            index++;
            while (index < s.Length && char.IsDigit(s[index])) index++;
        }
        if (index == start) return 0;
        return double.TryParse(s.Substring(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? (negative ? -number : number)
            : 0;
    }

    private static int DigitValue(char c, int radix) {
        var value = c switch {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1
        };
        return value >= 0 && value < radix ? value : -1;
    }
}
