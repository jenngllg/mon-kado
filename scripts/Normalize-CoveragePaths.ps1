param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,

    [Parameter(Mandatory = $true)]
    [string] $SourceRoot,

    [Parameter(Mandatory = $true)]
    [string] $TargetRoot
)

$ErrorActionPreference = 'Stop'
$sourcePrefix = $SourceRoot.Replace('\', '/').TrimEnd('/') + '/'
$targetPrefix = [System.IO.Path]::GetFullPath($TargetRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter coverage.opencover.xml)

if ($reports.Count -eq 0) {
    throw 'No OpenCover reports were found to normalize.'
}

foreach ($report in $reports) {
    [xml] $document = Get-Content -LiteralPath $report.FullName -Raw

    foreach ($file in $document.CoverageSession.Modules.Module.Files.File) {
        $sourcePath = ([string] $file.fullPath).Replace('\', '/')

        if (!$sourcePath.StartsWith($sourcePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'A coverage source path is outside the original checkout.'
        }

        $relativePath = $sourcePath.Substring($sourcePrefix.Length)

        if (($relativePath.Split('/') | Where-Object { $_ -in '.', '..', '' }).Count -gt 0) {
            throw 'A coverage source path contains an invalid segment.'
        }

        $targetPath = [System.IO.Path]::GetFullPath((Join-Path $targetPrefix $relativePath))

        if (!$targetPath.StartsWith($targetPrefix, [System.StringComparison]::Ordinal)) {
            throw 'A normalized coverage source path would escape the checkout.'
        }

        $file.fullPath = $targetPath
    }

    # Keep every module, sequence point, branch point, and visit count unchanged.
    $document.Save($report.FullName)
}

Write-Output "Normalized source paths in $($reports.Count) OpenCover report(s)."
