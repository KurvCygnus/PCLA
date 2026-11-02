using PCLCS.Settings;
using System.Globalization;
using System.Text.Json.Nodes;

namespace PCLCS.Download;

/// <summary>
/// 资源检索的条目模型与 Drop 缓存面（自宿主层 Modules\Minecraft\ModDownload.vb 迁入的模型面）。
/// 携带 JSON 的成员使用 System.Text.Json 类型（本工程不引 Newtonsoft）；宿主层在边界经 LegacyJsonExtensions.ToJToken 互转。
/// 字段与可见性对宿主层调用方逐字保持（含 PageDownloadModify / ModDownloadLib 既有调用方经 JsonSerializer 反序列化 DlOptiFineListEntry 的字段语义）。
/// </summary>

/// <summary>
/// Minecraft 客户端 版本列表的加载器结果（ModDownload.vb:182-195 逐字）。
/// </summary>
public struct DlClientListResult
{
    /// <summary>
    /// 数据来源名称，如“Mojang”，“BMCLAPI”。
    /// </summary>
    public string? SourceName;
    /// <summary>
    /// 是否为官方的实时数据。
    /// </summary>
    public bool IsOfficial;
    /// <summary>
    /// 获取到的 Json 数据。（VB 侧为 Newtonsoft JObject，随模型迁入收敛为 STJ JsonObject；宿主层经 ToJToken 转换）
    /// </summary>
    public JsonObject? Value;
}

/// <summary>
/// OptiFine 版本列表的加载器结果（ModDownload.vb:319-332 逐字）。
/// </summary>
public struct DlOptiFineListResult
{
    /// <summary>
    /// 数据来源名称，如“Official”，“BMCLAPI”。
    /// </summary>
    public string? SourceName;
    /// <summary>
    /// 是否为官方的实时数据。
    /// </summary>
    public bool IsOfficial;
    /// <summary>
    /// 获取到的数据。
    /// </summary>
    public List<DlOptiFineListEntry>? Value;
}

/// <summary>
/// OptiFine 版本条目（ModDownload.vb:334-372 逐字；公开成员保持字段形态，宿主层既有 STJ/Newtonsoft 序列化路径的成员语义不变）。
/// </summary>
public sealed class DlOptiFineListEntry
{
    /// <summary>
    /// 显示名称，已去除 HD_U 字样，如“1.12.2 C8”。
    /// </summary>
    public string? DisplayName;
    /// <summary>
    /// 原始文件名称，如“preview_OptiFine_1.11_HD_U_E1_pre.jar”。
    /// </summary>
    public string? FileName;
    /// <summary>
    /// 对应的版本名称，如“1.13.2-OptiFine_HD_U_E6”。
    /// </summary>
    public string? InstanceName;
    /// <summary>
    /// 是否为测试版。
    /// </summary>
    public bool IsPreview;
    /// <summary>
    /// 对应的 Minecraft 版本，如“1.12.2”。
    /// </summary>
    public string? Inherit
    {
        get => _inherit;
        set
        {
            if (value.EndsWithF(".0")) value = value!.Substring(0, value.Length - 2); //VB Left(value, value.Length - 2)
            _inherit = value;
        }
    }
    private string? _inherit;
    /// <summary>
    /// 发布时间，格式为“yyyy/mm/dd”。OptiFine 源无此数据。
    /// </summary>
    public string? ReleaseTime;
    /// <summary>
    /// 需要的最低 Forge 版本。空字符串为无限制，null 为不兼容，“28.1.56” 表示版本号，“1161” 表示版本号的最后一位。
    /// </summary>
    public string? RequiredForgeVersion;
}

/// <summary>
/// Forge / NeoForge 条目的公共基类（ModDownload.vb:480-517 逐字）。
/// </summary>
public abstract class DlForgelikeEntry : IComparable<DlForgelikeEntry>
{
    public bool IsNeoForge;
    /// <summary>
    /// 加载器名称。Forge 或 NeoForge。
    /// </summary>
    public abstract string LoaderName { get; }
    /// <summary>
    /// 文件扩展名。不以小数点开头。
    /// </summary>
    public abstract string FileExtension { get; }
    /// <summary>
    /// 标准化后的版本号，仅可用于比较与排序，且 NeoForge 项不包含 alpha/beta 编号！
    /// 格式：Major.Minor.Build.Revision
    /// Forge：如 “50.1.9.0”（最后一位固定为 0）、“14.22.1.2478”（Legacy）。
    /// NeoForge：如 “20.4.30.0”（最后一位固定为 0）、“19.47.1.99”（Legacy：第一位固定为 19）。
    /// </summary>
    public Version? Version;
    /// <summary>
    /// 可读的非格式化版本名。
    /// Forge：如 “50.1.9”、“14.22.1.2478”（Legacy）。
    /// NeoForge：如 “20.4.30-beta”、“47.1.99”（Legacy）。
    /// </summary>
    public string? VersionName;
    /// <summary>
    /// 对应的 Minecraft 版本，如 1.12.2、26.1-snapshot-1。
    /// </summary>
    public string? Inherit;

