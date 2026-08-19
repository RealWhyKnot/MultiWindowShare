<#
.SYNOPSIS
  Builds the GitHub release body for a tag from the promoted CHANGELOG section.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string] $Tag,

    [Parameter(Mandatory=$true)]
    [string] $Repo,

    [Parameter(Mandatory=$true)]
    [string] $ZipName,

    [Parameter(Mandatory=$true)]
    [string] $IntegrityName,

    [string] $RepoRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

$version = $Tag -replace '^v', ''
$changes = & (Join-Path $PSScriptRoot 'Update-Changelog.ps1') -Mode Notes -ForVersion -Version $Tag -RepoRoot $RepoRoot
$changeText = ($changes -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($changeText)) {
    $changeText = 'No changelog entries recorded for this tag.'
}

$priorTag = ''
Push-Location $RepoRoot
try {
    $described = & git describe --tags --abbrev=0 "$Tag^" 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($described)) {
        $priorTag = ([string]$described).Trim()
    }
}
finally {
    Pop-Location
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("MultiWindowShare $version") | Out-Null
$lines.Add('') | Out-Null
$lines.Add('## Changes') | Out-Null
$lines.Add('') | Out-Null
$lines.Add($changeText) | Out-Null
$lines.Add('') | Out-Null
$lines.Add('## Install') | Out-Null
$lines.Add('') | Out-Null
$lines.Add("1. Download ``$ZipName`` below and unblock it (right-click the zip, Properties, Unblock).") | Out-Null
$lines.Add('2. Extract anywhere and run `MultiWindowShare.exe`. Nothing is installed and nothing is written outside `%LocalAppData%\MultiWindowShare`.') | Out-Null
$lines.Add('3. In Discord, turn on "Use an experimental method to capture audio from applications" under Voice & Video, Screen Share.') | Out-Null
$lines.Add('') | Out-Null
$lines.Add('Windows 11, or Windows 10 2004 and later. The build is self-contained; no .NET runtime install is needed.') | Out-Null
$lines.Add('') | Out-Null
$lines.Add('## Verify the download') | Out-Null
$lines.Add('') | Out-Null
$lines.Add("``$IntegrityName`` lists the SHA256 and byte size of every asset. Check yours with:") | Out-Null
$lines.Add('') | Out-Null
$lines.Add('```powershell') | Out-Null
$lines.Add("Get-FileHash -Algorithm SHA256 .\$ZipName") | Out-Null
$lines.Add('```') | Out-Null
$lines.Add('') | Out-Null
$lines.Add('## Links') | Out-Null
$lines.Add('') | Out-Null
if ($priorTag) {
    $lines.Add("- [Full commit log since $priorTag](https://github.com/$Repo/compare/$priorTag...$Tag)") | Out-Null
}
$lines.Add("- [Changelog](https://github.com/$Repo/blob/main/CHANGELOG.md)") | Out-Null
$lines.Add("- [Report a bug](https://github.com/$Repo/issues/new/choose)") | Out-Null

$lines -join "`n"
