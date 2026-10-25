param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath $Root).Path
$extensions = @('.vb', '.cs', '.xaml', '.csproj', '.vbproj', '.sln', '.md', '.ps1', '.yml', '.yaml')
$skipFolders = @('bin', 'obj', '.git', '.vs', 'MeloongCore', 'docs', '.superpowers')
$allowed = @{
    'PCLA/My Project/AssemblyInfo.vb' = $true
    'PCLA/Modules/Minecraft/ModModpack.vb' = $true
    'PCLA/Pages/PageOther/PageOtherAbout.xaml' = $true
    'PCLA/Pages/PageOther/PageOtherAbout.xaml.vb' = $true
    'PCLA/Modules/Base/ModBase.vb' = $true
    'PCLCS/PCLCS.csproj' = $true
    'PCLCS/Startup/UserDataMigration.cs' = $true
    'tests/PCLCS.Tests/UserDataMigrationTests.cs' = $true
    'tools/check-branding.ps1' = $true
    'README.md' = $true
}

$violations = [System.Collections.Generic.List[string]]::new()
Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
    if ($_.Name -eq 'LICENCE') { return }
    if ($extensions -notcontains $_.Extension.ToLowerInvariant()) { return }
    $parts = $relative.Split('/')
    if ($parts | Where-Object { $skipFolders -contains $_ }) { return }
    if ($allowed.ContainsKey($relative)) { return }
    Select-String -LiteralPath $_.FullName -Pattern 'Plain Craft Launcher 2' | ForEach-Object {
        $violations.Add(('{0}:{1}: {2}' -f $relative, $_.LineNumber, $_.Line.Trim()))
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host $_ }
    exit 1
}
Write-Host '品牌扫描通过。'
exit 0
