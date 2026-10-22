#Requires -Version 7.0
<#
.SYNOPSIS
    启动 PCL，导航到下载页，校验分类切换与版本列表，然后截图并清理进程。
.DESCRIPTION
    依赖 winapp CLI。运行前会通过同意门请求桌面操作授权。
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe'),
    [string]$ConsentGate = (Join-Path $env:USERPROFILE '.config\opencode\tools\Show-ConsentGate.ps1'),
    [int]$TimeoutSec = 60
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ConsentGate -PathType Leaf)) {
    Write-Host "未安装同意门：$ConsentGate"
    exit 1
}

& $ConsentGate -Message '将启动 PCL 并通过 UI Automation 点击导航与下载分类，用于冒烟测试。' -DrivesInput
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Consent not granted; aborting.'
    exit 1
}

$exePath = (Resolve-Path -LiteralPath $Exe).Path
$artifactDir = Join-Path $PSScriptRoot '..\artifacts\ui-smoke'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$screenshot = Join-Path $artifactDir "$stamp.png"
$inspectFile = Join-Path $artifactDir "$stamp.inspect.json"
$diagnosticsFile = Join-Path $artifactDir "$stamp.diagnostics.txt"
$process = $null
$lastSelector = 'Nav.Launch'

function Invoke-Winapp {
    param([string[]]$Arguments, [string]$Selector, [switch]$EmptyOnNoMatch)
    if ($Selector) { $script:lastSelector = $Selector }
    $stderrFile = [System.IO.Path]::GetTempFileName()
    try {
        $stdout = & winapp @Arguments 2>$stderrFile
        $exitCode = $LASTEXITCODE
        $stderrText = Get-Content -LiteralPath $stderrFile -Raw -ErrorAction SilentlyContinue
        if ($exitCode -ne 0) {
            # winapp ui search 在没有匹配时以 1 退出；轮询把这种情形当作空结果，若仍有 JSON 则交给调用方解析
            if ($EmptyOnNoMatch -and $exitCode -eq 1) {
                $noMatchStdout = @($stdout)
                return [pscustomobject]@{
                    Output = $noMatchStdout
                    StdErr = if ($null -eq $stderrText) { '' } else { $stderrText.Trim() }
                    Empty  = [string]::IsNullOrWhiteSpace(($noMatchStdout -join ''))
                }
            }
            $detail = if ([string]::IsNullOrWhiteSpace($stderrText)) { '' } else { "`n$($stderrText.Trim())" }
            throw "winapp $($Arguments -join ' ') 失败（退出码 $exitCode）$detail"
        }
        return [pscustomobject]@{
            Output  = @($stdout)
            StdErr  = if ($null -eq $stderrText) { '' } else { $stderrText.Trim() }
            Empty   = $false
        }
    }
    finally {
        Remove-Item -LiteralPath $stderrFile -Force -ErrorAction SilentlyContinue
    }
}

function Get-VersionMatches {
    param([string]$App, [string]$Selector)
    $result = Invoke-Winapp -Arguments @('ui', 'search', $Selector, '-a', $App, '--json') -Selector $Selector -EmptyOnNoMatch
    if ($result.Empty) { return @() }
    $raw = ($result.Output -join "`n").Trim()
    $stderrSuffix = if ($result.StdErr) { "`nstderr：$($result.StdErr)" } else { '' }
    if (-not $raw) { throw "winapp ui search '$Selector' 未返回 JSON$stderrSuffix" }
    # -NoEnumerate 保留 JSON 数组形状：单条结果默认会被管道展开成对象，绕过数组校验
    $parsed = $raw | ConvertFrom-Json -NoEnumerate
    if ($null -eq $parsed) { return @() }
    if ($parsed -is [array]) { return @($parsed) }
    if ($parsed -isnot [pscustomobject] -or $parsed.PSObject.Properties.Name -notcontains 'matches') {
        throw "winapp ui search '$Selector' 返回了意外结构：$raw$stderrSuffix"
    }
    if ($null -ne $parsed.matches -and $parsed.matches -isnot [array]) {
        throw "winapp ui search '$Selector' 的 matches 不是数组：$raw$stderrSuffix"
    }
    if ($null -eq $parsed.matches) { return @() }
    return @($parsed.matches)
}

