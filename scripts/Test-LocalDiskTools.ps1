#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$passed = 0
function Assert-True([bool] $Condition, [string] $Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $script:passed++
    Write-Host "PASS: $Name"
}
function Assert-Throws([scriptblock] $Action, [string] $Message, [string] $Name) {
    $caught = $null
    try { & $Action | Out-Null } catch { $caught = $_.Exception.Message }
    Assert-True ($caught -and $caught.Contains($Message)) $Name
}
function Invoke-TestGit([string[]] $Arguments) {
    & git -C $script:fixtureRoot @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Fixture Git command failed: $Arguments" }
}

$diskCheck = Join-Path $PSScriptRoot 'Test-LocalDiskSpace.ps1'
$cleanup = Join-Path $PSScriptRoot 'Clear-CompletedBuilds.ps1'
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($PSScriptRoot))
$tempDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot([IO.Path]::GetTempPath()))
$aboveFree = [Math]::Ceiling([Math]::Max($drive.AvailableFreeSpace, $tempDrive.AvailableFreeSpace) / 1GB) + 10
Assert-Throws { & $diskCheck -MinimumGiB $aboveFree -WarningGiB $aboveFree } 'Local build refused' 'Insufficient space blocks without filling the drive'
Assert-Throws { & $diskCheck -MinimumGiB 0 } 'Thresholds' 'Invalid thresholds are rejected'
& $diskCheck -MinimumGiB 0.001 -WarningGiB 0.001
Assert-True $true 'Adequate space allows the operation'
& $diskCheck -MinimumGiB 0.001 -WarningGiB $aboveFree -WarningVariable observedWarning
Assert-True (@($observedWarning).Count -eq 1) 'Low headroom produces a warning'

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('monkado-disk-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
try {
    $project = Join-Path $fixtureRoot 'src/Example'
    New-Item -ItemType Directory -Path $project | Out-Null
    [IO.File]::WriteAllText((Join-Path $project 'Example.csproj'), '<Project />')
    [IO.File]::WriteAllText((Join-Path $fixtureRoot '.gitignore'), "**/bin/`n**/obj/`nTestResults/`n")
    Invoke-TestGit @('init', '--quiet')
    Invoke-TestGit @('add', '.')
    Invoke-TestGit @('-c', 'user.name=Disk Tools Test', '-c', 'user.email=test@example.invalid', 'commit', '-qm', 'Fixture')
    Invoke-TestGit @('update-ref', 'refs/remotes/origin/develop', 'HEAD')
    $bin = Join-Path $project 'bin'
    $obj = Join-Path $project 'obj'
    New-Item -ItemType Directory -Path $bin, $obj | Out-Null
    [IO.File]::WriteAllText((Join-Path $bin 'generated.txt'), 'generated')
    $nestedGit = Join-Path $bin '.git'
    New-Item -ItemType Directory -Path $nestedGit | Out-Null
    Assert-Throws { & $cleanup -Worktree $fixtureRoot -Apply } 'nested Git checkout' 'Nested Git sources are refused'
    Remove-Item -LiteralPath $nestedGit -Force
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'TestResults/nested-worktree') | Out-Null
    $protectedSource = Join-Path $fixtureRoot 'TestResults/nested-worktree/source.txt'
    [IO.File]::WriteAllText($protectedSource, 'preserve')
    $preview = & $cleanup -Worktree $fixtureRoot
    Assert-True ($preview.OutputDirectories -eq 2 -and -not $preview.Applied -and (Test-Path -LiteralPath $bin)) 'Dry run preserves output'
    & $cleanup -Worktree $fixtureRoot -Apply -WhatIf | Out-Null
    Assert-True (Test-Path -LiteralPath $bin) 'WhatIf preserves output'
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'local-change.txt'), 'user change')
    Assert-Throws { & $cleanup -Worktree $fixtureRoot -Apply } 'local changes' 'Dirty checkout is refused'
    Remove-Item -LiteralPath (Join-Path $fixtureRoot 'local-change.txt')
    Invoke-TestGit @('update-ref', '-d', 'refs/remotes/origin/develop')
    Assert-Throws { & $cleanup -Worktree $fixtureRoot -Apply 2>$null } 'not merged' 'Unmerged checkout is refused'
    Invoke-TestGit @('update-ref', 'refs/remotes/origin/develop', 'HEAD')
    Assert-Throws { & $cleanup -Worktree $project -Apply } 'exact root' 'Nested checkout path is refused'
    Invoke-TestGit @('add', '-f', 'src/Example/bin/generated.txt')
    Invoke-TestGit @('-c', 'user.name=Disk Tools Test', '-c', 'user.email=test@example.invalid', 'commit', '-qm', 'Tracked output fixture')
    Invoke-TestGit @('update-ref', 'refs/remotes/origin/develop', 'HEAD')
    Assert-Throws { & $cleanup -Worktree $fixtureRoot -Apply } 'tracked files' 'Tracked output is refused'
    Invoke-TestGit @('rm', '--cached', 'src/Example/bin/generated.txt')
    Invoke-TestGit @('-c', 'user.name=Disk Tools Test', '-c', 'user.email=test@example.invalid', 'commit', '-qm', 'Untrack generated output')
    Invoke-TestGit @('update-ref', 'refs/remotes/origin/develop', 'HEAD')
    if ($IsWindows) {
        Remove-Item -LiteralPath $obj
        New-Item -ItemType Junction -Path $obj -Target (Join-Path $fixtureRoot 'TestResults/nested-worktree') | Out-Null
        Assert-Throws { & $cleanup -Worktree $fixtureRoot -Apply } 'filesystem links' 'Filesystem links are refused without deleting their target'
        Remove-Item -LiteralPath $obj -Force
        New-Item -ItemType Directory -Path $obj | Out-Null
    }
    $applied = & $cleanup -Worktree $fixtureRoot -Apply
    Assert-True ($applied.Applied -and -not (Test-Path -LiteralPath $bin) -and -not (Test-Path -LiteralPath $obj)) 'Explicit apply removes only outputs'
    Assert-True ((Test-Path -LiteralPath $protectedSource) -and (Test-Path -LiteralPath (Join-Path $project 'Example.csproj'))) 'Sources and nested report worktrees survive'

    # Exercise MSBuild integration without restoring packages, compiling or allocating disk space.
    $targetsPath = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot '../Directory.Build.targets'))
    $msbuildFixture = Join-Path $fixtureRoot 'disk-tools.proj'
    $msbuildXml = '<Project><ItemGroup><ReferenceCopyLocalPaths Include="native/libSkiaSharp.pdb" /><ReferenceCopyLocalPaths Include="native/libSkiaSharp.dll" /><ReferenceCopyLocalPaths Include="managed/Application.pdb" /></ItemGroup><Import Project="{0}" /></Project>' -f $targetsPath
    [IO.File]::WriteAllText($msbuildFixture, $msbuildXml)
    $selection = & dotnet msbuild $msbuildFixture -t:OmitLocalSkiaNativeSymbols -p:OS=Windows_NT '-p:CI=' -p:LocalDiskOptimizedBuild=true -getItem:ReferenceCopyLocalPaths -nologo | ConvertFrom-Json
    Assert-True ($LASTEXITCODE -eq 0 -and $selection.Items.ReferenceCopyLocalPaths.Count -eq 2 -and
        $selection.Items.ReferenceCopyLocalPaths.Identity -contains 'managed/Application.pdb' -and
        $selection.Items.ReferenceCopyLocalPaths.Identity -contains 'native/libSkiaSharp.dll') 'Native symbols are omitted but managed symbols and native binaries survive'
    $selection = & dotnet msbuild $msbuildFixture -t:OmitLocalSkiaNativeSymbols -p:OS=Windows_NT -p:CI=true -p:LocalDiskOptimizedBuild=true -getItem:ReferenceCopyLocalPaths -nologo | ConvertFrom-Json
    Assert-True ($LASTEXITCODE -eq 0 -and $selection.Items.ReferenceCopyLocalPaths.Count -eq 3) 'CI symbol selection is unchanged'
    $selection = & dotnet msbuild $msbuildFixture -t:OmitLocalSkiaNativeSymbols -p:OS=Windows_NT '-p:CI=' -getItem:ReferenceCopyLocalPaths -nologo | ConvertFrom-Json
    Assert-True ($LASTEXITCODE -eq 0 -and $selection.Items.ReferenceCopyLocalPaths.Count -eq 3) 'Native debugging is available without the optimization flag'
    foreach ($skipCondition in @('-p:CI=true', '-p:OS=Unix', '-p:DesignTimeBuild=true')) {
        & dotnet msbuild $msbuildFixture -t:CheckLocalDiskSpace -p:OS=Windows_NT '-p:CI=' $skipCondition "-p:LocalBuildMinimumFreeGiB=$aboveFree" "-p:LocalBuildWarningFreeGiB=$aboveFree" -nologo -v:quiet | Out-Null
        Assert-True ($LASTEXITCODE -eq 0) "Guard is skipped for $skipCondition"
    }
    if ($IsWindows) {
        $failure = & dotnet msbuild $msbuildFixture -t:CheckLocalDiskSpace -p:OS=Windows_NT '-p:CI=' "-p:LocalBuildMinimumFreeGiB=$aboveFree" "-p:LocalBuildWarningFreeGiB=$aboveFree" -nologo -v:quiet 2>&1 | Out-String
        Assert-True ($LASTEXITCODE -ne 0 -and $failure.Contains('MSB3073')) 'Insufficient space fails the MSBuild target'
    }
} finally {
    $resolvedFixture = (Resolve-Path -LiteralPath $fixtureRoot).Path
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
    if (-not $resolvedFixture.StartsWith($resolvedTemp + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedFixture) -notmatch '^monkado-disk-test-[a-f0-9]{32}$') {
        throw 'Refusing to remove an unexpected test fixture path.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
Write-Host "$passed disk-tool checks passed."
# All expected subprocess failures were asserted above. Do not leak their exit codes to CI's pwsh epilogue.
$global:LASTEXITCODE = 0
