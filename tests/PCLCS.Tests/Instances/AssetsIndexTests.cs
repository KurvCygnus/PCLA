using PCLCS.Instances;
using PCLCS.Settings;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Instances;

//* 走临时目录实例；FixList 依赖 PathIndie（读实例/全局设置），与设置类测试共享串行集合
[Collection("PCLCS.Settings")]
public class AssetsIndexTests : IDisposable
{
    private readonly string _root;

    public AssetsIndexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pclcs.assets." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        SettingsService.Configure(_root);   // 全局 Settings.json 落在临时目录
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private string VersionDir(string name) => Path.Combine(_root, "versions", name + Path.DirectorySeparatorChar);

    private void WriteVersion(string name, JsonObject json)
    {
        Directory.CreateDirectory(VersionDir(name));
        File.WriteAllText(Path.Combine(VersionDir(name), name + ".json"), json.ToJsonString());
    }

    private InstanceCore NewCore(string name)
    {
        return new InstanceCore(InstancePaths.ResolveVersionFolder(name, _root), _root, VersionDir(name));
    }

    private static JsonObject BaseJson(string id, string assets = "5")
    {
        var json = new JsonObject
        {
            ["id"] = id,
            ["type"] = "release",
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["releaseTime"] = "2023-06-12T10:00:00Z",
            ["time"] = "2023-06-12T10:00:00Z"
        };
        if (assets.Length > 0) json["assets"] = assets;
        return json;
    }

    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static JsonObject ObjectsAAndB() => new()
    {
        ["sounds/a.ogg"] = new JsonObject { ["hash"] = HashA, ["size"] = 3 },
        ["sounds/b.ogg"] = new JsonObject { ["hash"] = HashB, ["size"] = 0 }
    };

    private void WriteIndex(string indexName, JsonObject indexJson)
    {
        Directory.CreateDirectory(Path.Combine(_root, "assets", "indexes"));
        File.WriteAllText(Path.Combine(_root, "assets", "indexes", indexName + ".json"), indexJson.ToJsonString());
    }

    #region GetIndexName（McAssetsGetIndexName，ModMinecraft.vb:1935-2018 回退链）

    [Fact]
    public void PrefersAssetIndexId()
    {
        var json = BaseJson("v1");
        json["assetIndex"] = new JsonObject { ["id"] = "5" };
        WriteVersion("v1", json);
        Assert.Equal("5", AssetsIndex.GetIndexName(NewCore("v1")));
    }

    [Fact]
    public void FallsBackToAssetsItem()
    {
        WriteVersion("v2", BaseJson("v2", assets: "17"));
        Assert.Equal("17", AssetsIndex.GetIndexName(NewCore("v2")));
    }

    [Fact]
    public void FallsBackToLegacyWhenNothingFound()
    {
        WriteVersion("v3", BaseJson("v3", assets: ""));
        Assert.Equal("legacy", AssetsIndex.GetIndexName(NewCore("v3")));
    }

    [Fact]
    public void WalksInheritChainBeforeLegacy()
    {
        var parentJson = BaseJson("parent");
        parentJson["assetIndex"] = new JsonObject { ["id"] = "9" };
        WriteVersion("parent", parentJson);
        var childJson = BaseJson("child", assets: "");
        childJson["inheritsFrom"] = "parent";
        WriteVersion("child", childJson);
        Assert.Equal("9", AssetsIndex.GetIndexName(NewCore("child")));
    }

    [Fact]
    public void BrokenJsonReturnsLegacy()
    {
        // Json 解析失败 → 告警并返回 legacy（:1989-1991 Catch）
        Directory.CreateDirectory(VersionDir("broken"));
        File.WriteAllText(Path.Combine(VersionDir("broken"), "broken.json"), "这不是 Json");
        Assert.Equal("legacy", AssetsIndex.GetIndexName(NewCore("broken")));
    }

    #endregion

    #region GetIndex（McAssetsGetIndex，ModMinecraft.vb:1898-1934）

    [Fact]
    public void ReturnsAssetIndexWhenIdPresent()
    {
        var json = BaseJson("v1");
        json["assetIndex"] = new JsonObject { ["id"] = "5", ["url"] = "https://example.com/5.json" };
        WriteVersion("v1", json);
        var index = AssetsIndex.GetIndex(NewCore("v1"));
        Assert.Equal("5", index["id"]!.ToString());
    }

    [Fact]
    public void ThrowsWhenOnlyAssetsAndNoLegacyRequested()
    {
        WriteVersion("v2", BaseJson("v2", assets: "17"));
        var ex = Assert.Throws<Exception>(() => AssetsIndex.GetIndex(NewCore("v2")));
        Assert.Equal("该版本不存在资源文件索引信息", ex.Message);
    }

    [Fact]
    public void LegacyFallbackReturnsCanonicalLegacyIndex()
    {
        WriteVersion("v2", BaseJson("v2", assets: "17"));
        var index = AssetsIndex.GetIndex(NewCore("v2"), returnLegacyOnError: true);
        Assert.Equal("legacy", index["id"]!.ToString());
        Assert.Equal("c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729", index["sha1"]!.ToString());
        Assert.Equal(134284, index["size"]!.GetValue<int>());
        Assert.Equal("https://launchermeta.mojang.com/mc-staging/assets/legacy/c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729/legacy.json", index["url"]!.ToString());
        Assert.Equal(111220701, index["totalSize"]!.GetValue<long>());
    }

