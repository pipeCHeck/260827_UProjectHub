[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'UProjectHub.sln'

function Invoke-NativeStep {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [scriptblock] $Action
    )

    Write-Host ""
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }

    Write-Host "PASS: $Name"
}

function Get-SourceMatches {
    param(
        [Parameter(Mandatory)]
        [string] $Pattern
    )

    $sourceRoot = Join-Path $repositoryRoot 'src'
    return @(Get-ChildItem $sourceRoot -Recurse -File -Include '*.cs', '*.xaml', '*.csproj' |
        Select-String -Pattern $Pattern)
}

function Assert-NoSourceMatches {
    param(
        [Parameter(Mandatory)]
        [string] $Label,

        [Parameter(Mandatory)]
        [string] $Pattern
    )

    $matches = @(Get-SourceMatches $Pattern)
    if ($matches.Count -gt 0) {
        $details = $matches | ForEach-Object {
            "$($_.Path):$($_.LineNumber):$($_.Line.Trim())"
        }
        throw "$Label failed:`n$($details -join [Environment]::NewLine)"
    }

    Write-Host "PASS: $Label"
}

function Invoke-SafetyChecks {
    Write-Host ""
    Write-Host "==> Forbidden-pattern safety checks"

    $deleteMatches = @(Get-SourceMatches '\b(?:File|Directory)\.Delete\s*\(')
    $allowedDeleteFiles = @(
        (Join-Path $repositoryRoot 'src\UProjectHub.Core\Storage\AtomicJsonFileWriter.cs'),
        (Join-Path $repositoryRoot 'src\UProjectHub.Windows\Logging\RollingFileLogger.cs'),
        (Join-Path $repositoryRoot 'src\UProjectHub.Windows\Cleanup\ProjectCleanupService.cs')
    )
    $unexpectedDeletes = @($deleteMatches | Where-Object {
        $allowedDeleteFiles -notcontains $_.Path
    })
    if ($unexpectedDeletes.Count -gt 0) {
        $details = $unexpectedDeletes | ForEach-Object {
            "$($_.Path):$($_.LineNumber):$($_.Line.Trim())"
        }
        throw "Unexpected production delete API usage:`n$($details -join [Environment]::NewLine)"
    }
    Write-Host 'PASS: delete APIs are limited to atomic writes, bounded logs, and validated Project Cleanup targets.'

    $registryRoot = Join-Path $repositoryRoot 'src\UProjectHub.Windows\Registry'
    $registryWriteMatches = @(Get-ChildItem $registryRoot -Recurse -File -Include '*.cs' |
        Select-String -Pattern '\b(?:SetValue|DeleteValue|DeleteSubKey|DeleteSubKeyTree|CreateSubKey)\s*\(')
    if ($registryWriteMatches.Count -gt 0) {
        throw 'Registry integration contains a write/delete API.'
    }
    Write-Host 'PASS: Registry write/delete APIs'
    Assert-NoSourceMatches `
        'Generate Project Files / UnrealBuildTool' `
        'GenerateProjectFiles|Generate Project Files|UnrealBuildTool'
    Assert-NoSourceMatches `
        'Process termination APIs' `
        'Process\s*\.\s*Kill\s*\(|\.Kill\s*\(|Thread\s*\.\s*Abort\s*\(|\.Abort\s*\('
    Assert-NoSourceMatches `
        'FileSystemWatcher' `
        '\bFileSystemWatcher\b'
    Assert-NoSourceMatches `
        'Project EngineAssociation mutation' `
        'project\s*\.\s*EngineAssociation\s*=|with\s*\{[^}]*EngineAssociation\s*='
    $descriptorWriteFiles = @(Get-ChildItem (Join-Path $repositoryRoot 'src') -Recurse -File -Include '*.cs' |
        Where-Object {
            $content = Get-Content -Raw $_.FullName
            $content -match '\.uproject' -and
            $content -match 'File\.WriteAll(?:Text|Bytes)|FileMode\.(?:Create|CreateNew|Truncate|Append)'
        })
    if ($descriptorWriteFiles.Count -gt 0) {
        throw ".uproject write APIs found in: $($descriptorWriteFiles.FullName -join ', ')"
    }
    Write-Host 'PASS: .uproject write APIs'
    Assert-NoSourceMatches `
        'External telemetry / remote logging' `
        '\b(?:TelemetryClient|ApplicationInsights|OpenTelemetry|Sentry|HttpClient)\b'

    $processStartMatches = @(Get-SourceMatches '\bProcess\.Start\s*\(')
    $allowedProcessStart = Join-Path $repositoryRoot 'src\UProjectHub.Windows\Launching\ProcessLauncher.cs'
    $unexpectedProcessStarts = @($processStartMatches | Where-Object {
        $_.Path -ne $allowedProcessStart
    })
    if ($unexpectedProcessStarts.Count -gt 0) {
        throw 'Process.Start exists outside ProcessLauncher.cs.'
    }
    Write-Host 'PASS: Process.Start is isolated to ProcessLauncher.cs.'

    $coordinatorPath = Join-Path $repositoryRoot 'src\UProjectHub.App\Services\ApplicationCoordinator.cs'
    $coordinator = Get-Content -Raw $coordinatorPath
    $startMethod = [regex]::Match(
        $coordinator,
        '(?s)public async Task StartAsync\(.*?(?=public async Task<bool> RefreshAsync\()')
    if (-not $startMethod.Success) {
        throw 'Could not locate ApplicationCoordinator.StartAsync for startup safety review.'
    }
    if ($startMethod.Value -match '\bRescanAsync\s*\(') {
        throw 'ApplicationCoordinator.StartAsync invokes RescanAsync.'
    }
    Write-Host 'PASS: startup path does not invoke full Rescan.'

    Write-Host 'PASS: forbidden-pattern safety checks'
}

Push-Location $repositoryRoot
try {
    Invoke-NativeStep 'dotnet restore' {
        & dotnet restore $solutionPath
    }
    Invoke-NativeStep 'dotnet test (full solution)' {
        & dotnet test $solutionPath --no-restore
    }
    Invoke-NativeStep 'dotnet build Release' {
        & dotnet build $solutionPath -c Release --no-restore
    }
    Invoke-NativeStep 'git diff --check' {
        & git diff --check
    }
    Invoke-SafetyChecks

    Write-Host ""
    Write-Host 'UProject Hub verification passed.'
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Pop-Location
}

exit 0
