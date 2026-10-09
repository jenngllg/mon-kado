#Requires -Version 7.0
param([string] $ReportPath = 'TestResults/local-disk-coverage.xml')

$ErrorActionPreference = 'Stop'
Import-Module Pester -RequiredVersion 5.7.1
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$configuration = New-PesterConfiguration
$configuration.Run.Path = Join-Path $PSScriptRoot 'LocalDiskTools.Tests.ps1'
$configuration.Run.PassThru = $true
$configuration.CodeCoverage.Enabled = $true
$configuration.CodeCoverage.Path = @(
    (Join-Path $repository 'scripts/Clear-CompletedBuilds.ps1'),
    (Join-Path $repository 'scripts/Invoke-LocalDotNet.ps1'),
    (Join-Path $repository 'scripts/Test-LocalDiskSpace.ps1')
)
$configuration.CodeCoverage.CoveragePercentTarget = 100
$configuration.CodeCoverage.OutputPath = Join-Path $repository 'TestResults/local-disk-jacoco.xml'
$configuration.Output.Verbosity = 'Detailed'
$result = Invoke-Pester -Configuration $configuration
if ($result.FailedCount -gt 0) { throw 'Disk-tool tests failed.' }
$result.CodeCoverage.CommandsMissed | Format-Table File,Line,Command -AutoSize
if ($IsWindows -and $result.CodeCoverage.CommandsMissedCount -ne 0) { throw 'Windows disk tooling must cover every command.' }
$document = [xml]'<coverage version="1" />'
$commands = @($result.CodeCoverage.CommandsExecuted) + @($result.CodeCoverage.CommandsMissed)
foreach ($fileGroup in $commands | Group-Object File) {
    $file = $document.CreateElement('file')
    $file.SetAttribute('path', [IO.Path]::GetRelativePath($repository, $fileGroup.Name).Replace('\', '/'))
    foreach ($lineGroup in $fileGroup.Group | Group-Object Line) {
        $line = $document.CreateElement('lineToCover')
        $line.SetAttribute('lineNumber', $lineGroup.Name)
        $covered = @($lineGroup.Group | Where-Object { $_.HitCount -gt 0 }).Count -gt 0
        $line.SetAttribute('covered', $covered.ToString().ToLowerInvariant())
        $file.AppendChild($line) | Out-Null
    }
    $document.DocumentElement.AppendChild($file) | Out-Null
}
$report = [IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($report)) -Force | Out-Null
$document.Save($report)
$global:LASTEXITCODE = 0
