# 去 .NET Framework 耦合与 NativeAOT 就绪设计

- 日期：2026-10-23
- 状态：已批准（m03516）
- 范围：S4
- 前置：S1 品牌与发布基线、S2 异常兜底、S3 行为黄金语料均已完成

## 1. 背景

`PCLCS` 是启动器业务核心的 C# 部分，但仍以 `net48` 为唯一目标，并引用 `MeloongCore.Wpf` 与 `Microsoft.VisualBasic`。这带来三个问题：

1. `PCLCS` 的依赖面包含 WPF 与 VB 运行时，无法独立验证跨平台与 AOT 约束。
2. 解析、迁移与配置仍使用 Newtonsoft.Json 的反射式序列化，NativeAOT 下不可用。
3. 代码中存在 `AppDomain.SetData`、`Enum.Parse(Type, ...)`、`RegexOptions.Compiled`、`Type.Assembly.GetManifestResourceStream` 等对裁剪与 AOT 不友好的写法。

S4 的目标是：让 `PCLCS` 与 `MeloongCore.Shared` 在保留 `net48` 的前提下具备可验证的 NativeAOT 路径，并把设置后端与 Getting Started 的契约定下来。设置与 Getting Started 本轮只写设计，不实现。

## 2. 目标与非目标

### 2.1 目标

- `PCLCS` 去掉对 `MeloongCore.Wpf` 与 `Microsoft.VisualBasic` 的引用，改为 `net48;net8.0` 多目标。
- `MeloongCore.Shared` 改为 `netstandard2.0;net8.0` 多目标，去掉 Newtonsoft.Json。
- `PCLCS` 与 `MeloongCore.Shared` 的 Newtonsoft.Json 全部替换为 System.Text.Json。
- 新增 `PCLCS.AotSmoke`，在 `net8.0` 下 `PublishAot` 并实际执行代表性解析、迁移与版本路径。
- 把设置后端、迁移、备份、Getting Started 写成设计契约；不写实现。
- 现有测试与行为语料不退：PCLCS 230 通过 + 1 跳过，行为 32/32。

### 2.2 非目标

- 不把 `PCLA` 的 VB/WPF 壳迁移到 .NET 8 或 NativeAOT；它继续 `net48`，等 C# 化与 UI 框架替换时处理。
- 不实现设置后端、Getting Started、迁移向导或备份 UI；这些留到 C# 化 + UI 框架之后的首个正式版。
- 不迁移启动器账号、凭据或登录态。
- 不改变 `PCLA` 单 exe 的 `AssemblyResolve` 方案；只补齐新增依赖的嵌入。
- 不修 `MeloongCore.Wpf` 的 Newtonsoft 使用；它不在 AOT 路径。

## 3. 术语

- **AOT 路径**：`PCLCS` 在 `net8.0` 下被 `PCLCS.AotSmoke` 实际调用的代码路径。
- **反射豁免**：明确不承诺 AOT、但登记在 `docs/TECHDEBT.md` 的 API；当前只有 `MeloongCore.Shared.General.Config<T>` 的泛型反序列化。
- **PCLA 数据根**：安装版 `%APPDATA%\PCLA`，便携版 `<exe>\PCLA`。
- **游戏目录设置**：`<游戏目录>\.pcla\Settings.json`，是 PCLA 自己的命名空间，不读上游 `PCL\Setup.ini`。

## 4. 去耦与多目标

### 4.1 PCLCS 项目

`PCLCS/PCLCS.csproj` 改为：

```xml
<TargetFrameworks>net48;net8.0</TargetFrameworks>
<LangVersion>13</LangVersion>
<Nullable>enable</Nullable>
<AppendTargetFrameworkToOutputPath>true</AppendTargetFrameworkToOutputPath>
```

