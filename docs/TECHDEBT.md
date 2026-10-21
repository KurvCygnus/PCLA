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
- 同一键在 `ModDownload.vb:199,393,499,648,883,1008,1107` 重复读取 7 次，任何一处拼写错误都只会静默落回默认值。
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

- 重试是乘法关系：`NetRequestByClientRetry`（`ModNet.vb:44-88`：10 s → 30 s → 4 s，429 固定睡 10 s）之上还有 `DlModRequest` 的源列表重试（`ModDownload.vb:1176-1209`）和 `DlSourceLoader` 的竞速循环（`:1317-1380`），一次逻辑请求可能放大成几十次。
- 无 DTO：所有响应都是 `JObject`/`JToken`，schema 漂移表现为 `NullReferenceException` 或"不是有效的 json 文件"。
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

- **已偿还：启动参数逻辑不可测。** 版本 JSON 规则判定、JVM/游戏参数收集、参数行拆分与去重、GC 策略已移入 `PCLCS\Minecraft\Launch\`（`VersionArgumentRules.cs`、`VersionArguments.cs`、`ArgumentLine.cs`、`GcPolicy.cs`），由 `tests\PCLCS.Tests` 的 69 项单测覆盖；VB 侧只保留适配层、设置读取与 `${...}` 展开。`ModLaunch.vb` 降至 2081 行。
- **残留：合并 JSON 的输入顺序。** 去重前的参数顺序与 VB 实现不同。JVM 参数最终结果一致；游戏参数在同一键跨继承层重复时，可能取子级值而不是 VB 取的值。当前无实例可做差分验证。
- **残留：`ArgumentLine` 的并发状态。** 当前实现没有规则缓存：`Deduplicate` 每次调用新建 `List`，`Split` 无可变静态状态，与 VB 原实现的 `List` + 线性扫描一致，不存在 `Dictionary` 缓存竞态。若以后为规则求值引入缓存，必须使用并发容器（`ConcurrentDictionary`），不要退回普通 `Dictionary`。
- **残留：实机差分未执行。** 本机没有 Minecraft 实例，三次启动与 `[Parity]` 日志对比未做；本轮只验证了单元测试与 Release 全量构建（0 警告 0 错误）。
- **残留：`McInstance` 仍是上帝类**，`${...}` 展开仍留在 VB（依赖登录态、路径、窗口尺寸与 UI 线程）。
