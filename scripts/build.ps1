[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

& dotnet build (Join-Path $repositoryRoot 'UProjectHub.sln') @args
exit $LASTEXITCODE
