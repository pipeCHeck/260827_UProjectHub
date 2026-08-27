[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repositoryRoot 'src\UProjectHub.App\UProjectHub.App.csproj'

& dotnet run --project $appProject -- @args
exit $LASTEXITCODE
