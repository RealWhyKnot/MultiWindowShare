[CmdletBinding()]
param(
    [string] $Version = "",
    [string] $Configuration = "Release",
    [switch] $Release
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory=$true)][string] $Path,
        [Parameter(Mandatory=$true)][string] $Content
    )
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

# AssemblyVersion rejects the -XXXX build suffix, so releases carry the numeric prefix only.
function Get-NumericVersion {
    param([Parameter(Mandatory=$true)][string] $BuildVersion)
    $base = ($BuildVersion -replace '-.*$', '')
    $parts = @($base.Split('.') | ForEach-Object {
        $value = 0
        if ([int]::TryParse($_, [ref]$value)) { $value } else { 0 }
    })
    while ($parts.Count -lt 4) { $parts += 0 }
    return ($parts[0..3] -join '.')
}

$hooksPath = (& git config --get core.hooksPath 2>$null)
if ($hooksPath -ne ".githooks") {
    & git config core.hooksPath ".githooks"
    Write-Host "Activated .githooks/ via core.hooksPath"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $today = Get-Date -Format "yyyy.M.d"
    $statePath = Join-Path "build" "local_build_state.json"
    $counter = 0
    if (Test-Path -LiteralPath $statePath) {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($state.date -eq $today) {
            $counter = [int]$state.counter + 1
        }
    }
    New-Item -ItemType Directory -Force -Path "build" | Out-Null
    $uid = ([guid]::NewGuid().ToString("N").Substring(0, 4)).ToUpper()
    $Version = "$today.$counter-$uid"
    Write-Utf8NoBom -Path $statePath -Content (@{ date = $today; counter = $counter } | ConvertTo-Json)
}

$numericVersion = Get-NumericVersion -BuildVersion $Version
Write-Utf8NoBom -Path "version.txt" -Content $Version
Write-Host "Build version: $Version"

dotnet restore MultiWindowShare.slnx
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

dotnet build MultiWindowShare.slnx --configuration $Configuration --no-restore -p:Version=$numericVersion
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

dotnet test MultiWindowShare.slnx --configuration $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed" }

if (-not $Release) {
    exit 0
}

$releaseDir = Join-Path $PSScriptRoot "release"
$appDir = Join-Path $releaseDir "app"
if (Test-Path -LiteralPath $releaseDir) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

dotnet publish src\MultiWindowShare\MultiWindowShare.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $appDir `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$numericVersion
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$publishedFiles = @(Get-ChildItem -LiteralPath $appDir -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne "MultiWindowShare.exe") {
    throw "Release publish should produce exactly one executable file."
}

$licensePath = Join-Path $PSScriptRoot "LICENSE"
if (-not (Test-Path -LiteralPath $licensePath)) {
    throw "LICENSE is required for release packaging."
}
Copy-Item -LiteralPath $licensePath -Destination $appDir -Force

$zipPath = Join-Path $releaseDir "MultiWindowShare-v$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $appDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
$exeSizeMb = [Math]::Round($publishedFiles[0].Length / 1MB, 2)
Write-Host "Release exe: $($publishedFiles[0].FullName) ($exeSizeMb MB)"
Write-Host "Release zip: $zipPath"
Write-Host "SHA256:      $zipHash"
