using MeloongCore;
using PCLCS;
using PCLCS.Install;

namespace PCLCS.Tests.Install;

/// <summary>
/// 临时版本文件夹覆盖现有版本的提交语义（ModDownloadLib.vb:2055-2099 McInstallCommit）。
/// 断言按旧函数实际行为收敛：非"整目录替换"，而是清单驱动的新增/删除/移动与 Manifest 重写；
/// 失败无内部回滚（异常上抛，由加载器框架兜底）。
/// </summary>
public class InstallCommitTests
{
    private static string TempRoot(string name) => Path.Combine(Path.GetTempPath(), "pclcs.commit." + name + "." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;

    [Fact]
    public void Commit_moves_new_files_deletes_stale_and_rewrites_manifest()
    {
        var root = TempRoot("ok");
        var build = root + "build-ver" + Path.DirectorySeparatorChar;    // 临时版本文件夹（尾部带 \）
        var target = root + "target-ver" + Path.DirectorySeparatorChar;  // 版本文件夹（pathVersion 约定，尾部带 \）
        const string name = "target-ver";
        Directory.CreateDirectory(build + "mods");
        Directory.CreateDirectory(build + "resources");
        Directory.CreateDirectory(target + "mods");
        File.WriteAllText(build + name + ".json", """{"id":"new"}""");
        File.WriteAllText(build + name + ".jar", "new-jar");
        File.WriteAllText(build + @"mods\newmod.jar", "new-mod");
        File.WriteAllText(build + @"resources\pack.png", "res");
        File.WriteAllText(target + name + ".json", """{"id":"old"}""");
        File.WriteAllText(target + name + ".jar", "old-jar");
        File.WriteAllText(target + "oldfile.txt", "stale-managed");
        File.WriteAllText(target + @"mods\oldmod.jar", "stale-component");

        var provider = new JsonConfigProvider(Path.Combine(Path.GetTempPath(), "PCLCS.Tests", Path.GetRandomFileName() + ".json"));
        var components = new McComponents(name, null, null, null, null, null, null, null);
        Configs.McInstallManifest.Set(new McInstallManifest(1, components, DateTime.UtcNow,
        [
            new McInstallManifest.ManagedFile("oldfile.txt", "s1", DateTime.UtcNow, null),
            new McInstallManifest.ManagedFile("mods/oldmod.jar", "s2", DateTime.UtcNow, "OptiFine")
        ]), provider);
        var newManifest = new McInstallManifest(1, components, DateTime.UtcNow,
        [
            new McInstallManifest.ManagedFile("mods/newmod.jar", "n1", DateTime.UtcNow, "OptiFine")
        ]);

        InstallCommit.Commit(provider, target, name, newManifest, build, target);

        //新版有而旧版无：复制；旧清单管理而新版无：删除到回收站；json 与 jar 换新（移动而非复制）
        Assert.Equal("new-mod", File.ReadAllText(target + @"mods\newmod.jar"));
        Assert.False(File.Exists(target + "oldfile.txt"));
        Assert.False(File.Exists(target + @"mods\oldmod.jar"));
        Assert.Equal("""{"id":"new"}""", File.ReadAllText(target + name + ".json"));
        Assert.Equal("new-jar", File.ReadAllText(target + name + ".jar"));
        Assert.False(File.Exists(build + name + ".jar"));
        //老版本 assets 文件夹整目录复制
        Assert.Equal("res", File.ReadAllText(target + @"resources\pack.png"));
        //Manifest 重写：仅剩新文件，Sha1/Component 保留，LastWriteTimeUtc 取目标文件现值
        var committed = Configs.McInstallManifest.Get(provider);
        var managedFile = Assert.Single(committed!.ManagedFiles);
        Assert.Equal("mods/newmod.jar", managedFile.RelativePath);
        Assert.Equal("n1", managedFile.Sha1);
        Assert.Equal("OptiFine", managedFile.Component);
        Assert.Equal(FileUtils.GetInfo(target + @"mods\newmod.jar").LastWriteTimeUtc, managedFile.LastWriteTimeUtc);
    }

    [Fact]
    public void Commit_throws_when_target_json_missing_and_leaves_files_untouched()
    {
        var root = TempRoot("fail");
        var build = root + "build-ver" + Path.DirectorySeparatorChar;
        var target = root + "target-ver" + Path.DirectorySeparatorChar;
        const string name = "target-ver";
        Directory.CreateDirectory(build);
        Directory.CreateDirectory(target);
        File.WriteAllText(build + name + ".json", """{"id":"new"}""");
        File.WriteAllText(target + "oldfile.txt", "stale");
        //目标版本文件夹无任何 *.json 候选：GetJsonPath 抛错（InstancePaths 语义），此时尚未修改任何文件
        var provider = new JsonConfigProvider(Path.Combine(Path.GetTempPath(), "PCLCS.Tests", Path.GetRandomFileName() + ".json"));
        var newManifest = new McInstallManifest(1, new McComponents(name, null, null, null, null, null, null, null),
            DateTime.UtcNow, [new McInstallManifest.ManagedFile("newfile.txt", "n1", DateTime.UtcNow, null)]);

        Assert.ThrowsAny<Exception>(() => InstallCommit.Commit(provider, target, name, newManifest, build, target));

        Assert.True(File.Exists(target + "oldfile.txt"));
        Assert.False(File.Exists(target + "newfile.txt"));
        Assert.Null(Configs.McInstallManifest.Get(provider));   //未走到清单重写
    }
}