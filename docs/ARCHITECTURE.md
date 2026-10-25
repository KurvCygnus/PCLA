# PCLA 架构说明

> 基线：上游 PCL 2.99.0.0（提交 `6b9a50d`），行号以当前分支为准。本文描述"代码实际是什么样"，目标形态与技术债见 `docs/TECHDEBT.md`。

## 1. 技术栈与工程拓扑

| 项目 | 语言 | 目标框架 | 作用 |
| --- | --- | --- | --- |
| `Plain Craft Launcher 2` | VB.NET + XAML | .NET Framework 4.8 | 启动器主体：UI、业务、Minecraft 域 |
| `PCLCS` | C# | net48 | 逐步外迁的强类型层：`McVersion`、`McInstallManifest`、`Java`、常量 |
| `MeloongCore` | C#（子模块） | net48 / netstandard2.0 | 通用基础设施：日志、路径、配置、网络、文件工具 |
| `MeloongCore.Wpf` | C# | net48 | WPF 相关基础件 |

- 规模：VB/XAML 共 176 个文件、约 46.1k 行。单文件超过 1000 行的有 12 个，前四名：`Modules\Minecraft\ModMinecraft.vb`（2117）、`Modules\Minecraft\ModLaunch.vb`（2081）、`Pages\PageDownload\ModDownloadLib.vb`（2080）、`Modules\Base\ModNet.vb`（1850）。
- 编译选项：`OptionStrict Off`、`OptionInfer On`、`WarningLevel 1`，并屏蔽了 `BC41999/42016-42022/42032/42036/42314` 一整批晚期绑定与类型转换警告（`Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj:75-85`）。这决定了整个代码库默认使用晚期绑定。
- `MeloongCore` 是 git submodule（`.gitmodules`），未初始化时无法编译。

## 2. 分层与现实

名义上的分层：

```
Pages / Controls (XAML + code-behind)
        ↓ 直接调用
Modules\ModMain (全局页面实例、Hint、MyMsgBox、Telemetry)
Modules\Base (ModBase 工具、ModLoader 任务框架、ModNet 网络、ModAnimation 动画)
Modules\Minecraft (ModLaunch / ModMinecraft / ModDownload / ModModpack / ModCrash / ModJava / ModWatcher)
Modules\Resource (CurseForge / Modrinth 资源模型)
        ↓
PCLCS (McVersion 等强类型) + MeloongCore (日志/路径/配置/HTTP 基础设施)
```

实际依赖是双向的：引擎层直接引用窗体（`ModLaunch.vb:108` 取 `FrmLaunchLeft`，`ModWatcher.vb:30-54` 直接 `RunInUi`），页面层直接做文件 IO 与网络请求（`Pages\PageLaunch\PageLaunchLeft.xaml.vb:24-108`）。没有依赖注入容器，也没有 ViewModel；模块级 `Public` 变量就是全局服务总线（`Modules\ModMain.vb:365-420`）。

## 3. 启动与生命周期

- 入口：`Application.xaml:9-10` 的 `StartupUri="FormMain.xaml"`，`ShutdownMode="OnExplicitShutdown"`。
- `Application.xaml.vb:7-14` 依次执行 `MeloongCore.Main.Init("PCL")`、`MeloongCore.Wpf.Main.Init()`、单实例互斥体 `PCL_SingletonMutex`（`:151-176`，Debug 构建跳过）、`Logger.Instance = New PclLogger`（`:14`）。
- 日志：`<exe>\PCL\Log1.txt` 至 `Log5.txt` 轮转，独立最低优先级线程异步写入（`MeloongCore\Shared\General\Logger.cs:297-343`）；`PclLogger` 负责脱敏用户名与 token，并把 `LogBehavior` 映射为提示、弹窗、反馈或崩溃（`Modules\Base\PclLogger.vb:5-52`）。
- 启动守卫：.NET 4.8 注册表检查（`Application.xaml.vb:28`）、`--update` 自替换、`--gpu` 显卡偏好、`--memory` 内存优化、缓存重试、`ServicePointManager` 调优、启动画面、临时目录与 32 位系统拒绝（`:41-132`）。
- 主窗体：`FormMain.xaml.vb:284-348` 构造时对比 `SystemLastVersionReg` 与 `VersionCode` 走升级/降级迁移（`:291-311`），挂载启动页，`McFolderListLoader.Start(0)`；`FormMain_Loaded`（`:349-457`）再做窗口尺寸恢复、`HwndSource` 钩子、入场动画，然后低优先级线程跑 EULA、`JavaInit`、版本列表、服务器列表与遥测。
- 未处理异常：`Application.xaml.vb:185-207` 先给当前页面 `IDispatcherUnhandledException` 机会，再走 `FeedbackInfo` 与 `Logger.Error(..., AlertThenCrash)`；致命退出路径在 `FormMain.EndProgramForce`（`:618-637`），用 `Process.Kill()` 兜底。
- 没有后台自动更新检查：只有 CLI `--update` 与版本号迁移；更新入口在"关于"页（`Pages\PageOther\PageOtherAbout.xaml.vb:30`）。

