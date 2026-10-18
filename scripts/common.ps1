$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$script:Dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Set-Location -LiteralPath $projectRoot
function Invoke-Dotnet { & $script:Dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $LASTEXITCODE" } }
