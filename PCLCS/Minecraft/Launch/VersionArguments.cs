namespace PCLCS.Minecraft.Launch;

/// <summary>
/// 从版本 JSON 收集 JVM 与游戏参数。
/// </summary>
public static class VersionArguments {
    // 防御异常 JSON 造成的无限继承链；正常版本最多两三层。
    private const int MaxInheritDepth = 64;

    /// <summary>
    /// 收集 JVM 参数；整条继承链都没有 <c>arguments.jvm</c> 时返回旧版固定参数。
    /// </summary>
    /// <param name="version">起始版本 JSON。</param>
    /// <param name="resolveParent">按 <c>inheritsFrom</c> 解析父版本 JSON；返回 <c>null</c> 时停止继承。</param>
    /// <param name="environment">规则求值环境。</param>
    /// <param name="warn">规则警告回调。</param>
    /// <param name="inheritName">
    /// 覆盖第一层父级名；<c>null</c> 表示沿用 JSON 的 <c>inheritsFrom</c>，空字符串表示没有父级。
    /// </param>
    public static IReadOnlyList<string> CollectJvm(
        JObject version,
        Func<string, JObject?> resolveParent,
        RuleEnvironment environment,
        Action<string>? warn = null,
        string? inheritName = null) {
        var chain = BuildChain(version, resolveParent, inheritName);
        var result = new List<string>();
        if (chain.Any(level => level["arguments"]?["jvm"] is not null)) {
            for (var i = 0; i < chain.Count; i++) {
                EmitSection(chain[i], "jvm", environment, warn, value => result.Add(value.Trim()));
            }
            for (var i = 0; i < result.Count; i++) {
                var argument = result[i];
                // 1.17.1 的 "-Dos.name=Windows 10" 不加引号会让 10 被当成独立参数。
                if (argument.Contains(' ')
                    && !argument.StartsWith("\"", StringComparison.Ordinal)
                    && !argument.EndsWith("\"", StringComparison.Ordinal)) {
                    result[i] = "\"" + argument.Replace("\"", "\\\"") + "\"";
                }
            }
        } else {
            result.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
            result.Add("-Djava.library.path=${natives_directory}");
            result.Add("-cp");
            result.Add("${classpath}");
        }
        return result;
    }

    /// <summary>
    /// 收集游戏参数并拼成单行；旧版 <c>minecraftArguments</c> 与新版 <c>arguments.game</c> 都会处理。
    /// </summary>
    /// <param name="inheritName">
    /// 覆盖第一层父级名；<c>null</c> 表示沿用 JSON 的 <c>inheritsFrom</c>，空字符串表示没有父级。
    /// </param>
    public static string CollectGame(
        JObject version,
        Func<string, JObject?> resolveParent,
        RuleEnvironment environment,
        Action<string>? warn = null,
        string? inheritName = null) {
        var chain = BuildChain(version, resolveParent, inheritName);
        var builder = new StringBuilder();
        // 合并时子版本的 minecraftArguments 覆盖父版本，等效于从选中版本向上取最近的一级。
        var legacy = chain
            .Where(level => level["minecraftArguments"] is not null)
            .Select(level => level["minecraftArguments"]!.ToString())
            .FirstOrDefault();
        if (!string.IsNullOrEmpty(legacy)) {
            builder.Append(' ').Append(legacy);
            // 总是补宽高，之后去重时覆盖 MC 自带的值。
            builder.Append(" --height ${resolution_height} --width ${resolution_width}");
        }
        if (chain.Any(level => level["arguments"]?["game"] is not null)) {
            for (var i = 0; i < chain.Count; i++) {
                EmitSection(chain[i], "game", environment, warn, value => Append(builder, value));
            }
        }
        return builder.ToString();
    }

    // 子版本在前、根在最后；输出时正序遍历，对齐 VB 的追加顺序：
    // 先输出选中版本的合并 JSON（内部祖先→子），再沿 InheritName 逐级追加父级。
    private static List<JObject> BuildChain(
        JObject version,
        Func<string, JObject?> resolveParent,
        string? inheritName = null) {
        var chain = new List<JObject>();
        JObject? current = version;
        for (var depth = 0; current is not null && depth < MaxInheritDepth; depth++) {
            chain.Add(current);
            // 老 LiteLoader 的 JSON 没有 inheritsFrom，由调用方通过 inheritName 提供真实父级；
            // 空字符串表示明确没有父级（HMCL 格式），此时不能回退到 JSON。
            var inherit = depth == 0 && inheritName is not null
                ? inheritName
                : current["inheritsFrom"]?.ToString();
            if (string.IsNullOrEmpty(inherit)) break;
            current = resolveParent(inherit!);
        }
        return chain;
    }

    private static void EmitSection(
        JObject level,
        string section,
        RuleEnvironment environment,
        Action<string>? warn,
        Action<string> emit) {
        if (level["arguments"] is not JObject arguments || arguments[section] is not JArray list) return;
        foreach (var token in list) {
            switch (token.Type) {
                case JTokenType.String:
                    emit(token.ToString());
                    break;
                case JTokenType.Object: {
                    if (token["rules"] is not null
                        && !VersionArgumentRules.IsAllowed(token["rules"], environment, warn)) continue;
                    var value = token["value"];
                    if (value is null) continue;
                    if (value.Type == JTokenType.String) {
                        emit(value.ToString());
                    } else {
                        foreach (var item in value) emit(item.ToString());
                    }
                    break;
                }
            }
        }
    }

    private static void Append(StringBuilder builder, string value) {
        builder.Append(' ');
        if (value.Contains(' ')
            && !value.StartsWith("\"", StringComparison.Ordinal)
            && !value.EndsWith("\"", StringComparison.Ordinal)) {
            builder.Append('"').Append(value.Replace("\"", "\\\"")).Append('"');
        } else {
            builder.Append(value);
        }
    }
}