## 4. UI 层

### 4.1 页面模型

- 两种基类：`MyPageRight : AdornerDecorator`（右侧内容页，`Controls\MyPageRight.vb:2`）与 `MyPageLeft : Grid`（左侧导航，`Controls\MyPageLeft.vb:2`）。
- 页面由 `FormMain.PageChangeActual` 用 `Select Case` 直接 `New` 出来（`FormMain.xaml.vb:1413-1498`），随后永久缓存在模块级字段里（`Modules\ModMain.vb:365-414`），只隐藏不销毁。
- 导航状态：`PageType`（0-9 顶层页面）与 `PageSubType`（`FormMain.xaml.vb:1183-1251`），配合 `PageStack As List(Of PageStackData)`（`:1319-1407`）实现返回栈。
- 过渡：`PageChangeAnim`（`:1513-1564`）在 110 ms 延迟后调用 `PageOnExit`/`PageOnEnter`；`MyPageRight` 内部是 9 态状态机（`MyPageRight.vb:18-40,173-385`），入场按控件树逐项做透明度与位移错峰动画（25 ms 步进）。
- 加载态：`PageLoaderInit`（`MyPageRight.vb:62-152`）把一个 `LoaderBase` 绑定到 `MyLoading`，失败可点击重试；内部用 `Thread.Sleep(10)` 轮询等待退出动画。

### 4.2 控件与视觉

- 自绘控件为主：`MyListItem`（619 行，行式列表项，代码构建背景/文字/按钮）、`MyCard`（阴影+展开折叠）、`MyDropShadow`（分层几何阴影）、`MyLoading`（自绘进度环）、`MyButton`、`MyExtraButton`、`MyResizer`（Win32 `WM_NCHITTEST`/`MINMAXINFO`）、`MyImage`（网络图 + 7 天磁盘缓存）。
- 主题：`Application.xaml:22-66` 声明 10 组画刷；运行时 `ModSecret.ThemeRefresh` 用 HSL 重算并覆盖 `Application.Current.Resources("ColorBrushN")`（`Modules\ModSecret.vb:68-104`）。大部分 XAML 用 `DynamicResource`，但仍有 `StaticResource` 与 84 处字面颜色不会跟随换肤。
- 动画：自研计时器引擎 `AniStart`/`AniTimer`（`Modules\Base\ModAnimation.vb:753-858`），约 25 个 `Aa*` 构造器，默认 400 ms；`AniControlEnabled` 是全局重入计数器。
- 对话框：`MyMsgBox`（`Modules\ModMain.vb:230-274`）通过 `ComponentDispatcher.PushModal()` + `Dispatcher.PushFrame` 实现嵌套模态，消息进 `ConcurrentQueue` 后由 `MyMsgBoxTick` 逐条渲染；主窗体未就绪时退回 WinForms `MsgBox`（`:238-255`）。
- 提示条：`Hint` 入队（`ModMain.vb:31`），`HintTick` 每 50 ms 手工构造 `Border`+`TextBlock`，最多 20 条并去重（`:35-139`）。

### 4.3 列表与内容渲染

