param(
    [Parameter(Mandatory=$true)][string]$DataDirectory,
    [string]$OutputDirectory,
    [switch]$Native,
    [ValidateRange(3,120)][int]$Seconds=30
)
. "$PSScriptRoot/common.ps1"
$privateRoot=[IO.Path]::GetFullPath($DataDirectory)
$softwareRoot=[IO.Path]::GetFullPath($projectRoot).TrimEnd('\')+'\'
if($privateRoot.TrimEnd('\') -eq $projectRoot.TrimEnd('\') -or $privateRoot.StartsWith($softwareRoot,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Instrument checks require a private directory outside the software checkout.'
}
if(-not (Test-Path -LiteralPath $privateRoot -PathType Container)) {
    $privateParent=Split-Path -Parent $privateRoot
    if(-not (Test-Path -LiteralPath $privateParent -PathType Container)){throw 'The selected personal parent directory is unavailable.'}
    New-Item -ItemType Directory -Path $privateRoot | Out-Null
}
$attempt=Join-Path $privateRoot ('instrument-run-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $attempt | Out-Null
if(-not $OutputDirectory){$OutputDirectory=Join-Path $attempt 'reports'}
$outputRoot=[IO.Path]::GetFullPath($OutputDirectory)
if($outputRoot.TrimEnd('\') -eq $projectRoot.TrimEnd('\') -or $outputRoot.StartsWith($softwareRoot,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Instrument reports must stay outside the software checkout.'
}
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
if(-not $Native) {
    Invoke-Dotnet test tests/LumaLyrics.Core.Tests -c Release --filter 'FullyQualifiedName~Instrument'
    Invoke-Dotnet test tests/LumaLyrics.Windows.Tests -c Release
    Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~Instrument'
    Write-Output 'Deterministic instrument checks passed; no live audio was captured.'
    return
}
Invoke-Dotnet build tools/LumaLyrics.Probe -c Release
$probeDll=Join-Path $projectRoot 'tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll'
$arguments=@(('"'+$probeDll+'"'),'--probe-instrument-audio','--data-dir',('"'+$attempt+'"'),'--seconds',"$Seconds")
$ownedProbe=Start-Process -FilePath $script:Dotnet -ArgumentList $arguments -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $outputRoot 'stdout.log') -RedirectStandardError (Join-Path $outputRoot 'stderr.log')
$ownedHandle=$ownedProbe.Handle
function Stop-OwnedProbeTree {
    if(-not $ownedProbe.HasExited) {
        # Capture runs in a child helper. Windows PowerShell 5 lacks Kill(bool),
        # so terminate only the tree rooted at the process launched above.
        & "$env:SystemRoot/System32/taskkill.exe" /PID $ownedProbe.Id /T /F | Out-Null
        $ownedProbe.WaitForExit(3000) | Out-Null
    }
}
try {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while(-not $ownedProbe.WaitForExit(1000)) {
        if($watch.Elapsed.TotalSeconds -gt $Seconds+15) {
            Stop-OwnedProbeTree
            throw 'The isolated native probe exceeded its deadline; private stage logs were retained.'
        }
    }
    $ownedProbe.WaitForExit()
    if($ownedProbe.ExitCode -ne 0){throw 'The native probe failed; inspect its private diagnostic logs.'}
    $reportPath=Join-Path $attempt 'instrument-audio-probe.json'
    if(-not (Test-Path -LiteralPath $reportPath)){throw 'The native probe did not produce a report.'}
    $report=Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath $reportPath -Destination (Join-Path $outputRoot 'instrument-native-check.json')
    [pscustomobject]@{Status=$report.status;Packets=$report.packets;EffectiveChunks=$report.effectiveChunks;PrivateReports=$outputRoot}
    if($report.status -ne 'ObservedAudio'){throw 'Live Apple Music signal validation did not pass; see the private report status.'}
} finally {
    Stop-OwnedProbeTree
    $ownedProbe.Dispose()
}
