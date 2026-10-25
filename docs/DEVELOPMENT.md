# 开发

## 环境

- Windows 10 1809 及以上。
- MSBuild：Visual Studio 2022 Build Tools 或完整 Visual Studio，并勾选 .NET Framework 4.8 目标包。
- .NET SDK：`PCLCS` 与 `MeloongCore` 使用 SDK 风格项目。

## 构建

```powershell
$msbuild = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild "PCLA.sln" -t:Restore -p:Configuration=Release
& $msbuild "PCLA.sln" -t:Rebuild -p:Configuration=Release -m:2
```

产物位于 `PCLA\bin\PCLA.exe`。

## 测试

### PCLCS 单元测试

```powershell
dotnet test tests\PCLCS.Tests\PCLCS.Tests.csproj -c Release
```

### 行为黄金语料

行为测试直连 VB 的 `PCLA`，必须用 MSBuild 构建测试项目，再用 `vstest.console.exe` 运行：

```powershell
.\tools\test-behavior.ps1
```

脚本会自动查找 MSBuild 与 `vstest.console.exe`，构建 `tests\PCLA.BehaviorTests\PCLA.BehaviorTests.csproj`，运行发现到的全部测试，并把测试结果作为退出码返回。

当前共 32 个测试：3 个宿主冒烟、10 个实例模型、8 个崩溃分析、11 个配置读写；其中 29 个是黄金语料用例。

默认使用 Release 配置，可用 `-Configuration` 切换。只跑一部分测试时传 `-TestCaseFilter`：

```powershell
.\tools\test-behavior.ps1 -TestCaseFilter 'FullyQualifiedName~CrashAnalyzerTests'
```

不要用 `dotnet test` 直接构建 `PCLA.BehaviorTests`：`dotnet build` 不支持该 VB WPF 项目的 XAML 构建。

### MeloongCore 测试

`MeloongCore.Tests` 不接入 CI（TUnit 在 .NET 10 下不可用，且存在既有失败）。本地运行：

```powershell
MeloongCore\Tests\bin\Release\net48\MeloongCore.Tests.exe
```
