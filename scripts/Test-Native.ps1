param([switch] $Race, [switch] $Coverage)
. "$PSScriptRoot/Native-Environment.ps1"
Set-Location -LiteralPath $MonKadoRoot
Import-NativeEnvironment
Invoke-NativeGo @('vet', './...')
Invoke-NativeGo @('-C', 'src/Tools.GmailOAuthBootstrap', 'vet', './...')
$arguments = @('test', '-tags=integration')
$toolArguments = @('-C', 'src/Tools.GmailOAuthBootstrap', 'test')
if ($Race) {
    if ([string]::IsNullOrEmpty($env:CC) -and $IsWindows) {
        $toolchains = Join-Path $MonKadoRoot '.local/toolchains'
        if (Test-Path -LiteralPath $toolchains) {
            $compilerDirectories = Get-ChildItem -LiteralPath $toolchains -Directory | Where-Object Name -Like 'llvm-mingw-*' | Sort-Object Name -Descending
            foreach ($directory in $compilerDirectories) {
                $compiler = Join-Path $directory.FullName 'bin/x86_64-w64-mingw32-gcc.exe'
                if (Test-Path -LiteralPath $compiler) { $env:CC = $compiler; break }
            }
        }
    }
    $env:CGO_ENABLED = '1'
    $arguments += '-race'
    $toolArguments += '-race'
}
if ($Coverage) {
    $arguments += @('-covermode=atomic', '-coverpkg=github.com/JennGllg/mon-kado/...', '-coverprofile=coverage.out')
    $toolArguments += '-coverprofile=coverage.out'
}
Invoke-NativeGo ($arguments + './...')
Invoke-NativeGo ($toolArguments + './...')
if ($Coverage) {
    & "$PSScriptRoot/Merge-GoCoverage.ps1"
    & "$PSScriptRoot/Test-GoCoverage.ps1" -Path coverage.out
    & "$PSScriptRoot/Test-GoCoverage.ps1" -Path src/Tools.GmailOAuthBootstrap/coverage.out
}
