using PCLCS.Instances;
using PCLCS.Settings;
using System.Text.Json.Nodes;

namespace PCLCS.Tests.Instances;

//* RuleCheck / GetLocalPath / ListGetJson 为纯函数，直接给定字符串路径；ListGet / ShouldIgnoreFileCheck 走临时目录实例
public class LibrariesTests
{
    private const string McFolder = @"D:\MC\";

    private static string ExpectedLibPath(string relative) => McFolder + @"libraries\" + relative;

    #region RuleCheck（ModMinecraft.vb:1558-1568 truth 表）

    [Fact]
    public void NullRulesAllow()
    {
        Assert.True(Libraries.RuleCheck(null));
    }

    [Fact]
    public void EmptyRulesArrayDeny()
    {
        // 旧实现 required 初值为 False，空规则数组不放行（VersionArgumentRules.IsAllowed 语义）
        Assert.False(Libraries.RuleCheck(JsonNode.Parse("[]")));
    }

    [Fact]
    public void SingleRuleObjectTreatedAsOneRule()
    {
        // 规则通常是数组，但旧实现也接受单条规则对象
        Assert.True(Libraries.RuleCheck(JsonNode.Parse("""{"action":"allow","os":{"name":"windows"}}""")));
    }

    [Fact]
    public void WindowsRulesMatchOnWindowsHost()
    {
        var rules = JsonNode.Parse("""[{"action":"allow","os":{"name":"windows"}},{"action":"disallow","os":{"name":"osx"}}]""");
        Assert.True(Libraries.RuleCheck(rules));
    }

    [Fact]
    public void OtherOsNameDeny()
    {
        Assert.False(Libraries.RuleCheck(JsonNode.Parse("""[{"action":"allow","os":{"name":"linux"}}]""")));
    }

    [Fact]
    public void UnknownOsNameAllow()
    {
        Assert.True(Libraries.RuleCheck(JsonNode.Parse("""[{"action":"allow","os":{"name":"unknown"}}]""")));
    }

    [Fact]
    public void DisallowMatchingOsDeny()
    {
        var rules = JsonNode.Parse("""[{"action":"allow","os":{"name":"windows"}},{"action":"disallow","os":{"name":"windows"}}]""");
        Assert.False(Libraries.RuleCheck(rules));
    }

    [Fact]
    public void ArchX86Deny()
    {
        Assert.False(Libraries.RuleCheck(JsonNode.Parse("""[{"action":"allow","os":{"name":"windows","arch":"x86"}}]""")));
    }

    [Fact]
    public void ArchArmDeny()
    {
        Assert.False(Libraries.RuleCheck(JsonNode.Parse("""[{"action":"allow","os":{"name":"windows","arch":"arm64"}}]""")));
    }

    [Fact]
    public void OsVersionRegexOnBareVersion()
    {
        // My.Computer.Info.OSVersion 为裸版本号（10.0.x），规则里的 version 正则按其书写；
        // C# 侧传入带前缀的 Environment.OSVersion.VersionString，由 MatchesBareVersion 兜底
        Assert.True(Libraries.RuleCheck(JsonNode.Parse("""[{"action":"allow","os":{"name":"windows","version":"^10"}}]""")));
        Assert.False(Libraries.RuleCheck(JsonNode.Parse("""[{"action":"allow","os":{"name":"windows","version":"^9"}}]""")));
    }

    [Fact]
    public void FeatureDemoUserDeny()
    {
        Assert.False(Libraries.RuleCheck(JsonNode.Parse("""[{"action":"allow","features":{"is_demo_user":{}}}]""")));
    }

    [Fact]
    public void FeatureCustomResolutionAllow()
    {
        Assert.True(Libraries.RuleCheck(JsonNode.Parse("""[{"action":"allow","features":{"has_custom_resolution":{}}}]""")));
    }

    [Fact]
    public void FeatureQuickPlayDeny()
    {
        Assert.False(Libraries.RuleCheck(JsonNode.Parse("""[{"action":"allow","features":{"quick_play_singleplayer":{}}}]""")));
    }

    #endregion

    #region GetLocalPath（McLibGet，ModMinecraft.vb:1872-1885）

    [Fact]
    public void JoinsLibrariesFolder()
    {
        Assert.Equal(
            ExpectedLibPath(@"com\mumfrey\liteloader\1.12.2-SNAPSHOT\liteloader-1.12.2-SNAPSHOT.jar"),
            Libraries.GetLocalPath("com.mumfrey:liteloader:1.12.2-SNAPSHOT", customMcFolder: McFolder));
    }

