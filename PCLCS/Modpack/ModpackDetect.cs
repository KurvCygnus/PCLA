using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCLCS.Modpack;

/// <summary>
/// 整合包种类（ModModpack.vb:44-113 的 PackType 判定结果；数值映射由宿主层 Select Case 承担）。
/// </summary>
public enum ModpackKind
{
    CurseForge,
    Modrinth,
    Mcbbs,
    Hmcl,
    Mcm,
    LauncherPack,
    /// <summary>未能识别，假定为普通压缩包（旧 PackType = -1，走 InstallPackCompress）。</summary>
    PlainArchive
}

/// <summary>
/// 整合包类型识别（ModModpack.vb:44-88 逐行移植）。
/// </summary>
public static class ModpackDetect
{
    //* 与 Newtonsoft JObject 的索引语义对齐：属性查询区分大小写（ Ordinal），容注释与尾逗号（DeserializeJson 默认行为）
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// 识别整合包种类（ModModpack.vb:44-79）：
    /// mcbbs.packmeta→MCBBS、mmc-pack.json→MCM、modrinth.index.json→Modrinth、manifest.json（有无 addons）→CF/MCBBS、
    /// modpack.zip/mrpack→带启动器的压缩包，均先根目录后一级目录逐项判定（判定顺序逐字，#4194 的优先级保持）。
    /// </summary>
    /// <param name="modpackZipPath">整合包压缩文件地址。</param>
    public static ModpackKind Detect(string modpackZipPath) => Detect(modpackZipPath, out _);

    /// <summary>
    /// 识别整合包种类并同时给出基础文件夹（ModModpack.vb:60-79 的 ArchiveBaseFolder 伴随产出）。
    /// rar 拒绝与打开失败的包装语义（:80-88）随识别链逐字保留。
    /// </summary>
    /// <param name="modpackZipPath">整合包压缩文件地址。</param>
    /// <param name="archiveBaseFolder">
    /// 基础文件夹：根目录命中或未能识别时为 null；一级目录命中时形如 <c>"包内文件夹/"</c>；
    /// 一级目录 manifest.json 判得 MCBBS 时固定 <c>"overrides/"</c>（:74）。未识别时末次扫描值与旧循环残留一致，不外发。
    /// </param>
    public static ModpackKind Detect(string modpackZipPath, out string? archiveBaseFolder)
    {
        archiveBaseFolder = null;
        try
        {
            using var archive = FileUtils.OpenZip(modpackZipPath);
            //从根目录判断整合包类型（:46-58）
            if (archive.GetEntry("mcbbs.packmeta") != null) return ModpackKind.Mcbbs; //MCBBS 整合包（优先于 manifest.json 判断）
            if (archive.GetEntry("mmc-pack.json") != null) return ModpackKind.Mcm; //MMC 整合包（优先于 manifest.json 判断，#4194）
            if (archive.GetEntry("modrinth.index.json") != null) return ModpackKind.Modrinth; //Modrinth 整合包
            if (archive.GetEntry("manifest.json") != null)
            {
                var json = (JsonObject?) JsonNode.Parse(
                    ReadEntry(archive.GetEntry("manifest.json")!), null, ParseOptions); //旧 DeserializeJson()：解析失败在此抛出
                if (!json!.ContainsKey("addons")) return ModpackKind.CurseForge; //CurseForge 整合包
                return ModpackKind.Mcbbs; //MCBBS 整合包
            }
            if (archive.GetEntry("modpack.json") != null) return ModpackKind.Hmcl; //HMCL 整合包
            if (archive.GetEntry("modpack.zip") != null || archive.GetEntry("modpack.mrpack") != null) return ModpackKind.LauncherPack; //带启动器的压缩包
            //从一级目录判断整合包类型（:60-79）
            foreach (var entry in archive.Entries)
            {
                var pathSplits = entry.FullName.Split('/');
                archiveBaseFolder = pathSplits[0] + "/"; //旧循环对每个条目先行赋值（含未命中项）的残留语义
                //确定为一级目录下
                if (pathSplits.Length != 2) continue;
                //判断是否为关键文件
                if (pathSplits[1] == "mcbbs.packmeta") return ModpackKind.Mcbbs; //MCBBS 整合包（优先于 manifest.json 判断）
                if (pathSplits[1] == "mmc-pack.json") return ModpackKind.Mcm; //MMC 整合包（优先于 manifest.json 判断，#4194）
                if (pathSplits[1] == "modrinth.index.json") return ModpackKind.Modrinth; //Modrinth 整合包
                if (pathSplits[1] == "manifest.json")
                {
                    var json = (JsonObject?) JsonNode.Parse(ReadEntry(entry), null, ParseOptions);
                    if (!json!.ContainsKey("addons")) return ModpackKind.CurseForge; //CurseForge 整合包
                    archiveBaseFolder = "overrides/"; //MCBBS 整合包（:74）
                    return ModpackKind.Mcbbs;
                }
                if (pathSplits[1] == "modpack.json") return ModpackKind.Hmcl; //HMCL 整合包
                if (pathSplits[1] == "modpack.zip" || pathSplits[1] == "modpack.mrpack") return ModpackKind.LauncherPack; //带启动器的压缩包
            }
            return ModpackKind.PlainArchive; //未能识别，假定为压缩包（:110-112）
        } catch (Exception ex)
        {
            //! 逐字移植豁免（Rider 反转 if）：VB :81-87 的三分支包装结构
            if (ex.GetDisplay(true).Contains("Error.WinIOError"))
            {
                throw new Exception("打开整合包文件失败", ex);
            }
            if (PathUtils.GetExtension(modpackZipPath) == "rar")
            {
                throw new Exception("PCL 无法处理 rar 格式的压缩包，请在解压后重新压缩为 zip 格式再试", ex);
            }
            throw new Exception("打开整合包文件失败，文件可能损坏或为不支持的压缩包格式", ex);
        }
    }

    /// <summary>读取压缩包条目全文（旧 Stream.ReadString(Encoding.UTF8) 的等价读取）。</summary>
    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}