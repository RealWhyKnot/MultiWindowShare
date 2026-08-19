[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('mws-notes-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null
try {
    @'
# Changelog

## Unreleased

---

## v2026.8.18.0 - 2026-08-18

### Added
- **audio:** Route the mix to the chosen sink (abcdef0)

---
'@ | Set-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') -Encoding utf8

    & git -C $root init -q .
    & git -C $root config user.name 'MultiWindowShare Tests'
    & git -C $root config user.email 'mws-tests@example.invalid'
    & git -C $root add .
    & git -C $root commit -q -m 'initial'

    $notes = & (Join-Path $PSScriptRoot 'Generate-ReleaseNotes.ps1') `
        -Tag 'v2026.8.18.0' `
        -Repo 'RealWhyKnot/MultiWindowShare' `
        -ZipName 'MultiWindowShare-v2026.8.18.0-win-x64.zip' `
        -IntegrityName 'MultiWindowShare-v2026.8.18.0.integrity.tsv' `
        -RepoRoot $root

    $text = $notes -join "`n"
    foreach ($needle in @('Route the mix to the chosen sink', 'MultiWindowShare-v2026.8.18.0-win-x64.zip', 'RealWhyKnot/MultiWindowShare', '## Install')) {
        if ($text -notmatch [regex]::Escape($needle)) {
            throw "Release notes missing expected content: $needle"
        }
    }

    if ($text -match [char]0x2014) {
        throw 'Release notes must stay ASCII.'
    }

    if ($text -match 'Full commit log') {
        throw 'Release notes must omit the comparison link when no prior tag exists.'
    }

    # The workflow step runs under `pwsh -command`, which exits with whatever the last native
    # command left behind. A first release fails the prior-tag probe, so a leaked code fails the
    # step after the notes are already written.
    if ($LASTEXITCODE -ne 0) {
        throw "Release notes left exit code $LASTEXITCODE behind."
    }

    & git -C $root tag 'v2026.8.18.0'
    & git -C $root commit -q --allow-empty -m 'second'
    & git -C $root tag 'v2026.8.19.0'
    $followUp = & (Join-Path $PSScriptRoot 'Generate-ReleaseNotes.ps1') `
        -Tag 'v2026.8.19.0' `
        -Repo 'RealWhyKnot/MultiWindowShare' `
        -ZipName 'MultiWindowShare-v2026.8.19.0-win-x64.zip' `
        -IntegrityName 'MultiWindowShare-v2026.8.19.0.integrity.tsv' `
        -RepoRoot $root

    if (($followUp -join "`n") -notmatch 'compare/v2026\.8\.18\.0\.\.\.v2026\.8\.19\.0') {
        throw 'Release notes must link the comparison range when a prior tag exists.'
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Release notes left exit code $LASTEXITCODE behind on the follow-up release."
    }
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Release notes script passed.'
