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
            if (c != '\r' && c != '\n') {
                builder.Append(c);
                continue;
            }
            builder.Append(' ');
            while (i + 1 < raw.Length && (raw[i + 1] == '\r' || raw[i + 1] == '\n')) i++;
        }
        return builder.ToString();
    }

    // 复刻 VB Val 的数值前缀解析，用于区分 "-xPos -50" 中的负数值。
    private static double VbVal(string text) {
        var s = text.TrimStart();
        var index = 0;
        if (index < s.Length && (s[index] == '+' || s[index] == '-')) index++;
        var start = index;
        while (index < s.Length && char.IsDigit(s[index])) index++;
        if (index < s.Length && s[index] == '.') {
            index++;
            while (index < s.Length && char.IsDigit(s[index])) index++;
        }
        if (index == start) return 0;
        return double.TryParse(s.Substring(0, index), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }
}
