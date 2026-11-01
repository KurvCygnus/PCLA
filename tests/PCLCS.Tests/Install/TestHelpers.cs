using System.Text.Json.Nodes;

namespace PCLCS.Tests.Install;

/// <summary>
/// 安装决策核测试共享的 STJ JSON 工厂（W2 计划裁定创建；形状对齐 PCLA.BehaviorTests Fixtures 的 Newtonsoft 夹具，
/// 以 STJ JsonObject 承载——PCLCS 迁移面禁用 Newtonsoft）。
/// </summary>
public static class TestHelpers
{
    /// <summary>Forge 新版安装链的 install_profile.json 最小形状（库名 + 下载项；ForgelikePlanTests 的内联串复用形态）。</summary>
    public static JsonObject ForgeVersionJson(string id = "1.19.3") => new()
    {
        ["id"] = id,
        ["minecraftArguments"] = "--a 1",
        ["libraries"] = new JsonArray(new JsonObject
        {
            ["name"] = $"net.minecraftforge:forge:{id}",
            ["downloads"] = new JsonObject
            {
                ["artifact"] = new JsonObject
                {
                    ["path"] = $"net/minecraftforge/forge/{id}/forge-{id}.jar",
                    ["url"] = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{id}/forge-{id}.jar",
                    ["size"] = 120000,
                    ["sha1"] = "bbbb"
                }
            }
        })
    };

    /// <summary>OptiFine 方式 B 的继承版本（旧 InheritInstance）最小版本 Json：
    /// minecraftArguments 为 null 时省键（新格式），非空时为旧格式（与 InstanceCore.IsOldJson 的判定输入一致）。</summary>
    public static JsonObject OptiFineTargetJson(string? minecraftArguments)
    {
        var json = new JsonObject { ["id"] = "1.12.2" };
        if (minecraftArguments is not null) json["minecraftArguments"] = minecraftArguments;
        return json;
    }

    /// <summary>LiteLoader 列表 latest 条目（DlLiteLoaderListEntry.JsonToken）最小形状：tweakClass / libraries / version。</summary>
    public static JsonObject LiteLoaderToken(string tweakClass = "net.minecraft.launchwrapper.LiteLoaderTweaker", string version = "1.12.2-SNAPSHOT")
        => (JsonObject) JsonNode.Parse($$"""
        {
          "tweakClass": "{{tweakClass}}",
          "libraries": [{"name": "net.minecraft:launchwrapper:1.12"}, {"name": "org.ow2.asm:asm-all:5.2"}],
          "stream": "release",
          "version": "{{version}}",
          "md5": "0123456789abcdef0123456789abcdef",
          "timestamp": "1451240760"
        }
        """)!;
}