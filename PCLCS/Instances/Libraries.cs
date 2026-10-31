using PCLCS.Minecraft.Launch;
using PCLCS.Settings;
using System.Globalization;
using System.Text.Json.Nodes;

namespace PCLCS.Instances;

/// <summary>
/// 单个支持库的数据面（自 VB McLibEntry，ModMinecraft.vb:1503-1553 移植）。
/// VB 层保留 McLibEntry 类并经 <c>ToLibEntry</c> 转换；NetFile 下载映射（URL 源、校验器）留在 VB。
/// </summary>
public sealed class LibEntry
{
    /// <param name="root">库在版本 Json 中的原始项；主 Jar 等非 Json 来源的条目为 null。</param>
    public LibEntry(JsonObject? root) => Root = root;

    /// <summary>库在版本 Json 中的原始项；主 Jar 等非 Json 来源的条目为 null（原 Token）。</summary>
    public JsonObject? Root { get; }

    /// <summary>原 Json 中的 name 项（原 OriginalName，ModMinecraft.vb:1546-1552）。</summary>
    public string? Name;

    /// <summary>由 Json 提供的 URL，若空白则视为没有（原 Url setter，ModMinecraft.vb:1523-1533）。</summary>
    public string? Url;

    /// <summary>Json 中 size 项的文本；null 表示无有效数据（原 Size=0 语义，ModMinecraft.vb:1511-1513）。</summary>
    public string? Size;

    /// <summary>文件的 SHA1（原 SHA1，ModMinecraft.vb:1518-1521）。</summary>
    public string? Checksum;

    /// <summary>文件的完整本地路径（原 LocalPath，ModMinecraft.vb:1506-1508）。</summary>
    public string? Path;

    /// <summary>是否为 Natives 文件（原 IsNatives，ModMinecraft.vb:1514-1516）。</summary>
    public bool IsNative;

    /// <summary>与原 McLibEntry.ToString 一致（ModMinecraft.vb:1550-1552）。</summary>
    public override string ToString() =>
        (IsNative ? "[Native] " : "") + StringUtils.FormatByteSize((long) Libraries.Val(Size)) + " | " + Path;

    /// <summary>原 McLibEntry.Name：name 项除去版本号部分的较前部分（ModMinecraft.vb:1534-1544）。</summary>
    public string? ShortName()
    {
        if (Name is null) return null;
        var splited = new List<string>(Name.Split(':'));
        splited.RemoveAt(2);   // Java 的此格式下版本号固定为第三段，第四段可能包含架构、分包等其他信息
        return string.Join(":", splited);
    }
}

/// <summary>
/// Minecraft 支持库解析的纯函数面（自 VB ModMinecraft.vb:1558-1889 移植）。
/// 旧代码经 Newtonsoft JToken 取值，键缺失与 JSON null 语义不同的位置以 <see cref="Token"/> 对齐。
/// </summary>
public static class Libraries
{
    /// <summary>
    /// 检查是否符合 Json 中的 Rules（McJsonRuleCheck，ModMinecraft.vb:1558-1568）。
    /// </summary>
    /// <param name="ruleToken">Json 中的 "rules" 项目；可以是规则数组或单条规则对象，null 时放行。</param>
    public static bool RuleCheck(JsonNode? ruleToken) =>
        VersionArgumentRules.IsAllowed(
            ruleToken,
            new RuleEnvironment(Environment.OSVersion.VersionString),
            static message => Logger.Warn(message));

