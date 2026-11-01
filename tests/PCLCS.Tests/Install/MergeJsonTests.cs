using PCLCS.Install;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Install;

/// <summary>
/// 多版本 JSON 合并决策核（ModDownloadLib.vb:1884-1931）的语义锁定。
/// 断言按旧 Newtonsoft Merge 实际行为收敛（brief 计划稿口径已修正）；
/// 输出 minecraftArguments 被 W3 启动参数链消费（MergedJsonTests 锁下游参数行为）。
/// </summary>
public class MergeJsonTests
{
    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Minecraft_arguments_are_reassembled_from_all_profiles()   // ModDownloadLib.vb:1901-1922，#2801
    {
        var baseJson = Parse("""{"id":"base","minecraftArguments":"--a 1 --b 2"}""");
        var overlay = Parse("""{"id":"child","minecraftArguments":"--b 9 --c 3"}""");
        var merged = MergeJson.Merge(baseJson, overlay);
        //实际 VB 语义（:1901-1902 全层原文先拼接，:1911-1922 重组后去重保留首次出现）：
        //重叠的 --b 不会"取覆盖值"，两组都保留 → 期望与计划稿不同，按真值收敛
        Assert.Equal("--a 1 --b 2 --b 9 --c 3", merged["minecraftArguments"]!.GetValue<string>());
    }

    [Fact]
    public void Value_tokens_attach_to_preceding_bare_flag()   // :1913-1918 归并分支
    {
        var baseJson = Parse("""{"minecraftArguments":"--tweakClass"}""");
        var overlay = Parse("""{"minecraftArguments":"net.minecraftforge.fml.common.launcher.FMLTweaker --width 800"}""");
        var merged = MergeJson.Merge(baseJson, overlay);
        Assert.Equal("--tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker --width 800",
            merged["minecraftArguments"]!.GetValue<string>());
    }

    [Fact]
    public void Duplicate_tokens_keep_first_occurrence()   // :1922 Distinct 保序去重
    {
        var baseJson = Parse("""{"minecraftArguments":"-Dfml.ignoreInvalidMinecraftCertificates=true"}""");
        var overlay = Parse("""{"minecraftArguments":"-Dfml.ignoreInvalidMinecraftCertificates=true --tweakClass x"}""");
        var merged = MergeJson.Merge(baseJson, overlay);
        Assert.Equal("-Dfml.ignoreInvalidMinecraftCertificates=true --tweakClass x",
            merged["minecraftArguments"]!.GetValue<string>());
    }

    [Fact]
    public void Base_arguments_survive_when_overlay_has_none()
    {
        var baseJson = Parse("""{"minecraftArguments":"--a 1"}""");
        var overlay = Parse("""{"id":"child"}""");
        var merged = MergeJson.Merge(baseJson, overlay);
        Assert.Equal("--a 1", merged["minecraftArguments"]!.GetValue<string>());
    }

    [Fact]
    public void Missing_arguments_leave_merged_json_untouched()   // :1922 SplitArguments 为空则不写入
    {
        var baseJson = Parse("""{"id":"base"}""");
        var overlay = Parse("""{"id":"child"}""");
        var merged = MergeJson.Merge(baseJson, overlay);
        Assert.False(merged.ContainsKey("minecraftArguments"));
    }

    [Fact]
    public void Explicit_allArguments_drives_reassembly()   // VB 循环传入跨层原文（:1901-1902），C# 按其覆写
    {
        var baseJson = Parse("""{"minecraftArguments":"1"}""");
        var overlay = Parse("""{"minecraftArguments":"2"}""");
        var merged = MergeJson.Merge(baseJson, overlay, "-a 1 -a 2");
        Assert.Equal("-a 1 -a 2", merged["minecraftArguments"]!.GetValue<string>());
    }

    [Fact]
    public void Libraries_arrays_are_concatenated_base_first()   // :1908 Newtonsoft Merge 默认 Concat（brief 计划稿"按名覆盖"口径已修正）
    {
        var baseJson = Parse("""{"id":"base","libraries":[{"name":"parent.lib"}]}""");
        var overlay = Parse("""{"id":"child","libraries":[{"name":"child.lib"}]}""");
        var merged = MergeJson.Merge(baseJson, overlay);
        var libs = merged["libraries"]!.AsArray();
        //JObject.Merge 默认 MergeArrayHandling.Concat：数组不按名称匹配合并，直接基底在前、覆盖层在后拼接
        Assert.Equal(2, libs.Count);
        Assert.Equal("parent.lib", libs[0]!["name"]!.GetValue<string>());
        Assert.Equal("child.lib", libs[1]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Scalars_are_overridden_by_overlay()
    {
        var baseJson = Parse("""{"id":"base","mainClass":"MAIN","logConfig":"base.xml"}""");
        var overlay = Parse("""{"id":"child","mainClass":"CHILD"}""");
        var merged = MergeJson.Merge(baseJson, overlay);
        Assert.Equal("child", merged["id"]!.GetValue<string>());
        Assert.Equal("CHILD", merged["mainClass"]!.GetValue<string>());
        Assert.Equal("base.xml", merged["logConfig"]!.GetValue<string>());   // 仅存在于基底的键保留
    }

    [Fact]
    public void Nested_objects_merge_recursively()   // :1908 对象递归、数组拼接
    {
        var baseJson = Parse("""{"arguments":{"game":["--x"]}}""");
        var overlay = Parse("""{"arguments":{"game":["--y"],"jvm":["-Dx=1"]}}""");
        var merged = MergeJson.Merge(baseJson, overlay);
        var arguments = merged["arguments"]!.AsObject();
        Assert.Equal(["--x", "--y"], arguments["game"]!.AsArray().Select(token => token!.GetValue<string>()));
        Assert.Equal(["-Dx=1"], arguments["jvm"]!.AsArray().Select(token => token!.GetValue<string>()));
    }

    [Fact]
    public void Overlay_json_null_is_ignored()   // Newtonsoft MergeNullValueHandling.Ignore 默认（W1 InstanceCore:628 房规同向）
    {
        var baseJson = Parse("""{"keep":"base","container":{"a":1}}""");
        var overlay = Parse("""{"keep":null,"added":null,"container":null}""");
        var merged = MergeJson.Merge(baseJson, overlay);
        Assert.Equal("base", merged["keep"]!.GetValue<string>());
        Assert.False(merged.ContainsKey("added"));   // 新键且值为 JSON null：不写入（与"缺失键"无可观察差异）
        Assert.Equal("""{"a":1}""", merged["container"]!.ToJsonString());   // 既有容器不被来源 null 覆盖
    }

    [Fact]
    public void Overlay_releaseTime_and_time_are_removed_base_kept()   // :1906-1907 仅清理覆盖层
    {
        var baseJson = Parse("""{"releaseTime":"r0","time":"t0"}""");
        var overlay = Parse("""{"releaseTime":"r1","time":"t1"}""");
        var merged = MergeJson.Merge(baseJson, overlay);
        Assert.Equal("r0", merged["releaseTime"]!.GetValue<string>());
        Assert.Equal("t0", merged["time"]!.GetValue<string>());
    }
}