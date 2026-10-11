param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$minimumPercentage = 80
$covered = 0L
$total = 0L
$blocks = @{}
Get-Content -LiteralPath $Path | Select-Object -Skip 1 | ForEach-Object {
    $fields = $_ -split ' '
    if ($fields.Length -ne 3) { throw 'Invalid Go coverage profile.' }
    $statements = [long]$fields[1]
    $key = $fields[0]
    if ($blocks.ContainsKey($key) -and $blocks[$key].Statements -ne $statements) { throw 'Inconsistent statement count for a coverage block.' }
    $previouslyCovered = $blocks.ContainsKey($key) -and $blocks[$key].Covered
    $blocks[$key] = @{ Statements = $statements; Covered = $previouslyCovered -or [long]$fields[2] -gt 0 }
}
foreach ($block in $blocks.Values) {
    $total += $block.Statements
    if ($block.Covered) { $covered += $block.Statements }
}
if ($total -eq 0) { throw 'Coverage profile contains no executable statements.' }
$percentage = 100 * $covered / $total
Write-Host ('Statement coverage: {0:F2}% ({1}/{2}). Minimum: {3}%.' -f $percentage, $covered, $total, $minimumPercentage)
if ($percentage -lt $minimumPercentage) { throw 'The documented Go statement coverage minimum has not been reached.' }
