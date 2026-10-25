# S3：行为黄金语料与最小接缝设计

- 日期：2026-10-23
- 状态：草案，待用户审阅
- 上游：`docs/superpowers/specs/2026-10-22-brand-release-baseline-design.md`、`docs/superpowers/specs/2026-10-23-error-handling-design.md`
- 顺序依据：`docs/ROADMAP.md`（先把行为锁进测试，再抽业务核心到 C#，最后换 UI 框架）

## 1. 背景

S1 收拢了品牌、数据迁移、版本与发布基线；S2 建立了 `Result` 与三个异常接缝。下一步不是继续迁逻辑，而是先把现有行为锁住，给后续 C# 化与 UI 框架替换提供回归安全网。

当前行为没有任何跨语言的自动化保护：

- `tests/PCLCS.Tests` 只引用 `PCLCS`，无法触达 `PCLA` 的 `McInstance`、`CrashAnalyzer`、`Settings`。
- `McInstance` 的版本继承、隔离、可加模组判断与真实实例目录强耦合。
- `CrashAnalyzer` 的判定藏在 `Private` 方法与 `Private` 字典后面，`Output` 还会弹 UI。
- `Settings` 有 182 个设置项，读写分散在 INI 与注册表，默认值、实例隔离、类型转换都靠运行时约定。

S3 用**可执行的特性测试**把这些规则固定下来。测试是行为合同，不是实现细节的镜像；后续重构只要行为不变，测试就不该改。

## 2. 目标与非目标

**目标**

- 新增独立的行为测试项目，直接引用 `PCLA`，在无 WPF 窗口的进程内运行。
- 用合成语料覆盖三组行为：实例模型、崩溃规则、配置读写。
- 用黄金快照记录期望结果；行为变化必须显式更新快照。
- 只加一个最小生产接缝：`CrashAnalyzer.GetAnalyzeResult` 对测试可见。
- 测试可重复、无网络；不写真实用户数据与真实注册表，注册表来源的路径不纳入用例。

**非目标**

- 不迁移 `McInstance`、`CrashAnalyzer`、`Settings` 的实现到 C#。
- 不重构崩溃判断逻辑，不追求覆盖所有崩溃原因。
- 不测试 WPF 页面渲染、动画、主题、下载网络请求。
- 不测试注册表来源的配置读写；该部分归 S4 的设置后端统一。
- 不把测试写成实现白盒；不反射私有字段。

## 3. 测试宿主

### 3.1 项目形态

新增 `tests/PCLA.BehaviorTests`：

- `net48`，C#，xUnit；`AssemblyName` 为 `PCLA.BehaviorTests`。
- `ProjectReference` 指向 `PCLA\PCLA.vbproj`，沿用 `PCLCS` 与 `MeloongCore` 的现有依赖。
- 不使用 `dotnet test` 直接构建：PCLA 是 VB + WPF，`dotnet build` 不跑 VB 的 XAML 生成目标，会报大量 `BC30506`。行为测试用 MSBuild 构建，再用 `vstest.console` 运行。
- 现有 `tests/PCLCS.Tests` 保持不动，继续由 `dotnet test` 运行。

新增 `tools/test-behavior.ps1`：

1. 用 VS Build Tools 的 MSBuild 构建 `tests\PCLA.BehaviorTests\PCLA.BehaviorTests.csproj`（`-t:Rebuild -p:Configuration=Release -m:2`）。
2. 用同一套 Build Tools 的 `vstest.console.exe` 运行生成的 `PCLA.BehaviorTests.dll`。
3. 任一步失败即以非零退出，并保留 TRX/控制台结果。
4. 脚本不启动 WPF 窗口，不访问网络。

`tools/test-behavior.ps1` 属于仓库脚本，不放临时目录。CI 在 PCLA 构建之后调用该脚本；`PCLCS.Tests` 的 `dotnet test` 步骤保持独立。

### 3.2 进程内初始化