function Wait-VersionRows {
    param([string]$App, [string]$Selector, [int]$Seconds, [switch]$Gone)
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $rows = @(Get-VersionMatches -App $App -Selector $Selector)
        if ($Gone) {
            if ($rows.Count -eq 0) { return }
        }
        elseif ($rows.Count -gt 0) { return }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    if ($Gone) { throw "选择器 '$Selector' 在 $Seconds 秒内未消失" }
    throw "选择器 '$Selector' 在 $Seconds 秒内未出现"
}

try {
    $process = Start-Process -FilePath $exePath -WorkingDirectory (Split-Path -Parent $exePath) -PassThru
    $app = "$($process.Id)"
    $timeoutMs = $TimeoutSec * 1000

    Invoke-Winapp -Arguments @('ui', 'wait-for', 'Nav.Launch', '-a', $app, '-t', "$timeoutMs", '--json') -Selector 'Nav.Launch' | Out-Null
    Invoke-Winapp -Arguments @('ui', 'invoke', 'Nav.Download', '-a', $app, '--json') -Selector 'Nav.Download' | Out-Null
    Invoke-Winapp -Arguments @('ui', 'wait-for', 'Download.Category.Vanilla', '-a', $app, '-t', "$timeoutMs", '--json') -Selector 'Download.Category.Vanilla' | Out-Null
    Invoke-Winapp -Arguments @('ui', 'invoke', 'Download.Category.Vanilla', '-a', $app, '--json') -Selector 'Download.Category.Vanilla' | Out-Null

    # 先等置顶卡渲染完成，否则后面的消失检查会因为元素从未出现而空过。
    # 这里不能用 wait-for：它要求选择器精确匹配，而版本行 ID 是 Download.Version.Latest.<id>。
    Wait-VersionRows -App $app -Selector 'Download.Version.Latest' -Seconds $TimeoutSec

    # 置顶卡本身就有版本行，只有切到其他分类再切回才能证明分类处理器真的生效
    Invoke-Winapp -Arguments @('ui', 'invoke', 'Download.Category.Mod', '-a', $app, '--json') -Selector 'Download.Category.Mod' | Out-Null
    Wait-VersionRows -App $app -Selector 'Download.Version.Latest' -Seconds $TimeoutSec -Gone
    Invoke-Winapp -Arguments @('ui', 'invoke', 'Download.Category.Vanilla', '-a', $app, '--json') -Selector 'Download.Category.Vanilla' | Out-Null
    Wait-VersionRows -App $app -Selector 'Download.Version.Latest' -Seconds $TimeoutSec

    Invoke-Winapp -Arguments @('ui', 'screenshot', '-a', $app, '-o', $screenshot, '--json') -Selector 'Download.Version.Latest' | Out-Null
    Write-Host "冒烟测试通过，截图：$screenshot"
    [console]::beep(880, 250)
    exit 0
}
catch {
    $failure = $_.Exception.Message
    Write-Host "冒烟测试失败：$failure"
    $diagnostics = @("失败：$failure", "最后查询选择器：$lastSelector")
    if ($process -and -not $process.HasExited) {
        $app = "$($process.Id)"
        try {
            $inspect = Invoke-Winapp -Arguments @('ui', 'inspect', $lastSelector, '-a', $app, '--json')
            if ($inspect.Output.Count -gt 0) {
                ($inspect.Output -join "`n") | Set-Content -LiteralPath $inspectFile -Encoding utf8
            }
            if ($inspect.StdErr) { $diagnostics += "inspect stderr：$($inspect.StdErr)" }
        }
        catch {
            $diagnostics += "inspect 失败：$($_.Exception.Message)"
        }
        try {
            Invoke-Winapp -Arguments @('ui', 'screenshot', '-a', $app, '-o', $screenshot, '--json') | Out-Null
            $diagnostics += "截图：$screenshot"
        }
        catch {
            $diagnostics += "screenshot 失败：$($_.Exception.Message)"
        }
    }
    else {
        $diagnostics += 'PCL 未启动或已退出，未抓取控件树与截图'
    }
    $diagnostics | Set-Content -LiteralPath $diagnosticsFile -Encoding utf8
    Write-Host "诊断信息：$diagnosticsFile"
    [console]::beep(440, 250)
    exit 1
}
finally {
    if ($process -and -not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) {
            Stop-Process -Id $process.Id -Force
        }
    }
}
