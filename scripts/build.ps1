. "$PSScriptRoot\common.ps1"
Invoke-Dotnet restore --locked-mode
Invoke-Dotnet build -c Release --no-restore