行为测试进程只初始化一次：

```text
MeloongCore.Main.Init("PCLABehaviorTests")
Logger.Instance = New PclLogger With {.logFolder = <temp>\logs, .MinLevel = LogLevel.Trace}
ModMinecraft.McFolderSelected = <temp>\.minecraft
```

隔离规则：

- 测试根目录：`%TEMP%\PCLA.BehaviorTests\<guid>`；每个测试类或每个用例独立子目录。
- `Paths.AppDataThenName` 会落到 `%APPDATA%\PCLABehaviorTests`；fixture 在启动时清空该目录，结束时再清空。
- `Paths.BaseThenName` 落在测试输出目录下的 `PCLABehaviorTests`；同样清理。
- 不写 `%APPDATA%\PCLA`、`<exe>\PCLA`、`HKCU\Software\PCLADebug`。
- xUnit 禁用并行：`[assembly: CollectionBehavior(DisableTestParallelization = true)]`，避免 `Settings` 静态缓存与 `McFolderSelected` 互相干扰。
- 每个用例结束重置自己写过的 `Settings` 键；不依赖测试顺序。

### 3.3 崩溃测试的 UI 隔离

`CrashAnalyzer.Output` 会 `FrmMain.ShowWindowToTop`、弹 `MyMsgBox`，还会写报告目录。行为测试**不调用 `Output`**，只调用 `Import`、`Prepare`、`Analyze`，再通过下面的最小接缝读取结果。

## 4. 黄金语料

全部语料为合成数据，放在 `tests/PCLA.BehaviorTests/Fixtures/`：

```text
Fixtures/
  Instances/<case>/
    versions/<name>/<name>.json
    versions/<name>/PCL/Setup.ini  # 仅隔离用例
    expected.json
  Crashes/<case>/
    crash.txt
    latest.log
    expected.json
  Settings/<case>.json             # 可选，记录输入与期望
```

规则：

- 每个用例的输入是最小必要字段，不复制真实实例、真实日志、真实用户名。
- `expected.json` 是黄金快照；只记录行为结果，不记录绝对路径、时间、随机 ID。
- 测试把临时根路径统一替换为 `%TEST_ROOT%`，把时间戳替换为 `<timestamp>`，再比较快照。
- 快照更新必须显式执行（例如设置 `PCLA_UPDATE_SNAPSHOTS=1`），默认只读；CI 不允许更新。
- 语料中的版本号、Java 版本、模组名使用公开的通用名称，不包含个人令牌、服务器地址或本地账号。

## 5. 实例模型测试

目标：锁定 `McInstance` 对版本 JSON、继承链、隔离与可加模组判断的行为。

公共入口：

- `New McInstance(name)`
- `Check() As Boolean`
- `Load() As McInstance`
- `State`、`Version`、`PathIndie`、`Modable`、`Name`

用例矩阵：

| 用例 | 输入 | 期望 |
| --- | --- | --- |
| vanilla | `1.20.1`，无 `inheritsFrom` | `Check=True`，`State=Original`，`Modable=False`，`Version.VanillaName=1.20.1` |
| forge | Forge JSON `inheritsFrom` 原版 | `Modable=True`，继承链解析出原版名 |
| neoforge | NeoForge JSON | `Modable=True`，`Name` 保留目录名，`Version.VanillaName` 解析出原版 |
| fabric | Fabric JSON | 同上，`Modable=True` |
| optifine | OptiFine 旧式 JSON | 正确识别原版与 OptiFine 层 |
| liteLoader | LiteLoader 旧式 JSON | 正确识别原版与 LiteLoader 层 |
| missing_json | 只有版本目录，没有 JSON | `Check=False`，不抛未处理异常 |
| broken_json | 截断/非法 JSON | `Check=False`；`Load` 的失败路径可断言，不允许进程崩溃 |
| isolated | `PCL\Setup.ini` 开启版本隔离 | `PathIndie` 指向版本目录，而非 `.minecraft` 根 |
| name_mismatch | 目录名与 JSON `id` 不同 | `Name` 取目录名，`Version.VanillaName` 取 JSON 继承结果 |

