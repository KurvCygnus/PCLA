namespace PCLCS.Instances;

/// <summary>
/// 实例的纯路径运算：版本文件夹解析与版本 JSON 定位。
/// 自 VB McInstance.New（ModMinecraft.vb:575-578）与 GetJsonPath（ModMinecraft.vb:361-385）移植。
/// </summary>
public static class InstancePaths
{
    /// <summary>
    /// VB McInstance.New（ModMinecraft.vb:575-578）的路径解析：相对名 → &lt;mcFolder&gt;versions\&lt;name&gt;\，完整路径原样保留。
    /// </summary>
    public static string ResolveVersionFolder(string nameOrFullPath, string mcFolderSelected)
    {
        nameOrFullPath = PathUtils.RemoveExtendedPrefix(nameOrFullPath);
        // 含盘符冒号（或其他冒号）视为完整路径，不再拼接 versions（:577）；net48 无 Contains(string, StringComparison) 重载
        return PathUtils.AddSlashSuffix((nameOrFullPath.IndexOfF(":") >= 0 ? "" : mcFolderSelected + @"versions\") + nameOrFullPath);
    }

    /// <summary>
    /// 获取该版本的 JSON 路径（ModMinecraft.vb:361-385）。
    /// 同名 JSON 缺失时按 *.json 探测，要求同时含 mainClass/type/id 三键；无候选时抛出异常。
    /// </summary>
    public static string GetJsonPath(string pathVersion, string name)
    {
        string jsonPath = pathVersion + name + ".json";
        if (FileUtils.Exists(jsonPath)) return jsonPath;
        // 尝试寻找 JSON 文件
        jsonPath = "";
        foreach (string jsonCandidatePath in DirectoryUtils.EnumerateFiles(pathVersion, searchPattern: "*.json"))
        {
            try
            {
                var jsonCandidate = FileUtils.ReadAsJson(jsonCandidatePath)?.AsObject();
                if (jsonCandidate is null) continue;   // 空/非对象候选：与 VB 的 Catch-Continue 一致
                // :369-371 三条件照抄：mainClass / type / id 缺一不可
                if (!jsonCandidate.ContainsKey("mainClass")) continue;
                if (!jsonCandidate.ContainsKey("type")) continue;
                if (!jsonCandidate.ContainsKey("id")) continue;
            } catch
            {
                continue;
            }
            jsonPath = jsonCandidatePath;
            break;
        }
        if (jsonPath.Length == 0) throw new Exception($"未找到版本 JSON 文件：{pathVersion}{name}.json");
        Logger.Warn($"未找到同名版本 JSON，自动换用 {jsonPath}");
        return jsonPath;
    }
}