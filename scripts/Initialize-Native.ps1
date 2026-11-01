param([int] $Port = 55432)
. "$PSScriptRoot/Native-Environment.ps1"
Set-Location -LiteralPath $MonKadoRoot
$localDirectory = Join-Path $MonKadoRoot '.local'
New-Item -ItemType Directory -Path $localDirectory -Force | Out-Null
$dataDirectory = Join-Path $localDirectory 'postgres-data'
$passwordPath = Join-Path $localDirectory 'postgres-password'
$environmentPath = Join-Path $localDirectory 'environment.json'
$randomBytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Fill($randomBytes)
if (!(Test-Path -LiteralPath $passwordPath)) {
    [IO.File]::WriteAllText($passwordPath, [Convert]::ToBase64String($randomBytes))
}
$databasePassword = [IO.File]::ReadAllText($passwordPath)
$pgControl = Get-NativeTool 'pg_ctl'
if (!(Test-Path -LiteralPath (Join-Path $dataDirectory 'PG_VERSION'))) {
    $initialize = Get-NativeTool 'initdb'
    & $initialize '-D' $dataDirectory '-U' 'mon_kado' '-A' 'scram-sha-256' '--pwfile' $passwordPath '--encoding=UTF8' '--locale=C'
    if ($LASTEXITCODE -ne 0) { throw 'Native PostgreSQL initialization failed.' }
}
& $pgControl '-D' $dataDirectory 'status' *> $null
if ($LASTEXITCODE -ne 0) {
    $logPath = Join-Path $localDirectory 'postgres-server.log'
    & $pgControl '-D' $dataDirectory '-l' $logPath '-o' "-h 127.0.0.1 -p $Port" '-w' 'start'
    if ($LASTEXITCODE -ne 0) { throw 'Native PostgreSQL startup failed.' }
}
$env:PGHOST = '127.0.0.1'
$env:PGPORT = [string] $Port
$env:PGUSER = 'mon_kado'
$env:PGPASSWORD = $databasePassword
$psql = Get-NativeTool 'psql'
$createDatabase = Get-NativeTool 'createdb'
foreach ($databaseName in @('mon_kado_go', 'mon_kado_go_test')) {
    $exists = & $psql '-d' 'postgres' '-tAc' "SELECT 1 FROM pg_database WHERE datname='$databaseName'"
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the development databases.' }
    if ($exists -notcontains '1') {
        & $createDatabase $databaseName
        if ($LASTEXITCODE -ne 0) { throw 'Could not create a development database.' }
    }
}
if (!(Test-Path -LiteralPath $environmentPath)) {
    $signingBytes = New-Object byte[] 32
    $encryptionBytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Fill($signingBytes)
    [Security.Cryptography.RandomNumberGenerator]::Fill($encryptionBytes)
    $escapedPassword = [Uri]::EscapeDataString($databasePassword)
    $settings = [ordered]@{
        DATABASE_URL = "postgres://mon_kado:$escapedPassword@127.0.0.1:$Port/mon_kado_go?sslmode=disable"
        MONKADO_TEST_DATABASE_URL = "postgres://mon_kado:$escapedPassword@127.0.0.1:$Port/mon_kado_go_test?sslmode=disable"
        JWT_SIGNING_KEY = [Convert]::ToBase64String($signingBytes)
        DATA_ENCRYPTION_KEY = [Convert]::ToBase64String($encryptionBytes)
        APP_ENV = 'local'
        HTTP_ADDRESS = '127.0.0.1:7000'
        FRONTEND_ORIGIN = 'http://localhost:5173'
        ALLOWED_ORIGINS = 'http://localhost:5173'
        ALLOWED_HOSTS = 'localhost,127.0.0.1'
        EMAIL_PROVIDER = 'Mailbox'
        MAILBOX_PATH = Join-Path $localDirectory 'mailbox'
        IMAGE_PATH = Join-Path $localDirectory 'gift-images'
        EXPORT_PATH = Join-Path $localDirectory 'personal-data-exports'
    }
    [IO.File]::WriteAllText($environmentPath, ($settings | ConvertTo-Json))
}
Import-NativeEnvironment
Invoke-NativeGo @('run', './cmd/migrate')
Write-Host 'Native PostgreSQL and the Go development configuration are ready. Secrets remain in the ignored .local directory.'
