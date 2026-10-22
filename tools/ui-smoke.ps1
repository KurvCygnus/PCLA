#Requires -Version 7.0
<#
.SYNOPSIS
    启动 PCL，导航到下载页并等待版本列表加载。
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe'),
    [int]$TimeoutSec = 60
)

$ErrorActionPreference = 'Stop'

& 'C:\Users\Lenovo\.config\opencode\tools\Show-ConsentGate.ps1' -Message '将启动 PCL 并通过 UI Automation 点击导航与下载分类，用于冒烟测试。' -DrivesInput
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
$process = $null

function Invoke-Winapp {
    param([string[]]$Arguments)
    $output = & winapp @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "winapp $($Arguments -join ' ') 失败（退出码 $LASTEXITCODE）"
    }
    return $output
}

try {
    $process = Start-Process -FilePath $exePath -PassThru
    $app = "$($process.Id)"
    $timeoutMs = $TimeoutSec * 1000

    Invoke-Winapp @('ui', 'wait-for', 'Nav.Launch', '-a', $app, '-t', "$timeoutMs", '--json') | Out-Null
    Invoke-Winapp @('ui', 'invoke', 'Nav.Download', '-a', $app, '--json') | Out-Null
    Invoke-Winapp @('ui', 'wait-for', 'Download.Category.Vanilla', '-a', $app, '-t', "$timeoutMs", '--json') | Out-Null
    Invoke-Winapp @('ui', 'invoke', 'Download.Category.Vanilla', '-a', $app, '--json') | Out-Null

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $found = $false
    while ((Get-Date) -lt $deadline) {
        $raw = & winapp ui search 'Download.Version' -a $app --json 2>$null
        if ($LASTEXITCODE -eq 0 -and $raw) {
            $parsed = $raw | ConvertFrom-Json
            $items = if ($parsed.PSObject.Properties.Name -contains 'matches') { $parsed.matches } else { $parsed }
            if (@($items).Count -gt 0) {
                $found = $true
                break
            }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $found) {
        throw "版本列表在 $TimeoutSec 秒内未出现"
    }

    Invoke-Winapp @('ui', 'screenshot', '-a', $app, '-o', $screenshot, '--json') | Out-Null
    Write-Host "冒烟测试通过，截图：$screenshot"
    [console]::beep(880, 250)
    exit 0
}
catch {
    Write-Host "冒烟测试失败：$($_.Exception.Message)"
    if ($process -and -not $process.HasExited) {
        $app = "$($process.Id)"
        & winapp ui inspect 'Nav.Launch' -a $app --json 2>$null | Set-Content -LiteralPath $inspectFile -Encoding utf8
        & winapp ui screenshot -a $app -o $screenshot --json 2>$null | Out-Null
    }
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
