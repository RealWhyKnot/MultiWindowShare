#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string] $RepoRoot = (Get-Location).Path,
    [datetime] $NowUtc = ([datetime]::UtcNow),
    [string] $OutputPath = $env:GITHUB_OUTPUT
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Git {
    param([Parameter(Mandatory=$true)][string[]] $Arguments, [switch] $AllowFailure)

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & git @Arguments 2>$null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($exitCode -ne 0) {
        if ($AllowFailure) {
            return $null
        }
        throw "git $($Arguments -join ' ') failed with exit code $exitCode"
    }
    return @($output)
}

function Get-FirstOutput {
    param($Value)

    $items = @($Value)
    if ($items.Count -eq 0) {
        return $null
    }
    return [string]$items[0]
}

function Get-CentralTimeZone {
    foreach ($id in @("Central Standard Time", "America/Chicago")) {
        try {
            return [System.TimeZoneInfo]::FindSystemTimeZoneById($id)
        }
        catch {
        }
    }
    throw "Could not resolve the America/Chicago release time zone."
}

function Get-DateStamp {
    param([datetime] $Utc)

    if ($Utc.Kind -ne [System.DateTimeKind]::Utc) {
        $Utc = $Utc.ToUniversalTime()
    }
    $central = [System.TimeZoneInfo]::ConvertTimeFromUtc($Utc, (Get-CentralTimeZone))
    return $central.ToString("yyyy.M.d", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-NextRevision {
    param([string] $DateStamp)

    $escapedDate = [regex]::Escape($DateStamp)
    $pattern = "^v$escapedDate\.(\d+)(-[A-Za-z0-9][A-Za-z0-9.-]*)?$"
    $tags = @(Invoke-Git -Arguments @("tag", "--list", "v$DateStamp.*"))
    $highest = -1
    foreach ($tag in $tags) {
        if ($tag -match $pattern) {
            $value = [int]$Matches[1]
            if ($value -gt $highest) {
                $highest = $value
            }
        }
    }
    return $highest + 1
}

function Write-OutputValue {
    param([string] $Name, [string] $Value)

    $line = "$Name=$Value"
    Write-Host $line
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        Add-Content -LiteralPath $OutputPath -Value $line -Encoding UTF8
    }
}

$resolvedRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
Push-Location $resolvedRoot
try {
    $head = (Get-FirstOutput -Value (Invoke-Git -Arguments @("rev-parse", "HEAD"))).Trim()
    $latestTagOutput = Get-FirstOutput -Value (Invoke-Git -Arguments @("describe", "--tags", "--abbrev=0", "--match", "v*", "HEAD") -AllowFailure)
    $latestTag = ""
    if (-not [string]::IsNullOrWhiteSpace($latestTagOutput)) {
        $latestTag = $latestTagOutput.Trim()
    }

    if ([string]::IsNullOrWhiteSpace($latestTag)) {
        $commitCount = [int]((Get-FirstOutput -Value (Invoke-Git -Arguments @("rev-list", "--count", "--no-merges", "HEAD"))).Trim())
    } else {
        $commitCount = [int]((Get-FirstOutput -Value (Invoke-Git -Arguments @("rev-list", "--count", "--no-merges", "$latestTag..HEAD"))).Trim())
    }

    $hasChanges = $commitCount -gt 0
    $dateStamp = Get-DateStamp -Utc $NowUtc
    $revision = Get-NextRevision -DateStamp $dateStamp
    $nextTag = "v$dateStamp.$revision-beta"

    Write-OutputValue -Name "has_changes" -Value $hasChanges.ToString().ToLowerInvariant()
    Write-OutputValue -Name "commit_count" -Value ([string]$commitCount)
    Write-OutputValue -Name "latest_tag" -Value $latestTag
    Write-OutputValue -Name "head_sha" -Value $head
    Write-OutputValue -Name "next_tag" -Value $nextTag
}
finally {
    Pop-Location
}
