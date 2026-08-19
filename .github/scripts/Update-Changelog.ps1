<#
.SYNOPSIS
  Maintains CHANGELOG.md from conventional commit subjects.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('Append', 'Promote', 'Notes')]
    [string] $Mode,

    [string] $Range,
    [string] $Version,
    [switch] $ForVersion,
    [string] $RepoRoot,
    [datetime] $NowUtc = ([datetime]::UtcNow)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

$ChangelogPath = Join-Path $RepoRoot 'CHANGELOG.md'
if (-not (Test-Path -LiteralPath $ChangelogPath)) {
    throw "CHANGELOG.md not found at $ChangelogPath"
}

function Read-TextUtf8 {
    param([string] $Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false))
}

function Write-TextUtf8 {
    param([string] $Path, [string] $Content)
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Get-CentralTimeZone {
    foreach ($id in @('Central Standard Time', 'America/Chicago')) {
        try { return [System.TimeZoneInfo]::FindSystemTimeZoneById($id) }
        catch { }
    }
    throw 'Could not resolve the America/Chicago release time zone.'
}

function Get-ReleaseDateStamp {
    param([datetime] $NowUtc, [string] $Format)

    $utc = $NowUtc
    if ($utc.Kind -ne [System.DateTimeKind]::Utc) {
        $utc = $utc.ToUniversalTime()
    }
    $central = [System.TimeZoneInfo]::ConvertTimeFromUtc($utc, (Get-CentralTimeZone))
    return $central.ToString($Format, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Strip-BuildStamp {
    param([string] $Subject)
    return ($Subject -replace ' \(\d{4}\.\d+\.\d+\.\d+(-[A-Fa-f0-9]{4,8})?\)$', '').Trim()
}

function Convert-SubjectToEntry {
    param([string] $Sha, [string] $Subject)

    $stripped = Strip-BuildStamp -Subject $Subject
    if ([string]::IsNullOrWhiteSpace($stripped)) { return $null }
    if ($stripped -match '\[skip changelog\]') { return $null }
    if ($stripped -match '^Merge ') { return $null }

    $pattern = '^(?<type>feat|fix|perf|refactor|docs|build|ci|chore|test|revert)(?:\((?<scope>[^)]+)\))?(?<bang>!)?:\s+(?<desc>.+)$'
    $match = [regex]::Match($stripped, $pattern)
    if (-not $match.Success) {
        return @{ Bucket = 'Changed'; Bullet = "- $stripped ($($Sha.Substring(0, 7)))" }
    }

    $type = $match.Groups['type'].Value
    $scope = $match.Groups['scope'].Value
    $desc = $match.Groups['desc'].Value
    if ($desc.Length -gt 0) {
        $desc = $desc.Substring(0, 1).ToUpper() + $desc.Substring(1)
    }

    $bucket = switch ($type) {
        'feat' { 'Added' }
        'fix' { 'Fixed' }
        'perf' { 'Changed' }
        'refactor' { 'Changed' }
        'revert' { 'Changed' }
        'chore' {
            if ($scope -and $scope -match '^deps') { 'Changed' } else { $null }
        }
        default { $null }
    }
    if (-not $bucket) { return $null }
    if ($match.Groups['bang'].Success) { $bucket = 'Breaking' }

    $scopePrefix = if ($scope) { "**${scope}:** " } else { '' }
    return @{ Bucket = $bucket; Bullet = "- $scopePrefix$desc ($($Sha.Substring(0, 7)))" }
}

function Get-UnreleasedBody {
    param([string] $Content)
    $pattern = '(?ms)^## Unreleased\s*(?<body>.*?)(?=^---|\z)'
    $match = [regex]::Match($Content, $pattern)
    if (-not $match.Success) { return '' }
    return $match.Groups['body'].Value.Trim()
}

if ($Mode -eq 'Append') {
    if ([string]::IsNullOrWhiteSpace($Range)) { throw '-Range is required for Append mode.' }
    $content = Read-TextUtf8 -Path $ChangelogPath
    if ($content -notmatch '(?m)^## Unreleased\s*$') {
        throw 'CHANGELOG.md must contain a ## Unreleased section.'
    }

    $entriesByBucket = [ordered]@{
        Breaking = New-Object System.Collections.Generic.List[string]
        Added = New-Object System.Collections.Generic.List[string]
        Changed = New-Object System.Collections.Generic.List[string]
        Fixed = New-Object System.Collections.Generic.List[string]
    }

    $lines = & git -C $RepoRoot log --no-merges --format='%H%x09%s' $Range
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split "`t", 2
        if ($parts.Count -ne 2) { continue }
        $entry = Convert-SubjectToEntry -Sha $parts[0] -Subject $parts[1]
        if ($null -ne $entry) {
            $entriesByBucket[$entry.Bucket].Add($entry.Bullet) | Out-Null
        }
    }

    $newBodyLines = New-Object System.Collections.Generic.List[string]
    foreach ($bucket in $entriesByBucket.Keys) {
        if ($entriesByBucket[$bucket].Count -eq 0) { continue }
        $newBodyLines.Add("### $bucket") | Out-Null
        foreach ($bullet in $entriesByBucket[$bucket]) {
            $newBodyLines.Add($bullet) | Out-Null
        }
        $newBodyLines.Add('') | Out-Null
    }

    if ($newBodyLines.Count -eq 0) {
        Write-Host 'No changelog entries produced.'
        exit 0
    }

    $newBody = (($newBodyLines -join "`n").TrimEnd()) + "`n`n"
    $updated = [regex]::Replace($content, '(?ms)^## Unreleased\s*', "## Unreleased`n`n$newBody", 1)
    Write-TextUtf8 -Path $ChangelogPath -Content $updated
    exit 0
}

if ($Mode -eq 'Promote') {
    if ([string]::IsNullOrWhiteSpace($Version)) { throw '-Version is required for Promote mode.' }
    $content = Read-TextUtf8 -Path $ChangelogPath
    $body = Get-UnreleasedBody -Content $content
    $date = Get-ReleaseDateStamp -NowUtc $NowUtc -Format 'yyyy-MM-dd'
    $replacement = "## Unreleased`n`n---`n`n## $Version - $date`n`n"
    if (-not [string]::IsNullOrWhiteSpace($body)) {
        $replacement += "$body`n`n"
    }
    $updated = [regex]::Replace($content, '(?ms)^## Unreleased\s*.*?(?=^---|\z)', $replacement.TrimEnd() + "`n`n", 1)
    Write-TextUtf8 -Path $ChangelogPath -Content $updated
    exit 0
}

if ($Mode -eq 'Notes') {
    $content = Read-TextUtf8 -Path $ChangelogPath
    if ($ForVersion) {
        if ([string]::IsNullOrWhiteSpace($Version)) { throw '-Version is required with -ForVersion.' }
        $escaped = [regex]::Escape($Version)
        $match = [regex]::Match($content, "(?ms)^## $escaped\b[^\n]*\n(?<body>.*?)(?=^---|\z)")
        if ($match.Success) {
            $match.Groups['body'].Value.Trim()
        }
    } else {
        Get-UnreleasedBody -Content $content
    }
}
