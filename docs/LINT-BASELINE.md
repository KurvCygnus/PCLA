# Lint 基线（2026-10-22）

## 范围与方法

- 覆盖全解决方案的手写源码：`PCLCS`、`tests\PCLCS.Tests`、`Plain Craft Launcher 2` 与 `MeloongCore` 子模块，共 187 个文件（75 个 .cs、112 个 .vb），不含 `obj/`、`bin/` 与生成文件。
- 工具：Rider MCP `lint_files`（ReSharper 检查），`min_severity=warning`，逐文件统计 error 与 warning。
- 状态含义：`已修` = 本计划修复了该文件的 lint error；`已修 / 保留` = error 已修、剩余 warning 保留；`保留` = 既有警告，本次不处理；`不适用` = 子模块既有问题，不在本仓库改动范围。
- 仅列出存在问题的文件；其余 25 个文件为 0 error / 0 warning。

## 汇总

| 范围 | 文件数 | error | warning |
| --- | ---: | ---: | ---: |
| PCLCS | 18 | 0 | 102 |
| tests\PCLCS.Tests | 9 | 0 | 64 |
| Plain Craft Launcher 2 | 108 | 0 | 4514 |
| MeloongCore（子模块） | 27 | 2 | 116 |
| 合计 | 162 | 2 | 4796 |

未修错误（均为 MeloongCore 子模块既有问题）：

- `MeloongCore\Shared\Utils\FileUtils.cs:554`：形参 'method' 具有相同的默认值
- `MeloongCore\Shared\General\Worker\RedoableWorker.cs:136`：形参 'behavior' 具有相同的默认值

## 类别分布

| 类别 | 数量 |
| --- | ---: |
| 命名规则 | 4187 |
| 可简化/现代化 | 315 |
| 其他 | 211 |
| 未使用/冗余 | 68 |
| 文档注释 | 17 |

`命名规则` 主要是 VB 项目长期使用的 PascalCase 局部变量/参数风格与 Rider 默认规则不一致，属于全项目既有风格，不在本计划范围内统一整改。

## 本次改动文件

| 文件 | error | warning | 主要类别 | 状态 |
| --- | ---: | ---: | --- | --- |
| `PCLCS\Minecraft\Download\ForgeBuildParser.cs` | 0 | 3 | 未使用/冗余(3) | 保留 |
| `PCLCS\Minecraft\Launch\VersionArguments.cs` | 0 | 8 | 文档注释(4) / 可简化/现代化(4) | 已修 / 保留 |
| `Plain Craft Launcher 2\FormMain.xaml.vb` | 0 | 87 | 命名规则(71) / 可简化/现代化(11) | 保留 |
| `Plain Craft Launcher 2\Modules\Minecraft\ModDownload.vb` | 0 | 139 | 命名规则(137) / 未使用/冗余(2) | 保留 |

## 全文件明细