多目标下必须保留 TFM 子目录（`bin\net48\`、`bin\net8.0\`），不能沿用 S1 的单输出目录；`PCLA.vbproj` 的嵌入项按对应 TFM 解析。

删除：

- `ProjectReference` 指向 `..\MeloongCore\Wpf\MeloongCore.Wpf.csproj`。
- `Reference Include="Microsoft.VisualBasic"`。
- 全局 Using 中的 `Microsoft.VisualBasic`、`MeloongCore.Wpf`、`Newtonsoft.Json`。

保留：

- `ProjectReference` 指向 `..\MeloongCore\Shared\MeloongCore.csproj`。
- `Microsoft.Win32.Registry` 包；`net8.0` 下只在 Windows 使用。
- `System.Text.Json` 包；`net48` 与 `net8.0` 共用。

`PCLCS` 内不得出现 `System.Windows`、`Microsoft.VisualBasic`、`MeloongCore.Wpf` 的引用；由编译与 `rg` 双重检查。

### 4.2 MeloongCore.Shared 项目

`MeloongCore/MeloongCore.Shared.csproj` 改为：

```xml
<TargetFrameworks>netstandard2.0;net8.0</TargetFrameworks>
```

删除 `Newtonsoft.Json` 包引用；增加 `System.Text.Json`。`netstandard2.0` 保留现有 `Microsoft.Win32.Registry`、`PolySharp`、`ThrottleDebounce` 依赖；`net8.0` 使用框架自带 API。

### 4.3 反射与 AOT 阻塞点

| 位置 | 现状 | S4 处理 |
| --- | --- | --- |
| `MeloongCore/Shared/General/Main.cs:7` | `AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", ...)` | 删除；改为所有 `Regex` 构造显式传 timeout，或使用静态 `Regex` 的 `matchTimeout` |
| `MeloongCore/Shared/Utils/EnumUtils.cs:23` | `Enum.Parse(typeof(T), value, true)` | `net8.0` 用 `Enum.Parse<T>(value, true)` 与 `Enum.GetValues<T>()`；`netstandard2.0` 保留旧路径，AOT 路径不调用 |
| `MeloongCore/Shared/Utils/FileUtils.cs:17` | `type.Assembly.GetManifestResourceStream(name)` | 新增显式 `Assembly`/资源名重载；AOT 路径只调用显式重载；`Type` 重载保留给 `net48` 并标注 |
| `MeloongCore/Shared/Extensions/StringExtensions.cs:271` | `RegexOptions.Compiled` | 删除 `Compiled`；改为 `RegexOptions.None` 或静态 `Regex` |
| `MeloongCore/Shared/Extensions/StringExtensions.cs:401` | `JsonConvert` | 改 `JsonSerializer` + `JsonTypeInfo` |
| `PCLCS/Startup/UserDataMigration.cs:38` | `AppDomain.CurrentDomain.BaseDirectory` | AOT 安全，保留 |

`MeloongCore.Shared.General.Config<T>` 是唯一明确豁免：它按调用方类型做泛型反序列化，S4 只增加 `JsonTypeInfo<T>` 重载，不承诺旧反射路径可 AOT。该豁免登记在 `docs/TECHDEBT.md` 第 8 节。

### 4.4 多目标下的条件编译

只在确实需要差异的地方使用 `#if NET8_0_OR_GREATER`：

- `EnumUtils` 的泛型枚举 API。
- `Config<T>` 的 `JsonTypeInfo<T>` 重载。
- `FileUtils` 的显式资源重载。
- `PCLCS` 的注册表路径：`net8.0` 下使用 `Microsoft.Win32.Registry` 包，`net48` 下使用框架程序集。

禁止用条件编译隐藏业务差异；所有差异必须在测试或 AOT 冒烟中有对应覆盖。

## 5. JSON 替换

### 5.1 包与边界

- `PCLCS` 与 `MeloongCore.Shared` 删除 Newtonsoft.Json，改用 `System.Text.Json`。
- `PCLA` 的 VB 代码可以继续使用 Newtonsoft.Json；只在调用 `PCLCS` 的接缝处改为 `JsonNode`/`JsonObject`。
- `MeloongCore.Wpf` 的 Newtonsoft 使用保持到 C# 化。

### 5.2 动态节点移植

解析器的 JSON 形状不固定，采用 `JsonNode` 动态移植，不把七类清单一次性改成强类型 DTO：

- `JObject` -> `JsonObject`。
- `JArray` -> `JsonArray`。
- `JToken` -> `JsonNode`。
- `JValue` -> `JsonValue`。
- `JObject.Parse` -> `JsonNode.Parse`。
- `token.Value<string>("id")` -> `JsonNodeUtils.GetString(token, "id")`。
- `ToObject<T>()` -> `JsonSerializer.Deserialize<T>(node, options)`。
- `JToken.FromObject` -> `JsonSerializer.SerializeToNode(value, typeInfo)`。

### 5.3 JsonNodeUtils

新增 `PCLCS/Json/JsonNodeUtils.cs`，集中处理 Newtonsoft 与 System.Text.Json 的行为差异：