    public int CompareTo(DlForgelikeEntry? other)
    {
        //VB 真值无判空：other 为 Nothing 时在取成员处抛 NullReferenceException（与宿主层一致）
        //! 逐字移植豁免：If/Else 结构对照 ModDownload.vb:510-516
        if (Version != other!.Version)
        {
            return Version!.CompareTo(other.Version);
        } else
        {
            return McUtils.CompareVersion(VersionName!, other.VersionName!);
        }
    }
}

/// <summary>
/// Forge 版本条目（ModDownload.vb:519-572 逐字，含 :561-563 的构造特殊分支）。
/// </summary>
public sealed class DlForgeVersionEntry : DlForgelikeEntry
{
    /// <summary>
    /// 发布时间，格式为“yyyy/MM/dd HH:mm”。
    /// </summary>
    public string? ReleaseTime;
    /// <summary>
    /// 文件的 MD5 或 SHA1（BMCLAPI 的老版本是 MD5，新版本是 SHA1；官方源总是 MD5）。
    /// </summary>
    public string? Hash;
    /// <summary>
    /// 是否为推荐版本。
    /// </summary>
    public bool IsRecommended;
    /// <summary>
    /// 安装类型。有 installer、client、universal 三种。
    /// </summary>
    public string? Category;
    /// <summary>
    /// 用于下载的文件版本名。可能在 Version 的基础上添加了分支。
    /// </summary>
    public string? FileVersion;

    /// <summary>
    /// 加载器名称。Forge 或 NeoForge。
    /// </summary>
    public override string LoaderName => "Forge";
    /// <summary>
    /// 文件扩展名。不以小数点开头。
    /// </summary>
    public override string FileExtension => Category == "installer" ? "jar" : "zip";

    public DlForgeVersionEntry(string version, string? branch, string inherit)
    {
        //司马版本的特殊处理（ModDownload.vb:562-563；末段数值比较沿用 ForgeBuildParser.ForgeBuildEntry 的既有移植）
        if (version is "11.15.1.2318" or "11.15.1.1902" or "11.15.1.1890") branch = "1.8.9";
        if (branch is null && inherit == "1.7.10" && int.Parse(version.Split('.')[3]) >= 1300) branch = "1.7.10";
        //为 DlForgelikeEntry 提供所有信息
        IsNeoForge = false;
        VersionName = version;
        Version = new Version(version);
        Inherit = inherit;
        FileVersion = version + (branch is null ? "" : "-" + branch);
    }
}

/// <summary>
/// NeoForge 版本列表的加载器结果（ModDownload.vb:625-638 逐字）。
/// </summary>
public struct DlNeoForgeListResult
{
    /// <summary>
    /// 数据来源名称，如“Official”，“BMCLAPI”。
    /// </summary>
    public string? SourceName;
    /// <summary>
    /// 是否为官方的实时数据。
    /// </summary>
    public bool IsOfficial;
    /// <summary>
    /// 所有版本的列表。已经按从新到老排序。
    /// </summary>
    public List<DlNeoForgeListEntry>? Value;
}

/// <summary>
/// NeoForge 版本条目（ModDownload.vb:640-703 逐字）。
/// </summary>
public sealed class DlNeoForgeListEntry : DlForgelikeEntry
{
    /// <summary>
    /// 是否是 Beta 版。
    /// </summary>
    public bool IsBeta;
    /// <summary>
    /// API 使用的原始版本字符串，如 “20.4.30-beta”、“1.20.1-47.1.99”（Legacy）。
    /// </summary>
    public string? ApiName;
    /// <summary>
    /// 文件在官网的基础地址，不包含后缀。
    /// </summary>
    public string UrlBase
    {
        get
        {
            var packageName = Inherit == "1.20.1" ? "forge" : "neoforge";
            return $"https://maven.neoforged.net/releases/net/neoforged/{packageName}/{ApiName}/{packageName}-{ApiName}";
        }
    }

    /// <summary>
    /// 加载器名称。Forge 或 NeoForge。
    /// </summary>
    public override string LoaderName => "NeoForge";
    /// <summary>
    /// 文件扩展名。不以小数点开头。
    /// </summary>
    public override string FileExtension => "jar";

    public DlNeoForgeListEntry(string apiName)
    {
        IsNeoForge = true;
        ApiName = apiName;
        IsBeta = apiName.ContainsIgnoreCase("beta") || apiName.ContainsIgnoreCase("alpha");
        if (apiName.Contains("1.20.1"))
        {
            //1.20.1-47.1.99
            VersionName = apiName.Replace("1.20.1-", "");
            Version = new Version("19." + VersionName);
            Inherit = "1.20.1";
        } else if (apiName.StartsWithF("0."))
        {
            //0.25w14craftmine.3-beta
            VersionName = apiName;
            var segments = apiName.BeforeFirst("-").Split('.');
            Version = new Version(0, 0, int.Parse(segments[segments.Length - 1])); //VB Segments.Last
            Inherit = segments[1];
        } else
        {
            //20.4.30-beta；26.1.0.0-alpha.1+snapshot-1
            VersionName = apiName;
            Version = new Version(apiName.BeforeFirst("-"));
            Inherit = Version.Major >= 24
                ? $"{Version.Major}.{Version.Minor}.{Version.Build}"
                : $"1.{Version.Major}.{Version.Minor}";
            if (Inherit.EndsWithF(".0")) Inherit = Inherit.Substring(0, Inherit.Length - 2); //VB Left(Inherit, Inherit.Length - 2)
            if (VersionName.Contains("+")) Inherit += "-" + VersionName.AfterFirst("+");
        }
    }
}

