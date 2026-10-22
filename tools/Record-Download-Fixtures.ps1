param(
    [string]$Output = (Join-Path $PSScriptRoot '..\tests\PCLCS.Tests\Fixtures\Download')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
New-Item -ItemType Directory -Force -Path $Output | Out-Null

$targets = @(
    @{ Name = 'mojang-manifest.json'; Url = 'https://launchermeta.mojang.com/mc/game/version_manifest.json' },
    @{ Name = 'bmclapi-manifest.json'; Url = 'https://bmclapi2.bangbang93.com/mc/game/version_manifest.json' },
    @{ Name = 'optifine-official.html'; Url = 'https://optifine.net/downloads' },
    @{ Name = 'optifine-bmclapi.json'; Url = 'https://bmclapi2.bangbang93.com/optifine/versionList' },
    @{ Name = 'forge-index.html'; Url = 'https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_1.2.4.html' },
    @{ Name = 'forge-builds.html'; Url = 'https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_1.20.1.html' },
    @{ Name = 'forge-builds-bmclapi.json'; Url = 'https://bmclapi2.bangbang93.com/forge/minecraft/1.20.1' },
    @{ Name = 'neoforge-latest.json'; Url = 'https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge' },
    @{ Name = 'neoforge-legacy.json'; Url = 'https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/forge' },
    @{ Name = 'liteloader.json'; Url = 'https://dl.liteloader.com/versions/versions.json' },
    @{ Name = 'fabric.json'; Url = 'https://meta.fabricmc.net/v2/versions' }
)

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($target in $targets) {
    $path = Join-Path $Output $target.Name
    $tempPath = "$path.tmp"
    try {
        Invoke-WebRequest -Uri $target.Url -OutFile $tempPath -TimeoutSec 60 -UseBasicParsing
        if ((Get-Item $tempPath).Length -eq 0) {
            throw "响应为空（0 字节）"
        }
        Move-Item -LiteralPath $tempPath -Destination $path -Force
        Write-Host "OK   $($target.Name)  $((Get-Item $path).Length) bytes"
    } catch {
        if (Test-Path -LiteralPath $tempPath) {
            Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
        }
        $message = $_.Exception.Message
        $failures.Add("$($target.Name)  $($target.Url)  $message")
        Write-Host "FAIL $($target.Name)  $message"
    }
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "失败的 URL（共 $($failures.Count) 个）:"
    foreach ($failure in $failures) {
        Write-Host "  $failure"
    }
    exit 1
}
exit 0
