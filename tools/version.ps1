#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('release', 'nightly', 'snapshot', 'debug')]
    [string]$Channel = 'snapshot',
    [string]$Commit = '',
    [string]$SemVer = '',
    [int]$ProtocolVersion = 0,
    [int]$HelpVersion = 0,
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $root 'Version.props'
if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
    throw "找不到 Version.props：$propsPath"
}
[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$semVer = if ($SemVer) { $SemVer } else { [string]$props.Project.PropertyGroup.PclaSemVer }
$protocol = if ($ProtocolVersion -gt 0) { $ProtocolVersion } else { [int]$props.Project.PropertyGroup.PclaProtocolVersion }
$help = if ($HelpVersion -gt 0) { $HelpVersion } else { [int]$props.Project.PropertyGroup.PclaHelpVersion }

if ($semVer -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "PclaSemVer 必须是 MAJOR.MINOR.PATCH：$semVer"
}

$commitShort = ''
if ($Commit -and $Commit -ne 'local' -and $Commit.Length -ge 7) {
    $commitShort = $Commit.Substring(0, 7)
}

$versionFull = switch ($Channel) {
    'release' { $semVer }
    'nightly' {
        if (-not $commitShort) { throw 'nightly 通道需要至少 7 位 commit' }
        "$semVer-nightly-$commitShort"
    }
    'snapshot' { "$semVer-snapshot" }
    'debug' { "$semVer-snapshot" }
}
$channelCode = switch ($Channel) {
    'release' { 0 }
    'nightly' { 1 }
    'snapshot' { 2 }
    'debug' { 3 }
}
$assemblyVersion = "$semVer.0"
$commitHash = if ($Channel -in @('snapshot', 'debug')) { '' } else { $Commit }

$data = [ordered]@{
    VersionFull     = $versionFull
    VersionChannel  = $Channel
    ChannelCode     = $channelCode
    AssemblyVersion = $assemblyVersion
    ProtocolVersion = $protocol
    HelpVersion     = $help
    CommitHash      = $commitHash
    CommitShort     = $commitShort
}

if ($OutputDir) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    $vb = @"
Namespace Global.PCL
    Public NotInheritable Class VersionInfo
        Public Const VersionFull As String = "$versionFull"
        Public Const VersionChannel As String = "$Channel"
        Public Const ChannelCode As Integer = $channelCode
        Public Const AssemblyVersion As String = "$assemblyVersion"
        Public Const ProtocolVersion As Integer = $protocol
        Public Const HelpVersion As Integer = $help
        Public Const CommitHash As String = "$commitHash"
    End Class
End Namespace
"@
    $cs = @"
namespace PCLCS;

public static class VersionInfo {
    public const string VersionFull = "$versionFull";
    public const string VersionChannel = "$Channel";
    public const int ChannelCode = $channelCode;
    public const string AssemblyVersion = "$assemblyVersion";
    public const int ProtocolVersion = $protocol;
    public const int HelpVersion = $help;
    public const string CommitHash = "$commitHash";
}
"@
    Set-Content -LiteralPath (Join-Path $OutputDir 'VersionInfo.g.vb') -Value $vb -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $OutputDir 'VersionInfo.g.cs') -Value $cs -Encoding utf8NoBOM
}

$data.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }
