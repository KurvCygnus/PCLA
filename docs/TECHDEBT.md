# PCLA 技术债清单

> 基线：上游 PCL 2.99.0.0（`6b9a50d`）+ PCLA 分支改动（`88c6613`）。所有条目均给出仓库内证据；标注"已核实"的条目经过本机实际验证，"已否证"的条目保留结论以免重复排查。
>
> 分级：**P0** 影响正确性/可维护性根基，接手后优先处理；**P1** 结构性缺陷，随重构推进；**P2** 卫生问题，顺手清理。

## 0. 先纠正三条容易被误传的结论

| 结论 | 实际情况 | 证据 |
| --- | --- | --- |
| "账号密码明文入库" | 已否证。`LoginNidePass`/`LoginAuthPass`/`CacheAuthPass` 均声明 `Encrypted:=True`，走 DES 加密 | `Plain Craft Launcher 2\Pages\PageSetup\Settings.vb:63,70,80,82`；加解密 `:255,347` |
| "resx 嵌入 DLL 存在构建竞态，可能打包旧 DLL" | 已否证。删除 `PCLCS\bin` 后单独重建 vbproj 成功（3.5 s），`PCLCS\bin` 被重新生成，说明 ProjectReference 在 `GenerateResource` 之前完成 | 实测；嵌入点 `Plain Craft Launcher 2\My Project\Resources.resx:148-167`，加载点 `Application.xaml.vb:217-233` |
| "设置项密文安全" | 不成立。DES + 固定 IV `95168702` + 由 `Identify` 推导的固定密钥，只能防"肉眼直读" | `Settings.vb:439-475` |

另外补一条实测踩坑：清空 `PCLCS\obj` 后直接 `msbuild` 会报 13 个 `CS0518: 预定义类型 System.Runtime.CompilerServices.IsExternalInit 未定义`，因为 PolySharp 源生成器依赖 restore 产物。构建顺序必须是先 `dotnet build PCLCS\PCLCS.csproj -c Release`（自带 restore），再构建 vbproj。

## 1. P0

### P0-1 编译器被配置成"不检查"

- `OptionStrict Off`（`Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj:75`），`WarningLevel 1`（`:48,65,108`，最低档），并显式屏蔽 `41999,42016-42022,42032,42036,42314`（`:44,62,109`）——正好是晚期绑定与隐式转换的诊断集合。
- 后果：46k 行里对 `Object`/`JObject` 的每次成员访问都是运行期赌博；拼错设置键、重命名资源不会编译失败，而是在用户机器上 `NullReferenceException`。
- 方向：逐文件加 `Option Strict On`，从叶子模块（`ModValidate.vb`、`ModJava.vb`、`ResourceVersion.vb`）开始，改完再翻转项目默认值并恢复警告。

### P0-2 取消模型是 `Thread.Interrupt`

- `LoaderTask` 用裸 `Thread`，取消等于 `LastRunningThread.Interrupt()`（`Plain Craft Launcher 2\Modules\Base\ModLoader.vb:397-404`）；下载引擎同样只有 `State` 标志与 `Interrupt`（`Modules\Base\ModNet.vb:121,129`）。
- `Interrupt` 无法打断阻塞中的 socket IO，且可能在任意指令点抛 `ThreadInterruptedException`，留下半写状态；重启任务时旧线程可能仍在跑（`:376-378` 捕获 `ThreadStateException` 后重试）。
- 连带：UI 线程被同步等待（`ModBase.vb:916-933` 的 `Dispatcher.Invoke`；`MyPageRight.vb:78-83` 的 `Thread.Sleep(10)` 轮询；`ModLaunch.vb:154-157` 的 `Thread.Sleep(100)`）。
- 方向：新代码一律 `async` + `CancellationToken`；旧 `LoaderBase` 保留外壳，内部改为 `Task`。

### P0-3 上帝对象与全局可变状态

| 文件 | 行数 | 职责 |
| --- | --- | --- |
| `Modules\Minecraft\ModLaunch.vb` | 2081 | 登录、参数拼装、进程启动、皮肤、UI 回调 |
| `Pages\PageDownload\ModDownloadLib.vb` | 2080 | 原版/Forge/Fabric 安装、manifest 提交 |
| `Modules\Minecraft\ModMinecraft.vb` | 2117 | 版本模型、库解析、资源、皮肤、锁 |
| `Modules\Base\ModNet.vb` | 1850 | HTTP、下载、限速、缓存 |
| `FormMain.xaml.vb` | 1678 | 窗口、路由、拖放、快捷键、Win32 钩子 |

- 页面实例是模块级全局变量（`Modules\ModMain.vb:365-420`），永不销毁；`FormMain` 既是窗口又是路由器还是事件总线。
- 19 个 VB `Module`（隐式静态）；无 DI、无 ViewModel、无接口边界。
- 方向：先给"启动/安装/下载"三条链路抽出接口，页面只做展示；这是后续任何重写的前置条件。

### P0-4 全局设置是字符串字典

- 383 处 `Settings.Get/Set` 调用，192 个声明键（`Pages\PageSetup\Settings.vb:7` 起），全部字符串索引；键不存在时抛 `KeyNotFoundException` 并被宽泛捕获（`Settings.vb:282-295`）。
- 同一键在 `ModDownload.vb:199,370,442,571,701,791,859` 重复读取 7 次，任何一处拼写错误都只会静默落回默认值（2026-10-22 下载清单抽离后仍是 7 次读取，只统一了读取后的 `SourcePolicy.GetPlan` 调用）。
- 迁移逻辑没有 schema 版本，全部是 `UpgradeSub` 里的代码分支（`FormMain.xaml.vb:477-556`）。
- 方向：用 `MeloongCore.Shared.General.Config`（`PCLCS\Configs.cs:4-17` 已有用法）建强类型模型，`Settings` 只保留兼容读写。

### P0-5 崩溃与退出路径吞异常

- `Application.xaml.vb:186` 的全局异常处理器以 `On Error Resume Next` 开头；`FormMain.xaml.vb:619,644` 的 `EndProgramForce`/关窗路径同样。
- 34 处空 `Catch … End Try`（10 个文件），集中在下载与安装：`ModNet.vb:123,1496`、`ModDownloadLib.vb:421,445,978,1001`、`ModCrash.vb:100`、`ModMinecraft.vb:1906`。
- 后果：失败表现为"卡在 99%"或无提示退出，日志里没有根因。
- 方向：禁止空 catch；所有兜底必须写 `Logger.Error` 并带上上下文。

### P0-6 发布物不完整、不可追溯

- CI 只上传 `Plain Craft Launcher 2\obj\<Config>\Plain Craft Launcher 2.exe`（`.github\workflows\build.yml:65-69`），丢掉 `Plain Craft Launcher 2.exe.config`（其中含 `useUnsafeHeaderParsing` 与 4 条 binding redirect，`App.config:4-6,13-30`）。
- `fetch-depth: 1`（`build.yml:24-27`）导致 `git describe --tags` 退化为裸 SHA；版本号硬编码在两处（`Modules\Base\ModBase.vb:9-15` 的 `2.99.0.0`/`413`，`My Project\AssemblyInfo.vb:54` 的 `2.99.0.0`），CI 从不改写。
- Debug 配置 `DefineConstants=SNAPSHOT`（`vbproj:39-40,50`），`#If DEBUG` 恒为假，Debug 构建对外自称"快照版 414"。
- 方向：单一版本源（MSBuild 属性 + 生成文件），CI 上传整个 `bin` 目录，补 `signtool`。

## 2. P1

### 2.1 网络与下载

- 重试是乘法关系：`NetRequestByClientRetry`（`ModNet.vb:44-88`：10 s → 30 s → 4 s，429 固定睡 10 s）之上还有 `DlModRequest` 的源列表重试（`ModDownload.vb:903-930`）和 `DlSourceLoader` 的竞速循环（`:999-1066`），一次逻辑请求可能放大成几十次。
- 无 DTO：下载清单已有类型化解析器（`PCLCS\Minecraft\Download\*Parser.cs`），其余响应都是 `JObject`/`JToken`，schema 漂移表现为 `NullReferenceException` 或“不是有效的 json 文件”。
- 无代理抽象：代码里没有 `IWebProxy`，只能吃系统代理；凭据只有 `x-api-key` 一种头部注入（`ModSecret.vb:26-38`）。
- 依赖 `CacheCow.Client` 的 `FileStore`（`ModNet.vb:226-229`）与 `.NET Framework` 绑定；全局 TLS 回调把 `Sender` 强转成 `HttpWebRequest`（`:1916-1931`），对 `HttpClient` 已无效，且是进程级安全口子。
- 临时文件随机名 + 会话启动清空（`:1806-1815`），崩溃即留下孤儿文件；没有跨进程续传。