- 属性名大小写不敏感。
- 缺失属性返回默认值，不抛异常。
- 数字与字符串之间按目标类型宽松转换。
- `null` 与缺失区分：`null` 返回 `null`，缺失返回默认值。
- 注释 `Skip`，尾逗号允许。
- 日期按本地时区解析；需要 UTC 的调用方显式指定。
- 提供 `GetString`、`GetInt`、`GetBool`、`GetDouble`、`GetObject`、`GetArray`、`GetDateTime`、`GetPropertyIgnoreCase`。

`JsonNodeUtils` 是过渡层；后续 C# 化时改为强类型 DTO + 源生成后删除宽松层。该债务登记在 `docs/TECHDEBT.md` 第 9 节。

### 5.4 源生成

固定 DTO 使用 `JsonSerializerContext` 源生成：

- `McInstallManifest`、`VersionArguments`、`VersionArgumentRules`。
- `MeloongCore.Shared` 的配置 DTO。
- 不把 `JsonNode` 动态解析包进源生成。

`JsonSerializerOptions` 统一放在 `PCLCS/Json/JsonOptions.cs`：`PropertyNameCaseInsensitive = true`、`AllowTrailingCommas = true`、`ReadCommentHandling = Skip`、`WriteIndented = false`。

### 5.5 单 exe 发布

`PCLA` 继续把 `PCLCS.dll`、`MeloongCore.dll`、`MeloongCore.Wpf.dll`、`Microsoft.Win32.Registry.dll` 以 `LogicalName` 嵌入。新增 `System.Text.Json` 及其运行时依赖也要嵌入，至少包括：

- `System.Text.Json.dll`
- `System.Text.Encodings.Web.dll`
- `System.Memory.dll`
- `System.Buffers.dll`
- `System.Numerics.Vectors.dll`
- `System.Runtime.CompilerServices.Unsafe.dll`
- `System.Threading.Tasks.Extensions.dll`
- `Microsoft.Bcl.AsyncInterfaces.dll`

实际列表以 `PCLA` 构建输出为准；`PCLA.vbproj` 的嵌入项必须在构建后检查，缺一个就会在运行时 `FileNotFoundException`。

验收时删除 `PCLA` 输出目录中除 `PCLA.exe` 之外的所有 DLL，启动并解析一个真实清单，确认单 exe 仍可用。

## 6. AOT 冒烟

### 6.1 项目

新增 `tests/PCLCS.AotSmoke/PCLCS.AotSmoke.csproj`：

```xml
<TargetFramework>net8.0</TargetFramework>
<PublishAot>true</PublishAot>
<InvariantGlobalization>true</InvariantGlobalization>
<TrimMode>full</TrimMode>
```

引用 `PCLCS` 的 `net8.0` 目标，不引用 WPF、VB 或测试框架。

### 6.2 覆盖路径

冒烟程序按顺序执行：

1. 用内嵌夹具调用 `FabricListParser.Parse`，断言 `IsSuccess` 且版本列表非空。
2. 用内嵌夹具调用 `ForgeBuildParser` 或 `NeoForgeParser` 的代表性路径。
3. 用临时目录 + 内存注册表调用 `UserDataMigration.Run`，断言报告非空且目标文件存在。
4. 读取 `VersionInfo`，断言 `VersionFull` 以 `0.1.0` 开头。
5. 输出 `AOT smoke OK` 并以 0 退出。

冒烟程序不得访问网络、不得弹窗、不得写真实用户目录。

### 6.3 告警门禁

`dotnet publish -c Release -r win-x64` 必须满足：

- 0 个 `IL2026`/`IL3050` 等 trim/AOT 告警。
- 0 个编译警告。
- 产物可执行并输出 `AOT smoke OK`。

出现告警时，先修；不能修的必须登记到 `docs/TECHDEBT.md` 并说明为何不影响 AOT 路径。禁止用 `NoWarn` 全局压制。

## 7. 设置与 Getting Started（仅设计）

### 7.1 原则

PCLA 是独立软件，只读写自己的命名空间。旧启动器目录只作为显式迁移的来源，运行时不做兼容读取、不回退、不合并。

### 7.2 存储

- 全局设置：PCLA 数据根下的 `Settings.json`。
- 游戏目录设置：`<游戏目录>\.pcla\Settings.json`。
- 不读取上游 `PCL\Setup.ini`，不读取 `HKCU\Software\PCLADebug` 作为运行时配置。
- Schema：

