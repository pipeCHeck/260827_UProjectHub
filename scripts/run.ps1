[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repositoryRoot 'src\UProjectHub.App\UProjectHub.App.csproj'

& dotnet run --project $appProject -c $Configuration -- @args
exit $LASTEXITCODE
