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
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Release notes script passed.'
