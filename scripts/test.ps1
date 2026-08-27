[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

& dotnet test (Join-Path $repositoryRoot 'UProjectHub.sln') @args
exit $LASTEXITCODE
