#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [switch] $Check,
    [switch] $ChangedOnly,
    [string] $ChangedBase = "origin/main",
    [switch] $NoRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Set-Location $PSScriptRoot

function Invoke-Step {
    param(
        [Parameter(Mandatory=$true)][string] $Name,
        [Parameter(Mandatory=$true)][scriptblock] $Command
    )

    Write-Host "lint: $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Get-ChangedCSharpFiles {
    param([string] $Base)

    $files = @()
    $output = & git diff --name-only --diff-filter=ACMRT "$Base...HEAD" 2>$null
    if ($LASTEXITCODE -ne 0) {
        $output = & git diff --name-only --diff-filter=ACMRT "$Base..HEAD" 2>$null
    }
    if ($LASTEXITCODE -ne 0 -or -not $output) {
        return @()
    }

    foreach ($file in @($output)) {
        if ($file -match '\.cs$' -and
            $file -notmatch '(^|/)(bin|obj|build|release|stage)(/|$)') {
            $files += $file
        }
    }
    return $files
}

Invoke-Step -Name "workflow script syntax" -Command {
    ./.github/scripts/Test-WorkflowSyntax.ps1
    ./.github/scripts/Test-UpdateChangelog.ps1
    ./.github/scripts/Test-GenerateReleaseNotes.ps1
    ./.github/scripts/Test-ReleaseVersionSequence.ps1
}

if (-not $NoRestore) {
    Invoke-Step -Name "restore" -Command {
        dotnet restore MultiWindowShare.slnx
    }
}

$formatArgs = @("format", "MultiWindowShare.slnx", "--verbosity", "minimal")
if ($NoRestore) {
    $formatArgs += "--no-restore"
}
if ($Check) {
    $formatArgs += "--verify-no-changes"
}

$skipFormat = $false
if ($ChangedOnly) {
    $changedCSharp = @(Get-ChangedCSharpFiles -Base $ChangedBase)
    if ($changedCSharp.Count -eq 0) {
        Write-Host "lint: no changed C# files to format-check."
        $skipFormat = $true
    } else {
        $formatArgs += "--include"
        $formatArgs += $changedCSharp
    }
}

if (-not $skipFormat) {
    Invoke-Step -Name "dotnet format" -Command {
        dotnet @formatArgs
    }
}

Invoke-Step -Name "warnings as errors" -Command {
    dotnet build MultiWindowShare.slnx --configuration Release /warnaserror
}

Write-Host "lint: passed."