- 全仓库没有 `ItemsControl`/`ListBox`/`DataTemplate`；列表一律 `Panel.Children.Clear()` 后重新 `Add`。
- 大列表用 `MyVirtualizingElement(Of T)` + `LazyLoadBehavior`：占位元素进入视口后替换成真实控件（`Controls\MyVirtualizingElement.vb:1-28`、`Controls\Behaviors\LazyLoadBehavior.vb:6-36`），实现"延迟创建"而没有"回收复用"。
- 内置网页内容不是 WebView：`PageLaunchRight` 与 `PageOtherHelpDetail` 把 XAML 字符串去 `xmlns` 后 `XamlReader` 解析进 `StackPanel`（`PageLaunchRight.xaml.vb:234-265`、`PageOtherHelpDetail.xaml.vb:16-35`，两处互为复制粘贴）。仓库里没有 WebView2/WebBrowser/CefSharp。

## 5. 核心服务

### 5.1 设置与持久化

四套并存的存储：

| 存储 | 位置 | 典型内容 |
| --- | --- | --- |
| 注册表 | `HKCU\Software\PCL`（`Settings.vb:265,334`） | 全局设置、登录缓存 |
| INI | `<exe>\Setup.ini`（`ModBase.vb:511,536`） | 早期设置项 |
| 实例 INI | `<版本目录>\PCL\Setup.ini`（`Settings.vb:271,339`） | 版本名、加载器版本、启动参数 |
| JSON | `%AppData%\PCL\config.json`（`MeloongCore\Shared\General\Config.cs:123`） | MeloongCore 自身配置 |

`Settings` 是静态字典 `Entries`（`Settings.vb:5-198`），读写靠 `CTypeDynamic` 做运行期类型转换（`:285,356`）；`Encrypted:=True` 的项用 DES + 固定 IV `95168702`、密钥 `"PCL" & Identify` 加密（`:255,439-475`）。配置版本迁移没有 schema，全部塞在 `UpgradeSub`/`DowngradeSub` 的代码分支里（`FormMain.xaml.vb:477-556`）。

### 5.2 任务框架

- `LoaderBase`（`Modules\Base\ModLoader.vb:7-259`）：`State` 每次变迁都经 `RunInUi` 派发，`WaitForExit` 以 10 ms 轮询（`:210-228`）。
- `LoaderTask(Of TIn,TOut)`（`:263-416`）：每个任务一条裸 `Thread`，取消依赖 `Thread.Interrupt()`（`:397-404`），输入不变时按 `ReloadTimeout` 复用上次结果。
- `LoaderCombo`（`:467-669`）：按 `ProgressWeight` 加权汇总进度，把上一个子的 `Output` 接到下一个的 `Input`，失败即整链失败。
- `LoaderDownload`（`ModNet.vb:1396-1473`）：把一组 URL/路径变成下载任务，交给全局 `NetManager`。

### 5.3 网络与下载

- 请求入口 `NetRequestByClient`（`ModNet.vb:10-38`）与重试包装 `NetRequestByClientRetry`（`:44-88`）：第 0 次 10 s、第 1 次 30 s、第 2 次 4 s，429 固定睡 10 s；403/404 直接抛出。
- 底层共享一个 `HttpClient`，用 `CacheCow.Client` 的 `FileStore` 做磁盘缓存（`:226-229`）；响应整包读进 `MemoryStream`（`:267-271`）。仓库内没有代理配置，走系统代理。
- UA 与鉴权头由 `SecretHeadersSign` 统一注入（`Modules\ModSecret.vb:26-38`）：UA 为 `PCL2/{版本}.{构建类型}`，`api.curseforge.com` 与 `forgecdn.net` 附加 `x-api-key`（值来自环境变量 `PCL_CURSEFORGE_API_KEY`）。`{CDN}` 占位符在本分支只是被删除（`:19-22`）。
- 下载引擎 `NetFile`（`ModNet.vb:579-1301`）：256 KiB 分片、多线程抢占最大分片、Range 请求；不支持 Range 的源自动降级单线程（`:964-971,1078-1099`）。临时文件在 `%Temp%\PCL\Download\*.tmp`，会话启动时整目录清空（`:1806-1815`），因此没有跨进程续传。
- 并发与限速：线程数 = `ToolDownloadThread + 1`，速度上限由 `ToolDownloadSpeed` 档位换算（`:311-339`）；BMCLAPI 额外每线程延迟 100 ms，且 `pcl2-server`/`meloong.com`/`bmclapi`/`github.com`/`optifine.net`/`momot.rs` 强制单线程（`:853-854`）。
- 镜像：Mojang 元数据/资源/库走 BMCLAPI 或官方源（`ModDownload.vb:954-1000`）；CurseForge/Modrinth 走 `mod.mcimirror.top`（`:994`）；多源通过 `DlSourceLoader` 竞速（`:999-1066`）。下载源与清单解析细节见 §6.3。
- 资源 API：CurseForge `api.curseforge.com/v1/mods/search`（`Modules\Resource\ResourceSearcher.vb:73-91`）与 Modrinth `/v2/search`（`:96-111`），页大小 40；中文查询先查本地 MCWiki 词条映射成英文关键词（`:184-288`）；响应全程 `JObject`/`JToken`，无 DTO。

