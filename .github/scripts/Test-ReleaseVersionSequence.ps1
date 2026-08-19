#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$assertScript = Join-Path $scriptRoot "Assert-ReleaseVersionSequence.ps1"

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
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("mws-release-sequence-" + [System.Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root | Out-Null
    Invoke-TestGit -RepoRoot $root -Arguments @("init", "-q", ".") | Out-Null
    Invoke-TestGit -RepoRoot $root -Arguments @("config", "user.name", "MultiWindowShare Tests") | Out-Null
    Invoke-TestGit -RepoRoot $root -Arguments @("config", "user.email", "mws-tests@example.invalid") | Out-Null
    Set-Content -LiteralPath (Join-Path $root "sample.txt") -Value "base" -Encoding ASCII
    Invoke-TestGit -RepoRoot $root -Arguments @("add", ".") | Out-Null
    Invoke-TestGit -RepoRoot $root -Arguments @("commit", "-q", "-m", "initial") | Out-Null
    return $root
}

function Assert-Passes {
    param([string] $RepoRoot, [string] $Tag, [string] $Message)

    & $assertScript -RepoRoot $RepoRoot -Tag $Tag | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw $Message
    }
}

function Assert-Fails {
    param([string] $RepoRoot, [string] $Tag, [string] $Message)

    $failed = $false
    try {
        & $assertScript -RepoRoot $RepoRoot -Tag $Tag | Out-Host
    }
    catch {
        $failed = $true
    }

    if (-not $failed) {
        throw $Message
    }
}

$tempRoots = New-Object System.Collections.Generic.List[string]
try {
    $repo = New-TestRepo
    $tempRoots.Add($repo) | Out-Null
    Assert-Passes -RepoRoot $repo -Tag "v2026.6.16.0-beta" -Message "First same-day prerelease should be .0."
    Assert-Fails -RepoRoot $repo -Tag "v2026.6.16.1-beta" -Message ".1 should fail when no same-day tag exists."

    $repo = New-TestRepo
    $tempRoots.Add($repo) | Out-Null
    Invoke-TestGit -RepoRoot $repo -Arguments @("tag", "v2026.6.16.0") | Out-Null
    Assert-Passes -RepoRoot $repo -Tag "v2026.6.16.1-beta" -Message "Prerelease after same-day stable .0 should be .1."
    Assert-Fails -RepoRoot $repo -Tag "v2026.6.16.0-beta" -Message ".0-beta should fail after same-day stable .0."

    $repo = New-TestRepo
    $tempRoots.Add($repo) | Out-Null
    Invoke-TestGit -RepoRoot $repo -Arguments @("tag", "v2026.6.16.0-beta") | Out-Null
    Invoke-TestGit -RepoRoot $repo -Arguments @("tag", "v2026.6.16.1") | Out-Null
    Assert-Passes -RepoRoot $repo -Tag "v2026.6.16.2-beta" -Message "Sequence should increment the highest same-day tag."

    $repo = New-TestRepo
    $tempRoots.Add($repo) | Out-Null
    Invoke-TestGit -RepoRoot $repo -Arguments @("tag", "v2026.6.16.0-beta") | Out-Null
    Assert-Passes -RepoRoot $repo -Tag "v2026.6.16.0-beta" -Message "Rerunning the exact release tag should be allowed."

    Write-Host "Release version sequence tests passed."
}
finally {
    foreach ($tempRoot in $tempRoots) {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }
}
