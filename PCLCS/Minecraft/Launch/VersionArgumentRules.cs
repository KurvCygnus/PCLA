using System.Text.Json.Nodes;

namespace PCLCS.Minecraft.Launch;

/// <summary>
/// 版本 JSON 中 <c>rules</c> 的求值。
/// </summary>
public static class VersionArgumentRules {
    /// <summary>
    /// 判断一条规则集合是否放行。
    /// </summary>
    /// <param name="rules">版本 JSON 的 <c>rules</c> 节点；为 <c>null</c> 时放行。</param>
    /// <param name="environment">当前运行环境。</param>
    /// <param name="warn">规则正则无效时的警告回调。</param>
    public static bool IsAllowed(JsonNode? rules, RuleEnvironment environment, Action<string>? warn = null) {
        if (rules is null) return true;

        var required = false;
        // 版本 JSON 里 rules 总是数组；单条规则对象按一条规则处理。
        var ruleList = rules is JsonArray array ? array.AsEnumerable() : new[] { rules };
        foreach (var rule in ruleList) {
            if (rule is null) continue;
            var matches = true;
            var os = rule["os"];
            if (os is not null) {
                var name = os["name"]?.ToString();
                if (name is not null) {
                    if (name == "windows") {
                        var version = os["version"]?.ToString();
                        if (version is not null) matches &= RegexCheck(environment.OsVersion, version, warn);
                    } else if (name != "unknown") {
                        matches = false;
                    }
                }

                var arch = os["arch"]?.ToString();
                if (arch is not null) matches &= arch != "x86" && !arch.Contains("arm");
            }

            var features = rule["features"];
            if (features is not null) {
                matches &= features["is_demo_user"] is null;
                var featureObject = features.AsObject();
                if (featureObject.Select(property => property.Key).Any(key => key.Contains("quick_play"))) matches = false;
            }

            if (rule["action"]?.ToString() == "allow") {
                if (matches) required = true;
            } else if (matches) {
                required = false;
            }
        }
        return required;
    }

    private static bool RegexCheck(string input, string pattern, Action<string>? warn) {
        try {
            // My.Computer.Info.OSVersion 是裸版本号（10.0.x）；传入 Environment.OSVersion.VersionString 时带 "Microsoft Windows NT " 前缀。
            return Regex.IsMatch(input, pattern) || MatchesBareVersion(input, pattern);
        } catch (Exception ex) {
            warn?.Invoke($"规则正则无效：{pattern}（{ex.Message}）");
            return false;
        }
    }

    private static bool MatchesBareVersion(string input, string pattern) {
        const string prefix = "Microsoft Windows NT ";
        return input.StartsWith(prefix, StringComparison.Ordinal)
               && Regex.IsMatch(input.Substring(prefix.Length), pattern);
    }
}
