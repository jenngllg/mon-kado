. "$PSScriptRoot/Native-Environment.ps1"
Set-Location -LiteralPath $MonKadoRoot
$env:CGO_ENABLED = '0'
$outputDirectory = Join-Path $MonKadoRoot '.local/bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
foreach ($service in @('api', 'worker', 'migrate')) {
    Invoke-NativeGo @('build', '-trimpath', '-ldflags=-s -w', '-o', (Join-Path $outputDirectory "$service.exe"), "./cmd/$service")
}
Invoke-NativeGo @('-C', 'src/Tools.GmailOAuthBootstrap', 'build', '-trimpath', '-ldflags=-s -w', '-o', (Join-Path $outputDirectory 'gmail-oauth-bootstrap.exe'), '.')
Write-Host "Native binaries are available in $outputDirectory."
