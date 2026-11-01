using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCLCS.Install;

/// <summary>
/// 多版本 JSON 合并决策核（ModDownloadLib.vb:1884-1931 私有 MergeJson 的核心段迁入）：
/// Newtonsoft JObject.Merge 默认语义（数组拼接/对象递归/标量覆盖/来源 null 忽略）与
/// minecraftArguments 重组（:1901-1922，#2801）。输出 minecraftArguments 被 W3 启动参数链消费
/// （MergedJsonTests 锁参数行为）。多版本场景由 VB 宿主层逐层调用本方法完成折叠。
/// </summary>
public static class MergeJson
{
    /// <summary>合并 JSON 的写出选项：缩进 + 非 ASCII 原样输出，与旧 Newtonsoft ToString 的文件布局对齐（InstanceCore.RenameJsonOptions 同款）。</summary>
    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 合并单个覆盖层到基底（就地修改基底并返回；ModDownloadLib.vb:1903-1922 的逐层步骤）。
    /// 顺序：取覆盖前参数原文 → 清理覆盖层 releaseTime/time（:1906-1907）→ JObject.Merge 等价合并（:1908）→
    /// 按 #2801 语义重组 minecraftArguments 并覆写（:1911-1922，重组结果为空时保持合并产物）。
    /// </summary>
    /// <param name="baseJson">基底层（原版 JSON），就地修改。</param>
    /// <param name="overlayJson">覆盖层（OptiFine/Forge 等），其 releaseTime/time 会被移除。</param>
    /// <param name="allArguments">
    /// 覆盖前的全部层 minecraftArguments 原文（VB 宿主跨所有层拼接，:1901-1902）；
    /// null 时退化为取本层两输入自身的值（单层调用形态，等价于两版本合并）。
    /// </param>
    public static JsonObject Merge(JsonObject baseJson, JsonObject overlayJson, string? allArguments = null)
    {
        //先获取 minecraftArguments，避免合并时原版参数被覆盖（:1901-1902）
        //? JSON null 与缺失键此处无可观察差异：Newtonsoft 的 JSON null 取 ToString 为空串、缺失取 " "，
        //? STJ 两者索引器均返回 null 统一取 " "；空串与空白经分词后同样不产生 token
        var pairArguments = allArguments ?? (
            (baseJson["minecraftArguments"]?.ToString() ?? " ") + " " +
            (overlayJson["minecraftArguments"]?.ToString() ?? " "));
        //覆盖层清理（:1906-1907）；基底（原版 JSON）的 releaseTime/time 不动
        overlayJson.Remove("releaseTime");
        overlayJson.Remove("time");
        //合并（相关讨论见 #2801；:1908）
        MergeOverlay(baseJson, overlayJson);
        //填入 minecraftArguments（:1910-1922）
        var rebuiltArguments = ReassembleArguments(pairArguments);
        if (rebuiltArguments is not null) baseJson["minecraftArguments"] = rebuiltArguments;
        return baseJson;
    }

    /// <summary>
    /// minecraftArguments 重组（ModDownloadLib.vb:1911-1922 逐字）：
    /// 非旗标 token 并入前置裸旗标（旗标不含空格时）、去重保留首次出现；无 token 时返回 null（调用方跳过写入）。
    /// </summary>
    public static string? ReassembleArguments(string allArguments)
    {
        var splitArguments = new List<string>();
        foreach (var argument in allArguments.Split(" ", removeEmptyEntries: true).Select(value => value.Trim()))
        {
            if (!argument.StartsWithF("-") && splitArguments.Count > 0)
            {
                var lastArgument = splitArguments[^1];
                if (lastArgument.StartsWithF("-") && !lastArgument.Contains(" "))
                {
                    splitArguments[^1] = lastArgument + " " + argument;
                    continue;
                }
            }
            splitArguments.Add(argument);
        }
        return splitArguments.Count > 0 ? splitArguments.Distinct().Join(" ") : null;
    }

    /// <summary>
    /// Newtonsoft JObject.Merge（默认 MergeArrayHandling.Concat / MergeNullValueHandling.Ignore）的 STJ 等价实现，
    /// 与 InstanceCore 继承合并（InstanceCore.cs:623-640）同向：对象递归、数组拼接（基底在前）、标量覆盖、来源 JSON null 忽略。
    /// 键匹配为序数大小写敏感（STJ JsonObject 语义；Newtonsoft Merge 默认忽略大小写，Minecraft 版本 JSON 键全小写无实际差异）。
    /// internal 供 InstallerPlans 的 Forge 安装链使用（ModDownloadLib.vb:1160 的裸 Json.Merge(Json2)，无参数重组语义）。
    /// </summary>
    internal static void MergeOverlay(JsonObject target, JsonObject incoming)
    {
        foreach (var property in incoming.ToArray())
        {
            var node = property.Value;
            if (node is null || node.GetValueKind() == JsonValueKind.Null) continue;   // 默认忽略来源 null 值
            if (target[property.Key] is JsonObject targetObject && node is JsonObject incomingObject)
            {
                MergeOverlay(targetObject, incomingObject);
            } else if (target[property.Key] is JsonArray targetArray && node is JsonArray incomingArray)
            {
                foreach (var item in incomingArray) targetArray.Add(item?.DeepClone());
            } else
            {
                target[property.Key] = node.DeepClone();
            }
        }
    }
}