[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

& dotnet build (Join-Path $repositoryRoot 'UProjectHub.sln') -c $Configuration @args
exit $LASTEXITCODE