    [Fact]
    public void CheckUrlEmptyAcceptsIndexWithoutId()
    {
        var json = BaseJson("v4", assets: "");
        json["assetIndex"] = new JsonObject { ["url"] = "https://example.com/index.json" };
        WriteVersion("v4", json);
        var core = NewCore("v4");
        Assert.NotNull(AssetsIndex.GetIndex(core, checkUrlEmpty: true));
        Assert.Throws<Exception>(() => AssetsIndex.GetIndex(core));
    }

    [Fact]
    public void WalksInheritChainForMissingIndex()
    {
        var parentJson = BaseJson("parent");
        parentJson["assetIndex"] = new JsonObject { ["id"] = "9" };
        WriteVersion("parent", parentJson);
        var childJson = BaseJson("child", assets: "");
        childJson["inheritsFrom"] = "parent";
        childJson["assetIndex"] = new JsonObject();
        WriteVersion("child", childJson);
        Assert.Equal("9", AssetsIndex.GetIndex(NewCore("child"))["id"]!.ToString());
    }

    #endregion

    #region FixList（McAssetsFixList，ModMinecraft.vb:2019-2051；描述对象由 VB 映射 NetFile）

    private InstanceCore CoreWithIndex(JsonObject indexJson)
    {
        var json = BaseJson("vanilla");
        json["assetIndex"] = new JsonObject { ["id"] = "5" };
        WriteVersion("vanilla", json);
        WriteIndex("5", indexJson);
        return NewCore("vanilla");
    }

    [Fact]
    public void CheckHashReturnsAllObjects()
    {
        var core = CoreWithIndex(new JsonObject { ["objects"] = ObjectsAAndB() });
        var list = AssetsIndex.FixList(core, checkHash: true);
        Assert.Equal(2, list.Count);
        Assert.Equal(_root + @"assets\objects\aa\" + HashA, list[0]!["path"]!.ToString());
        Assert.Equal(HashA, list[0]!["hash"]!.ToString());
        Assert.Equal(3, list[0]!["size"]!.GetValue<long>());
        Assert.Equal(_root + @"assets\objects\bb\" + HashB, list[1]!["path"]!.ToString());
    }

    [Fact]
    public void WithoutCheckHashSkipsExistingFilesWithMatchingSize()
    {
        var core = CoreWithIndex(new JsonObject { ["objects"] = ObjectsAAndB() });
        Directory.CreateDirectory(_root + @"assets\objects\aa");
        File.WriteAllText(_root + @"assets\objects\aa\" + HashA, "abc");   // 3 字节，与 size 匹配
        Directory.CreateDirectory(_root + @"assets\objects\bb");
        File.WriteAllText(_root + @"assets\objects\bb\" + HashB, "toolong"); // size=0：存在即跳过

        var list = AssetsIndex.FixList(core, checkHash: false);

        Assert.Empty(list);
    }

    [Fact]
    public void WithoutCheckHashKeepsMissingOrWrongSizeFiles()
    {
        var core = CoreWithIndex(new JsonObject { ["objects"] = ObjectsAAndB() });
        Directory.CreateDirectory(_root + @"assets\objects\aa");
        File.WriteAllText(_root + @"assets\objects\aa\" + HashA, "abcd");  // 大小不符

        var list = AssetsIndex.FixList(core, checkHash: false);

        Assert.Equal(2, list.Count);
        Assert.EndsWith(@"\assets\objects\aa\" + HashA, list[0]!["path"]!.ToString());
        Assert.EndsWith(@"\assets\objects\bb\" + HashB, list[1]!["path"]!.ToString());
    }

    [Fact]
    public void MapToResourcesRedirectsToVersionResources()
    {
        var core = CoreWithIndex(new JsonObject { ["map_to_resources"] = true, ["objects"] = ObjectsAAndB() });
        SettingsService.Set("LaunchArgumentIndieV2", 0);   // 全局关闭隔离 → PathIndie = Minecraft 根目录

        var list = AssetsIndex.FixList(core, checkHash: true);

        Assert.Equal(_root + @"resources\sounds\a.ogg", list[0]!["path"]!.ToString());
    }

    [Fact]
    public void VirtualRedirectsToLegacyFolder()
    {
        var core = CoreWithIndex(new JsonObject { ["virtual"] = true, ["objects"] = ObjectsAAndB() });

        var list = AssetsIndex.FixList(core, checkHash: true);

        Assert.Equal(_root + @"assets\virtual\legacy\sounds\a.ogg", list[0]!["path"]!.ToString());
    }

    [Fact]
    public void MissingIndexFileThrows()
    {
        var json = BaseJson("vanilla");
        json["assetIndex"] = new JsonObject { ["id"] = "5" };
        WriteVersion("vanilla", json);

        Assert.ThrowsAny<Exception>(() => AssetsIndex.FixList(NewCore("vanilla"), checkHash: true));
    }

    #endregion
}
