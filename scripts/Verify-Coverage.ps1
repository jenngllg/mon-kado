param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory
)

$requiredModules = @(
    'JennGllg.Fr.MonKado.Back.Api',
    'JennGllg.Fr.MonKado.Back.Application',
    'JennGllg.Fr.MonKado.Back.Domain',
    'JennGllg.Fr.MonKado.Back.Infrastructure.Images',
    'JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql',
    'JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap',
    'JennGllg.Fr.MonKado.Back.Worker'
)
$reports = @(Get-ChildItem -Path $ResultsDirectory -Filter coverage.opencover.xml -Recurse)

if ($reports.Count -eq 0) {
    throw "No OpenCover report was found under '$ResultsDirectory'."
}

$modules = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
$lines = @{}
$branches = @{}

foreach ($report in $reports) {
    [xml] $document = Get-Content -Path $report.FullName

    foreach ($module in $document.CoverageSession.Modules.Module) {
        $moduleName = [string] $module.ModuleName
        [void] $modules.Add($moduleName)
        $files = @{}

        foreach ($file in $module.Files.File) {
            $files[[string] $file.uid] = [string] $file.fullPath
        }

        foreach ($class in $module.Classes.Class) {
            foreach ($method in $class.Methods.Method) {
                $methodName = [string] $method.Name

                foreach ($point in @($method.SequencePoints.SequencePoint)) {
                    if ($null -eq $point) {
                        continue
                    }

                    $path = $files[[string] $point.fileid]

                    if ([string]::IsNullOrWhiteSpace($path)) {
                        continue
                    }

                    $key = "$path|$($point.sl)"
                    $visits = [int] $point.vc

                    if (!$lines.ContainsKey($key) -or $visits -gt $lines[$key].Visits) {
                        $lines[$key] = [pscustomobject]@{
                            Path = $path
                            Line = [int] $point.sl
                            Visits = $visits
                        }
                    }
                }

                foreach ($point in @($method.BranchPoints.BranchPoint)) {
                    if ($null -eq $point) {
                        continue
                    }

                    $path = $files[[string] $point.fileid]

                    if ([string]::IsNullOrWhiteSpace($path)) {
                        continue
                    }

                    $key = "$path|$methodName|$($point.sl)|$($point.offset)|$($point.path)"
                    $visits = [int] $point.vc

                    if (!$branches.ContainsKey($key) -or $visits -gt $branches[$key].Visits) {
                        $branches[$key] = [pscustomobject]@{
                            Path = $path
                            Line = [int] $point.sl
                            Method = $methodName
                            BranchPath = [int] $point.path
                            Visits = $visits
                        }
                    }
                }
            }
        }
    }
}

$missingModules = @($requiredModules | Where-Object { !$modules.Contains($_) })

if ($missingModules.Count -gt 0) {
    throw "Coverage reports are missing required modules: $($missingModules -join ', ')."
}

$lineValues = @($lines.Values)
$branchValues = @($branches.Values)
$coveredLines = @($lineValues | Where-Object { $_.Visits -gt 0 }).Count
$coveredBranches = @($branchValues | Where-Object { $_.Visits -gt 0 }).Count
$lineCoverage = 100 * $coveredLines / [Math]::Max(
    1,
    $lineValues.Count)
$branchCoverage = 100 * $coveredBranches / [Math]::Max(
    1,
    $branchValues.Count)

Write-Output (
    'Coverage: lines {0}/{1} ({2:N2}%), branches {3}/{4} ({5:N2}%).' -f
    $coveredLines,
    $lineValues.Count,
    $lineCoverage,
    $coveredBranches,
    $branchValues.Count,
    $branchCoverage)

if ($coveredLines -ne $lineValues.Count -or $coveredBranches -ne $branchValues.Count) {
    $lineValues |
        Where-Object { $_.Visits -eq 0 } |
        Sort-Object Path, Line |
        ForEach-Object { Write-Error "Uncovered line: $($_.Path):$($_.Line)" }
    $branchValues |
        Where-Object { $_.Visits -eq 0 } |
        Sort-Object Path, Line, BranchPath |
        ForEach-Object {
            Write-Error (
                "Uncovered branch: $($_.Path):$($_.Line), " +
                "path $($_.BranchPath), method $($_.Method)")
        }

    throw 'Coverage must remain at 100% for both lines and branches.'
}
