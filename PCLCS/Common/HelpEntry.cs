using System.Globalization;
using System.Text.Json.Nodes;
using PCLCS.Json;

namespace PCLCS.Common;

/// <summary>
/// 帮助条目解析核（自宿主层 ModMain.vb:489-523 构造段逐字移植；ToListItem/SetToListItem 与页面字段消费留宿主层，
/// 宿主层同名壳持有旧公共字段面并逐字段回拷）。Newtonsoft → JsonNodeUtils 宽松层，TECHDEBT §9 逐点对照：
/// - 缺失属性默认值：Title 缺失抛「未找到 Title 项」（旧 :497）；Desc/Search 缺失为 ""（旧 :499-500）；
///   Logo 缺失为 null（旧 :501「为保持 Nothing，不要加 If」）；Show* 缺失保持默认 True（旧 :502-504）；Types 缺失为空列表（旧 :506）。
/// - 属性名大小写：统一层大小写不敏感（JsonNodeUtils.NodeOptions；旧 Newtonsoft JObject 索引器大小写敏感——病态输入差异按 §9 记录）。
/// - 数字/字符串宽松：按目标类型经 Convert 语义（1→True、"true"→True；"1" 抛 FormatException 与旧隐转一致；数字→ToString 用不变文化）。
/// - null 与缺失：STJ 中 JSON null 与缺失同判（统一层口径；旧 Newtonsoft 对 null JValue 隐转 False/空串——病态输入差异按 §9 记录不修）。
/// - 注释 Skip、尾逗号允许（DocumentOptions）；帮助 JSON 无日期字段，日期/时区点不涉及。
/// </summary>
public sealed class HelpEntry
{

    /// <summary>原始信息路径。用于刷新。（旧 :431）</summary>
    public string RawPath { get; }

    //基础

    /// <summary>显示标题。（旧 :438）</summary>
    public string Title { get; }
    /// <summary>显示描述。（旧 :442）</summary>
    public string Desc { get; }
    /// <summary>检索关键字。（旧 :446）</summary>
    public string Search { get; }
    /// <summary>用于分类的标签列表。（旧 :450）</summary>
    public List<string> Types { get; }

    //显示（可选）

    /// <summary>帮助项的自定义图标。可能为 null。（旧 :457）</summary>
    public string? Logo { get; }
    /// <summary>是否显示在搜索结果。默认为 True。（旧 :461）</summary>
    public bool ShowInSearch { get; }
    /// <summary>是否在公开版的 PCL 中显示（这会影响主页与搜索）。默认为 True。（旧 :465）</summary>
    public bool ShowInPublic { get; }
    /// <summary>是否在快照版的 PCL 中显示（这会影响主页与搜索）。默认为 True。（旧 :469）</summary>
    public bool ShowInSnapshot { get; }

    //动作

    /// <summary>是否为 “执行事件”。（旧 :476）</summary>
    public bool IsEvent { get; }
    /// <summary>事件类型原始串（旧 :511 ToObject(Of CustomEvent.EventType) 的输入面；数字/数字串经宽松层转字符串，
    /// 枚举转换由宿主层壳 EnumUtils.FromString 承担）。非事件条目为 ""（宿主层壳回拷得 None，与旧字段默认一致）。</summary>
    public string EventType { get; } = "";
    /// <summary>事件参数。（旧 :478；非事件条目为 ""）</summary>
    public string EventData { get; } = "";
    /// <summary>若非执行事件，其对应的 .xaml 本地文件内容。可能为 null。（旧 :482）</summary>
    public string? XamlContent { get; }

