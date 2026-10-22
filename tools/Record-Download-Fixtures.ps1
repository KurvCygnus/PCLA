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

foreach ($target in $targets) {
    $path = Join-Path $Output $target.Name
    try {
        Invoke-WebRequest -Uri $target.Url -OutFile $path -TimeoutSec 60 -UseBasicParsing
        Write-Host "OK   $($target.Name)  $((Get-Item $path).Length) bytes"
    } catch {
        Write-Host "FAIL $($target.Name)  $($_.Exception.Message)"
    }
}
