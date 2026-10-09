#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('build', 'test', 'restore', 'publish')]
    [string] $Command = 'test',
    [Parameter(Mandatory)]
    [string] $Project,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string[]] $AdditionalArguments = @()
)

$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $Project).Path
& (Join-Path $PSScriptRoot 'Test-LocalDiskSpace.ps1') -Path $projectPath
$arguments = @($Command, $projectPath)
if ($Command -ne 'restore') { $arguments += @('--configuration', $Configuration) }
if ($IsWindows) {
    # Keep local Windows native assets only, without imposing a Windows RID on Linux CI/deployment.
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $arguments += @('--runtime', "win-$architecture", '-p:LocalDiskOptimizedBuild=true')
    if ($Command -in @('build', 'publish')) { $arguments += @('--self-contained', 'false') }
}
$arguments += $AdditionalArguments
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "dotnet $Command failed with exit code $LASTEXITCODE." }