说明：

- `State` 的枚举值按现有行为记录，不在 S3 改语义。
- `Load` 只在 `Check=True` 的用例调用；失败路径单独断言。
- 若某用例需要 `Settings`，只用 `Sources.Normal` 或 `Sources.Instance`，不读注册表。

## 6. 崩溃规则测试

目标：锁定 `CrashAnalyzer` 对典型崩溃的分类与报告关键文本。

公共入口：

- `New CrashAnalyzer(instance)`
- `Import(filePath)`
- `Prepare() As Boolean`
- `Analyze()`
- `Friend GetAnalyzeResult(IsHandAnalyze As Boolean) As String`（S3 新增可见性）

用例矩阵：

| 用例 | 输入特征 | 期望原因/文本 |
| --- | --- | --- |
| java_too_old | 日志含 Java 8 与 MC 17 | Java 版本过低 |
| java_32bit | 日志含 32 位 JVM | Java 位数不匹配 |
| duplicate_mods | 两个同名模组 | 模组重复 |
| missing_dependency | `NoClassDefFoundError` + 模组名 | 缺失前置/依赖 |
| nightconfig | `ParsingException: Not enough data available` | NightConfig 解析问题 |
| out_of_memory | `java.lang.OutOfMemoryError` | 内存不足 |
| opengl | `GLFW error` / `OpenGL` 初始化失败 | 图形驱动/OpenGL |
| unknown | 无已知特征的堆栈 | 未识别原因，报告仍生成 |

断言方式：

- `expected.json` 记录 `reasons`（按现有 `CrashReason` 的稳定文本）与 `reportContains`（报告必须包含的关键句）。
- 报告中的绝对路径、时间、实例名、Java 路径先规范化，再比较。
- 不比较完整报告全文，避免无关文案改动造成脆弱；关键原因必须精确匹配。
- `Output` 不进入测试；`IsManualAnalyze=False` 只作为 `GetAnalyzeResult` 的参数。

## 7. 配置读写测试

目标：锁定 `Settings` 的默认值、Normal/Instance 读写、实例隔离、类型转换与存在性判断。

公共入口：

- `Settings.Get(Of T)(key, instance)`
- `Settings.Set(key, value, instance)`
- `Settings.Reset(key, instance)`
- `Settings.GetDefault(key)`
- `Settings.HasSaved(key, instance)`

用例矩阵：

| 用例 | 输入 | 期望 |
| --- | --- | --- |
| default_int | 未写入 `WindowWidth` | 返回 900 |
| set_normal | `Set("WindowWidth", 1024)` | 再读为 1024，INI 落在测试 AppData |
| reset_normal | 写入后 `Reset` | 回到 900，`HasSaved=False` |
| instance_isolated | 两个实例写 `VersionArgumentIndieV2` | 各自读回自己的值，互不影响 |
| instance_missing | `Sources.Instance` 未提供实例 | 读取返回默认值/写入被记录，不写文件、不崩溃 |
| has_saved | 写入前后 | 分别为 `False` / `True` |
| get_default | `GetDefault("WindowWidth")` | 返回默认值的字符串形式 |
| unknown_key | 不存在的键 | `KeyNotFoundException` |
| invalid_type | `Set("WindowWidth", "not-a-number")` | 保持原值/默认值，记录错误，不崩溃 |
| set_safe_encrypted | 对加密项调用 `SetSafe` | `InvalidOperationException` |
| string_roundtrip | 含换行、引号、反斜杠的字符串 | 写入后读回完全一致 |

明确不测：

- `Sources.Registry` 的写入与读取；S4 的统一设置后端会替换注册表来源，届时用后端测试覆盖。
- 加密往返不新增解密接缝；S4 后端测试覆盖完整加解密。S3 只断言 `SetSafe` 拒绝加密项，以及普通字符串往返。