    [Fact]
    public void WithoutHeadOmitsFolder()
    {
        Assert.Equal(
            @"com\mumfrey\liteloader\1.12.2-SNAPSHOT\liteloader-1.12.2-SNAPSHOT.jar",
            Libraries.GetLocalPath("com.mumfrey:liteloader:1.12.2-SNAPSHOT", withHead: false, customMcFolder: McFolder));
    }

    [Fact]
    public void FourSegmentNameDropsLastInFolderKeepsInFile()
    {
        // #7306：第四段（client）只进文件名，不进文件夹
        Assert.Equal(
            ExpectedLibPath(@"net\minecraftforge\forge\1.21.4-54.0.34\forge-1.21.4-54.0.34-client.jar"),
            Libraries.GetLocalPath("net.minecraftforge:forge:1.21.4-54.0.34:client", customMcFolder: McFolder));
    }

    [Fact]
    public void IgnoreLiteLoaderParamKeepsSignatureOnly()
    {
        // 旧实现该参数未参与逻辑，仅保留签名
        var withFlag = Libraries.GetLocalPath("a:b:1.0", ignoreLiteLoader: true, customMcFolder: McFolder);
        var withoutFlag = Libraries.GetLocalPath("a:b:1.0", customMcFolder: McFolder);
        Assert.Equal(withoutFlag, withFlag);
    }

    [Fact]
    public void NullCustomMcFolderThrows()
    {
        // VB 侧由转发层传入 McFolderSelected；C# 侧无模块状态，null 直接拒绝
        Assert.Throws<ArgumentNullException>(static () => Libraries.GetLocalPath("a:b:1.0", customMcFolder: null));
    }

    #endregion

    #region ListGetJson（McLibListGetWithJson，ModMinecraft.vb:1621-1733）

    private static JsonObject Lib(string name)
    {
        var json = new JsonObject { ["mainClass"] = "net.minecraft.client.main.Main" };
        json["libraries"] = new JsonArray { new JsonObject { ["name"] = name } };
        return json;
    }

    private static List<LibEntry> ListLibs(JsonObject root, bool keepSame = false) =>
        Libraries.ListGetJson(root, keepSame, McFolder);

    [Fact]
    public void NameOnlyUsesDefaultPathWithoutUrlSizeSha1()
    {
        var entry = ListLibs(Lib("com.google.guava:guava:31.1-jre")).Single();
        Assert.Equal("com.google.guava:guava:31.1-jre", entry.Name);
        // Maven 布局：组段 com.google.guava → com\google\guava，再接 artifact\version
        Assert.Equal(ExpectedLibPath(@"com\google\guava\guava\31.1-jre\guava-31.1-jre.jar"), entry.Path);
        Assert.Null(entry.Url);
        Assert.Null(entry.Size);
        Assert.Null(entry.Checksum);
        Assert.False(entry.IsNative);
        Assert.NotNull(entry.Root);
    }

