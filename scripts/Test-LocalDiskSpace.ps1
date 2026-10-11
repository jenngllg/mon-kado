[CmdletBinding()]
param(
    [string] $Path,
    [double] $MinimumGiB = 10,
    [double] $WarningGiB = 20
)

$ErrorActionPreference = 'Stop'
if (-not $Path) { $Path = Join-Path $PSScriptRoot '..' }
if ($MinimumGiB -le 0 -or $WarningGiB -lt $MinimumGiB) {
    throw 'Thresholds must be positive and warning must be at least the minimum.'
}
$resolved = (Resolve-Path -LiteralPath $Path).Path
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($resolved))
$freeGiB = $drive.AvailableFreeSpace / 1GB
if ($freeGiB -lt $MinimumGiB) {
    throw "Local build refused: $([Math]::Round($freeGiB, 2)) GiB free on $($drive.Name); at least $MinimumGiB GiB required. Clean completed build outputs first."
}
if ($freeGiB -lt $WarningGiB) {
    Write-Warning "Only $([Math]::Round($freeGiB, 2)) GiB free on $($drive.Name); recommended headroom is $WarningGiB GiB."
}
Write-Host "Disk check: $([Math]::Round($freeGiB, 2)) GiB available on $($drive.Name)."
