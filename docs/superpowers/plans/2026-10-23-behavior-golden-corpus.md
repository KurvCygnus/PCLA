# S3 行为黄金语料 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `McInstance`、`CrashAnalyzer`、`Settings` 建立可执行的行为黄金语料，在 C# 化与 UI 替换前锁定当前语义。

**Architecture:** 新增独立的 `tests/PCLA.BehaviorTests` 项目，通过 `ProjectReference` 直连 VB 的 `PCLA`。测试进程内初始化临时数据目录，不启动 WPF；语料在临时目录中合成，断言行为结果。唯一生产接缝是 `CrashAnalyzer.GetAnalyzeResult` 改为 `Friend`。

**Tech Stack:** .NET Framework 4.8、VB.NET（PCLA）、C# 13（测试）、xUnit 2.9.2、MSBuild、`vstest.console.exe`。

**Spec:** `docs/superpowers/specs/2026-10-23-behavior-golden-corpus-design.md`

**Baseline:** `ed3d6ea`（S3 spec 提交；S2 已合并到 `eb5ced5`）

## Global Constraints

- PCLA 与测试都只面向 `net48`；不新增 NuGet 依赖，不引入 .NET 6+ 运行时。
- 行为测试项目必须用 MSBuild 构建，再用 `vstest.console.exe` 运行；禁止用 `dotnet test` 直接构建引用 PCLA 的测试项目。
- 测试进程不启动 WPF 窗口，不访问网络，不写真实 `%APPDATA%\PCLA`、`<exe>\PCLA`、`HKCU\Software\PCLADebug`。
- 语料全部为合成数据，不复制真实实例、真实日志、真实账号或服务器地址。
- xUnit 禁用并行；`Settings` 静态缓存与 `McFolderSelected` 不能跨用例互相污染。
- 除 `CrashAnalyzer.GetAnalyzeResult` 的可见性外，不改 PCLA 的生产行为。
- 提交信息用中文；每个任务通过后再进入下一个任务。

---

## File Map

| 文件 | 责任 |
| --- | --- |
| `tests/PCLA.BehaviorTests/PCLA.BehaviorTests.csproj` | 测试项目、xUnit、PCLA 引用、Newtonsoft 引用 |
| `tests/PCLA.BehaviorTests/AssemblyInfo.cs` | 禁用 xUnit 并行 |
| `tests/PCLA.BehaviorTests/BehaviorTestHost.cs` | 一次性初始化、临时根目录、用例目录 |
| `tests/PCLA.BehaviorTests/HostSmokeTests.cs` | 证明宿主能加载 `McInstance` |
| `tests/PCLA.BehaviorTests/Fixtures/InstanceCases.cs` | 10 个实例用例定义 |
| `tests/PCLA.BehaviorTests/Fixtures/CrashCases.cs` | 8 个崩溃用例定义 |
| `tests/PCLA.BehaviorTests/Fixtures/FixtureFactory.cs` | 写版本 JSON、Setup.ini、崩溃日志 |
| `tests/PCLA.BehaviorTests/InstanceModelTests.cs` | 实例模型断言 |
| `tests/PCLA.BehaviorTests/CrashAnalyzerTests.cs` | 崩溃分类断言 |
| `tests/PCLA.BehaviorTests/SettingsBehaviorTests.cs` | 配置读写断言 |
| `PCLA/Modules/Minecraft/ModCrash.vb` | `GetAnalyzeResult` 改 `Friend` |
| `PCLA/My Project/AssemblyInfo.vb` | `InternalsVisibleTo("PCLA.BehaviorTests")` |
| `tools/test-behavior.ps1` | 构建并运行行为测试 |
| `.github/workflows/build.yml` | CI 在 PCLA 构建后运行行为测试 |
| `docs/ARCHITECTURE.md` | 记录行为测试宿主与语料边界 |
| `docs/DEVELOPMENT.md` | 记录本地运行命令 |

---

## Task 1: 行为测试项目与宿主

**Files:**
- Create: `tests/PCLA.BehaviorTests/PCLA.BehaviorTests.csproj`
- Create: `tests/PCLA.BehaviorTests/AssemblyInfo.cs`
- Create: `tests/PCLA.BehaviorTests/BehaviorTestHost.cs`
- Create: `tests/PCLA.BehaviorTests/HostSmokeTests.cs`
- Modify: `PCLA.sln`（加入测试项目）

