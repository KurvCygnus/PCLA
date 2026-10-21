namespace PCLCS.Minecraft.Launch;

/// <summary>
/// 从版本 JSON 收集 JVM 与游戏参数。
/// </summary>
public static class VersionArguments {
    // 防御异常 JSON 造成的无限继承链；正常版本最多两三层。
    private const int MaxInheritDepth = 64;

    /// <summary>
    /// 收集 JVM 参数；新版 JSON 无 <c>arguments.jvm</c> 时返回旧版固定参数。
    /// </summary>
    /// <param name="version">起始版本 JSON。</param>
    /// <param name="resolveParent">按 <c>inheritsFrom</c> 解析父版本 JSON；返回 <c>null</c> 时停止继承。</param>
    /// <param name="environment">规则求值环境。</param>
    /// <param name="warn">规则警告回调。</param>
    public static IReadOnlyList<string> CollectJvm(
        JObject version,
        Func<string, JObject?> resolveParent,
        RuleEnvironment environment,
        Action<string>? warn = null) {
        var result = new List<string>();
        if (version["arguments"]?["jvm"] is not null) {
            Walk(version, resolveParent, environment, warn, "jvm", value => result.Add(value.Trim()));
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
    public static string CollectGame(
        JObject version,
        Func<string, JObject?> resolveParent,
        RuleEnvironment environment,
        Action<string>? warn = null) {
        var builder = new StringBuilder();
        var legacy = version["minecraftArguments"]?.ToString();
        if (!string.IsNullOrEmpty(legacy)) {
            builder.Append(' ').Append(legacy);
            // 总是补宽高，之后去重时覆盖 MC 自带的值。
            builder.Append(" --height ${resolution_height} --width ${resolution_width}");
        }
        if (version["arguments"]?["game"] is not null) {
            Walk(version, resolveParent, environment, warn, "game", value => Append(builder, value));
        }
        return builder.ToString();
    }

    private static void Walk(
        JObject version,
        Func<string, JObject?> resolveParent,
        RuleEnvironment environment,
        Action<string>? warn,
        string section,
        Action<string> emit) {
        JObject? current = version;
        for (var depth = 0; current is not null && depth < MaxInheritDepth; depth++) {
            if (current["arguments"] is JObject arguments && arguments[section] is JArray list) {
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

            var inherit = current["inheritsFrom"]?.ToString();
            if (string.IsNullOrEmpty(inherit)) return;
            current = resolveParent(inherit!);
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
