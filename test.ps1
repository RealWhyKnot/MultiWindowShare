[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet test MultiWindowShare.slnx -c $Configuration
exit $LASTEXITCODE