**Interfaces:**
- Produces: `BehaviorTestHost.Initialize()`, `BehaviorTestHost.Root`, `BehaviorTestHost.CreateCaseRoot(string)`
- Consumes: `MeloongCore.Main.Init`, `MeloongCore.Logger`, `PCL.PclLogger`, `PCL.ModBase.PathTemp`, `PCL.ModMinecraft.McFolderSelected`, `PCL.McInstance`

- [ ] **Step 1: 建测试项目文件**

创建 `tests/PCLA.BehaviorTests/PCLA.BehaviorTests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>13.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <PlatformTarget>x64</PlatformTarget>
    <Prefer32Bit>false</Prefer32Bit>
    <Configurations>Debug;Release</Configurations>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\PCLA\PCLA.vbproj" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="Newtonsoft.Json">
      <HintPath>..\..\PCLA\Resources\Newtonsoft.Json.dll</HintPath>
    </Reference>
  </ItemGroup>

</Project>
```

创建 `tests/PCLA.BehaviorTests/AssemblyInfo.cs`：

```csharp
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

- [ ] **Step 2: 写失败的宿主冒烟测试**

创建 `tests/PCLA.BehaviorTests/HostSmokeTests.cs`：

```csharp
using System;
using System.IO;
using PCL;

namespace PCLA.BehaviorTests;

public sealed class HostSmokeTests : IDisposable
{
    private readonly string _root;

    public HostSmokeTests()
    {
        BehaviorTestHost.Initialize();
        _root = BehaviorTestHost.CreateCaseRoot("host-smoke");
    }

    [Fact]
    public void CanLoadVanillaInstance()
    {
        WriteVanilla(_root, "1.20.1");

        ModMinecraft.McFolderSelected = _root;
        var instance = new McInstance("1.20.1");

        Assert.True(instance.Check());
        var loaded = instance.Load();

        Assert.Equal(McInstance.McInstanceState.Original, loaded.State);
        Assert.Equal("1.20.1", loaded.Version.VanillaName);
    }

    private static void WriteVanilla(string root, string name)
    {
        var dir = Path.Combine(root, "versions", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".json"), """
        {
          "id": "1.20.1",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "releaseTime": "2023-06-12T10:00:00+00:00",
          "time": "2023-06-12T10:00:00+00:00",
          "assets": "5",
          "assetIndex": { "id": "5" },
          "libraries": []
        }
        """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
```

- [ ] **Step 3: 运行测试确认失败**

```powershell
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild tests\PCLA.BehaviorTests\PCLA.BehaviorTests.csproj -t:Build -p:Configuration=Release -m:2
```

预期：编译失败，`BehaviorTestHost` 不存在。

- [ ] **Step 4: 实现宿主**

创建 `tests/PCLA.BehaviorTests/BehaviorTestHost.cs`：

```csharp
using System;
using System.IO;
using MeloongCore;
using PCL;

namespace PCLA.BehaviorTests;

internal static class BehaviorTestHost
{
    private static readonly object Gate = new();
    private static bool _initialized;

    public static string Root { get; private set; } = "";

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized) return;

            Root = Path.Combine(Path.GetTempPath(), "PCLA.BehaviorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, "logs"));
            Directory.CreateDirectory(Path.Combine(Root, "temp"));

            Main.Init("PCLABehaviorTests");
            Directory.CreateDirectory(Paths.AppDataThenName);

            Logger.Instance = new SilentLogger
            {
                logFolder = Path.Combine(Root, "logs") + Path.DirectorySeparatorChar,
                MinLevel = LogLevel.Trace
            };
            ModBase.PathTemp = Path.Combine(Root, "temp") + Path.DirectorySeparatorChar;
            ModMinecraft.McFolderSelected = Path.Combine(Root, "minecraft");

            _initialized = true;
        }
    }