`PCLCS/Results` 提供 `Result<T, TError>`、领域错误与 `PclaException`；三个首批接缝（用户数据迁移、Fabric 列表解析、Java 探测）返回 Result，VB 边界用 `.OrThrow()` 或 `IsSuccess` 消费。全局未处理异常在 `Application.xaml.vb` 统一记录，日志系统失败时写 `%TEMP%\PCLA\crash-*.log`。

### 5.4 登录与凭据

- 微软登录六步都在 `ModLaunch.vb`：设备码（`:882-912`）、刷新令牌（`:914-946`）、Xbox Live（`:948-962`）、XSTS（`:964-1014`）、`login_with_xbox`（`:1016-1046`）、`/entitlements/mcstore`（`:1048-1066`）与 `/minecraft/profile`（`:1068-1098`）。
- 令牌缓存在 `CacheMsV2*` 系列设置项（`Settings.vb:52-57`，加密）；Nide/Authlib 的账号密码在 `LoginNidePass`/`LoginAuthPass`（`:80,82`，同样加密，但为 DES + 固定 IV）。
- 客户端 ID 走环境变量 `PCL_MS_CLIENT_ID`（`ModSecret.vb:10`），仓库不携带任何密钥。

## 6. Minecraft 域

### 6.1 模型

- 版本元数据用 C# 类型 `McVersion`（`PCLCS\Minecraft\McVersion.cs:8`），由 JSON 文本反推：版本号（`:154-207`）、加载器（`:129-151`，靠字符串包含与正则）、快照/愚人节（`:132-133`）。
- 加载器是标志位枚举 `ModLoaders { None, Forge=1, LiteLoader=2, Fabric=4, NeoForge=16 }`（`PCLCS\Constants.cs:42-49`）：OptiFine 用附加字段表示，Quilt 被识别为 Fabric（`McVersion.cs:140-142`）。
- 实例模型是 VB 的 `McInstance`（`ModMinecraft.vb:218-867`），但状态枚举 `McInstanceState`（`:220-231`）把"版本类型"和"加载器"揉成一个维度，分类靠 `Select Case` 顺序（`:635-663`），因此 Forge+OptiFine 会显示为 OptiFine、Fabric 优先于 Forge。

### 6.2 安装

