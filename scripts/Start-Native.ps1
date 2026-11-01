param([ValidateSet('api', 'worker')] [string] $Service = 'api')
. "$PSScriptRoot/Native-Environment.ps1"
Set-Location -LiteralPath $MonKadoRoot
Import-NativeEnvironment
Invoke-NativeGo @('run', "./cmd/$Service")
