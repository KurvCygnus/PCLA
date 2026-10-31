using PCLCS.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCLCS.Instances;

/// <summary>
/// 资源文件索引解析的纯函数面（自 VB ModMinecraft.vb:1898-2051 移植）。
/// NetFile 下载映射（DlSourceAssetsGet、FileChecker、进度反馈）留在 VB ModMinecraft.vb。
/// </summary>
public static class AssetsIndex
{
    /// <summary>
    /// 获取某版本资源文件索引的对应 Json 项，详见版本 Json 中的 assetIndex 项。失败会抛出异常（McAssetsGetIndex，ModMinecraft.vb:1898-1934）。
    /// </summary>
    public static JsonObject GetIndex(InstanceCore instance, bool returnLegacyOnError = false, bool checkUrlEmpty = false)
    {
        // 旧 :1541 的 assets 记账仅被注释掉的 legacy 分支使用，属死代码，未随迁
        try
        {
            while (true)
            {
                var index = IndexObject(instance);
                if (index is not null && index.ContainsKey("id")) return index;
                if (checkUrlEmpty)
                {
                    // 旧 :1547：Index 为 Nothing 时 Index("url") 会 NRE，落入 Catch
                    if (index is null) throw new NullReferenceException("assetIndex 不存在");
                    if (index.ContainsKey("url")) return index;
                }
                //下一个版本
                if (instance.InheritName == "") break;
                instance = InstanceByName(instance, instance.InheritName);
            }
        } catch
        {
            // 旧 :1552 空 Catch
        }
        //无法获取到下载地址
        if (!returnLegacyOnError) throw new Exception("该版本不存在资源文件索引信息");
        //返回 assets 文件名会由于没有下载地址导致全局失败
        Logger.Info("无法获取资源文件索引下载地址，使用默认的 legacy 下载地址");
        return JsonNodeUtils.ParseObject("""
            {
                "id": "legacy",
                "sha1": "c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729",
                "size": 134284,
                "url": "https://launchermeta.mojang.com/mc-staging/assets/legacy/c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729/legacy.json",
                "totalSize": 111220701
            }
            """);
    }

    /// <summary>
    /// 获取某版本资源文件索引名，优先使用 assetIndex，其次使用 assets。失败会返回 legacy（McAssetsGetIndexName，ModMinecraft.vb:1935-2018）。
    /// </summary>
    public static string GetIndexName(InstanceCore instance)
    {
        try
        {
            while (true)
            {
                var index = IndexObject(instance);
                if (index is not null)
                {
                    // 旧 :1980：id 为 JSON null 时经 JValue.ToString 返回 ""
                    string? id = Libraries.Token(index, "id");
                    if (id is not null) return id;
                }
                var assets = Libraries.Token(instance.VersionJson, "assets");
                if (assets is not null) return assets;
                if (instance.InheritName == "") break;
                instance = InstanceByName(instance, instance.InheritName);
            }
        } catch (Exception ex)
        {
            Logger.Warn(ex, "获取资源文件索引名失败");
        }
        return "legacy";
    }

    /// <summary>
    /// 获取版本资源文件的下载描述列表（McAssetsFixList 的数据面，ModMinecraft.vb:2019-2051）。
    /// 每项为 {"path", "hash", "size"} JsonObject；映射为 NetFile 与进度反馈由 VB 层完成。
    /// </summary>
    /// <param name="instance">目标实例。</param>
    /// <param name="checkHash">true 时返回全部项（Hash 检查留到下载时多线程处理）；false 时仅返回缺失或大小不符的项。</param>
    public static List<JsonObject> FixList(InstanceCore instance, bool checkHash)
    {
        var assetsList = ListGet(instance);
        if (checkHash) return assetsList;
        //如果不检查 Hash，则立即处理
        var result = new List<JsonObject>();
        foreach (var token in assetsList)
        {
            //检查文件是否存在
            var file = FileUtils.GetInfo(token["path"]!.ToString());
            long size = token["size"]!.GetValue<long>();
            if (file.Exists && (size == 0 || size == file.Length)) continue;
            //文件不存在，添加下载
            result.Add(token);
        }
        return result;
    }

