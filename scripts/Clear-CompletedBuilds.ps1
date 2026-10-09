#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $Worktree,
    [int] $MergedPullRequest = 0,
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Worktree).Path.TrimEnd('\', '/')
$ancestor = Get-Item -LiteralPath $root
while ($null -ne $ancestor) {
    if (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing a linked worktree path.' }
    $ancestor = $ancestor.Parent
}
$gitRoot = (& git -C $root rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot).TrimEnd('\', '/') -ne $root) {
    throw 'Worktree must be the exact root of a Git checkout.'
}
if (@(& git -C $root status --porcelain).Count -ne 0 -or $LASTEXITCODE -ne 0) {
    throw 'The worktree has local changes. Preserve it until they are reviewed.'
}
$head = & git -C $root rev-parse HEAD
if ($MergedPullRequest -gt 0) {
    Push-Location $root
    try {
        $pullRequestJson = & gh pr view $MergedPullRequest --json state,headRefOid
        if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the merged pull request.' }
        $pullRequest = $pullRequestJson | ConvertFrom-Json
        if ($pullRequest.state -ne 'MERGED' -or $pullRequest.headRefOid -ne $head) {
            throw 'The pull request is not merged at this exact worktree revision.'
        }
    } finally { Pop-Location }
} else {
    & git -C $root merge-base --is-ancestor HEAD origin/develop
    if ($LASTEXITCODE -ne 0) { throw 'HEAD is not merged into origin/develop. Fetch or provide the merged PR number for a squash merge.' }
}
if ($IsWindows) {
    $activeProcesses = @(Get-CimInstance Win32_Process | Where-Object {
        $_.ProcessId -ne $PID -and $_.Name -match '^(dotnet|node|testhost|MSBuild|VBCSCompiler)' -and
        $_.CommandLine -and $_.CommandLine.Replace('/', '\').Contains($root, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($activeProcesses.Count -gt 0) { throw 'A runtime or build still references this worktree.' }
}
$targets = [Collections.Generic.List[string]]::new()
$bytes = 0L
foreach ($area in @('src', 'tests')) {
    $areaPath = Join-Path $root $area
    if (-not (Test-Path -LiteralPath $areaPath -PathType Container)) { continue }
    if (((Get-Item -LiteralPath $areaPath).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Refusing a linked source/test directory.'
    }
    foreach ($project in Get-ChildItem -LiteralPath $areaPath -Directory) {
        if (($project.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing a linked project directory.' }
        if (@(Get-ChildItem -LiteralPath $project.FullName -Filter '*.csproj' -File).Count -eq 0) { continue }
        foreach ($name in @('bin', 'obj')) {
            $path = Join-Path $project.FullName $name
            if (-not (Test-Path -LiteralPath $path -PathType Container)) { continue }
            $path = (Resolve-Path -LiteralPath $path).Path
            if (-not $path.StartsWith($areaPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Output path escapes the approved project area.'
            }
            $entries = @((Get-Item -LiteralPath $path)) + @(Get-ChildItem -LiteralPath $path -Recurse -Force)
            if (@($entries | Where-Object { $_.Name -eq '.git' }).Count -gt 0) {
                throw 'Refusing output containing a nested Git checkout.'
            }
            if (@($entries | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count -gt 0) {
                throw 'Refusing an output containing filesystem links.'
            }
            $relative = [IO.Path]::GetRelativePath($root, $path).Replace('\', '/')
            $tracked = @(& git -C $root ls-files -- $relative)
            if ($LASTEXITCODE -ne 0 -or $tracked.Count -gt 0) { throw 'Refusing output containing tracked files.' }
            foreach ($entry in $entries) { if (-not $entry.PSIsContainer) { $bytes += $entry.Length } }
            $targets.Add($path)
        }
    }
}
# Validate the entire plan before deleting anything. Sources, reports, volumes and worktrees are never targets.
foreach ($target in $targets) {
    if ($Apply -and $PSCmdlet.ShouldProcess($target, 'Remove regenerable project output')) {
        Remove-Item -LiteralPath $target -Recurse -Force
    } else { Write-Information "Would remove: $target" -InformationAction Continue }
}
[pscustomobject]@{
    Worktree = $root
    OutputDirectories = $targets.Count
    LogicalGiB = [Math]::Round($bytes / 1GB, 2)
    Applied = [bool]$Apply -and -not [bool]$WhatIfPreference
}
