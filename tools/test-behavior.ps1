param(
    [string]$Configuration = 'Release',
    [string]$PclaChannel = 'snapshot',
    [string]$PclaCommit = 'local',
    [string]$TestCaseFilter = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'tests\PCLA.BehaviorTests\PCLA.BehaviorTests.csproj'

function Find-MSBuild {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe'
        if ($LASTEXITCODE -eq 0 -and $found) { return @($found)[0] }
    }

    $programFilesX86 = ${env:ProgramFiles(x86)}
    $programFiles = $env:ProgramFiles
    $candidates = @(
        (Join-Path $programFilesX86 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $programFiles 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $programFiles 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $programFiles 'Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $programFiles 'Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $programFiles 'Microsoft Visual Studio\2022\Preview\MSBuild\Current\Bin\MSBuild.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

function Find-VSTest {
    param([string]$MSBuildPath)

    $command = Get-Command vstest.console.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }

    # MSBuild 位于 <VS>\MSBuild\Current\Bin，测试平台在 <VS>\Common7\IDE 下。
    if ($MSBuildPath) {
        $vsRoot = Split-Path (Split-Path (Split-Path (Split-Path $MSBuildPath -Parent) -Parent) -Parent) -Parent
        $nearby = Join-Path $vsRoot 'Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe'
        if (Test-Path $nearby) { return $nearby }
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -find 'Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe'
        if ($LASTEXITCODE -eq 0 -and $found) { return @($found)[0] }
    }

    foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        $vs2022 = Join-Path $base 'Microsoft Visual Studio\2022'
        if (-not (Test-Path $vs2022)) { continue }
        $found = Get-ChildItem $vs2022 -Recurse -Filter vstest.console.exe -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
        if ($found) { return $found }
    }
    return $null
}

if (-not (Test-Path $project)) {
    throw "找不到行为测试项目：$project"
}

$msbuild = Find-MSBuild
if (-not $msbuild) {
    throw '找不到 MSBuild。请安装 Visual Studio 2022 Build Tools 或完整 Visual Studio，并勾选 .NET Framework 4.8 目标包。'
}

$vstest = Find-VSTest -MSBuildPath $msbuild
if (-not $vstest) {
    throw '找不到 vstest.console.exe。请确认 Visual Studio 安装了测试工具。'
}

Write-Host "MSBuild：$msbuild"
Write-Host "vstest：$vstest"

# 新检出的仓库没有 obj/project.assets.json，-restore 保证 Build 前完成 NuGet 还原。
# 测试项目引用 PCLA.vbproj，必须一并转发版本属性，否则会以默认 snapshot/local 重编 PCLA。
& $msbuild $project -t:Build -restore -p:Configuration=$Configuration -p:PclaChannel=$PclaChannel -p:PclaCommit=$PclaCommit -m:2
$buildExit = $LASTEXITCODE
if ($null -eq $buildExit) { $buildExit = 1 }
if ($buildExit -ne 0) {
    Write-Host "行为测试项目构建失败（退出码 $buildExit）。"
    exit $buildExit
}

$dll = Join-Path $root "tests\PCLA.BehaviorTests\bin\$Configuration\net48\PCLA.BehaviorTests.dll"
if (-not (Test-Path $dll)) {
    throw "构建完成但找不到测试程序集：$dll"
}

$vstestArgs = @($dll)
if (-not [string]::IsNullOrWhiteSpace($TestCaseFilter)) {
    $vstestArgs += "--TestCaseFilter:$TestCaseFilter"
}
& $vstest @vstestArgs
$testExit = $LASTEXITCODE
if ($null -eq $testExit) { $testExit = 1 }
exit $testExit