    /// <summary>
    /// 递归获取 Minecraft 某一版本的完整支持库列表（McLibListGet，ModMinecraft.vb:1569-1620）。
    /// </summary>
    public static List<LibEntry> ListGet(InstanceCore instance, bool includeMainJar)
    {
        //获取当前支持库列表
        Logger.Info($"获取支持库列表：{instance.Name}");
        var result = ListGetJson(instance.VersionJson, customMcFolder: instance.McFolderSelected);

        //需要添加原版 Jar
        if (!includeMainJar) return result;

        InstanceCore realInstance;
        // 旧 :1220 Optional 链取值：JSON null 经 JValue.ToString 得 ""，与缺失不同（"" 会走 New McInstance("") 后回退）
        string? requiredJar = Token(instance.VersionJson, "jar");
        if (instance.IsHmclFormatJson || requiredJar is null)
        {
            //HMCL 项直接使用自身的 Jar
            //根据 Inherit 获取最深层版本
            var originalInstance = instance;
            //1.17+ 的 Forge 不寻找 Inherit
            if (!((instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) || instance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge)) && instance.Version.Vanilla.Major >= 17))
            {
                while (originalInstance.InheritName != "")
                {
                    if (originalInstance.InheritName == originalInstance.Name) break;
                    originalInstance = InstanceByName(instance.McFolderSelected, originalInstance.InheritName);
                }
            }
            //需要新建对象，否则后面的 Check 会导致 McInstanceCurrent 的 State 变回 Original
            //复现：启动一个 Snapshot 版本
            realInstance = InstanceByPath(instance.McFolderSelected, originalInstance.PathVersion);
        } else
        {
            //Json 已提供 Jar 字段，使用该字段的信息
            realInstance = InstanceByName(instance.McFolderSelected, requiredJar);
        }
        //判断需求的版本是否存在
        //不能调用 RealVersion.Check()，可能会莫名其妙地触发 CheckPermission 正被另一进程使用，导致误判前置不存在
        if (!FileUtils.Exists(realInstance.PathVersion + realInstance.Name + ".json"))
        {
            realInstance = instance;
            Logger.Warn($"可能缺少前置版本 {realInstance.Name}，找不到对应的 json 文件");
        }
        //获取详细下载信息
        var client = NodeObjectStrict(NodeObjectStrict(realInstance.VersionJson, "downloads"), "client");
        string? clientUrl, clientSha1;
        if (client is not null)
        {
            clientUrl = Token(client, "url");
            clientSha1 = Token(client, "sha1");
        } else
        {
            clientUrl = null;
            clientSha1 = null;
        }
        //把所需的原版 Jar 添加进去
        result.Add(new LibEntry(null)
        {
            Path = realInstance.PathVersion + realInstance.Name + ".jar",
            Size = null,
            IsNative = false,
            Url = NormalizeUrl(clientUrl),
            Checksum = clientSha1
        });

        return result;
    }

    /// <summary>
    /// 获取 Minecraft 某一版本忽视继承的支持库列表，即结果中没有继承项（McLibListGetWithJson，ModMinecraft.vb:1621-1733）。
    /// </summary>
    /// <param name="json">版本 Json，需含 libraries 数组。</param>
    /// <param name="keepSameNameDifferentVersion">同名不同版本的支持库是否保留多个。</param>
    /// <param name="customMcFolder">Minecraft 根文件夹，以 \ 结尾（VB 转发层传入 McFolderSelected）。</param>
    public static List<LibEntry> ListGetJson(JsonObject json, bool keepSameNameDifferentVersion = false, string? customMcFolder = null)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));
        if (customMcFolder is null) throw new ArgumentNullException(nameof(customMcFolder), "VB 转发层需传入 McFolderSelected，C# 侧无模块状态");

        //转换为 LibEntry（旧 :1268：libraries 缺失或非数组时抛出）
        var basicArray = new List<LibEntry>();
        foreach (var libraryNode in json["libraries"]!.AsArray())
        {
            var library = (JsonObject) libraryNode!;   // 旧 As JObject：非对象项抛 InvalidCastException

            // 旧 :1270-1273 会先清理 JSON null 项（BakaXL 会把没有的项序列化为 null，#409）；STJ 下 JSON null 即 null 引用，无需清理

            //检查是否需要（Rules）
            if (!RuleCheck(library["rules"])) continue;

            //获取根节点下的 url
            string? rootUrl = Token(library, "url");
            if (rootUrl is not null)
            {
                rootUrl += GetLocalPath(library["name"]?.ToString()!, false, true, customMcFolder).Replace("\\", "/");
            }

            //根据是否本地化处理（Natives）
            if (library["natives"] is null)
            {
                //没有 Natives
                string localPath = GetLocalPath(library["name"]?.ToString()!, customMcFolder: customMcFolder);
                try
                {
                    var artifact = NodeObjectStrict(NodeObject(library, "downloads"), "artifact");
                    if (artifact is not null)
                    {
                        basicArray.Add(new LibEntry(library)
                        {
                            Name = library["name"]?.ToString(),
                            Url = NormalizeUrl(Token(artifact, "url") ?? rootUrl),
                            Path = Token(artifact, "path") is { } artifactPath
                                ? customMcFolder + @"libraries\" + artifactPath.Replace("/", "\\")
                                : GetLocalPath(library["name"]?.ToString()!, customMcFolder: customMcFolder),
                            // 旧 :1295 Val(...ToString)：size 项缺失时 NRE，走 Catch 回退
                            Size = Token(artifact, "size") ?? throw new NullReferenceException("size 项缺失"),
                            IsNative = false,
                            Checksum = Token(artifact, "sha1")
                        });
                    } else
                    {
                        basicArray.Add(PlainEntry(library, rootUrl, localPath));
                    }
                } catch (Exception ex)
                {
                    Logger.Warn(ex, $"处理实际支持库列表失败（无 Natives，{library["name"]?.ToString() ?? "Nothing"}）");
                    basicArray.Add(PlainEntry(library, rootUrl, localPath));
                }
            } else
            {
                // 旧 :1305：natives 为原始值时索引器抛 ArgumentException（不在此处捕获）
                var natives = NodeObject(library, "natives") ?? throw new ArgumentException("natives 不是 JSON 对象");
                //有 Windows Natives
                string? nativeName = Token(natives, "windows");
                if (nativeName is null) continue;   // 旧 :1305 ElseIf 不成立：跳过该库
                try
                {
                    // 旧 :1308：downloads / classifiers / 分类项为 JSON null 或原始值时经索引器抛 ArgumentException，走 Catch 回退
                    var classifiers = NodeObjectStrict(NodeObject(library, "downloads"), "classifiers");
                    var nativeData = classifiers is null ? null : NodeObjectStrict(classifiers, nativeName);
                    if (nativeData is not null)
                    {
                        basicArray.Add(new LibEntry(library)
                        {
                            Name = library["name"]?.ToString(),
                            Url = NormalizeUrl(Token(nativeData, "url") ?? rootUrl),
                            Path = Token(nativeData, "path") is { } nativePath
                                ? $"{customMcFolder}libraries\\{nativePath.Replace("/", "\\")}"
                                : GetLocalPath(library["name"]?.ToString()!, customMcFolder: customMcFolder).
                                    Replace(".jar", "-" + nativeName + ".jar").
                                    Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32"),
                            // 旧 :1318-1320：size / sha1 取值无 Optional 链，缺失即 NRE 走 Catch 回退
                            Size = Token(nativeData, "size") ?? throw new NullReferenceException("size 项缺失"),
                            IsNative = true,
                            Checksum = Token(nativeData, "sha1") ?? throw new NullReferenceException("sha1 项缺失")
                        });
                    } else
                    {
                        basicArray.Add(NativeEntry(library, rootUrl, nativeName, customMcFolder));
                    }
                } catch (Exception ex)
                {
                    Logger.Warn(ex, $"处理实际支持库列表失败（有 Natives，{library["name"]?.ToString() ?? "Nothing"}）");
                    basicArray.Add(NativeEntry(library, rootUrl, nativeName, customMcFolder));
                }
            }
        }

        //去重
        var resultArray = new Dictionary<string, LibEntry>();
        for (int i = 0; i < basicArray.Count; i++)
        {
            // 旧 :1353 Key = Name & IsNatives.ToString：VB 拼接把 Nothing 当作 ""、布尔作 "True"/"False"
            string key = (basicArray[i].ShortName() ?? "") + (basicArray[i].IsNative ? "True" : "False");
            if (resultArray.ContainsKey(key))
            {
                string basicArrayVersion = GetVersion(basicArray[i]);
                string resultArrayVersion = GetVersion(resultArray[key]);
                if (basicArrayVersion != resultArrayVersion && keepSameNameDifferentVersion)
                {
                    Logger.Info($"发现疑似重复的支持库：{basicArray[i]} ({basicArrayVersion}) 与 {resultArray[key]} ({resultArrayVersion})");
                    resultArray.Add(key + Uuid(), basicArray[i]);
                } else
                {
                    Logger.Info($"发现重复的支持库：{basicArray[i]} ({basicArrayVersion}) 与 {resultArray[key]} ({resultArrayVersion})，已忽略其中之一");
                    if (McUtils.CompareVersionGE(basicArrayVersion, resultArrayVersion))
                    {
                        resultArray[key] = basicArray[i];
                    }
                }
            } else
            {
                resultArray.Add(key, basicArray[i]);
            }
        }
        return resultArray.Values.ToList();
    }

    /// <summary>
    /// 获取对应的支持库文件地址（McLibGet，ModMinecraft.vb:1872-1885）。
    /// </summary>
    /// <param name="original">原始地址，如 com.mumfrey:liteloader:1.12.2-SNAPSHOT。</param>
    /// <param name="withHead">是否包含 Lib 文件夹头部，若不包含，则会类似以 com\xxx\ 开头。</param>
    /// <param name="ignoreLiteLoader">与旧签名一致；旧实现未参与逻辑。</param>
    /// <param name="customMcFolder">Minecraft 根文件夹，以 \ 结尾（VB 转发层传入 McFolderSelected）。</param>
    public static string GetLocalPath(string original, bool withHead = true, bool ignoreLiteLoader = false, string? customMcFolder = null)
    {
        // 旧实现传入 Nothing 的 name 时在 Split 处 NRE；这里显式拒绝
        if (original is null) throw new ArgumentNullException(nameof(original), "版本 Json 中的 name 项缺失");
        if (customMcFolder is null) throw new ArgumentNullException(nameof(customMcFolder), "VB 转发层需传入 McFolderSelected，C# 侧无模块状态");
        //有时候原始的有四段，例如 net.minecraftforge:forge:1.21.4-54.0.34:client
        //文件夹名应该忽略最后一段（client），但文件名需要保留
        //参见 #7306（以及 #5376 也是因为这里没有处理好导致的）
        string[] splited = original.Split(':');
        return (withHead ? customMcFolder + @"libraries\" : "") +
            $@"{splited[0].Replace(".", "\\")}\{splited[1]}\{splited[2]}\{string.Join("-", splited.Skip(1))}.jar";
    }

    /// <summary>
    /// 检查设置，是否应当忽略文件检查？（ShouldIgnoreFileCheck，ModMinecraft.vb:1886-1889）。
    /// </summary>
    public static bool ShouldIgnoreFileCheck(InstanceCore instance) =>
        SettingsService.Get<bool>("VersionAdvanceAssetsV2", instance.InstanceSettingsKey) ||
        SettingsService.Get<int>("VersionAdvanceAssets", instance.InstanceSettingsKey) == 2;

    #region 内部实现

    /// <summary>
    /// Newtonsoft JToken 取值的移植语义（旧代码经 JToken 索引器 + String 窄化取值）：
    /// 键缺失 → null（等价 JToken Nothing）；键存在但值为 JSON null → ""（等价 JValue.Null.ToString()）。
    /// </summary>
    internal static string? Token(JsonObject? node, string key)
    {
        if (node is null || !node.ContainsKey(key)) return null;
        return node[key]?.ToString() ?? "";
    }

    /// <summary>取 node[key] 且要求为对象（库级键，旧代码 :1270-1273 已清理 JSON null）：缺失或 JSON null → null；原始值 → 抛异常。</summary>
    private static JsonObject? NodeObject(JsonObject? node, string key)
    {
        if (node is null) return null;
        var child = node[key];
        return child is null ? null : child as JsonObject ?? throw new ArgumentException(key + " 不是 JSON 对象");
    }

    /// <summary>取 node[key] 且要求为对象（嵌套键，旧代码无清理）：缺失 → null；JSON null 或原始值 → 抛 ArgumentException（对应旧 JValue 索引器/取值行为）。</summary>
    private static JsonObject? NodeObjectStrict(JsonObject? node, string key)
    {
        if (node is null || !node.ContainsKey(key)) return null;
        return node[key] as JsonObject ?? throw new ArgumentException(key + " 不是 JSON 对象");
    }

    /// <summary>旧 McLibEntry.Url setter：孤儿 Forge 作者喜欢把没有 URL 的写个空字符串（ModMinecraft.vb:1529-1531）。</summary>
    private static string? NormalizeUrl(string? url) => string.IsNullOrWhiteSpace(url) ? null : url;

    /// <summary>旧 :1299/:1303 的回退条目：仅保留 name/url/localPath。</summary>
    private static LibEntry PlainEntry(JsonObject library, string? rootUrl, string localPath) => new(library)
    {
        Name = library["name"]?.ToString(),
        Url = NormalizeUrl(rootUrl),
        Path = localPath,
        Size = null,
        IsNative = false,
        Checksum = null
    };

    /// <summary>旧 :1322-1327/:1331-1336 的回退条目：以坐标拼 natives 默认路径。</summary>
    private static LibEntry NativeEntry(JsonObject library, string? rootUrl, string nativeName, string customMcFolder) => new(library)
    {
        Name = library["name"]?.ToString(),
        Url = NormalizeUrl(rootUrl),
        Path = GetLocalPath(library["name"]?.ToString()!, customMcFolder: customMcFolder).
                Replace(".jar", $"-{nativeName}.jar").
                Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32"),
        Size = null,
        IsNative = true,
        Checksum = null
    };

    /// <summary>按版本名（或完整路径）构建只读子实例，对应旧 New McInstance(...)；runInUi 为 null 即非 UI 线程的读取重试语义。</summary>
    private static InstanceCore InstanceByName(string mcFolder, string nameOrFullPath) =>
        InstanceByPath(mcFolder, InstancePaths.ResolveVersionFolder(nameOrFullPath, mcFolder));

    private static InstanceCore InstanceByPath(string mcFolder, string versionFolder) =>
        new(versionFolder, mcFolder, versionFolder);

    /// <summary>去重键中的版本号取法（ModMinecraft.vb:1344-1351）。</summary>
    private static string GetVersion(LibEntry token) =>
        PathUtils.GetLastPart(PathUtils.RemoveLastPart(token.Path ?? ""));

    /// <summary>与 VB GetUuid（ModBase.vb:822-828）一致的递增序号，仅作去重键后缀。</summary>
    private static int uuidCounter;
    private static string Uuid() => Interlocked.Increment(ref uuidCounter).ToString();

    private static readonly Regex ValPrefixRegex = new(@"^\s*[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?", RegexOptions.Compiled);

    /// <summary>VB Val 的移植（旧 Size 记账用）：解析字符串前缀的数值部分，无效时为 0。</summary>
    internal static double Val(string? text)
    {
        if (text is null) return 0;
        var match = ValPrefixRegex.Match(text);
        return match.Success ? double.Parse(match.Value, CultureInfo.InvariantCulture) : 0;
    }

    #endregion
}