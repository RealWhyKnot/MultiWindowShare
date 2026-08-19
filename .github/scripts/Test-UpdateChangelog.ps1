[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('mws-changelog-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null
try {
    @'
# Changelog

## Unreleased

### Added
- Example entry (abcdef0)

---
'@ | Set-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') -Encoding utf8

    & (Join-Path $PSScriptRoot 'Update-Changelog.ps1') -Mode Promote -Version 'v2026.6.13.0' -RepoRoot $root -NowUtc ([datetime]::Parse('2026-06-16T01:30:00Z'))
    $promoted = Get-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') -Raw
    if ($promoted -notmatch '## v2026\.6\.13\.0 - 2026-06-15') {
        throw 'Promoted changelog date did not use Central release date.'
    }
    $notes = & (Join-Path $PSScriptRoot 'Update-Changelog.ps1') -Mode Notes -ForVersion -Version 'v2026.6.13.0' -RepoRoot $root
    if (($notes -join "`n") -notmatch 'Example entry') {
        throw 'Promoted changelog notes were not readable.'
    }

    $repo = Join-Path $root 'repo'
    New-Item -ItemType Directory -Force -Path $repo | Out-Null
    & git -C $repo init --quiet
    & git -C $repo config user.email 'test@example.invalid'
    & git -C $repo config user.name 'Changelog Test'
    & git -C $repo config commit.gpgsign false
    @'
# Changelog

## Unreleased

### Added
- **ui:** Older entry (abcdef0)

### Changed
- Older change (abcdef1)

---
'@ | Set-Content -LiteralPath (Join-Path $repo 'CHANGELOG.md') -Encoding utf8
    & git -C $repo add CHANGELOG.md
    & git -C $repo commit --quiet --no-verify -m 'feat(ui): Newer entry'

    & (Join-Path $PSScriptRoot 'Update-Changelog.ps1') -Mode Append -Range 'HEAD' -RepoRoot $repo
    $appended = Get-Content -LiteralPath (Join-Path $repo 'CHANGELOG.md') -Raw

    $addedHeadings = ([regex]::Matches($appended, "(?m)^### Added\r?$")).Count
    if ($addedHeadings -ne 1) {
        throw "Append should leave one ### Added heading, found $addedHeadings."
    }
    foreach ($expected in @('Newer entry', 'Older entry', 'Older change')) {
        if ($appended -notmatch [regex]::Escape($expected)) {
            throw "Append dropped '$expected'."
        }
    }
    if ($appended.IndexOf('Newer entry') -gt $appended.IndexOf('Older entry')) {
        throw 'Append should list new entries above the ones already in Unreleased.'
    }

    & (Join-Path $PSScriptRoot 'Update-Changelog.ps1') -Mode Append -Range 'HEAD' -RepoRoot $repo
    $twice = Get-Content -LiteralPath (Join-Path $repo 'CHANGELOG.md') -Raw
    if (([regex]::Matches($twice, [regex]::Escape('Newer entry'))).Count -ne 1) {
        throw 'Re-running Append over the same range duplicated an entry.'
    }
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Changelog scripts passed.'
