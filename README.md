# Plain Craft Launcher (PCL) Alternative (PCLA)

PCLA 是 [Plain Craft Launcher 2](https://github.com/Meloong-Git/PCL)（PCL / PCL2）的第三方分支，由 [KurvCygnus](https://github.com/KurvCygnus) 基于其开源代码独立进行二次创作，与龙腾猫跃及 PCL 官方没有任何隶属关系，也不代表 PCL 官方立场。

## 免责声明

在使用、分发本项目的任何部分之前，请完整阅读本节。下载或运行即视为已理解并接受以下全部内容。

1. **本程序按“现状”提供。** 在适用法律允许的最大范围内，作者与贡献者不对本程序的适用性、可用性、准确性或功能完整性作任何明示或默示的担保，也不承诺持续维护、及时更新或修复任何缺陷。
2. **风险自担。** 因安装、运行、卸载本程序，或因信赖本程序输出的任何信息（包括但不限于崩溃分析结论、下载源、版本校验、配置迁移）而导致的任何直接或间接损失——包括但不限于游戏数据损坏、存档丢失、实例文件丢失、系统故障、数据泄露、账号争议、时间与带宽成本——由使用者本人承担。
3. **微软账号与第三方凭据。** 登录微软账号属于使用者与微软之间的行为，本程序只传递使用者显式发起的登录请求，不存储密码，也不对账号安全、封禁、限制或凭证失效承担任何责任。请自行评估将账号用于第三方工具的风险。
4. **第三方内容不背书。** 本程序检索、展示或下载的模组、整合包、光影、材质、服务端等资源均来自第三方网站与作者（CurseForge、Modrinth 及其他源）。这些内容的可用性、安全性、正确性与授权状态由其作者与平台负责，本程序不参与审核，也不为其背书。
5. **下载与网络服务可能失效。** 下载源地址、接口、镜像与更新机制可能随时变更、限流或失效。对此类失效，作者没有义务提供替代方案或补偿。
6. **非官方工具。** 本程序与 Mojang、Microsoft 没有任何关联，也不代表它们。Minecraft 是 Mojang Synergies AB 的商标，相关权利归其权利人所有；使用本程序仍须遵守 Minecraft 最终用户许可协议。
7. **自行分发须完整。** 任何再分发都应附带本声明、[LICENCE](LICENCE) 与对应源码链接；剥离声明或来源信息的再分发不受作者支持。
8. **问题反馈。** 软件缺陷请提交到本仓库 Issues；资源失效、账号或游戏相关的问题请优先联系对应平台或官方渠道，本仓库无法处理。

## 与上游的差异

目前与上游项目的区别是，提供了正常的游戏社区访问服务。

## 构建

需要 Windows 10 1809 及以上系统、MSBuild（Visual Studio 2022 Build Tools 或完整 Visual Studio，并勾选 .NET Framework 4.8 目标包），以及 .NET SDK（`PCLCS` 与 `MeloongCore` 使用 SDK 风格项目）。

```powershell
git clone --recurse-submodules https://github.com/KurvCygnus/PCLA.git
$msbuild = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild "PCLA.sln" -t:Restore -p:Configuration=Release
& $msbuild "PCLA.sln" -t:Rebuild -p:Configuration=Release -m:2
```

产物位于 `PCLA\bin\PCLA.exe`。

发布包未做代码签名，首次运行时 Windows SmartScreen 可能提示“未知发布者”；确认文件来自本仓库 Release 后，可选择“更多信息”→“仍要运行”。

## 凭据

微软 OAuth 的 Client ID 与 CurseForge API Key 不写入源码，也不在构建时注入，运行时从环境变量读取（读取位置：`PCLA\Modules\ModSecret.vb`）：

| 变量 | 用途 |
| --- | --- |
| `PCL_MS_CLIENT_ID` | 微软 OAuth 应用（客户端）ID，公开客户端，使用设备代码流 |
| `PCL_CURSEFORGE_API_KEY` | CurseForge API Key |

## 许可证

- `PCLA\` 目录继续遵循 [LICENCE](LICENCE)（《PCL 分发有限许可》与《PCL 存储库合理使用指南》）
- `MeloongCore\` 为上游子模块，遵循其自身许可证

## 致谢

- 龙腾猫跃（LTCat）— Plain Craft Launcher 原作者，[赞助链接](https://meloong.com/afd/a/LTCat)
- PCL 与 MeloongCore 的所有贡献者
