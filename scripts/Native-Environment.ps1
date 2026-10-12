$ErrorActionPreference = 'Stop'
$MonKadoRoot = Split-Path $PSScriptRoot -Parent

function Get-NativeTool([string] $Name) {
    $bundled = Join-Path $MonKadoRoot ".local/toolchains/go/bin/$Name.exe"
    if (Test-Path -LiteralPath $bundled) { return $bundled }
    $bundled = Join-Path $MonKadoRoot ".local/toolchains/pgsql/bin/$Name.exe"
    if (Test-Path -LiteralPath $bundled) { return $bundled }
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    throw "Native executable '$Name' was not found. Install Go and PostgreSQL, or add their bin directories to PATH."
}

function Import-NativeEnvironment {
    $path = Join-Path $MonKadoRoot '.local/environment.json'
    if (!(Test-Path -LiteralPath $path)) { throw 'Run scripts/Initialize-Native.ps1 first.' }
    $settings = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    foreach ($property in $settings.PSObject.Properties) {
        if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($property.Name, 'Process'))) {
            [Environment]::SetEnvironmentVariable($property.Name, [string] $property.Value, 'Process')
        }
    }
}

function Invoke-NativeGo([string[]] $Arguments) {
    $go = Get-NativeTool 'go'
    & $go @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Go command failed with exit code $LASTEXITCODE." }
}