    /// <summary>
    /// 从文件初始化 HelpEntry 对象，失败会抛出异常。（旧 :489-523 逐行）
    /// </summary>
    /// <param name="filePath">帮助 JSON 文件路径。</param>
    /// <param name="argumentReplace">替换标记预处理回调（旧 :492 ArgumentReplace(..., AddressOf StringUtils.XmlEscape)；
    /// 深绑页面数据留宿主层。null 时不替换，测试直用）。</param>
    public HelpEntry(string filePath, Func<string, string>? argumentReplace = null)
    {
        RawPath = filePath;
        if (!FileUtils.Exists(filePath)) throw new FileNotFoundException($"未找到帮助文件：{filePath}", filePath);
        var jsonText = FileUtils.ReadAsString(filePath);
        if (argumentReplace is not null) jsonText = argumentReplace(jsonText);
        var jsonData = JsonNodeUtils.ParseObject(jsonText);
        //加载常规信息
        if (jsonData["Title"] is not null)
        {
            Title = LaxString(jsonData["Title"])!; //旧 :495 late-bind 转 String（数字/布尔宽松同判）
        }
        else
        {
            throw new ArgumentException("未找到 Title 项");
        }
        Desc = LaxString(jsonData["Description"]) ?? "";
        Search = LaxString(jsonData["Keywords"]) ?? "";
        Logo = LaxString(jsonData["Logo"]); //为保持 null，不要加 If（旧 :501）
        ShowInSearch = LaxBool(jsonData["ShowInSearch"], true); //缺省 True 即旧 :461-469 的字段初值
        ShowInPublic = LaxBool(jsonData["ShowInPublic"], true);
        ShowInSnapshot = LaxBool(jsonData["ShowInSnapshot"], true);
        Types = jsonData["Types"] is JsonArray typeArray //缺失与非数组形态同空列表（旧 :506 New JArray 兜底）
            ? typeArray.Select(static nameOfType => LaxString(nameOfType)!).ToList() //元素经 late-bind 转 String；JSON null 元素按旧语义记 null（病态输入差异记录，故意保留）
            : [];
        //加载事件信息
        if (LaxBool(jsonData["IsEvent"], false))
        {
            EventType = LaxString(jsonData["EventType"] ?? throw new NullReferenceException())!; //旧 :511 缺失时 Nothing 上 late-bind → NRE
            EventData = LaxString(jsonData["EventData"]) ?? "";
            IsEvent = true;
        }
        else
        {
            var xamlAddress = Path.ChangeExtension(filePath, ".xaml");
            if (FileUtils.Exists(xamlAddress))
            {
                XamlContent = FileUtils.ReadAsString(xamlAddress);
                IsEvent = false;
            }
            else
            {
                throw new FileNotFoundException($"未找到帮助条目 .json 对应的 .xaml 文件（{xamlAddress}）"); //旧 :520 无 fileName 实参，逐字
            }
        }
    }

    /// <summary>§9 宽松层：按 String 目标类型转换（旧 late-bind Conversions.ToString(JValue) 语义，不变文化）。</summary>
    private static string? LaxString(JsonNode? node)
    {
        if (node is not JsonValue value) return null; //缺失或 JSON null（统一层口径，见类型头注）
        return value.TryGetValue<string>(out var s) ? s
            : value.TryGetValue<bool>(out var b) ? b ? "True" : "False"
            : value.TryGetValue<long>(out var l) ? l.ToString(CultureInfo.InvariantCulture)
            : value.TryGetValue<double>(out var d) ? d.ToString(CultureInfo.InvariantCulture)
            : value.ToJsonString(); //对象/数组等其余形态：取字面（旧 late-bind 在此为非常规串或抛出，病态输入差异记录）
    }

    /// <summary>§9 宽松层：按 Boolean 目标类型转换（旧 JValue 隐转 = Convert.ToBoolean(Value, InvariantCulture) 语义；
    /// 不可转的字符串（如 "1"）抛 FormatException，与旧一致地向上传播）。</summary>
    private static bool LaxBool(JsonNode? node, bool fallback)
    {
        if (node is not JsonValue value) return fallback; //缺失或 JSON null（统一层口径，见类型头注）
        return value.TryGetValue<bool>(out var b) ? b
            : value.TryGetValue<double>(out var d) ? Convert.ToBoolean(d, CultureInfo.InvariantCulture)
            : value.TryGetValue<string>(out var s) ? Convert.ToBoolean(s, CultureInfo.InvariantCulture)
            : fallback;
    }

}