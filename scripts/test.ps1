. "$PSScriptRoot\common.ps1"
Invoke-Dotnet test -c Release --logger 'trx;LogFilePrefix=results' --results-directory TestResults