    public static string CreateCaseRoot(string name)
    {
        Initialize();
        var root = Path.Combine(Root, "cases", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

internal sealed class SilentLogger : PclLogger
{
    public override void HandleBehavior(string rawMessage, string formattedMessage, LogBehavior behavior, Exception? ex)
    {
        // 测试宿主不弹窗；错误仍写入日志文件。
    }
}
```

- [ ] **Step 5: 把测试项目加入解决方案**

```powershell
dotnet sln PCLA.sln add tests\PCLA.BehaviorTests\PCLA.BehaviorTests.csproj
```

- [ ] **Step 6: 运行冒烟测试**

```powershell
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
$vstest = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\Extensions\TestPlatform\vstest.console.exe'
& $msbuild tests\PCLA.BehaviorTests\PCLA.BehaviorTests.csproj -t:Build -p:Configuration=Release -m:2
& $vstest tests\PCLA.BehaviorTests\bin\Release\net48\PCLA.BehaviorTests.dll
```

预期：`CanLoadVanillaInstance` 通过；若 `vstest.console.exe` 路径不存在，用 `Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022" -Recurse -Filter vstest.console.exe` 找到实际路径后继续。

- [ ] **Step 7: 提交**

```powershell
git add tests/PCLA.BehaviorTests PCLA.sln
git commit -m "测试：新增 PCLA 行为测试宿主"
```

---

## Task 2: 实例模型语料

**Files:**
- Create: `tests/PCLA.BehaviorTests/Fixtures/InstanceCases.cs`
- Create: `tests/PCLA.BehaviorTests/Fixtures/FixtureFactory.cs`
- Create: `tests/PCLA.BehaviorTests/InstanceModelTests.cs`

**Interfaces:**
- Consumes: `BehaviorTestHost.CreateCaseRoot`, `PCL.McInstance`
- Produces: `InstanceCases.All`、`FixtureFactory.PrepareInstance(InstanceCase)`、`FixtureFactory.WriteVersion(string, InstanceCase)`

- [ ] **Step 1: 写实例语料定义**

创建 `tests/PCLA.BehaviorTests/Fixtures/InstanceCases.cs`：

```csharp
using PCL;

namespace PCLA.BehaviorTests.Fixtures;

internal sealed class InstanceCase
{
    public string Name { get; }
    public string Id { get; }
    public string Type { get; }
    public string? InheritsFrom { get; }
    public string[] Libraries { get; }
    public McInstance.McInstanceState ExpectedState { get; }
    public bool CheckExpected { get; }
    public bool Isolated { get; }
    public bool MissingJson { get; }
    public bool BrokenJson { get; }
    public bool? ExpectedModable { get; }
    public string ExpectedVanilla { get; }

    public InstanceCase(
        string name,
        string id,
        string type,
        string? inheritsFrom,
        string[] libraries,
        McInstance.McInstanceState expectedState,
        string expectedVanilla,
        bool checkExpected = true,
        bool isolated = false,
        bool missingJson = false,
        bool brokenJson = false,
        bool? expectedModable = null)
    {
        Name = name;
        Id = id;
        Type = type;
        InheritsFrom = inheritsFrom;
        Libraries = libraries;
        ExpectedState = expectedState;
        ExpectedVanilla = expectedVanilla;
        CheckExpected = checkExpected;
        Isolated = isolated;
        MissingJson = missingJson;
        BrokenJson = brokenJson;
        ExpectedModable = expectedModable;
    }
}

internal static class InstanceCases
{
    public static TheoryData<InstanceCase> All { get; } = new()
    {
        new("vanilla", "1.20.1", "release", null, [], McInstance.McInstanceState.Original, "1.20.1", expectedModable: false),
        new("forge", "1.20.1-forge-47.2.0", "release", "1.20.1",
            ["net.minecraftforge:forge:1.20.1-47.2.0"], McInstance.McInstanceState.Forge, "1.20.1", expectedModable: true),
        new("neoforge", "1.20.1-neoforge-20.4.237", "release", "1.20.1",
            ["net.neoforged:neoforge:20.4.237"], McInstance.McInstanceState.NeoForge, "1.20.1", expectedModable: true),
        new("fabric", "1.20.1-fabric-0.15.0", "release", "1.20.1",
            ["net.fabricmc:fabric-loader:0.15.0"], McInstance.McInstanceState.Fabric, "1.20.1", expectedModable: true),
        new("optifine", "1.20.1-OptiFine_HD_U_I6", "release", "1.20.1",
            ["optifine:OptiFine:1.20.1_HD_U_I6"], McInstance.McInstanceState.OptiFine, "1.20.1"),
        new("liteloader", "1.12.2-LiteLoader", "release", "1.12.2",
            ["com.mumfrey:liteloader:1.12.2"], McInstance.McInstanceState.LiteLoader, "1.12.2"),
        new("missing-json", "missing-json", "release", null, [], McInstance.McInstanceState.Error, "",
            checkExpected: false, missingJson: true),
        new("broken-json", "broken-json", "release", null, [], McInstance.McInstanceState.Error, "",
            checkExpected: false, brokenJson: true),
        new("isolated", "1.20.1", "release", null, [], McInstance.McInstanceState.Original, "1.20.1", isolated: true),
        new("name-mismatch", "custom-name", "release", null, [], McInstance.McInstanceState.Original, "custom-name")
    };
}
```

- [ ] **Step 2: 写语料工厂**

创建 `tests/PCLA.BehaviorTests/Fixtures/FixtureFactory.cs`：

```csharp
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using PCL;

namespace PCLA.BehaviorTests.Fixtures;

internal static class FixtureFactory
{
    public static string PrepareInstance(InstanceCase c)
    {
        var root = BehaviorTestHost.CreateCaseRoot("instance-" + c.Name);
        Directory.CreateDirectory(Path.Combine(root, "versions"));

        if (c.InheritsFrom is not null)
        {
            WriteVanilla(root, c.InheritsFrom);
        }

        var versionDir = Path.Combine(root, "versions", c.Name);
        Directory.CreateDirectory(versionDir);

        if (c.BrokenJson)
        {
            File.WriteAllText(Path.Combine(versionDir, c.Name + ".json"), "{");
        }
        else if (!c.MissingJson)
        {
            WriteVersion(root, c);
        }

        if (c.Isolated)
        {
            var pclDir = Path.Combine(versionDir, "PCL");
            Directory.CreateDirectory(pclDir);
            File.WriteAllText(Path.Combine(pclDir, "Setup.ini"),
                "[Setup]\r\nVersionArgumentIndieV2=true\r\n");
        }

        return root;
    }

    public static void WriteVersion(string root, InstanceCase c)
    {
        var dir = Path.Combine(root, "versions", c.Name);
        Directory.CreateDirectory(dir);

        var json = new JObject
        {
            ["id"] = c.Id,
            ["type"] = c.Type,
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["releaseTime"] = "2023-06-12T10:00:00+00:00",
            ["time"] = "2023-06-12T10:00:00+00:00",
            ["assets"] = "5",
            ["assetIndex"] = new JObject { ["id"] = "5" },
            ["libraries"] = new JArray(c.Libraries.Select(l => new JObject { ["name"] = l }))
        };
        if (c.InheritsFrom is not null) json["inheritsFrom"] = c.InheritsFrom;

        File.WriteAllText(Path.Combine(dir, c.Name + ".json"), json.ToString());
    }

    private static void WriteVanilla(string root, string name)
    {
        WriteVersion(root, new InstanceCase(name, name, "release", null, [],
            McInstance.McInstanceState.Original, name));
    }
}
```

- [ ] **Step 3: 写实例测试**

创建 `tests/PCLA.BehaviorTests/InstanceModelTests.cs`：

```csharp
using System;
using System.IO;
using PCL;
using PCLA.BehaviorTests.Fixtures;

namespace PCLA.BehaviorTests;

public sealed class InstanceModelTests : IDisposable
{
    private string _root = "";

    public InstanceModelTests()
    {
        BehaviorTestHost.Initialize();
    }

    [Theory]
    [MemberData(nameof(InstanceCases.All), MemberType = typeof(InstanceCases))]
    public void MatchesCorpus(InstanceCase c)
    {
        _root = FixtureFactory.PrepareInstance(c);
        ModMinecraft.McFolderSelected = _root;

        var instance = new McInstance(c.Name);
        Assert.Equal(c.CheckExpected, instance.Check());

        var loaded = instance.Load();
        Assert.Equal(c.ExpectedState, loaded.State);
        Assert.Equal(c.Name, loaded.Name);

        if (c.ExpectedVanilla.Length > 0)
        {
            Assert.Equal(c.ExpectedVanilla, loaded.Version.VanillaName);
        }
        if (c.ExpectedModable is not null)
        {
            Assert.Equal(c.ExpectedModable.Value, loaded.Modable);
        }
        if (c.Isolated)
        {
            Assert.Equal(Path.Combine(_root, "versions", c.Name) + "\\", loaded.PathIndie);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
```

- [ ] **Step 4: 运行实例测试**

```powershell
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
$vstest = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\Extensions\TestPlatform\vstest.console.exe'
& $msbuild tests\PCLA.BehaviorTests\PCLA.BehaviorTests.csproj -t:Build -p:Configuration=Release -m:2
& $vstest tests\PCLA.BehaviorTests\bin\Release\net48\PCLA.BehaviorTests.dll --TestCaseFilter:"FullyQualifiedName~InstanceModelTests"
```

预期：10 个用例通过。若某模组类型的 `State` 与预期不符，先检查 `McVersion` 的识别字段（libraries 的 `name`、`id`、`inheritsFrom`），只调整语料或断言到实际行为，不改生产逻辑。

- [ ] **Step 5: 提交**

```powershell
git add tests/PCLA.BehaviorTests/Fixtures tests/PCLA.BehaviorTests/InstanceModelTests.cs
git commit -m "测试：锁定实例模型黄金语料"
```

---

## Task 3: 崩溃规则接缝与语料

**Files:**
- Modify: `PCLA/Modules/Minecraft/ModCrash.vb:924`
- Modify: `PCLA/My Project/AssemblyInfo.vb`
- Create: `tests/PCLA.BehaviorTests/Fixtures/CrashCases.cs`
- Create: `tests/PCLA.BehaviorTests/CrashAnalyzerTests.cs`

**Interfaces:**
- Consumes: `CrashAnalyzer.Import(string)`, `Prepare()`, `Analyze()`, `GetAnalyzeResult(bool)`
- Produces: `CrashCases.All`、`FixtureFactory.PrepareCrash(CrashCase)`

- [ ] **Step 1: 写失败的崩溃测试**

创建 `tests/PCLA.BehaviorTests/Fixtures/CrashCases.cs`：

```csharp
namespace PCLA.BehaviorTests.Fixtures;

internal sealed class CrashCase
{
    public string Name { get; }
    public string Log { get; }
    public string ExpectedText { get; }

    public CrashCase(string name, string log, string expectedText)
    {
        Name = name;
        Log = log;
        ExpectedText = expectedText;
    }
}

internal static class CrashCases
{
    public static TheoryData<CrashCase> All { get; } = new()
    {
        new("out-of-memory", "java.lang.OutOfMemoryError: Java heap space", "内存不足"),
        new("night-config", "com.electronwill.nightconfig.core.io.ParsingException: Not enough data available", "NightConfig"),
        new("duplicate-mods", "DuplicateModsFoundException: Found a duplicate mod example.jar", "重复"),
        new("missing-dependency", "java.lang.NoClassDefFoundError: com/example/ModClass", "前置"),
        new("java-too-new", "Unsupported class file major version 65", "Java"),
        new("java-32bit", "Invalid maximum heap size: -Xmx4G\nThe specified size exceeds the maximum representable size.", "32 位"),
        new("opengl", "The driver does not appear to support OpenGL", "OpenGL"),
        new("unknown", "java.lang.RuntimeException: something unexpected happened", "很抱歉")
    };
}
```

创建 `tests/PCLA.BehaviorTests/CrashAnalyzerTests.cs`：

```csharp
using System;
using System.IO;
using PCL;
using PCLA.BehaviorTests.Fixtures;

namespace PCLA.BehaviorTests;

public sealed class CrashAnalyzerTests : IDisposable
{
    private string _root = "";

    public CrashAnalyzerTests()
    {
        BehaviorTestHost.Initialize();
    }

    [Theory]
    [MemberData(nameof(CrashCases.All), MemberType = typeof(CrashCases))]
    public void ClassifiesKnownCrash(CrashCase c)
    {
        _root = BehaviorTestHost.CreateCaseRoot("crash-" + c.Name);
        var logDir = Path.Combine(_root, "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "latest.log");
        File.WriteAllText(logPath, c.Log);

        var analyzer = new CrashAnalyzer();
        analyzer.Import(logPath);
        Assert.True(analyzer.Prepare());
        analyzer.Analyze();

        var text = analyzer.GetAnalyzeResult(false);
        Assert.Contains(c.ExpectedText, text, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild tests\PCLA.BehaviorTests\PCLA.BehaviorTests.csproj -t:Build -p:Configuration=Release -m:2
```

预期：编译失败，`GetAnalyzeResult` 不可访问。

- [ ] **Step 3: 加最小可见性接缝**

把 `PCLA/Modules/Minecraft/ModCrash.vb:924` 的：

```vb
Private Function GetAnalyzeResult(IsHandAnalyze As Boolean) As String
```

改为：

```vb
Friend Function GetAnalyzeResult(IsHandAnalyze As Boolean) As String
```

在 `PCLA/My Project/AssemblyInfo.vb` 末尾加：

```vb
<Assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PCLA.BehaviorTests")>
```

- [ ] **Step 4: 运行崩溃测试**

```powershell
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
$vstest = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\Extensions\TestPlatform\vstest.console.exe'
& $msbuild tests\PCLA.BehaviorTests\PCLA.BehaviorTests.csproj -t:Build -p:Configuration=Release -m:2
& $vstest tests\PCLA.BehaviorTests\bin\Release\net48\PCLA.BehaviorTests.dll --TestCaseFilter:"FullyQualifiedName~CrashAnalyzerTests"
```

预期：8 个用例通过。若 `unknown` 的实际文案不是“很抱歉”，改成实际文案；`duplicate-mods` 若没命中，改用日志中的 `Found duplicate mods` 形式。

- [ ] **Step 5: 提交**

```powershell
git add PCLA/Modules/Minecraft/ModCrash.vb "PCLA/My Project/AssemblyInfo.vb" tests/PCLA.BehaviorTests
git commit -m "测试：新增崩溃规则黄金语料"
```

---

## Task 4: 配置读写语料

**Files:**
- Create: `tests/PCLA.BehaviorTests/SettingsBehaviorTests.cs`

**Interfaces:**
- Consumes: `Settings.Get<T>`, `Settings.Set`, `Settings.Reset`, `Settings.GetDefault`, `Settings.HasSaved`, `FixtureFactory.PrepareInstance`
- Produces: 11 个配置行为断言

- [ ] **Step 1: 写配置测试**

创建 `tests/PCLA.BehaviorTests/SettingsBehaviorTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using PCL;
using PCLA.BehaviorTests.Fixtures;

namespace PCLA.BehaviorTests;

public sealed class SettingsBehaviorTests : IDisposable
{
    private string _root = "";

    public SettingsBehaviorTests()
    {
        BehaviorTestHost.Initialize();
    }

    private McInstance NewInstance(string name)
    {
        var c = new InstanceCase(name, "1.20.1", "release", null, [],
            McInstance.McInstanceState.Original, "1.20.1");
        _root = FixtureFactory.PrepareInstance(c);
        ModMinecraft.McFolderSelected = _root;
        return new McInstance(name);
    }

    [Fact]
    public void DefaultInt()
    {
        Assert.Equal(900, Settings.Get<int>("WindowWidth"));
    }

    [Fact]
    public void SetNormal()
    {
        Settings.Set("WindowWidth", 1024);
        Assert.Equal(1024, Settings.Get<int>("WindowWidth"));
        Settings.Reset("WindowWidth");
    }

    [Fact]
    public void ResetNormal()
    {
        Settings.Set("WindowWidth", 1024);
        Settings.Reset("WindowWidth");
        Assert.Equal(900, Settings.Get<int>("WindowWidth"));
        Assert.False(Settings.HasSaved("WindowWidth"));
    }

    [Fact]
    public void InstanceIsolated()
    {
        var a = NewInstance("a");
        var rootA = _root;
        var b = NewInstance("b");
        var rootB = _root;

        Settings.Set("VersionArgumentIndieV2", true, a);
        Settings.Set("VersionArgumentIndieV2", false, b);

        Assert.True(Settings.Get<bool>("VersionArgumentIndieV2", a));
        Assert.False(Settings.Get<bool>("VersionArgumentIndieV2", b));

        Directory.Delete(rootA, true);
        Directory.Delete(rootB, true);
    }

    [Fact]
    public void InstanceMissing()
    {
        Assert.False(Settings.Get<bool>("VersionArgumentIndieV2"));
        Settings.Set("VersionArgumentIndieV2", true);
        Assert.False(Settings.HasSaved("VersionArgumentIndieV2"));
    }

    [Fact]
    public void HasSaved()
    {
        Assert.False(Settings.HasSaved("WindowWidth"));
        Settings.Set("WindowWidth", 1024);
        Assert.True(Settings.HasSaved("WindowWidth"));
        Settings.Reset("WindowWidth");
        Assert.False(Settings.HasSaved("WindowWidth"));
    }

    [Fact]
    public void GetDefault()
    {
        Assert.Equal("900", Settings.GetDefault("WindowWidth"));
    }

    [Fact]
    public void UnknownKey()
    {
        Assert.Throws<KeyNotFoundException>(() => Settings.Get<int>("NoSuchKey"));
    }

    [Fact]
    public void InvalidType()
    {
        Settings.Set("WindowWidth", "not-a-number");
        Assert.Equal(900, Settings.Get<int>("WindowWidth"));
        Settings.Reset("WindowWidth");
    }

    [Fact]
    public void SetSafeEncrypted()
    {
        Assert.Throws<InvalidOperationException>(() => Settings.SetSafe("SystemCount", 1));
    }

    [Fact]
    public void StringRoundtrip()
    {
        var value = "line1\n\"quoted\"\\backslash";
        Settings.Set("LaunchArgumentInfo", value);
        Assert.Equal(value, Settings.Get<string>("LaunchArgumentInfo"));
        Settings.Reset("LaunchArgumentInfo");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
```

- [ ] **Step 2: 运行测试**

```powershell
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
$vstest = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\Extensions\TestPlatform\vstest.console.exe'
& $msbuild tests\PCLA.BehaviorTests\PCLA.BehaviorTests.csproj -t:Build -p:Configuration=Release -m:2
& $vstest tests\PCLA.BehaviorTests\bin\Release\net48\PCLA.BehaviorTests.dll --TestCaseFilter:"FullyQualifiedName~SettingsBehaviorTests"
```

预期：11 个用例通过。若 `InstanceMissing` 的实际行为是抛异常而不是返回默认值，改成 `Assert.Throws` 并记录实际行为；不要修改 `Settings` 的生产代码。

- [ ] **Step 3: 提交**

```powershell
git add tests/PCLA.BehaviorTests/SettingsBehaviorTests.cs
git commit -m "测试：锁定配置读写与实例隔离行为"
```

---

## Task 5: 运行脚本与 CI

**Files:**
- Create: `tools/test-behavior.ps1`
- Modify: `.github/workflows/build.yml`
- Create: `docs/DEVELOPMENT.md`

**Interfaces:**
- Produces: `tools/test-behavior.ps1`（退出码 0/非 0）
- Consumes: Task 1–4 的测试项目

- [ ] **Step 1: 写脚本**

创建 `tools/test-behavior.ps1`：

```powershell
param(
    [string]$Configuration = 'Release',
    [string]$TestCaseFilter = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'tests\PCLA.BehaviorTests\PCLA.BehaviorTests.csproj'

$msbuild = Get-Command msbuild.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $msbuild) {
    $msbuild = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
}
if (-not (Test-Path $msbuild)) {
    throw "找不到 MSBuild：$msbuild"
}

$vstest = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022') `
    -Recurse -Filter vstest.console.exe -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $vstest) {
    throw '找不到 vstest.console.exe'
}

& $msbuild $project -t:Build -p:Configuration=$Configuration -m:2
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Join-Path $root "tests\PCLA.BehaviorTests\bin\$Configuration\net48\PCLA.BehaviorTests.dll"
$vstestArgs = @($dll)
if ($TestCaseFilter) { $vstestArgs += "--TestCaseFilter:$TestCaseFilter" }
& $vstest @vstestArgs
exit $LASTEXITCODE
```

- [ ] **Step 2: 运行脚本**

```powershell
.\tools\test-behavior.ps1
```

预期：30 个测试通过（Task 1 冒烟 1 个 + Task 2 的 10 个 + Task 3 的 8 个 + Task 4 的 11 个；其中 29 个是黄金语料，冒烟只验证宿主；实际总数以测试发现为准），退出码 0。

- [ ] **Step 3: 接入 CI**

在 `.github/workflows/build.yml` 的 PCLA 构建步骤之后加：

```yaml
      - name: Run behavior tests
        shell: pwsh
        run: ./tools/test-behavior.ps1
```

- [ ] **Step 4: 建立开发文档**

创建 `docs/DEVELOPMENT.md`，写入构建/测试命令，并加入行为测试章节：

```markdown
### 行为黄金语料

行为测试直连 VB 的 `PCLA`，必须用 MSBuild 构建测试项目，再用 `vstest.console.exe` 运行：

```powershell
.\tools\test-behavior.ps1
```

不要用 `dotnet test` 直接构建 `PCLA.BehaviorTests`：`dotnet build` 不支持该 VB WPF 项目的 XAML 构建。
```

- [ ] **Step 5: 提交**

```powershell
git add tools/test-behavior.ps1 .github/workflows/build.yml docs/DEVELOPMENT.md
git commit -m "构建：新增行为测试运行脚本并接入 CI"
```

---

## Task 6: 全量验收与文档

**Files:**
- Modify: `docs/ARCHITECTURE.md`
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: Task 1–5 的全部产物
- Produces: 可复现的验收记录

- [ ] **Step 1: 补架构文档**

在 `docs/ARCHITECTURE.md` 的测试章节加：

```markdown
### 行为黄金语料（S3）

- `tests/PCLA.BehaviorTests` 通过 `ProjectReference` 直连 `PCLA`，覆盖 `McInstance`、`CrashAnalyzer`、`Settings`。
- 测试进程内初始化临时目录；不启动 WPF、不访问网络、不写真实用户目录。
- `CrashAnalyzer.GetAnalyzeResult` 是唯一生产可见性接缝（`Friend` + `InternalsVisibleTo`）。
- 语料是合成数据；行为变化必须显式更新断言，不能靠放宽断言通过。
```

- [ ] **Step 2: 更新 Changelog**

在 `CHANGELOG.md` 的 `[Unreleased]` 下加一条面向用户的条目：

```markdown
- 新增行为黄金语料，锁定实例识别、崩溃分析与配置读写，为后续 C# 化提供回归基线。
```

- [ ] **Step 3: 全量构建与测试**

```powershell
dotnet test tests\PCLCS.Tests\PCLCS.Tests.csproj -c Release --no-restore
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' PCLA\PCLA.vbproj -t:Rebuild -p:Configuration=Release -m:2
.\tools\test-behavior.ps1
```

预期：`PCLCS.Tests` 230 通过 + 1 跳过；PCLA 0 警告 0 错误；行为测试全部通过。

- [ ] **Step 4: 检查差异**

```powershell
git diff --check
git status --short
```

预期：只有计划内的文件；无尾随空格错误。

- [ ] **Step 5: 提交**

```powershell
git add docs/ARCHITECTURE.md CHANGELOG.md
git commit -m "文档：补充行为测试与验收说明"
```

---

## 风险与回退

- `McInstance` 的模组识别依赖 `McVersion` 对 libraries 名称的解析；如果语料字段不足，测试会暴露实际识别规则。只调整语料，不改 `McInstance`。
- `CrashAnalyzer` 的文案是用户可见文本；断言只比较关键原因，不比较完整报告，避免无关文案变化导致脆弱。
- `Settings` 的静态缓存会跨用例保留；xUnit 并行已禁用，且每个用例使用独立实例路径，`WindowWidth` 等 Normal 键在用例结束时 Reset。
- `vstest.console.exe` 路径依赖 VS BuildTools；脚本找不到时会明确报错，不静默跳过。
- 行为测试项目不进入发布产物；它只引用 PCLA，不改变 PCLA 的打包内容。

## Self-Review

- Spec 覆盖：实例 10 例、崩溃 8 例、配置 11 例；测试宿主、临时目录、禁用并行、最小接缝、脚本与 CI 均有对应任务。
- 占位符：无 TBD/TODO；所有测试与实现步骤都有代码。
- 类型一致：`InstanceCase`、`CrashCase`、`FixtureFactory`、`BehaviorTestHost` 的签名在任务间一致。
- 风险：模组识别与崩溃文案以实际运行结果为准；若实际行为与 spec 表述不同，记录实际行为并更新断言，不修改生产逻辑。