```json
{
  "schemaVersion": 1,
  "values": {
    "UiLauncherTheme": 0,
    "LaunchArgumentInfo": "…"
  },
  "encrypted": {
    "Key": { "$enc": "v1", "data": "…" }
  }
}
```

`values` 保持原始 JSON 类型；未知键原样保留；加密值用显式包装，不做前缀猜测。

### 7.3 写入

- 临时文件 + 原子替换。
- 全局与游戏目录各自加锁；跨进程使用命名互斥体。
- 导出时生成归档；不在每次写入时维护兼容备份。
- 旧数据不删除、不就地改写。

### 7.4 显式迁移

- `ISettingsMigration`：`Detect()`、`Preview()`、`Import(selection)`、`Export()`、`Restore()`。
- 来源：PCL2（注册表 + 全局/游戏目录 `Setup.ini`）、HMCL（`hmcl.json` + `versions`）、Prism（`prismlauncher.cfg` + `instances`）。
- 账号凭据不迁移。
- 只在 Getting Started 或设置页“迁移与备份”中执行；不做静默导入。
- 失败逐项报告，不自动重试；旧数据保留，可回滚到迁移前状态。

### 7.5 Getting Started

流程：欢迎 -> 检测来源 -> 选择数据 -> 预览 -> 执行 -> 结果。可跳过、可取消、失败可重试。首版只在 C# 化 + UI 框架 + Getting Started 三项完成后发布；S4 不实现该流程。

## 8. 验收

### 8.1 构建与测试

- `PCLCS` 的 `net48` 与 `net8.0` 均 0 警告 0 错误。
- `MeloongCore.Shared` 的 `netstandard2.0` 与 `net8.0` 均 0 警告 0 错误。
- PCLCS 测试 230 通过 + 1 跳过。
- 行为语料 32/32。
- `PCLA` Release 0 警告 0 错误。

### 8.2 AOT

- `PCLCS.AotSmoke` `PublishAot` 0 告警，运行输出 `AOT smoke OK`。
- 删除 PCLA 输出目录所有旁置 DLL 后，`PCLA.exe` 仍可启动并解析清单。

### 8.3 依赖与扫描

- `rg "Newtonsoft" PCLCS MeloongCore/Shared` 为空。
- `rg "System.Windows|Microsoft.VisualBasic|MeloongCore.Wpf" PCLCS` 为空。
- PCLA 的品牌扫描仍通过。
- `docs/TECHDEBT.md` 第 8、9 节存在且与实现一致。

### 8.4 设置设计

- 本文第 7 节通过审阅；不要求代码、测试或 UI。
- S4 计划不得包含设置后端、Getting Started 或备份实现任务。

## 9. 风险

- **System.Text.Json 行为差异**：缺失属性、大小写、数字/字符串、日期、注释、尾逗号。用 `JsonNodeUtils` 与 parity 夹具收敛；未覆盖的差异登记到第 9 节。
- **单 exe 依赖遗漏**：`System.Text.Json` 的传递依赖多，缺一个就运行时失败；用“删 DLL 后启动”验收。
- **AOT 告警误报**：库的 `IsAotCompatible` 与冒烟项目 `PublishAot` 可能给出不同结果；以冒烟 publish 为准。
- **Config<T> 豁免**：泛型反序列化不承诺 AOT；新增调用点若触发告警，按第 8 节处理。
- **多目标包冲突**：`netstandard2.0` 与 `net8.0` 的包版本可能不同；集中版本，禁止各项目自行浮动。
- **PCLA 接缝破坏**：`JObject` -> `JsonObject` 会改变 PCLCS 公共 API；所有 VB 调用点必须同批修改，行为语料覆盖。
- **设置设计被误实现**：S4 计划明确排除设置后端；若后续提前实现，必须先补独立 spec。

## 10. 待办

- S4 实现计划：去耦、多目标、JSON、AOT 冒烟、依赖嵌入、验收。
- 设置后端、Getting Started、迁移与备份：后续独立 spec。
- `Config<T>` 的 `JsonTypeInfo<T>` 重载：S4 内完成；旧反射路径的清理留到 C# 化。
- 账号迁移、凭据迁移：不在 S4，也不在首个正式版承诺内。

## 11. 参考

- `docs/superpowers/specs/2026-10-22-brand-release-baseline-design.md`
- `docs/superpowers/specs/2026-10-23-error-handling-design.md`
- `docs/superpowers/specs/2026-10-23-behavior-golden-corpus-design.md`
- `docs/TECHDEBT.md` 第 8、9 节
- `docs/ARCHITECTURE.md`