## 8. 唯一生产接缝

`PCLA\Modules\Minecraft\ModCrash.vb:924`：

```vb
Private Function GetAnalyzeResult(IsHandAnalyze As Boolean) As String
```

改为：

```vb
Friend Function GetAnalyzeResult(IsHandAnalyze As Boolean) As String
```

在 `PCLA\My Project\AssemblyInfo.vb` 增加：

```vb
<Assembly: InternalsVisibleTo("PCLA.BehaviorTests")>
```

约束：

- 只改可见性，不改方法体、不改 `CrashReasons`、不改报告文本。
- 不把 `CrashReasons` 暴露为公共 API。
- 不添加 `#If TEST` 分支；生产行为与测试行为一致。
- 测试项目程序集名必须与 `InternalsVisibleTo` 完全一致。

## 9. 运行与 CI

本地：

```powershell
& '.\tools\test-behavior.ps1'
dotnet test tests\PCLCS.Tests\PCLCS.Tests.csproj -c Release
```

CI：

1. 构建 `PCLA`（现有 MSBuild 步骤）。
2. 运行 `tools\test-behavior.ps1`。
3. 运行 `PCLCS.Tests` 的 `dotnet test`。
4. 三步都通过才算绿色；行为测试失败不重试、不吞输出。

`tools/test-behavior.ps1` 不依赖 `dotnet test` 构建 PCLA，避免 VB WPF 的 XAML 目标缺失；脚本内部固定使用 Build Tools 的 MSBuild 与 `vstest.console`。

## 10. 验收

- 新增行为测试全部通过，数量不少于 29 个（实例 10、崩溃 8、设置 11）。
- `tools/test-behavior.ps1` 退出码 0；重复运行结果一致。
- 现有 `PCLCS.Tests` 仍为 230 通过 + 1 跳过；PCLA Release 构建 0 警告 0 错误。
- 行为测试无网络请求；不写 `%APPDATA%\PCLA`、不写 `HKCU\Software\PCLADebug`。
- 测试进程不创建 WPF 窗口；`CrashAnalyzer.Output` 不被调用。
- 故意改动一条行为（例如把 `WindowWidth` 默认值改为 901）会使对应用例失败；恢复后通过。
- 语料中不出现真实用户名、令牌、服务器地址或本地绝对路径。
- `PCLA.BehaviorTests` 的测试程序集名与 `InternalsVisibleTo` 一致。

## 11. 风险

| 风险 | 缓解 |
| --- | --- |
| VB WPF 项目无法被 `dotnet test` 直接构建 | 行为测试项目用 MSBuild 构建，`vstest.console` 运行；CI 明确顺序 |
| `Settings` 静态缓存跨用例污染 | 禁用并行；每个用例重置自己写过的键；使用独立临时根 |
| `McInstance` 依赖真实目录或注册表 | 只用临时实例与 `Sources.Normal/Instance`；探针已验证 `Check` 可在控制台进程运行 |
| 崩溃报告文本长且含动态路径 | 只断言原因与关键句；路径/时间规范化；不比较全文 |
| 快照随实现漂移被随手更新 | 默认只读；更新需显式环境变量；快照改动必须人工审阅 |
| `Friend` 接缝扩大 API 面 | 只对测试程序集开放；不改方法体；不暴露 `CrashReasons` |
| 语料包含隐私数据 | 合成语料；提交前扫描用户名、令牌、绝对路径 |

## 12. 后续

- S3 完成后，S4 处理设置后端统一文件与注册表导入；届时补注册表来源与加密往返测试。
- C# 化阶段把 `McInstance`、`CrashAnalyzer`、`Settings` 抽到 `PCLCS` 时，黄金语料直接复用，只改测试宿主的引用路径。
- UI 框架替换阶段只跑同一套行为测试，不重写期望。
