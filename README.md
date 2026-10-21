# Plain Craft Launcher Alternative (PCLA)

PCLA 是 [Plain Craft Launcher 2](https://github.com/Meloong-Git/PCL)（PCL / PCL2）的第三方分支，由 [KurvCygnus](https://github.com/KurvCygnus) 基于其开源代码独立进行二次创作，与龙腾猫跃及 PCL 官方没有任何隶属关系，也不代表 PCL 官方立场。

## 与上游的差异

目前与上游项目的区别是，提供了正常的游戏社区访问服务。

## 构建

需要 Windows 10 1809 及以上系统、MSBuild（Visual Studio 2022 Build Tools 或完整 Visual Studio，并勾选 .NET Framework 4.8 目标包），以及 .NET SDK（`PCLCS` 与 `MeloongCore` 使用 SDK 风格项目）。

```powershell
git clone --recurse-submodules https://github.com/KurvCygnus/PCLA.git
$msbuild = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild "Plain Craft Launcher 2.sln" -t:Restore -p:Configuration=Release
& $msbuild "Plain Craft Launcher 2.sln" -t:Rebuild -p:Configuration=Release -m:2
```

产物位于 `Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe`。

## 凭据

微软 OAuth 的 Client ID 与 CurseForge API Key 不写入源码，构建后通过环境变量提供（读取位置：`Plain Craft Launcher 2\Modules\ModSecret.vb`）：

| 变量 | 用途 |
| --- | --- |
| `PCL_MS_CLIENT_ID` | 微软 OAuth 应用（客户端）ID，公开客户端，使用设备代码流 |
| `PCL_CURSEFORGE_API_KEY` | CurseForge API Key |

## 许可证

- `Plain Craft Launcher 2\` 目录继续遵循 [LICENCE](LICENCE)（《PCL 分发有限许可》与《PCL 存储库合理使用指南》）
- `MeloongCore\` 为上游子模块，遵循其自身许可证

## 致谢

- 龙腾猫跃（LTCat）— Plain Craft Launcher 原作者，[赞助链接](https://meloong.com/afd/a/LTCat)
- PCL 与 MeloongCore 的所有贡献者
