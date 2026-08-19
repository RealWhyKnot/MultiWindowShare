#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$planner = Join-Path $scriptRoot "Get-NightlyBetaPlan.ps1"

function Invoke-TestGit {
    param([string] $RepoRoot, [string[]] $Arguments)

    Push-Location $RepoRoot
    try {
        $output = & git @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
        }
        return @($output)
    }
    finally {
        Pop-Location
    }
}

function New-TestRepo {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("multiwindowshare-nightly-beta-" + [System.Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root | Out-Null
    Invoke-TestGit -RepoRoot $root -Arguments @("init", "-q", ".") | Out-Null
    Invoke-TestGit -RepoRoot $root -Arguments @("config", "user.name", "MultiWindowShare Tests") | Out-Null
    Invoke-TestGit -RepoRoot $root -Arguments @("config", "user.email", "multiwindowshare-tests@example.invalid") | Out-Null
    Set-Content -LiteralPath (Join-Path $root "sample.txt") -Value "base" -Encoding ASCII
    Invoke-TestGit -RepoRoot $root -Arguments @("add", ".") | Out-Null
    Invoke-TestGit -RepoRoot $root -Arguments @("commit", "-q", "-m", "initial") | Out-Null
    return $root
}

function Read-Outputs {
    param([string] $Path)

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^([^=]+)=(.*)$') {
            $values[$Matches[1]] = $Matches[2]
        }
    }
    return $values
}

function Invoke-Planner {
    param([string] $RepoRoot)

    $outputPath = Join-Path $RepoRoot "outputs.txt"
    & $planner -RepoRoot $RepoRoot -NowUtc ([datetime]::Parse("2026-06-16T12:00:00Z")) -OutputPath $outputPath | Out-Host
    return Read-Outputs -Path $outputPath
}

$tempRoots = New-Object System.Collections.Generic.List[string]
try {
    $repo = New-TestRepo
    $tempRoots.Add($repo) | Out-Null
    $first = Invoke-Planner -RepoRoot $repo
    if ($first["has_changes"] -ne "true") { throw "Planner should publish when no tag exists." }
    if ($first["next_tag"] -ne "v2026.6.16.0-beta") { throw "Planner chose unexpected first beta tag: $($first["next_tag"])" }

    Invoke-TestGit -RepoRoot $repo -Arguments @("tag", "v2026.6.16.0-beta") | Out-Null
    Remove-Item -LiteralPath (Join-Path $repo "outputs.txt") -Force
    $sameHead = Invoke-Planner -RepoRoot $repo
    if ($sameHead["has_changes"] -ne "false") { throw "Planner should skip when latest tag is at HEAD." }

    Set-Content -LiteralPath (Join-Path $repo "sample.txt") -Value "changed" -Encoding ASCII
    Invoke-TestGit -RepoRoot $repo -Arguments @("add", ".") | Out-Null
    Invoke-TestGit -RepoRoot $repo -Arguments @("commit", "-q", "-m", "feat: new nightly input") | Out-Null
    Remove-Item -LiteralPath (Join-Path $repo "outputs.txt") -Force
    $changed = Invoke-Planner -RepoRoot $repo
    if ($changed["has_changes"] -ne "true") { throw "Planner should publish after commits since latest tag." }
    if ($changed["next_tag"] -ne "v2026.6.16.1-beta") { throw "Planner chose unexpected second beta tag: $($changed["next_tag"])" }

    Write-Host "Nightly beta planner tests passed."
}
finally {
    foreach ($tempRoot in $tempRoots) {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }
}