/// <summary>
/// LiteLoader 版本列表的加载器结果（ModDownload.vb:745-762 逐字）。
/// </summary>
public struct DlLiteLoaderListResult
{
    /// <summary>
    /// 数据来源名称，如“Official”，“BMCLAPI”。
    /// </summary>
    public string? SourceName;
    /// <summary>
    /// 是否为官方的实时数据。
    /// </summary>
    public bool IsOfficial;
    /// <summary>
    /// 获取到的数据。
    /// </summary>
    public List<DlLiteLoaderListEntry>? Value;
    /// <summary>
    /// 官方源的失败原因。若没有则为 Nothing。
    /// </summary>
    public Exception? OfficialError;
}

/// <summary>
/// LiteLoader 版本条目（ModDownload.vb:764-793 逐字）。
/// </summary>
public sealed class DlLiteLoaderListEntry
{
    /// <summary>
    /// 实际的文件名，如“liteloader-installer-1.12-00-SNAPSHOT.jar”。
    /// </summary>
    public string? FileName;
    /// <summary>
    /// 是否为测试版。
    /// </summary>
    public bool IsPreview;
    /// <summary>
    /// 对应的 Minecraft 版本，如“1.12.2”。
    /// </summary>
    public string? Inherit;
    /// <summary>
    /// 是否为 1.7 及更早的远古版。
    /// </summary>
    public bool IsLegacy;
    /// <summary>
    /// 发布时间，格式为“yyyy/mm/dd HH:mm”。
    /// </summary>
    public string? ReleaseTime;
    /// <summary>
    /// 文件的 MD5。
    /// </summary>
    //! 逐字移植豁免：字段名对照 ModDownload.vb:788（MD5 命名不随 C# 风格改为 Md5）
    public string? MD5;
    /// <summary>
    /// 对应的 Json 项。
    /// </summary>
    public JsonNode? JsonToken;
}

/// <summary>
/// Fabric 列表的加载器结果（ModDownload.vb:848-861 逐字）。
/// </summary>
public struct DlFabricListResult
{
    /// <summary>
    /// 数据来源名称，如“Official”，“BMCLAPI”。
    /// </summary>
    public string? SourceName;
    /// <summary>
    /// 是否为官方的实时数据。
    /// </summary>
    public bool IsOfficial;
    /// <summary>
    /// 获取到的数据。（VB 侧为 Newtonsoft JObject，随模型迁入收敛为 STJ JsonObject；宿主层经 ToJToken 转换）
    /// </summary>
    public JsonObject? Value;
}

/// <summary>
/// 缓存的 Minecraft Drop 序数表（ModDownload.vb:162-179 AllDrops 的数据面）。
/// </summary>
public static class Drops
{
    private static List<int>? allDrops; //VB _AllDrops

    /// <summary>
    /// 所有正式版的 Minecraft Drop 序数。
    /// 若从未完成过获取，返回 null；否则必定存在元素，且从高到低排列。
    /// </summary>
    public static List<int>? AllDrops
    {
        get
        {
            if (allDrops is null)
            {
                allDrops = SettingsService.Get<string>("CacheDrops")
                    .Split(",", true) //VB Split(",", True)：去空段的字符串分段
                    .Select(static d => Convert.ToInt32(Val(d))) //VB CInt(Val(d))：银行家舍入与溢出语义一致
                    .ToList();
            }
            return allDrops.Count > 0 ? allDrops : null; //不要将 _allDrops 再设为 null，以防止反复获取设置尝试初始化
        }
        set
        {
            allDrops = value;
            SettingsService.Set("CacheDrops", string.Join(",", value!)); //VB value.Join(","c)：Nothing 时同样抛出
        }
    }

    /// <summary>清空进程内的 Drop 缓存，使下次取值重读设置（模拟宿主层重启；仅供测试对齐进程边界）。</summary>
    internal static void ResetForTests() => allDrops = null;

    /// <summary>VB Val 的移植（ModBase.vb:609 包装 Conversion.Val）：解析前缀数值，无效为 0（与 Instances\Libraries.cs 的既有移植同源）。</summary>
    private static double Val(string? text)
    {
        if (text is null) return 0;
        var match = ValPrefixRegex.Match(text);
        return match.Success ? double.Parse(match.Value, CultureInfo.InvariantCulture) : 0;
    }

    private static readonly Regex ValPrefixRegex = new(@"^\s*[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?", RegexOptions.Compiled);
}