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

function Get-SourceFiles {
    $sourceRoot = Join-Path $repositoryRoot 'src'
    return @(Get-ChildItem $sourceRoot -Recurse -File | Where-Object {
        $_.Extension -in '.cs', '.xaml', '.csproj' -and
        $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]'
    })
}

function Get-SourceMatches {
    param(
        [Parameter(Mandatory)]
        [string] $Pattern
    )

    return @(Get-SourceFiles | Select-String -Pattern $Pattern)
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

function Assert-SourceMatchesOnlyInFiles {
    param(
        [Parameter(Mandatory)]
        [string] $Label,

        [Parameter(Mandatory)]
        [string] $Pattern,

        [Parameter(Mandatory)]
        [string[]] $AllowedFiles
    )

    $allowedPaths = @($AllowedFiles | ForEach-Object {
        [System.IO.Path]::GetFullPath($_)
    })
    $matches = @(Get-SourceMatches $Pattern)
    $unexpected = @($matches | Where-Object {
        $allowedPaths -notcontains [System.IO.Path]::GetFullPath($_.Path)
    })
    if ($unexpected.Count -gt 0) {
        $details = $unexpected | ForEach-Object {
            "$($_.Path):$($_.LineNumber):$($_.Line.Trim())"
        }
        throw "$Label failed:`n$($details -join [Environment]::NewLine)"
    }

    Write-Host "PASS: $Label"
}

function Assert-TextContainsPatterns {
    param(
        [Parameter(Mandatory)]
        [string] $Label,

        [Parameter(Mandatory)]
        [string] $Text,

        [Parameter(Mandatory)]
        [string[]] $Patterns
    )

    $missing = @($Patterns | Where-Object { $Text -notmatch $_ })
    if ($missing.Count -gt 0) {
        throw "$Label failed; required safety marker(s) missing: $($missing -join ', ')"
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

    $cleanupPath = Join-Path $repositoryRoot 'src\UProjectHub.Windows\Cleanup\ProjectCleanupService.cs'
    $cleanup = Get-Content -Raw $cleanupPath
    $cleanupDeleteMatches = @(Select-String -Path $cleanupPath -Pattern '\b(?:File|Directory)\.Delete\s*\(')
    $allowedCleanupDeleteLines = @(
        'File.Delete(path);',
        'File.Delete(entry.FullName);',
        'Directory.Delete(directoryPath, recursive: false);'
    )
    $unexpectedCleanupDeletes = @($cleanupDeleteMatches | Where-Object {
        $_.Line.Trim() -notin $allowedCleanupDeleteLines
    })
    if ($unexpectedCleanupDeletes.Count -gt 0) {
        $details = $unexpectedCleanupDeletes | ForEach-Object {
            "$($_.Path):$($_.LineNumber):$($_.Line.Trim())"
        }
        throw "Project Cleanup contains an unapproved delete call:`n$($details -join [Environment]::NewLine)"
    }
    foreach ($expectedDelete in $allowedCleanupDeleteLines) {
        if ($cleanupDeleteMatches.Line.Trim() -notcontains $expectedDelete) {
            throw "Project Cleanup delete boundary changed; expected call missing: $expectedDelete"
        }
    }

    $targetMethod = [regex]::Match(
        $cleanup,
        '(?s)private static string GetDirectoryTarget\(.*?(?=\r?\n    private static void ValidateDirectoryTarget)')
    if (-not $targetMethod.Success) {
        throw 'Could not locate ProjectCleanupService.GetDirectoryTarget.'
    }
    $actualCleanupDirectoryNames = @([regex]::Matches(
        $targetMethod.Value,
        '=>\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object)
    $expectedCleanupDirectoryNames = @(
        '.vs',
        'Binaries',
        'DerivedDataCache',
        'Intermediate'
    ) | Sort-Object
    $targetDifferences = @(Compare-Object `
        $expectedCleanupDirectoryNames `
        $actualCleanupDirectoryNames)
    if ($targetDifferences.Count -gt 0) {
        throw "Project Cleanup directory targets changed outside the approved set:`n$($targetDifferences | Out-String)"
    }
    Assert-TextContainsPatterns `
        'Project Cleanup containment, reparse-point, and top-level solution guards' `
        $cleanup `
        @(
            'ValidateContainedPath\(root, directoryPath\)',
            'RejectReparsePoint\(directoryPath\)',
            '(?s)Path\.GetExtension\(fullPath\).*?"\.sln"',
            '(?s)Path\.GetDirectoryName\(fullPath\).*?root',
            'Directory\.Delete\(directoryPath, recursive: false\)'
        )

    $registrySourceFiles = @(Get-SourceFiles | Where-Object {
        $_.Extension -eq '.cs' -and
        (Get-Content -Raw $_.FullName) -match `
            'Microsoft\.Win32|\bRegistryKey\b|\bRegistry\s*\.'
    })
    $registryWriteMatches = @($registrySourceFiles | Select-String -Pattern `
        '\b(?:SetValue|DeleteValue|DeleteSubKey|DeleteSubKeyTree|CreateSubKey)\s*\(|writable\s*:\s*true')
    if ($registryWriteMatches.Count -gt 0) {
        $details = $registryWriteMatches | ForEach-Object {
            "$($_.Path):$($_.LineNumber):$($_.Line.Trim())"
        }
        throw "Production source contains a Registry write/delete API:`n$($details -join [Environment]::NewLine)"
    }
    Write-Host 'PASS: no Registry write/delete APIs in production source.'

    $generatorPath = Join-Path $repositoryRoot 'src\UProjectHub.Windows\Launching\UnrealProjectFilesGenerator.cs'
    Assert-SourceMatchesOnlyInFiles `
        'UnrealBuildTool executable selection is isolated to UnrealProjectFilesGenerator' `
        '["'']UnrealBuildTool\.exe["'']' `
        @($generatorPath)
    $generator = Get-Content -Raw $generatorPath
    Assert-TextContainsPatterns `
        'Generate Project Files uses the external process boundary' `
        $generator `
        @(
            'new ExternalProcessRequest\(',
            '_processRunner\.RunAsync\(',
            'UnrealBuildTool\.exe'
        )
    Assert-NoSourceMatches `
        'Unsupported shell/batch fallback' `
        '(?i)\b(?:cmd(?:\.exe)?|powershell(?:\.exe)?|pwsh(?:\.exe)?)\b|\.(?:bat|cmd)["'']'

    $externalRunnerPath = Join-Path $repositoryRoot 'src\UProjectHub.Windows\Launching\ExternalProcessRunner.cs'
    Assert-SourceMatchesOnlyInFiles `
        'Process-tree termination is isolated to ExternalProcessRunner' `
        '\.Kill\s*\(' `
        @($externalRunnerPath)
    Assert-NoSourceMatches `
        'Thread abort APIs' `
        '\bThread\s*\.\s*Abort\s*\(|\.Abort\s*\('
    $externalRunner = Get-Content -Raw $externalRunnerPath
    Assert-TextContainsPatterns `
        'Generate cancellation uses bounded process cleanup' `
        $externalRunner `
        @(
            'CancellationCleanupTimeout\s*=\s*TimeSpan\.FromSeconds\([1-9][0-9]*\)',
            'process\.Kill\(entireProcessTree: true\)',
            'WaitForCancellationCleanupAsync\(',
            'cleanupTask\.WaitAsync\(timeout\)'
        )

    Assert-NoSourceMatches `
        'FileSystemWatcher' `
        '\bFileSystemWatcher\b'
    Assert-NoSourceMatches `
        'Project EngineAssociation mutation' `
        'project\s*\.\s*EngineAssociation\s*=|with\s*\{[^}]*EngineAssociation\s*='
    $descriptorWriteFiles = @(Get-SourceFiles | Where-Object { $_.Extension -eq '.cs' } |
        Where-Object {
            $content = Get-Content -Raw $_.FullName
            $content -match '\.uproject' -and
            $content -match 'File\.(?:WriteAllText|WriteAllBytes|WriteAllLines|AppendAllText|AppendAllLines|Create|CreateText|OpenWrite|Move|Replace|Copy)|new\s+FileStream|FileMode\.(?:Create|CreateNew|Truncate|Append)'
        })
    if ($descriptorWriteFiles.Count -gt 0) {
        throw ".uproject write APIs found in: $($descriptorWriteFiles.FullName -join ', ')"
    }
    Write-Host 'PASS: .uproject write APIs'
    Assert-NoSourceMatches `
        'External telemetry / remote logging' `
        '\b(?:TelemetryClient|ApplicationInsights|OpenTelemetry|Sentry|HttpClient)\b'

    Assert-SourceMatchesOnlyInFiles `
        'Process start is isolated to launch/process-runner boundaries' `
        '\bProcess\.Start\s*\(' `
        @(
            (Join-Path $repositoryRoot 'src\UProjectHub.Windows\Launching\ProcessLauncher.cs'),
            $externalRunnerPath
        )

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
