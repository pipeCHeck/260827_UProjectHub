[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

& dotnet test (Join-Path $repositoryRoot 'UProjectHub.sln') -c $Configuration @args
exit $LASTEXITCODE
