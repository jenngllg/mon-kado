param(
    [string[]] $Paths = @('coverage.out', 'src/Tools.GmailOAuthBootstrap/coverage.out'),
    [string] $Destination = 'coverage.sonar.out'
)
$ErrorActionPreference = 'Stop'
$blocks = @{}
foreach ($profile in $Paths) {
    Get-Content -LiteralPath $profile | Select-Object -Skip 1 | ForEach-Object {
        $fields = $_ -split ' '
        if ($fields.Length -ne 3) { throw 'Invalid Go coverage profile.' }
        $location = $fields[0]
        $toolPrefix = 'github.com/JennGllg/mon-kado/gmail-oauth-bootstrap/'
        $modulePrefix = 'github.com/JennGllg/mon-kado/'
        if ($location.StartsWith($toolPrefix, [StringComparison]::Ordinal)) {
            $location = 'src/Tools.GmailOAuthBootstrap/' + $location.Substring($toolPrefix.Length)
        } elseif ($location.StartsWith($modulePrefix, [StringComparison]::Ordinal)) {
            $location = $location.Substring($modulePrefix.Length)
        } else {
            throw 'Coverage contains a package outside the MonKado modules. Select the module import path explicitly.'
        }
        $statements = [long]$fields[1]
        if ($blocks.ContainsKey($location) -and $blocks[$location].Statements -ne $statements) { throw 'Inconsistent statement count for a coverage block.' }
        $previouslyCovered = $blocks.ContainsKey($location) -and $blocks[$location].Covered
        $blocks[$location] = @{ Statements = $statements; Covered = $previouslyCovered -or [long]$fields[2] -gt 0 }
    }
}
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('mode: set')
foreach ($location in ($blocks.Keys | Sort-Object)) {
    $block = $blocks[$location]
    $count = 0
    if ($block.Covered) { $count = 1 }
    $lines.Add(('{0} {1} {2}' -f $location, $block.Statements, $count))
}
[IO.File]::WriteAllLines([IO.Path]::GetFullPath($Destination), $lines, [Text.UTF8Encoding]::new($false))
