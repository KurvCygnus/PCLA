using PCLCS.Instances;

namespace PCLCS.Install;

/// <summary>
/// 将安装好的临时版本文件夹覆盖到一个现有版本文件夹中
/// （ModDownloadLib.vb:2055-2099 公有 McInstallCommit 的纯决策核迁入；实例路径与清单按参数注入）。
/// 语义为清单驱动的新增/删除/移动与 Manifest 重写，而非整目录替换；失败时异常上抛，已完成的步骤不回滚（与原函数一致）。
/// </summary>
public static class InstallCommit
{
    /// <param name="instanceConfig">实例设置提供器（VB McInstance.Config）；用于读取旧清单与写入新清单。</param>
    /// <param name="pathVersion">实例版本文件夹（尾部带 \ 的 PathVersion 约定）。</param>
    /// <param name="name">实例名。</param>
    /// <param name="newManifest">临时版本构建产物的安装清单。</param>
    /// <param name="buildFolder">临时版本文件夹（尾部带 \）。</param>
    /// <param name="targetFolder">覆盖目标文件夹（尾部带 \）。</param>
    public static void Commit(JsonConfigProvider instanceConfig, string pathVersion, string name,
        McInstallManifest newManifest, string buildFolder, string targetFolder)
    {
        var oldFiles = Configs.McInstallManifest.Get(instanceConfig)?.ManagedFiles ?? [];
        var newFiles = newManifest.ManagedFiles;

        //============== 整理变更的文件列表 ==============

        var filesToDelete = new List<string>();
        var filesToMove = new List<(string Source, string Target)>();
        //ManagedFiles
        //新版里有：添加或覆盖新版文件
        foreach (var newFile in newFiles)
        {
            filesToMove.Add((Path.Combine(buildFolder, newFile.RelativePath), Path.Combine(targetFolder, newFile.RelativePath)));
        }
        //新版里没有，老版里有：删除本地文件到回收站
        var newFilePaths = new HashSet<string>(newFiles.Select(file => file.RelativePath));
        foreach (var oldFile in oldFiles)
        {
            if (newFilePaths.Contains(oldFile.RelativePath)) continue;
            filesToDelete.Add(PathUtils.ForCompare(Path.Combine(targetFolder, oldFile.RelativePath)));
        }
        //老版本 MC 的 assets 文件
        if (DirectoryUtils.Exists(buildFolder + @"resources\")) DirectoryUtils.Copy(buildFolder + @"resources\", targetFolder + @"resources\");
        //json 与 jar
        string oldJsonPath = InstancePaths.GetJsonPath(pathVersion, name);
        filesToDelete.Add(oldJsonPath);
        filesToDelete.Add(Path.ChangeExtension(oldJsonPath, ".jar"));
        filesToDelete.Add(pathVersion + name + ".jar");
        filesToDelete = filesToDelete.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        //============== 实际修改 ==============

        instanceConfig.Save();
        foreach (var targetPath in filesToDelete)
        {
            FileUtils.Delete(targetPath, true);
        }
        FileUtils.Move(buildFolder + name + ".jar", pathVersion + name + ".jar");
        FileUtils.Move(buildFolder + name + ".json", oldJsonPath);
        foreach (var file in filesToMove)
        {
            DirectoryUtils.Create(PathUtils.RemoveLastPart(file.Target));
            FileUtils.Copy(file.Source, file.Target);
        }
        var managedFiles = newFiles.Select(file => new McInstallManifest.ManagedFile(file.RelativePath, file.Sha1,
            FileUtils.GetInfo(Path.Combine(targetFolder, file.RelativePath)).LastWriteTimeUtc, file.Component)).ToList();
        Configs.McInstallManifest.Set(new McInstallManifest(newManifest.SchemaVersion, newManifest.Components, newManifest.LastModifiedUtc, managedFiles), instanceConfig);
        instanceConfig.Save();
    }
}