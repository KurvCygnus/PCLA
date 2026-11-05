namespace PCLCS.Install;

/// <summary>
/// 原版客户端安装决策（ModDownloadLib.vb McDownloadClientLoader:13-73 的决策段迁入）：
/// 版本 Json 落盘路径与 clientVersion 标记写入。Loader 链本体留 VB。
/// </summary>
public static class VanillaInstallPlans
{
    /// <summary>版本 Json 文件落盘路径（ModDownloadLib.vb:14/:23/:27/:58/:60）：版本文件夹尾部直接拼接文件夹名与 .json。</summary>
    public static string VersionJsonPath(string versionFolder)
        => versionFolder + PathUtils.GetLastPart(versionFolder) + ".json";

    /// <summary>
    /// 顺手添加 Json 项目（ModDownloadLib.vb:56-63）：读取版本 Json、追加 clientVersion 后按旧 Newtonsoft 布局回写。
    /// 读取失败或非 JSON 对象时抛出，由 VB 宿主层包装为"添加客户端版本失败"。
    /// </summary>
    public static void WriteClientVersion(string versionFolder, string id)
    {
        var versionJson = FileUtils.ReadAsJson(VersionJsonPath(versionFolder))?.AsObject()
            ?? throw new InvalidDataException($"版本 Json 不是 JSON 对象：{VersionJsonPath(versionFolder)}");
        versionJson["clientVersion"] = id;
        FileUtils.Write(VersionJsonPath(versionFolder), versionJson.ToJsonString(MergeJson.WriteOptions));
    }
}