    #region 内部实现

    /// <summary>获取 Minecraft 的资源文件列表。失败会抛出异常（McAssetsListGet，ModMinecraft.vb:1979-2017）。</summary>
    private static List<JsonObject> ListGet(InstanceCore instance)
    {
        string indexName = GetIndexName(instance);
        try
        {
            //初始化
            string mcFolder = instance.McFolderSelected;
            string indexPath = $@"{mcFolder}assets\indexes\{indexName}.json";
            if (!FileUtils.Exists(indexPath)) throw new FileNotFoundException("未找到 Asset Index", indexPath);
            var result = new List<JsonObject>();
            var json = FileUtils.ReadAsJson(indexPath)?.AsObject()
                ?? throw new NullReferenceException("索引 Json 为空");

            //读取列表（旧 :1631 In Json("objects").Children：objects 缺失时 NRE；为 JSON null 时 JValue.Children 为空集，此处与缺失同样抛出）
            var objects = json["objects"]!.AsObject();
            foreach (var assetPair in objects)
            {
                string assetName = assetPair.Key;
                var assetValue = assetPair.Value;
                // 旧 For Each As JProperty：非对象项抛 InvalidCastException
                var asset = assetValue as JsonObject ?? throw new InvalidCastException("资源项不是 JSON 对象");
                // 旧 :1646-1647：hash/size 取值无 Optional 链，缺失即 NRE；JSON null 经 JValue.ToString 为 ""
                string hash = Libraries.Token(asset, "hash") ?? throw new NullReferenceException("hash 项缺失");
                long size = ParseLong(Libraries.Token(asset, "size") ?? throw new NullReferenceException("size 项缺失"));
                string localPath;
                if (Flag(json, "map_to_resources"))
                {
                    //Remap
                    localPath = $@"{instance.PathIndie}resources\{assetName}";
                } else if (Flag(json, "virtual"))
                {
                    //Virtual
                    localPath = $@"{mcFolder}assets\virtual\legacy\{assetName}";
                } else
                {
                    //正常
                    localPath = $@"{mcFolder}assets\objects\{hash[..Math.Min(2, hash.Length)]}\{hash}";
                }
                result.Add(new JsonObject
                {
                    ["path"] = localPath.Replace("/", "\\"),
                    ["hash"] = hash,
                    ["size"] = size
                });
            }
            return result;
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"获取资源文件列表失败：{indexName}");
            throw;
        }
    }

    /// <summary>取版本 Json 的 assetIndex 项：缺失或 JSON null → null；原始值 → 抛 ArgumentException（对应旧 Index("id") 在原始值上的异常）。</summary>
    private static JsonObject? IndexObject(InstanceCore instance)
    {
        var node = instance.VersionJson["assetIndex"];
        return node as JsonObject ?? (node is null ? null : throw new ArgumentException("assetIndex 不是 JSON 对象"));
    }

    /// <summary>按 InheritName 走向父版本，对应旧 New McInstance(McFolderSelected & "versions\" & InheritName)。</summary>
    private static InstanceCore InstanceByName(InstanceCore instance, string inheritName)
    {
        string versionFolder = InstancePaths.ResolveVersionFolder(inheritName, instance.McFolderSelected);
        return new InstanceCore(versionFolder, instance.McFolderSelected, versionFolder);
    }

    /// <summary>旧 Json(key) IsNot Nothing AndAlso .ToObject(Of Boolean) 的移植；非法值按 Convert 语义抛异常入 Catch。</summary>
    private static bool Flag(JsonObject? json, string key)
    {
        var node = json?[key];
        if (node is null) return false;   // 缺失或 JSON null 均为 False（旧 JValue.Null ToObject(Boolean) 亦为 False）
        return node.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => bool.Parse(node.ToString())
        };
    }

    /// <summary>旧 Conversions.ToLong 的移植：数值直接转换，空串为 0，非法串抛 FormatException。</summary>
    private static long ParseLong(string text)
    {
        if (text.Length == 0) return 0;
        return long.Parse(text, CultureInfo.InvariantCulture);
    }

    #endregion
}