- 入口 `McInstallStart`/`McModificationStart`（`Pages\PageDownload\ModDownloadLib.vb:1948-2062`），按 `McInstallRequest` 生成加载器链：原版、Forge/NeoForge、LiteLoader、Fabric、OptiFine（有加载器时作为 mod 安装）、合并 JSON、补库、写 manifest（`:1733-1944`）。
- 原版文件计划 `DlClientFix`（`Modules\Minecraft\ModDownload.vb:54-153`）：jar、资源索引、库、资源对象，权重分别为 16/27 等。
- 库解析 `McLibListGetWithJson`（`ModMinecraft.vb:1617-1725`）：处理 rules、natives、`${arch}`，按 `名称+是否 native` 去重并保留高版本；下载地址重写 `McLibNetFilesFromTokens`（`:1809-1862`）。
- 资源对象按哈希分桶 `assets\objects\ab\hash`，legacy 资源额外做 `virtual\legacy` 与 `map_to_resources`（`:1930-2082`）。
- 磁盘布局：`versions\{名称}\{名称}.json|.jar`、`{名称}-natives\`、`libraries\`、`assets\indexes|objects`、`runtime\{组件}\`，PCL 自身状态写在 `PCL\Setup.ini` 与 `PCL\config.json`。
- 修改已装实例用临时目录构建后 `McInstallCommit`（`ModDownloadLib.vb:2067-2111`）做 manifest 差量提交。

### 6.3 下载清单与下载源

- 清单解析全部在 C#（`PCLCS\Minecraft\Download\`）：客户端清单 `ClientListParser`（`ComputeDrops`/`ContainsVersion`/`NormalizeId`）、OptiFine `OptiFineListParser`（官方 HTML / BMCLAPI JSON）、Forge 版本 `ForgeVersionParser`、Forge 构建 `ForgeBuildParser`（官方 HTML / BMCLAPI JSON，含 1.7.10/1.8.9 分支修正）、NeoForge `NeoForgeParser`、LiteLoader `LiteLoaderParser`、Fabric `FabricListParser`。VB 只负责网络请求、读取设置与字段映射。
- 解析失败统一抛 `DownloadSourceException`（`PCLCS\Minecraft\Download\DownloadSourceException.cs`）：`Kind` 取 `DownloadFailureKind`（NotFound/InvalidPayload/TooFewEntries/MissingField/TargetVersionMissing/Timeout/Unexpected），`IsUnavailable` 只覆盖 NotFound 与 TargetVersionMissing。Forge 官方构建页的 404 由 `HttpRequestCodeException.StatusCode = HttpStatusCode.NotFound` 识别。
- 地址映射在 `DownloadSources`（`PCLCS\Minecraft\Download\DownloadSources.cs`）：`Order`/`Assets`/`Library`/`LauncherOrMeta`/`Mod`，与旧 VB 规则逐字一致——资源统一 https，modded 库只给 BMCLAPI 的 maven/libraries 两个候选，Mod 域名改为 `mod.mcimirror.top`。
- 版本列表与文件下载使用不同设置：`SourcePolicy.GetPlan(ToolDownloadVersion)` 返回 `SourcePlan(OfficialFirst, FirstSeconds, SecondSeconds)`，0 → 镜像先 30/90 s，1 → 官方先 5/35 s，其余 → 官方先 60/120 s；`PreferMojangForVersionList` 决定版本列表是否官方优先，`DlSourcePreferMojang` 决定文件下载是否官方优先。
- VB 侧 `DlSourceLoader`（`ModDownload.vb:999`）按计划顺序启动两个源竞速，先完成者胜出并取消另一个；全部失败时用 `IsUnavailableSourceError`（`:1073`）优先保留“源没有目标数据”类错误，再抛出原始异常。`DlSourceLoaderList`（`:1087`）是七个主加载器共用的列表构造器。
- 七个主加载器（`DlClientListMain`、`DlOptiFineListMain`、`DlForgeListMain`、`DlForgeVersionMain`、`DlNeoForgeListMain`、`DlLiteLoaderListMain`、`DlFabricListMain`）只做三件事：读 `ToolDownloadVersion` 得到 `SourcePlan`、调用 `DlSourceLoader`、把 C# 结果映射回 `Dl*Entry`。

### 6.4 启动

- `McLaunchStart`（`ModLaunch.vb:31-206`）组装加载器链：Java → 登录 → 补文件 → 参数 → natives → 预执行 → 自定义命令 → 启动 → 等窗口 → 结束，权重见 `:122-145`。
- JVM 参数 `McLaunchArgumentsJVM`（`:1221-1347`）：继承链合并、`-Xmx`、GC 策略、Authlib/Nide 注入、JLW、log4j 缓解参数、编码参数。
- 游戏参数 `McLaunchArgumentsGame`（`:1349-1422`）：新旧两种格式、OptiFineForgeTweaker 重排、2023-05-04 之后改用 `--quickPlayMultiplayer`。
- 占位符替换 `McLaunchArgumentsReplace`（`:1424-1497`）：`${auth_*}`, `${classpath}`, `${natives_directory}` 等；classpath 中 OptiFine 被强制放到倒数第二。
- 进程 `McLaunchRun`（`:1953-2001`）：启动 `java.exe`（注释说明 `javaw` 有问题 #6263），把 `appdata` 环境变量指向所选 MC 目录，工作目录为版本隔离目录，重定向 stdout/stderr。
- 监控 `Watcher`（`ModWatcher.vb:58-394`）：状态机 `Loading/Running/Crashed/Ended/Canceled`，用 `EnumWindows` 找游戏窗口，崩溃判定基于日志关键字。

### 6.5 Java

- `Java` 类型与探测在 `PCLCS\Java.cs:6-77`：执行 `java.exe -XshowSettings:properties -version`，15 s 超时，解析版本、位数与编码；候选目录约 20 个硬编码位置加 `JDK_HOME`/`JAVA_HOME`（`:136-168`），排序偏好 Java 21（`:124`）。
- 版本需求表 `GetJavaRequirement`（`Modules\Minecraft\ModJava.vb:128-273`）：按 MC 版本、日期与加载器分别收紧区间（如 Forge 34.0.0-36.2.25 要求 Java 8u320 以下）。
- 缺失时从 Mojang `java-runtime` 清单下载到 `.minecraft\runtime\{组件}`（`:379-382,545-551`）。

### 6.6 整合包与崩溃分析

- 五种格式：CurseForge `manifest.json`、Modrinth `modrinth.index.json`、HMCL `mcbbs.packmeta`、MultiMC `mmc-pack.json`、MCBBS；入口 `ModpackInstall`（`ModModpack.vb:31`），按压缩包内文件名嗅探（`:44-72`），overrides 复制到版本目录并写入 `VersionArgumentIndie`（`:126-143`）。
- 崩溃分析 `CrashAnalyzer`（`ModCrash.vb:1`）：收集最近 3 分钟的 crash-report 与 latest.log（`:21-85`），按文件类型截取头尾窗口（`:214-264`），再用约 60 条 `String.Contains` 规则匹配原因（`:474-584`），最后做 Mixin/模组名归因（`:589-670`）。规则即产品知识，行为由 `tests\PCLA.BehaviorTests` 的黄金语料锁定。

## 7. 测试

### 7.1 PCLCS 单元测试

`tests\PCLCS.Tests` 覆盖已经外迁到 C# 的类型：版本元数据、下载清单解析、下载源与 Java 探测。它们不依赖 VB 工程，直接用 `dotnet test` 运行。

### 7.2 行为黄金语料

- `tests\PCLA.BehaviorTests` 通过 `ProjectReference` 直连 `PCLA`，覆盖 `McInstance`、`CrashAnalyzer`、`Settings`。
- 测试进程内初始化临时目录；不启动 WPF、不访问网络、不写真实用户目录。
- `CrashAnalyzer.GetAnalyzeResult` 是唯一生产可见性接缝（`Friend` + `InternalsVisibleTo`）。
- 语料是合成数据；行为变化必须显式更新断言，不能靠放宽断言通过。
- 当前共 32 个测试：3 个宿主冒烟、10 个实例模型、8 个崩溃分析、11 个配置读写。
- 该 VB WPF 工程是非 SDK 形式的旧式项目，`dotnet build` 完全无法构建；行为测试必须走 `.\tools\test-behavior.ps1`（MSBuild 构建 + `vstest.console.exe` 运行）。

## 8. 关键路径速查

| 场景 | 入口 | 关键实现 |
| --- | --- | --- |
| 启动 | `Application.xaml.vb:7` | `FormMain.xaml.vb:284` → `:349` |
| 页面切换 | `FormMain.PageChange` `:1362` | `PageChangeActual` `:1413` → `PageChangeAnim` `:1513` |
| 安装版本 | `PageDownloadInstall.BtnStart_Click:1149` | `ModDownloadLib.McInstallStart:1948` |
| 启动游戏 | `ModLaunch.McLaunchStart:31` | `:106` → `McLaunchRun:2093` → `Watcher:58` |
| 下载文件 | `LoaderDownload.Start` `ModNet.vb:1396` | `NetManager:1639` → `NetFile:579` |
| 资源搜索 | `ResourceSearcher.Search:73` | CurseForge / Modrinth JSON |
| 微软登录 | `McLoginMsStart:542` | `MsLoginStep1New..6` `:882-1098` |
| 崩溃分析 | `CrashAnalyzer:1` | `AnalyzeCrit1..3` `:474-670` |