    [Fact]
    public void ArtifactFieldsOverrideDefaults()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "net.minecraftforge:forge:1.12.2-14.23.5.2859",
                ["downloads"] = new JsonObject
                {
                    ["artifact"] = new JsonObject
                    {
                        ["path"] = "net/minecraftforge/forge/1.12.2-14.23.5.2859/forge-1.12.2-14.23.5.2859-universal.jar",
                        ["url"] = "https://maven.minecraftforge.net/net/forge-universal.jar",
                        ["sha1"] = "abc123",
                        ["size"] = 431455
                    }
                }
            })
        };
        var entry = ListLibs(json).Single();
        Assert.Equal(ExpectedLibPath(@"net\minecraftforge\forge\1.12.2-14.23.5.2859\forge-1.12.2-14.23.5.2859-universal.jar"), entry.Path);
        Assert.Equal("https://maven.minecraftforge.net/net/forge-universal.jar", entry.Url);
        Assert.Equal("431455", entry.Size);
        Assert.Equal("abc123", entry.Checksum);
    }

    [Fact]
    public void RootUrlAppendsMavenPath()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject { ["name"] = "org.glavo:foo:1.0", ["url"] = "https://example.com/maven/" })
        };
        var entry = ListLibs(json).Single();
        Assert.Equal("https://example.com/maven/org/glavo/foo/1.0/foo-1.0.jar", entry.Url);
        Assert.Equal(ExpectedLibPath(@"org\glavo\foo\1.0\foo-1.0.jar"), entry.Path);
    }

    [Fact]
    public void ArtifactWithoutUrlFallsBackToRootUrl()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "a:b:1.0",
                ["url"] = "https://example.com/maven/",
                ["downloads"] = new JsonObject
                {
                    ["artifact"] = new JsonObject { ["path"] = "a/b/1.0/b-1.0.jar", ["size"] = 7, ["sha1"] = "s" }
                }
            })
        };
        var entry = ListLibs(json).Single();
        Assert.Equal("https://example.com/maven/a/b/1.0/b-1.0.jar", entry.Url);
        Assert.Equal(ExpectedLibPath(@"a\b\1.0\b-1.0.jar"), entry.Path);
        Assert.Equal("7", entry.Size);
        Assert.Equal("s", entry.Checksum);
    }

    [Fact]
    public void ArtifactEmptyUrlNormalizesToNull()
    {
        // 旧 McLibEntry.Url setter：空白 URL 视为没有
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "a:b:1.0",
                ["downloads"] = new JsonObject
                {
                    ["artifact"] = new JsonObject { ["path"] = "a/b/1.0/b-1.0.jar", ["size"] = 7, ["sha1"] = "s", ["url"] = "" }
                }
            })
        };
        Assert.Null(ListLibs(json).Single().Url);
    }

    [Fact]
    public void ArtifactSha1JsonNullBecomesEmptyString()
    {
        // Newtonsoft 语义：键存在但为 JSON null → ""（JValue.Null.ToString()）；缺失则整个条目回退
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "a:b:1.0",
                ["downloads"] = new JsonObject
                {
                    ["artifact"] = new JsonObject { ["path"] = "a/b/1.0/b-1.0.jar", ["size"] = 7, ["sha1"] = null }
                }
            })
        };
        Assert.Equal("", ListLibs(json).Single().Checksum);
    }

    [Fact]
    public void ArtifactMissingSizeFallsBackToPlainEntry()
    {
        // 旧实现对缺 size 的条目整体走 Catch 回退：url/path/sha1 全部丢失，仅告警
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "a:b:1.0",
                ["downloads"] = new JsonObject
                {
                    ["artifact"] = new JsonObject { ["url"] = "https://x/a.jar", ["path"] = "a/b/1.0/b-1.0.jar", ["sha1"] = "s" }
                }
            })
        };
        var entry = ListLibs(json).Single();
        Assert.Equal(ExpectedLibPath(@"a\b\1.0\b-1.0.jar"), entry.Path);
        Assert.Null(entry.Url);
        Assert.Null(entry.Size);
        Assert.Null(entry.Checksum);
    }

    [Fact]
    public void NativeClassifierUsesArtifactPath()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "org.lwjgl.lwjgl:lwjgl-platform:2.9.4-nightly-20150209",
                ["natives"] = new JsonObject { ["windows"] = "natives-windows" },
                ["downloads"] = new JsonObject
                {
                    ["classifiers"] = new JsonObject
                    {
                        ["natives-windows"] = new JsonObject
                        {
                            ["path"] = "org/lwjgl/lwjgl/lwjgl-platform/2.9.4-nightly-20150209/lwjgl-platform-2.9.4-nightly-20150209-natives-windows.jar",
                            ["url"] = "https://libraries.minecraft.net/natives.jar",
                            ["sha1"] = "nh",
                            ["size"] = 10
                        }
                    }
                }
            })
        };
        var entry = ListLibs(json).Single();
        Assert.True(entry.IsNative);
        Assert.Equal(ExpectedLibPath(@"org\lwjgl\lwjgl\lwjgl-platform\2.9.4-nightly-20150209\lwjgl-platform-2.9.4-nightly-20150209-natives-windows.jar"), entry.Path);
        Assert.Equal("https://libraries.minecraft.net/natives.jar", entry.Url);
        Assert.Equal("10", entry.Size);
        Assert.Equal("nh", entry.Checksum);
    }

    [Fact]
    public void NativeWithoutClassifierBuildsNameFromCoordinate()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "x:y:1.0",
                ["natives"] = new JsonObject { ["windows"] = "natives-windows" }
            })
        };
        var entry = ListLibs(json).Single();
        Assert.True(entry.IsNative);
        Assert.Equal(ExpectedLibPath(@"x\y\1.0\y-1.0-natives-windows.jar"), entry.Path);
        Assert.Null(entry.Url);
        Assert.Null(entry.Size);
        Assert.Null(entry.Checksum);
    }

    [Fact]
    public void NativeArchPlaceholderReplacedByHostBitness()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "x:y:1.0",
                ["natives"] = new JsonObject { ["windows"] = "natives-windows-${arch}" }
            })
        };
        var entry = ListLibs(json).Single();
        string bitness = Environment.Is64BitOperatingSystem ? "64" : "32";
        Assert.Equal(ExpectedLibPath($@"x\y\1.0\y-1.0-natives-windows-{bitness}.jar"), entry.Path);
    }

    [Fact]
    public void NativeWithoutWindowsEntrySkipped()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "x:y:1.0",
                ["natives"] = new JsonObject { ["linux"] = "natives-linux" }
            })
        };
        Assert.Empty(ListLibs(json));
    }

    [Fact]
    public void RuleExcludedLibrarySkipped()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "x:y:1.0",
                ["rules"] = new JsonArray(new JsonObject { ["action"] = "allow", ["os"] = new JsonObject { ["name"] = "linux" } })
            })
        };
        Assert.Empty(ListLibs(json));
    }

    [Fact]
    public void BakaXlNullPropertiesTreatedAsMissing()
    {
        // #409：BakaXL 把缺失项序列化为 null；清理后按“没有该项”处理
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "com.google.guava:guava:31.1-jre",
                ["natives"] = null,
                ["rules"] = null,
                ["url"] = null,
                ["downloads"] = null
            })
        };
        var entry = ListLibs(json).Single();
        Assert.Equal(ExpectedLibPath(@"com\google\guava\guava\31.1-jre\guava-31.1-jre.jar"), entry.Path);
        Assert.False(entry.IsNative);
    }

    [Fact]
    public void NewerVersionWinsByDefault()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(
                new JsonObject { ["name"] = "com.google.guava:guava:31.1-jre" },
                new JsonObject { ["name"] = "com.google.guava:guava:30.1-jre" })
        };
        var entries = ListLibs(json);
        Assert.Single(entries);
        Assert.Contains("31.1-jre", entries[0].Path);
    }

    [Fact]
    public void OlderFirstStillKeepsNewer()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(
                new JsonObject { ["name"] = "com.google.guava:guava:30.1-jre" },
                new JsonObject { ["name"] = "com.google.guava:guava:31.1-jre" })
        };
        var entries = ListLibs(json);
        Assert.Single(entries);
        Assert.Contains("31.1-jre", entries[0].Path);
    }

    [Fact]
    public void KeepSameNameDifferentVersionReturnsBoth()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(
                new JsonObject { ["name"] = "com.google.guava:guava:31.1-jre" },
                new JsonObject { ["name"] = "com.google.guava:guava:30.1-jre" })
        };
        var entries = ListLibs(json, keepSame: true);
        Assert.Equal(2, entries.Count);
        Assert.Contains("31.1-jre", entries[0].Path);
        Assert.Contains("30.1-jre", entries[1].Path);
    }

    [Fact]
    public void KeepSameNameSameVersionStillDeduped()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(
                new JsonObject { ["name"] = "com.google.guava:guava:31.1-jre" },
                new JsonObject { ["name"] = "com.google.guava:guava:31.1-jre" })
        };
        Assert.Single(ListLibs(json, keepSame: true));
    }

    [Fact]
    public void NativeAndPlainSameNameBothKept()
    {
        var json = new JsonObject
        {
            ["mainClass"] = "x",
            ["libraries"] = new JsonArray(
                new JsonObject { ["name"] = "x:y:1.0" },
                new JsonObject { ["name"] = "x:y:1.0", ["natives"] = new JsonObject { ["windows"] = "natives-windows" } })
        };
        var entries = ListLibs(json);
        Assert.Equal(2, entries.Count);
        Assert.False(entries[0].IsNative);
        Assert.True(entries[1].IsNative);
    }

    #endregion

    #region ListGet（McLibListGet，ModMinecraft.vb:1569-1620）与 ShouldIgnoreFileCheck（:1886-1889）

    [Collection("PCLCS.Settings")]
    public class InstanceBasedTests : IDisposable
    {
        private readonly string _root;

        public InstanceBasedTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "pclcs.libs." + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
            SettingsService.Configure(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private string VersionDir(string name) => Path.Combine(_root, "versions", name + Path.DirectorySeparatorChar);

        private static JsonObject BaseJson(string id, params string[] libraryNames)
        {
            var json = new JsonObject
            {
                ["id"] = id,
                ["type"] = "release",
                ["mainClass"] = "net.minecraft.client.main.Main",
                ["releaseTime"] = "2023-06-12T10:00:00Z",
                ["time"] = "2023-06-12T10:00:00Z",
                ["libraries"] = new JsonArray()
            };
            foreach (var name in libraryNames) json["libraries"]!.AsArray().Add(new JsonObject { ["name"] = name });
            return json;
        }

        private void WriteVersion(string name, JsonObject json)
        {
            Directory.CreateDirectory(VersionDir(name));
            File.WriteAllText(Path.Combine(VersionDir(name), name + ".json"), json.ToJsonString());
        }

        private InstanceCore NewCore(string name)
        {
            return new InstanceCore(InstancePaths.ResolveVersionFolder(name, _root), _root, VersionDir(name));
        }

        [Fact]
        public void MergesChildAndParentLibraries()
        {
            WriteVersion("parent", BaseJson("parent", "parent:lib:1.0"));
            var childJson = BaseJson("child", "child:lib:1.0");
            childJson["inheritsFrom"] = "parent";
            WriteVersion("child", childJson);
            var core = NewCore("child");

            var entries = Libraries.ListGet(core, includeMainJar: false);

            Assert.Equal(2, entries.Count);
            Assert.Equal("child:lib:1.0", entries[0].Name);
            Assert.Equal("parent:lib:1.0", entries[1].Name);
        }

        [Fact]
        public void MainJarWalksToDeepestInherit()
        {
            var parentJson = BaseJson("parent", "parent:lib:1.0");
            parentJson["downloads"] = new JsonObject
            {
                ["client"] = new JsonObject { ["url"] = "https://client/parent.jar", ["sha1"] = "psha" }
            };
            WriteVersion("parent", parentJson);
            var childJson = BaseJson("child", "child:lib:1.0");
            childJson["inheritsFrom"] = "parent";
            WriteVersion("child", childJson);
            var core = NewCore("child");

            var entries = Libraries.ListGet(core, includeMainJar: true);

            Assert.Equal(3, entries.Count);
            var mainJar = entries[^1];
            Assert.Equal(VersionDir("parent") + "parent.jar", mainJar.Path);
            Assert.Equal("https://client/parent.jar", mainJar.Url);
            Assert.Equal("psha", mainJar.Checksum);
            Assert.Null(mainJar.Root);
        }

        [Fact]
        public void JarFieldPointsToProvidedVersion()
        {
            var customJson = BaseJson("custom");
            customJson["downloads"] = new JsonObject
            {
                ["client"] = new JsonObject { ["url"] = "https://client/custom.jar", ["sha1"] = "csha" }
            };
            WriteVersion("custom", customJson);
            var childJson = BaseJson("child", "child:lib:1.0");
            childJson["jar"] = "custom";
            WriteVersion("child", childJson);
            var core = NewCore("child");

            var mainJar = Libraries.ListGet(core, includeMainJar: true)[^1];

            Assert.Equal(VersionDir("custom") + "custom.jar", mainJar.Path);
            Assert.Equal("csha", mainJar.Checksum);
        }

        [Fact]
        public void MissingJarVersionFallsBackToSelf()
        {
            var childJson = BaseJson("child", "child:lib:1.0");
            childJson["jar"] = "ghost";
            childJson["downloads"] = new JsonObject
            {
                ["client"] = new JsonObject { ["url"] = "https://client/child.jar", ["sha1"] = "chsha" }
            };
            WriteVersion("child", childJson);
            var core = NewCore("child");

            var mainJar = Libraries.ListGet(core, includeMainJar: true)[^1];

            Assert.Equal(VersionDir("child") + "child.jar", mainJar.Path);
            Assert.Equal("chsha", mainJar.Checksum);
        }

        [Fact]
        public void IgnoreFileCheckFollowsVersionSettings()
        {
            var json = BaseJson("vanilla");
            WriteVersion("vanilla", json);
            var core = NewCore("vanilla");

            Assert.False(Libraries.ShouldIgnoreFileCheck(core));
            SettingsService.Set("VersionAdvanceAssets", 2, VersionDir("vanilla"));
            Assert.True(Libraries.ShouldIgnoreFileCheck(core));
            SettingsService.Set("VersionAdvanceAssets", 1, VersionDir("vanilla"));
            Assert.False(Libraries.ShouldIgnoreFileCheck(core));
            SettingsService.Set("VersionAdvanceAssetsV2", true, VersionDir("vanilla"));
            Assert.True(Libraries.ShouldIgnoreFileCheck(core));
        }
    }

    #endregion
}
