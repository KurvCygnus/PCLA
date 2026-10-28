using MeloongCore;
using PCLCS;
using System.Text.Json.Nodes;

namespace PCLCS.Tests;

public class McInstallManifestTests
{

    private static readonly string TestConfigPath = Path.Combine(Path.GetTempPath(), "PCLCS.Tests", Path.GetRandomFileName() + ".json");

    /// <summary>构建一个含 OptiFine 条目与多加载器的完整清单。</summary>
    private static McInstallManifest BuildManifest()
    {
        var optiFineEntry = new JsonObject
        {
            ["InstanceName"] = "1.12.2-OptiFine_HD_U_G7",
            ["Path"] = "versions/1.12.2/1.12.2-OptiFine_HD_U_G7"
        };
        var components = new McComponents(
            "1.12.2", optiFineEntry,
            forgeVersion: "14.23.5.2859",
            neoForgeVersion: null, fabricVersion: null, fabricApiVersion: "0.2.1",
            optiFabricVersion: null, liteLoaderVersion: "1.12");
        return new McInstallManifest(
            Versions.McInstallManifestVersion, components, DateTime.UtcNow, [
                new McInstallManifest.ManagedFile("optifine/OptiFine.jar", "aa11", DateTime.UtcNow, "OptiFine"),
                new McInstallManifest.ManagedFile("mods/fabric-api.jar", "bb22", DateTime.UtcNow, "FabricApi")
            ]);
    }

    [Fact]
    public void JsonConfigProvider_Roundtrip_PreservesManifest()
    {
        try
        {
            var provider = new JsonConfigProvider(TestConfigPath);
            var entry = new DynamicConfigEntry<McInstallManifest>("MinecraftInstallManifest", null);
            var manifest = BuildManifest();

            entry.Set(manifest, provider);
            provider.Save();

            // 模拟重开进程：新建 provider，从磁盘文件读取而非内存缓存。
            var reloadedProvider = new JsonConfigProvider(TestConfigPath);
            var reloaded = entry.Get(reloadedProvider);

            Assert.NotNull(reloaded);
            Assert.Equal(manifest.SchemaVersion, reloaded.SchemaVersion);
            Assert.Equal(manifest.Components.MinecraftVersion, reloaded.Components.MinecraftVersion);
            Assert.Equal(manifest.Components.ModLoaderVersions[ModLoaders.Forge], reloaded.Components.ModLoaderVersions[ModLoaders.Forge]);
            Assert.Equal(manifest.Components.ModLoaderVersions[ModLoaders.LiteLoader], reloaded.Components.ModLoaderVersions[ModLoaders.LiteLoader]);
            Assert.Equal(manifest.Components.FabricApiVersion, reloaded.Components.FabricApiVersion);
            Assert.Equal(manifest.Components.OptiFineVersion, reloaded.Components.OptiFineVersion);
            Assert.True(manifest.Components.IsEquivalentTo(reloaded.Components));
            Assert.Equal(manifest.ManagedFiles.Count, reloaded.ManagedFiles.Count);
            Assert.Equal("OptiFine", reloaded.ManagedFiles[0].Component);
            Assert.Equal(manifest.LastModifiedUtc.ToUniversalTime(), reloaded.LastModifiedUtc.ToUniversalTime());
        } finally
        {
            if (File.Exists(TestConfigPath)) File.Delete(TestConfigPath);
        }
    }

    [Fact]
    public void OptiFineEntry_JsonObject_TypingSurvivesRoundtrip()
    {
        try
        {
            var provider = new JsonConfigProvider(TestConfigPath);
            var entry = new DynamicConfigEntry<McInstallManifest>("MinecraftInstallManifest", null);
            entry.Set(BuildManifest(), provider);
            provider.Save();

            var reloaded = entry.Get(new JsonConfigProvider(TestConfigPath));

            // OptiFineEntry 以 JsonObject 类型原样保存与还原，InstanceName 必须能以 string 取回。
            Assert.NotNull(reloaded!.Components.OptiFineEntry);
            Assert.Equal("1.12.2-OptiFine_HD_U_G7", reloaded.Components.OptiFineEntry!["InstanceName"]!.GetValue<string>());
            Assert.Equal("HD_U_G7", reloaded.Components.OptiFineVersion);
        } finally
        {
            if (File.Exists(TestConfigPath)) File.Delete(TestConfigPath);
        }
    }

}