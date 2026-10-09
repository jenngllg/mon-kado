BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $cleanup = Join-Path $repository 'scripts/Clear-CompletedBuilds.ps1'
}

Describe 'Local disk tools' {
    It 'preserves sources and tests disk guard integration deterministically' {
        & (Join-Path $repository 'scripts/Test-LocalDiskTools.ps1')
        $LASTEXITCODE | Should -Be 0
    }

    Context 'Filesystem boundary defenses' {
        BeforeEach {
            $fixture = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
            $fixtureAreaPath = Join-Path $fixture 'src'
            $fixtureProjectPath = Join-Path $fixtureAreaPath 'Example'
            $fixtureOutputPath = Join-Path $fixtureProjectPath 'bin'
            New-Item -ItemType Directory -Path $fixtureOutputPath | Out-Null
            [IO.File]::WriteAllText((Join-Path $fixtureProjectPath 'Example.csproj'), '<Project />')
            Mock git {
                $global:LASTEXITCODE = 0
                if ($args -contains '--show-toplevel') { return $fixture }
                if ($args -contains 'rev-parse') { return 'fixture-head' }
            }
        }
        It 'rejects a linked source area' {
            Mock Get-Item { [pscustomobject]@{Attributes=[IO.FileAttributes]::ReparsePoint} } -ParameterFilter { $LiteralPath -eq $fixtureAreaPath }
            { & $cleanup -Worktree $fixture -Apply } | Should -Throw '*linked source/test*'
            Test-Path -LiteralPath $fixtureOutputPath | Should -BeTrue
        }
        It 'rejects a linked project directory' {
            Mock Get-ChildItem { [pscustomobject]@{Attributes=[IO.FileAttributes]::ReparsePoint;FullName=$fixtureProjectPath} } -ParameterFilter { $LiteralPath -eq $fixtureAreaPath -and $Directory }
            { & $cleanup -Worktree $fixture -Apply } | Should -Throw '*linked project*'
            Test-Path -LiteralPath $fixtureOutputPath | Should -BeTrue
        }
        It 'rejects a resolved output outside the approved source area' {
            Mock Resolve-Path { [pscustomobject]@{Path=(Join-Path $TestDrive 'outside')} } -ParameterFilter { $LiteralPath -eq $fixtureOutputPath }
            { & $cleanup -Worktree $fixture -Apply } | Should -Throw '*escapes the approved*'
            Test-Path -LiteralPath $fixtureOutputPath | Should -BeTrue
        }
    }
}