| 文件 | error | warning | 主要类别 | 状态 |
| --- | ---: | ---: | --- | --- |
| `MeloongCore\Shared\Extensions\EnumerableExtensions.cs` | 0 | 5 | 其他(3) / 可简化/现代化(2) | 不适用 |
| `MeloongCore\Shared\Extensions\ExceptionExtensions.cs` | 0 | 3 | 可简化/现代化(2) / 未使用/冗余(1) | 不适用 |
| `MeloongCore\Shared\Extensions\StringExtensions.cs` | 0 | 3 | 可简化/现代化(2) / 其他(1) | 不适用 |
| `MeloongCore\Shared\General\ConcurrentList.cs` | 0 | 1 | 命名规则(1) | 不适用 |
| `MeloongCore\Shared\General\Config.cs` | 0 | 3 | 命名规则(1) / 未使用/冗余(1) | 不适用 |
| `MeloongCore\Shared\General\Logger.cs` | 0 | 4 | 其他(2) / 命名规则(1) | 不适用 |
| `MeloongCore\Shared\General\MsDelta.cs` | 0 | 1 | 命名规则(1) | 不适用 |
| `MeloongCore\Shared\General\OneOf.cs` | 0 | 1 | 命名规则(1) | 不适用 |
| `MeloongCore\Shared\General\Progress.cs` | 0 | 8 | 可简化/现代化(5) / 其他(1) | 不适用 |
| `MeloongCore\Shared\General\ValueRange.cs` | 0 | 1 | 命名规则(1) | 不适用 |
| `MeloongCore\Shared\General\Worker\RateLimitedWorker.cs` | 0 | 2 | 未使用/冗余(1) / 命名规则(1) | 不适用 |
| `MeloongCore\Shared\General\Worker\RedoableWorker.cs` | 1 | 2 | 其他(2) / 命名规则(1) | 不适用 |
| `MeloongCore\Shared\Utils\CryptographyUtils.cs` | 0 | 1 | 命名规则(1) | 不适用 |
| `MeloongCore\Shared\Utils\DirectoryUtils.cs` | 0 | 7 | 可简化/现代化(6) / 命名规则(1) | 不适用 |
| `MeloongCore\Shared\Utils\EnumUtils.cs` | 0 | 1 | 命名规则(1) | 不适用 |
| `MeloongCore\Shared\Utils\FileUtils.cs` | 1 | 38 | 可简化/现代化(33) / 文档注释(4) | 不适用 |
| `MeloongCore\Shared\Utils\GeneralUtils.cs` | 0 | 1 | 命名规则(1) | 不适用 |
| `MeloongCore\Shared\Utils\MathUtils.cs` | 0 | 3 | 文档注释(2) / 命名规则(1) | 不适用 |
| `MeloongCore\Shared\Utils\PathUtils.cs` | 0 | 3 | 其他(1) / 命名规则(1) | 不适用 |
| `MeloongCore\Shared\Utils\RegistryUtils.cs` | 0 | 1 | 命名规则(1) | 不适用 |
| `MeloongCore\Shared\Utils\StringUtils.cs` | 0 | 4 | 其他(2) / 命名规则(1) | 不适用 |
| `MeloongCore\Shared\Utils\TaskUtils.cs` | 0 | 7 | 其他(4) / 可简化/现代化(2) | 不适用 |
| `MeloongCore\Shared\Utils\WebUtils.cs` | 0 | 1 | 命名规则(1) | 不适用 |
| `MeloongCore\Wpf\Controls\ModernTooltipService.cs` | 0 | 4 | 可简化/现代化(3) / 命名规则(1) | 不适用 |
| `MeloongCore\Wpf\General\Dialogs.cs` | 0 | 6 | 可简化/现代化(5) / 命名规则(1) | 不适用 |
| `MeloongCore\Wpf\General\ValueConverters.cs` | 0 | 4 | 其他(3) / 命名规则(1) | 不适用 |
| `MeloongCore\Wpf\Utils\WindowsUtils.cs` | 0 | 1 | 命名规则(1) | 不适用 |
| `PCLCS\Configs.cs` | 0 | 1 | 可简化/现代化(1) | 保留 |
| `PCLCS\Java.cs` | 0 | 29 | 可简化/现代化(21) / 其他(5) | 保留 |
| `PCLCS\Launch\LaunchUtils.cs` | 0 | 3 | 其他(1) / 文档注释(1) | 保留 |
| `PCLCS\Minecraft\Download\ClientListParser.cs` | 0 | 7 | 可简化/现代化(5) / 未使用/冗余(2) | 保留 |
| `PCLCS\Minecraft\Download\FabricListParser.cs` | 0 | 2 | 未使用/冗余(2) | 保留 |
| `PCLCS\Minecraft\Download\ForgeBuildParser.cs` | 0 | 3 | 未使用/冗余(3) | 已修 / 保留 |
| `PCLCS\Minecraft\Download\ForgeVersionParser.cs` | 0 | 4 | 未使用/冗余(2) / 可简化/现代化(2) | 保留 |
| `PCLCS\Minecraft\Download\LiteLoaderParser.cs` | 0 | 2 | 未使用/冗余(2) | 保留 |
| `PCLCS\Minecraft\Download\NeoForgeParser.cs` | 0 | 4 | 可简化/现代化(3) / 未使用/冗余(1) | 保留 |
| `PCLCS\Minecraft\Download\OptiFineListParser.cs` | 0 | 11 | 文档注释(6) / 可简化/现代化(3) | 保留 |
| `PCLCS\Minecraft\Download\SourcePolicy.cs` | 0 | 1 | 其他(1) | 保留 |
| `PCLCS\Minecraft\Launch\VersionArgumentRules.cs` | 0 | 1 | 可简化/现代化(1) | 保留 |
| `PCLCS\Minecraft\Launch\VersionArguments.cs` | 0 | 8 | 文档注释(4) / 可简化/现代化(4) | 已修 / 保留 |
| `PCLCS\Minecraft\McInstallManifest.cs` | 0 | 4 | 可简化/现代化(3) / 命名规则(1) | 保留 |
| `PCLCS\Minecraft\MCUtils.cs` | 0 | 1 | 命名规则(1) | 保留 |
| `PCLCS\Minecraft\McVersion.cs` | 0 | 16 | 可简化/现代化(10) / 其他(5) | 保留 |
| `PCLCS\Resource\Resource.cs` | 0 | 3 | 其他(2) / 命名规则(1) | 保留 |
| `PCLCS\Resource\WikiEntry.cs` | 0 | 2 | 可简化/现代化(1) / 命名规则(1) | 保留 |
| `Plain Craft Launcher 2\Application.xaml.vb` | 0 | 29 | 命名规则(21) / 其他(3) | 保留 |
| `Plain Craft Launcher 2\Controls\Behaviors\ClipboardInterceptor.vb` | 0 | 2 | 命名规则(2) | 保留 |
| `Plain Craft Launcher 2\Controls\Behaviors\Interfaces.vb` | 0 | 1 | 命名规则(1) | 保留 |
| `Plain Craft Launcher 2\Controls\Behaviors\LazyLoadBehavior.vb` | 0 | 5 | 命名规则(5) | 保留 |
| `Plain Craft Launcher 2\Controls\MyButton.xaml.vb` | 0 | 3 | 命名规则(3) | 保留 |
| `Plain Craft Launcher 2\Controls\MyCard.vb` | 0 | 36 | 命名规则(35) / 未使用/冗余(1) | 保留 |
| `Plain Craft Launcher 2\Controls\MyCheckBox.xaml.vb` | 0 | 5 | 命名规则(5) | 保留 |
| `Plain Craft Launcher 2\Controls\MyComboBox.vb` | 0 | 13 | 命名规则(13) | 保留 |
| `Plain Craft Launcher 2\Controls\MyComboBoxItem.vb` | 0 | 7 | 命名规则(7) | 保留 |
| `Plain Craft Launcher 2\Controls\MyDropShadow.vb` | 0 | 7 | 可简化/现代化(5) / 命名规则(2) | 保留 |
| `Plain Craft Launcher 2\Controls\MyExtraButton.xaml.vb` | 0 | 9 | 命名规则(9) | 保留 |
| `Plain Craft Launcher 2\Controls\MyExtraTextButton.xaml.vb` | 0 | 5 | 命名规则(5) | 保留 |
| `Plain Craft Launcher 2\Controls\MyHint.xaml.vb` | 0 | 7 | 命名规则(7) | 保留 |
| `Plain Craft Launcher 2\Controls\MyIconButton.xaml.vb` | 0 | 8 | 命名规则(8) | 保留 |
| `Plain Craft Launcher 2\Controls\MyIconTextButton.xaml.vb` | 0 | 5 | 命名规则(3) / 其他(2) | 保留 |
| `Plain Craft Launcher 2\Controls\MyImage.vb` | 0 | 11 | 命名规则(10) / 可简化/现代化(1) | 保留 |
| `Plain Craft Launcher 2\Controls\MyListItem.xaml.vb` | 0 | 34 | 命名规则(31) / 其他(3) | 保留 |
| `Plain Craft Launcher 2\Controls\MyLoading.xaml.vb` | 0 | 28 | 命名规则(25) / 其他(2) | 保留 |
| `Plain Craft Launcher 2\Controls\MyMenuItem.vb` | 0 | 7 | 命名规则(6) / 可简化/现代化(1) | 保留 |
| `Plain Craft Launcher 2\Controls\MyMsg\MyMsgInput.xaml.vb` | 0 | 4 | 命名规则(4) | 保留 |
| `Plain Craft Launcher 2\Controls\MyMsg\MyMsgSelect.xaml.vb` | 0 | 6 | 命名规则(6) | 保留 |
| `Plain Craft Launcher 2\Controls\MyMsg\MyMsgText.xaml.vb` | 0 | 4 | 命名规则(4) | 保留 |
| `Plain Craft Launcher 2\Controls\MyPageLeft.vb` | 0 | 19 | 命名规则(16) / 其他(2) | 保留 |
| `Plain Craft Launcher 2\Controls\MyPageRight.vb` | 0 | 73 | 命名规则(58) / 可简化/现代化(8) | 保留 |
| `Plain Craft Launcher 2\Controls\MyRadioBox.xaml.vb` | 0 | 12 | 命名规则(11) / 未使用/冗余(1) | 保留 |
| `Plain Craft Launcher 2\Controls\MyRadioButton.xaml.vb` | 0 | 11 | 命名规则(11) | 保留 |
| `Plain Craft Launcher 2\Controls\MyResizer.vb` | 0 | 64 | 命名规则(50) / 其他(6) | 保留 |
| `Plain Craft Launcher 2\Controls\MyScrollBar.vb` | 0 | 4 | 命名规则(4) | 保留 |
| `Plain Craft Launcher 2\Controls\MyScrollViewer.vb` | 0 | 7 | 命名规则(5) / 可简化/现代化(2) | 保留 |
| `Plain Craft Launcher 2\Controls\MySearchBox.xaml.vb` | 0 | 2 | 可简化/现代化(1) / 命名规则(1) | 保留 |
| `Plain Craft Launcher 2\Controls\MySlider.xaml.vb` | 0 | 16 | 命名规则(15) / 可简化/现代化(1) | 保留 |
| `Plain Craft Launcher 2\Controls\MyTextBox.vb` | 0 | 19 | 命名规则(16) / 可简化/现代化(2) | 保留 |
| `Plain Craft Launcher 2\Controls\MyTextButton.vb` | 0 | 4 | 命名规则(4) | 保留 |
| `Plain Craft Launcher 2\Controls\MyVirtualizingElement.vb` | 0 | 13 | 命名规则(13) | 保留 |
| `Plain Craft Launcher 2\FormMain.xaml.vb` | 0 | 87 | 命名规则(71) / 可简化/现代化(11) | 已修 / 保留 |
| `Plain Craft Launcher 2\Modules\Base\ModAnimation.vb` | 0 | 209 | 命名规则(209) | 保留 |
| `Plain Craft Launcher 2\Modules\Base\ModBase.vb` | 0 | 130 | 命名规则(126) / 其他(4) | 保留 |
| `Plain Craft Launcher 2\Modules\Base\ModLoader.vb` | 0 | 106 | 命名规则(98) / 可简化/现代化(8) | 保留 |
| `Plain Craft Launcher 2\Modules\Base\ModNet.vb` | 0 | 275 | 命名规则(254) / 其他(13) | 保留 |
| `Plain Craft Launcher 2\Modules\Base\ModValidate.vb` | 0 | 74 | 命名规则(74) | 保留 |
| `Plain Craft Launcher 2\Modules\Base\MyBitmap.vb` | 0 | 32 | 命名规则(31) / 其他(1) | 保留 |
| `Plain Craft Launcher 2\Modules\Base\PclLogger.vb` | 0 | 12 | 命名规则(12) | 保留 |
| `Plain Craft Launcher 2\Modules\Minecraft\ModCrash.vb` | 0 | 123 | 命名规则(117) / 其他(4) | 保留 |
| `Plain Craft Launcher 2\Modules\Minecraft\ModDownload.vb` | 0 | 139 | 命名规则(137) / 未使用/冗余(2) | 已修 / 保留 |
| `Plain Craft Launcher 2\Modules\Minecraft\ModJava.vb` | 0 | 63 | 命名规则(63) | 保留 |
| `Plain Craft Launcher 2\Modules\Minecraft\ModLaunch.vb` | 0 | 204 | 命名规则(199) / 未使用/冗余(3) | 保留 |
| `Plain Craft Launcher 2\Modules\Minecraft\ModMinecraft.vb` | 0 | 243 | 命名规则(236) / 可简化/现代化(3) | 保留 |
| `Plain Craft Launcher 2\Modules\Minecraft\ModModpack.vb` | 0 | 175 | 命名规则(171) / 其他(2) | 保留 |
| `Plain Craft Launcher 2\Modules\Minecraft\ModWatcher.vb` | 0 | 33 | 命名规则(32) / 未使用/冗余(1) | 保留 |
| `Plain Craft Launcher 2\Modules\ModDevelop.vb` | 0 | 1 | 命名规则(1) | 保留 |
| `Plain Craft Launcher 2\Modules\ModEvent.vb` | 0 | 26 | 命名规则(26) | 保留 |
| `Plain Craft Launcher 2\Modules\ModMain.vb` | 0 | 110 | 命名规则(104) / 其他(4) | 保留 |
| `Plain Craft Launcher 2\Modules\ModMusic.vb` | 0 | 18 | 命名规则(18) | 保留 |
| `Plain Craft Launcher 2\Modules\ModSecret.vb` | 0 | 22 | 命名规则(22) | 保留 |
| `Plain Craft Launcher 2\Modules\Resource\LocalResourceFile.vb` | 0 | 17 | 命名规则(17) | 保留 |
| `Plain Craft Launcher 2\Modules\Resource\LocalResourceLoaders.vb` | 0 | 70 | 命名规则(59) / 其他(11) | 保留 |
| `Plain Craft Launcher 2\Modules\Resource\ResourceProject.vb` | 0 | 45 | 命名规则(44) / 未使用/冗余(1) | 保留 |
| `Plain Craft Launcher 2\Modules\Resource\ResourceSearcher.vb` | 0 | 85 | 命名规则(66) / 其他(16) | 保留 |
| `Plain Craft Launcher 2\Modules\Resource\ResourceVersion.vb` | 0 | 39 | 命名规则(39) | 保留 |
| `Plain Craft Launcher 2\Modules\ThirdParty\DragHelper.vb` | 0 | 14 | 命名规则(11) / 其他(2) | 保留 |
| `Plain Craft Launcher 2\My Project\MyExtensions\MyWpfExtension.vb` | 0 | 9 | 命名规则(6) / 其他(3) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\ModDownloadLib.vb` | 0 | 364 | 命名规则(349) / 其他(13) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\PageDownloadInstall.xaml.vb` | 0 | 73 | 命名规则(67) / 可简化/现代化(4) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\PageDownloadLeft.xaml.vb` | 0 | 6 | 命名规则(6) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\PageDownloadModify.vb` | 0 | 128 | 命名规则(110) / 其他(16) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\Resource\MyResourceItem.xaml.vb` | 0 | 18 | 命名规则(16) / 可简化/现代化(2) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\Resource\PageDownloadCompDetail.xaml.vb` | 0 | 89 | 命名规则(83) / 可简化/现代化(3) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\Resource\PageDownloadDataPack.xaml.vb` | 0 | 1 | 命名规则(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\Resource\PageDownloadMod.xaml.vb` | 0 | 1 | 命名规则(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\Resource\PageDownloadPack.xaml.vb` | 0 | 1 | 命名规则(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\Resource\PageDownloadResourcePack.xaml.vb` | 0 | 1 | 命名规则(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\Resource\PageDownloadShader.xaml.vb` | 0 | 1 | 命名规则(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageDownload\Resource\PageResource.xaml.vb` | 0 | 21 | 命名规则(19) / 可简化/现代化(2) | 保留 |
| `Plain Craft Launcher 2\Pages\PageInstance\MyLocalModItem.xaml.vb` | 0 | 48 | 命名规则(43) / 其他(4) | 保留 |
| `Plain Craft Launcher 2\Pages\PageInstance\PageInstanceExport.xaml.vb` | 0 | 97 | 命名规则(88) / 其他(6) | 保留 |
| `Plain Craft Launcher 2\Pages\PageInstance\PageInstanceLeft.xaml.vb` | 0 | 7 | 命名规则(6) / 可简化/现代化(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageInstance\PageInstanceMod.xaml.vb` | 0 | 91 | 命名规则(88) / 可简化/现代化(2) | 保留 |
| `Plain Craft Launcher 2\Pages\PageInstance\PageInstanceModDisabled.xaml.vb` | 0 | 4 | 可简化/现代化(2) / 命名规则(2) | 保留 |
| `Plain Craft Launcher 2\Pages\PageInstance\PageInstanceOverall.xaml.vb` | 0 | 31 | 命名规则(24) / 可简化/现代化(7) | 保留 |
| `Plain Craft Launcher 2\Pages\PageInstance\PageInstanceSetup.xaml.vb` | 0 | 62 | 命名规则(54) / 可简化/现代化(4) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLaunch\MyMsgLogin.xaml.vb` | 0 | 13 | 命名规则(13) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLaunch\MySkin.xaml.vb` | 0 | 21 | 命名规则(20) / 未使用/冗余(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLaunch\PageLaunchLeft.xaml.vb` | 0 | 53 | 命名规则(49) / 可简化/现代化(3) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLaunch\PageLaunchRight.xaml.vb` | 0 | 20 | 命名规则(18) / 其他(2) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLaunch\PageLoginAuth.xaml.vb` | 0 | 10 | 命名规则(8) / 可简化/现代化(2) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLaunch\PageLoginAuthSkin.xaml.vb` | 0 | 6 | 命名规则(5) / 可简化/现代化(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLaunch\PageLoginLegacy.xaml.vb` | 0 | 7 | 命名规则(6) / 可简化/现代化(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLaunch\PageLoginMs.xaml.vb` | 0 | 9 | 命名规则(8) / 未使用/冗余(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLaunch\PageLoginMsSkin.xaml.vb` | 0 | 13 | 命名规则(10) / 可简化/现代化(2) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLaunch\PageLoginNide.xaml.vb` | 0 | 8 | 命名规则(6) / 可简化/现代化(2) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLaunch\PageLoginNideSkin.xaml.vb` | 0 | 4 | 命名规则(3) / 可简化/现代化(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageLink\PageLinkMain.xaml.vb` | 0 | 137 | 命名规则(114) / 其他(17) | 保留 |
| `Plain Craft Launcher 2\Pages\PageOther\PageOtherAbout.xaml.vb` | 0 | 6 | 可简化/现代化(3) / 命名规则(2) | 保留 |
| `Plain Craft Launcher 2\Pages\PageOther\PageOtherHelp.xaml.vb` | 0 | 25 | 命名规则(20) / 其他(5) | 保留 |
| `Plain Craft Launcher 2\Pages\PageOther\PageOtherHelpDetail.xaml.vb` | 0 | 4 | 命名规则(3) / 可简化/现代化(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageOther\PageOtherLeft.xaml.vb` | 0 | 8 | 命名规则(8) | 保留 |
| `Plain Craft Launcher 2\Pages\PageOther\PageOtherTest.xaml.vb` | 0 | 6 | 命名规则(6) | 保留 |
| `Plain Craft Launcher 2\Pages\PageSelectLeft.xaml.vb` | 0 | 42 | 命名规则(41) / 其他(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageSelectRight.xaml.vb` | 0 | 36 | 命名规则(33) / 可简化/现代化(2) | 保留 |
| `Plain Craft Launcher 2\Pages\PageSetup\PageSetupLaunch.xaml.vb` | 0 | 66 | 命名规则(58) / 可简化/现代化(8) | 保留 |
| `Plain Craft Launcher 2\Pages\PageSetup\PageSetupLeft.xaml.vb` | 0 | 8 | 命名规则(8) | 保留 |
| `Plain Craft Launcher 2\Pages\PageSetup\PageSetupLink.xaml.vb` | 0 | 3 | 命名规则(2) / 可简化/现代化(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageSetup\PageSetupSystem.xaml.vb` | 0 | 11 | 可简化/现代化(7) / 命名规则(3) | 保留 |
| `Plain Craft Launcher 2\Pages\PageSetup\PageSetupUI.xaml.vb` | 0 | 39 | 命名规则(23) / 可简化/现代化(15) | 保留 |
| `Plain Craft Launcher 2\Pages\PageSetup\Settings.vb` | 0 | 45 | 命名规则(43) / 可简化/现代化(2) | 保留 |
| `Plain Craft Launcher 2\Pages\PageSetup\SettingService.vb` | 0 | 9 | 命名规则(9) | 保留 |
| `Plain Craft Launcher 2\Pages\PageSpeedLeft.xaml.vb` | 0 | 19 | 命名规则(18) / 可简化/现代化(1) | 保留 |
| `Plain Craft Launcher 2\Pages\PageSpeedRight.xaml.vb` | 0 | 1 | 命名规则(1) | 保留 |
| `tests\PCLCS.Tests\ClientListParserTests.cs` | 0 | 7 | 可简化/现代化(4) / 未使用/冗余(3) | 保留 |
| `tests\PCLCS.Tests\DownloadSourcesTests.cs` | 0 | 2 | 可简化/现代化(2) | 保留 |
| `tests\PCLCS.Tests\FabricListParserTests.cs` | 0 | 3 | 可简化/现代化(2) / 未使用/冗余(1) | 保留 |
| `tests\PCLCS.Tests\ForgeBuildParserTests.cs` | 0 | 12 | 其他(7) / 可简化/现代化(5) | 保留 |
| `tests\PCLCS.Tests\ForgeVersionParserTests.cs` | 0 | 3 | 可简化/现代化(2) / 其他(1) | 保留 |
| `tests\PCLCS.Tests\LiteLoaderParserTests.cs` | 0 | 6 | 可简化/现代化(6) | 保留 |
| `tests\PCLCS.Tests\NeoForgeParserTests.cs` | 0 | 14 | 可简化/现代化(13) / 其他(1) | 保留 |
| `tests\PCLCS.Tests\OptiFineListParserTests.cs` | 0 | 6 | 可简化/现代化(5) / 其他(1) | 保留 |
| `tests\PCLCS.Tests\VersionArgumentsTests.cs` | 0 | 11 | 可简化/现代化(11) | 保留 |

## 结论

- 本次改动的 4 个文件 error 均为 0；`VersionArguments.cs` 的 `JTokenType` switch 缺 `default` 错误已修复。
- 全解决方案剩余 2 条 error 全部位于 `MeloongCore` 子模块，为既有问题，本次不动。
- 其余 warning 以命名规则与现代化建议为主，属既有代码风格，保留并在 `docs\TECHDEBT.md` 之外单独记录于此，避免后续任务重复排查。