### 2.2 UI

- 页面切换动画复制 5 份：`FormMain.xaml.vb:1513-1564`、`PageDownloadLeft.xaml.vb:61-80`、`PageInstanceLeft.xaml.vb:84`、`PageOtherLeft.xaml.vb:91`、`PageSetupLeft.xaml.vb:123-142`。
- 事件订阅只增不减：`PageInstanceMod.xaml.vb:20` 的 `AddHandler FrmMain.KeyDown` 从不移除；`:104` 的 `Entry.OnUpdate` lambda 捕获列表项；`MyComboBox.vb:16` 的 `ChangedEventList` 持续增长。
- 列表没有 `ItemsControl`/`DataTemplate`，靠 `Panel.Children.Clear()` 重建；`MyVirtualizingElement` 只做延迟创建、不做回收。
- 视觉硬编码：84 处字面颜色、2217 处字面尺寸/圆角；`StaticResource` 与 `DynamicResource` 混用导致换肤不完整。
- 无障碍为零：全仓库没有 `AutomationProperties`。

### 2.3 构建与仓库

- 10 个第三方 DLL 以二进制形式提交在 `Plain Craft Launcher 2\Resources\`（`vbproj:123-174` 的 `HintPath` + `Private=False`），无 `packages.config`/`PackageReference`；Newtonsoft 同时存在 13.0.0（DLL）与 13.0.3（NuGet，`MeloongCore\Shared\MeloongCore.csproj:19`）。
- 无中央包版本管理；`MeloongCore` 目标 `netstandard2.0`，把 `Microsoft.Win32.Registry.dll` 拖进负载（`MeloongCore.csproj:18`）再嵌入 exe。
- `.sln` 缺 `MeloongCore.Tests`（`MeloongCore\Tests\MeloongCore.Tests.csproj`），CI 也没有测试步骤；CI 路径过滤只覆盖 `Plain Craft Launcher 2/**` 与 workflow（`build.yml:4-10`），改 `PCLCS/` 或 `MeloongCore/` 不触发。
- CI 的 "Update Help" 步骤先克隆 `PCL2Help`，然后在克隆目录里 `Remove-Item -Path *.* -Recurse -Force`，再压缩（`build.yml:40-50`）——按顺序会把内容删空。
- "Replace" 步骤用 Windows PowerShell 的 `Out-File` 改写 UTF-8 无 BOM 的 `ModSecret.vb`（`:53-57`），会把中文注释写成 UTF-16，源码在中途被破坏。
- 运行期死代码与杂物：`patches\*.patch` 14 个、共 10.1 MB，全部是 `PA30` 二进制增量包，仓库内无任何消费方；`Controls\MySliderDot.xaml(.vb)` 存在但未加入编译项；`vbproj:867,913` 指向不存在的 `java-wrapper.jar`/`lwjgl-unsafe-agent.jar`；`.sln:33-38` 残留未列出的项目 GUID。
- `.git` 约 169 MB，主要来自补丁与历史二进制。

### 2.4 许可与身份

- 上游许可要求：声明第三方修改、名称以 "Plain Craft Launcher (PCL)" 开头并带修改后缀、关于页首位署名龙腾猫跃与赞助链接、保留许可文件、保留 `McLaunchPrecheck` 式购买提示、主色调为蓝色、不得做赞助解锁。
- 已满足：`PageOtherAbout.xaml:21` 首位署名、`:39-40` 赞助按钮、`ModSecret.vb:65` `ColorHue=210`、`ThemeCheck*` 无付费门槛（`:135-142`）、`.github/FUNDING.yml:3`。
- 未满足：`My Project\AssemblyInfo.vb:11-15` 仍是 `AssemblyTitle("Plain Craft Launcher 启动器")`、`AssemblyProduct("Plain Craft Launcher")`、`AssemblyCopyright("Copyright © 成都瓜皮龙科技有限公司")`；产物名仍是 `Plain Craft Launcher 2.exe`；`ModBase.vb:9` 的 `2.99.0.0` 与上游版本号冲突。README 写 PCLA，二进制里没有任何 PCLA 标识，存在"与 PCL 混淆"的许可风险。

## 3. P2

- 魔法数字：崩溃日志窗口 300/700、1500/500、1000（`ModCrash.vb:214-264`）；`LatestLog` 上限 501（`ModWatcher.vb:192`）；进度权重散落在 `ModLaunch.vb:122-145`、`ModDownloadLib.vb:1799-1832`。
- 进度条数学：`1-(1-p)^0.9` 再 `*0.93+0.05`（`ModNet.vb:741-743`），本地复制权重 0.2（`:1374-1390`）。
- 无本地化层：`Lang="zh_CN"` 硬编码（`ModBase.vb:54`），全部文案中文内联。
- XML 文档文件随 exe 发布（196 KB），但源码几乎没有 `'''` 注释。
- `.NETFramework,Version=v4.5.2` 引导先决条件残留（`vbproj:741`）。
- `My.Computer`/`My.Settings` 32 处（`ModMain.vb:978-980` 等），是迁移到 .NET 8 时必须先拆的 Framework 依赖。
- `Settings` 的注册表源 107 处（`Sources.Registry`），迁移时需要一个兼容层。

## 4. 建议路线

**阶段 0（1-2 周，止血）**
1. 恢复可构建性基线：固定构建顺序（先 `dotnet build PCLCS` 再 msbuild）、把 `MeloongCore.Tests` 加入 sln、CI 加 `dotnet test`。
2. 修 CI：上传整个 `bin`、取消 `fetch-depth: 1` 或改用 `github.run_number`、`Replace` 改用 UTF-8 写入、修掉自毁的 Help 步骤。
3. 单一版本源 + 构建标识；Debug 不再自称 Snapshot。
4. 补 `AssemblyInfo` 的 PCLA 身份，消除许可风险。

**阶段 1（结构性，按收益排序）**
1. 抽网络与下载到 C#：`ModNet` + `ModDownload` + `ModDownloadLib` + `ModModpack` 约 6.1k 行，是崩溃与空 catch 的聚集地，也是唯一有测试回报的模块（可复用 `MeloongCore\Tests\Utils\FileUtilsTest.cs` 的风格）。
2. 设置强类型化，先做只读映射，再替换写入点。
3. 逐文件开 `Option Strict`，从叶子模块推进到核心。

**阶段 2（平台）**
1. 决定是否迁移到 `net8.0-windows`；前置条件是注册表设置、`My.Computer`、`CacheCow`、`AssemblyResolve` 嵌入机制全部拆掉。
2. UI 若重写，先做 `MyListItem`/`MyCard` 的模板化试点，再谈 MVVM。

## 5. 启动参数抽离：已偿还与残留（2026-10-21）

- **已偿还：启动参数逻辑不可测。** 版本 JSON 规则判定、JVM/游戏参数收集、参数行拆分与去重、GC 策略已移入 `PCLCS\Minecraft\Launch\`（`VersionArgumentRules.cs`、`VersionArguments.cs`、`ArgumentLine.cs`、`GcPolicy.cs`），由 `tests\PCLCS.Tests` 的 79 项单测覆盖；VB 侧只保留适配层、设置读取与 `${...}` 展开。`ModLaunch.vb` 降至 2081 行。
- **已偿还：合并 JSON 的输入顺序。** `CollectJvm` / `CollectGame` 原先反向遍历继承链（父级先、选中版本后），与 VB 的追加顺序不符：VB 先输出选中版本的合并 JSON（数组内部祖先→子），再沿 `InheritName` 逐级追加父级。JVM 去重保留首次出现，两种顺序的最终值恰好一致；游戏参数去重由后者覆盖，跨层重复键会取到父级值。现已改为按 `chain` 正序遍历，并由 `tests\PCLCS.Tests\MergedJsonTests.cs` 用合并 JSON fixture 锁定最终列表与父级覆盖语义。
- **残留：`ArgumentLine` 的并发状态。** 当前实现没有规则缓存：`Deduplicate` 每次调用新建 `List`，`Split` 无可变静态状态，与 VB 原实现的 `List` + 线性扫描一致，不存在 `Dictionary` 缓存竞态。若以后为规则求值引入缓存，必须使用并发容器（`ConcurrentDictionary`），不要退回普通 `Dictionary`。
- **已偿还：实机差分。** 2026-10-21 晚用 `本机 MC 目录` 的三个真实实例（Forge 1.20.1 ZombieCraft、Forge 1.20.1-47.4.23、Fabric 1.21.8）各启动一次，全部进入主菜单并完成材质加载。ZombieCraft 的新旧日志参数块各 80 行，逐行比对仅 4 处环境差异（`-Xmx` 设置、离线用户名、两个会话 token），其余参数（`-cp`、Forge 模块参数、`--add-opens`、GC、游戏参数）完全一致；比对同时发现并修复了丢失的 `GC 设置：…` 日志行（`517a028`）。
- **残留：`McInstance` 仍是上帝类**，`${...}` 展开仍留在 VB（依赖登录态、路径、窗口尺寸与 UI 线程）。

## 6. 下载源映射的既有缺陷（2026-10-21）

以下四条是抽离时按“与 legacy 逐字一致”保留的既有行为，**本轮仅记录，不修**；legacy 解析代码已随抽离删除，位置以现行实现为准。

### 6.1 `staging-api.modrinth.com` 先被 `api.modrinth.com` 规则命中

- **现象与影响**：`Mod` 按顺序执行 `Replace`，而 `"api.modrinth.com"` 是 `"staging-api.modrinth.com"` 的子串，第一条规则先命中，`https://staging-api.modrinth.com/v2/...` 被改写成 `https://staging-mod.mcimirror.top/modrinth/v2/...`；针对 `staging-api` 的第二条规则永远无法命中。staging API 请求因此指向不存在的主机名，且路径多出 `staging-` 前缀。
- **位置**：`PCLCS\Minecraft\Download\DownloadSources.cs:60-61`；legacy `ModDownload.vb` 的对应实现已随抽离删除。
- **期望行为**：`staging-api.modrinth.com` 与 `api.modrinth.com` 一样映射到 `mod.mcimirror.top/modrinth`，不产生 `staging-mod.mcimirror.top`。
- **处理**：本轮仅记录，不修。

### 6.2 modded 库忽略 `preferOfficial`，Forge 官方 Maven 还会产生两个相同候选

- **现象与影响**：`Library` 命中 `minecraftforge`/`fabricmc`/`neoforged` 时直接返回 `{ maven, libraries }`，从不尝试原始地址，`preferOfficial` 在该分支完全无效；`MavenHosts` 只覆盖 `piston-data`/`piston-meta`/`libraries.minecraft.net`，对 `https://maven.minecraftforge.net/...` 两次替换都不命中，`maven` 与 `libraries` 完全相同，调用方会对同一 URL 重试两遍。影响：`preferOfficial` 的 true 分支完全无效；对 `maven.minecraftforge.net` 这类外部宿主，两个候选都是官方地址、镜像根本不在候选里；镜像故障时同一地址还会重复请求，白白消耗一次超时预算。
- **位置**：`PCLCS\Minecraft\Download\DownloadSources.cs:36-40`；legacy `ModDownload.vb` 的对应实现已随抽离删除（`DlSourceLibraryGet` 现在只做转调）。
- **期望行为**：modded 库在 `preferOfficial` 为真时把原始地址作为首候选，并对候选列表去重。
- **处理**：本轮仅记录，不修。

### 6.3 三主机之外的资产地址没有备用源

- **现象与影响**：`Assets` 只把 `resources.download.minecraft.net` 归一为 https，再对 `piston-data`/`piston-meta`/`resources` 三个主机生成镜像；其他宿主（自建镜像、第三方 CDN）以及 `http://piston-*` 这类明文地址替换后 `mirror` 与 `original` 相同，`Order` 的 `Union` 去重后只剩一个候选。影响：这些地址没有源切换能力，单点失败即整体失败；`http://piston-*` 还停留在明文 HTTP。
- **位置**：`PCLCS\Minecraft\Download\DownloadSources.cs:23-30`；legacy `ModDownload.vb` 的对应实现已随抽离删除。
- **期望行为**：未知宿主至少保留一个可用候选，`http://piston-*` 与 resources 一样做 https 归一。
- **处理**：本轮仅记录，不修。

### 6.4 Forge 官方页 IsRecommended 恒为 false

- **现象与影响**：`ForgeBuildParser.ParseOfficialHtml` 解析 1.20.1 真实页面（`tests\PCLCS.Tests\Fixtures\Download\forge-builds.html`，137 个构建）时，所有条目的 `IsRecommended` 全部为 false。表格行内的推荐徽标是 `class="promo-latest promo-recommended  fa"`（`fa` 在末尾、中间双空格），无法被子串 `"fa promo-recommended"` 命中；该子串只命中页面侧栏的 `<i class="fa promo-recommended"></i>Download Recommended` 标签（夹具内偏移 34666，全页仅此一处；表格行内的徽标在偏移 38469）。侧栏命中与否取决于 chunk 切分边界，与构建条目无关，因此每个构建块的判定结果恒为 false。
- **位置**：`PCLCS\Minecraft\Download\ForgeBuildParser.cs:99`；legacy `ModDownload.vb:694` 的同样子串判定已随旧解析删除，缺陷同源。
- **期望行为**：判定应基于构建条目自身 chunk 内的推荐徽标（如匹配 `promo-recommended` 而不带 `fa ` 前缀），页面标注 Recommended 的构建（当前为 47.4.27）的 `IsRecommended` 应为 true。
- **处理**：本轮仅记录，不修。

## 7. 下载清单抽离的偿还与残留（2026-10-22）

- **已偿还：七类清单解析的重复实现。** 客户端、OptiFine、Forge、NeoForge、LiteLoader、Fabric 共七类清单的 HTML/JSON 解析从 `ModDownload.vb` 移入 `PCLCS\Minecraft\Download\*Parser.cs`，`ModDownload.vb` 从 1443 行降到 1099 行；旧的正则常量（`RegexSearch`/`RegexSeek`）与临时差分区（`DlParity`）全部删除，每个解析器都有对应的 `tests\PCLCS.Tests\*ParserTests.cs`。
- **已偿还：`TimeSplit` 死代码。** Forge BMCLAPI 构建解析里的 `TimeSplit` 计算从未被使用，已随旧解析删除。
- **残留：NeoForge BMCLAPI 误标官方源。** `DlNeoForgeListBmclapiMain` 仍写 `.IsOfficial = True`，上层无法区分镜像与官方；修复涉及 UI 来源标注，留待单独评估。
- **残留：Forge 构建分支正则未转义。** `PCLCS\Minecraft\Download\ForgeBuildParser.cs:82` 用 `$"(?<=-{name}-)..."` 拼正则，`name`（如 `47.1.0`）未 `Regex.Escape`，且 `[a-z]+.[a-z]{3}` 的 `.` 未转义；当前构建号只含数字与点，未触发误判，换成含正则元字符的版本名会失配。
- **残留：LiteLoader 的 `OfficialError` 无写入点。** `ModDownload.vb:752` 声明了 `DlLiteLoaderListResult.OfficialError`，全仓库没有任何赋值，恒为 `Nothing`；它是旧“官方源错误备用”方案的遗留字段，可随下次 UI 收敛删除。
- **残留：Forge/NeoForge 的 VB 构造器重复解析。** `DlForgeVersionEntry.New`（`ModDownload.vb:550-562`）与 `DlNeoForgeListEntry.New`（`ModDownload.vb:668-695`）在 C# 解析器已经做过分支修正/版本推导之后又各算一遍。修正幂等，当前行为安全；风险是今后只改 C# 一侧时 VB 副本会静默过期。待 UI 收敛时改为逐字段直传，与其余五类加载器一致。
- **残留：BMCLAPI 客户端清单异常拼接完整响应体。** `ModDownload.vb:268` 把 `ResultText` 整段拼进用户可见异常消息。旧代码同样拼接 `Json.ToString`，属保真保留；后续可改用 `DownloadDiagnostics.Snippet` 截断。
