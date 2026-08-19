[CmdletBinding()]
param(
    [string] $Root = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$errors = New-Object System.Collections.Generic.List[string]

function Add-ParserErrors {
    param([string] $Source, $ParseErrors)
    if (-not $ParseErrors) { return }
    foreach ($e in $ParseErrors) {
        $errors.Add("$Source (line $($e.Extent.StartLineNumber):$($e.Extent.StartColumnNumber)): $($e.Message)") | Out-Null
    }
}

$scriptDir = Join-Path $Root '.github/scripts'
if (Test-Path -LiteralPath $scriptDir) {
    Get-ChildItem -LiteralPath $scriptDir -Filter '*.ps1' -Recurse | ForEach-Object {
        $parseErrors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$null, [ref]$parseErrors)
        Add-ParserErrors -Source $_.FullName -ParseErrors $parseErrors
    }
}

$workflowDir = Join-Path $Root '.github/workflows'
if (Test-Path -LiteralPath $workflowDir) {
    $ghaPattern = [regex]'\$\{\{[^}]*\}\}'
    Get-ChildItem -LiteralPath $workflowDir -Filter '*.yml' | ForEach-Object {
        $wfPath = $_.FullName
        $lines = Get-Content -LiteralPath $wfPath
        $stepName = '<unnamed>'
        $isPwsh = $false
        $inRun = $false
        $runIndent = -1
        $runStartLine = 0
        $blockLines = New-Object System.Collections.Generic.List[string]

        $flushBlock = {
            if ($blockLines.Count -eq 0) { return }
            $baseline = -1
            foreach ($line in $blockLines) {
                if ($line.Trim().Length -eq 0) { continue }
                $baseline = $line.Length - $line.TrimStart(' ').Length
                break
            }
            if ($baseline -lt 0) { return }
            $body = ($blockLines | ForEach-Object {
                if ($_.Length -gt $baseline) { $_.Substring($baseline) } else { '' }
            }) -join "`n"
            $stubbed = $ghaPattern.Replace($body, '__GHA_EXPR__')
            $parseErrors = $null
            [void][System.Management.Automation.Language.Parser]::ParseInput($stubbed, [ref]$null, [ref]$parseErrors)
            Add-ParserErrors -Source "$wfPath step '$stepName' run block starting at line $runStartLine" -ParseErrors $parseErrors
        }

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $indent = $line.Length - $line.TrimStart(' ').Length
            $trimmed = $line.Trim()

            if ($inRun) {
                if ($trimmed.Length -gt 0 -and $indent -le $runIndent) {
                    & $flushBlock
                    $blockLines.Clear()
                    $inRun = $false
                } else {
                    $blockLines.Add($line) | Out-Null
                    continue
                }
            }

            if ($trimmed -match '^- name:\s*(.+?)\s*$') {
                if ($blockLines.Count -gt 0) { & $flushBlock; $blockLines.Clear(); $inRun = $false }
                $stepName = $Matches[1]
                $isPwsh = $false
                continue
            }
            if ($trimmed -match '^shell:\s*(.+?)\s*$') {
                $isPwsh = ($Matches[1] -eq 'pwsh')
                continue
            }
            if ($isPwsh -and $trimmed -match '^run:\s*\|\s*$') {
                $inRun = $true
                $runIndent = $indent
                $runStartLine = $i + 1
                $blockLines.Clear()
            }
        }
        if ($inRun) { & $flushBlock }
    }
}

if ($errors.Count -gt 0) {
    Write-Host 'PowerShell syntax errors:'
    foreach ($e in $errors) { Write-Host "  $e" }
    throw "Found $($errors.Count) PowerShell syntax error(s)."
}

Write-Host 'PowerShell scripts and workflow pwsh blocks parsed cleanly.'
