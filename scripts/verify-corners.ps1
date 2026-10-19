param([string]$OutputDirectory='artifacts/overlay-corners')
. "$PSScriptRoot\common.ps1"
Invoke-Dotnet restore --locked-mode
Invoke-Dotnet build -c Release --no-restore

$captureDirectory = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$dataDirectory = Join-Path $projectRoot "TestResults/corners-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $captureDirectory,$dataDirectory | Out-Null
@{ OnlineLookup=$false; AutoPrefetchLibrary=$false; OverlayVisible=$true; CloseToTray=$false } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataDirectory 'settings.json') -Encoding utf8
$application = Join-Path $projectRoot 'src/LumaLyrics.App/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.dll'
$arguments = '"{0}" --data-dir "{1}" --capture-dir "{2}" --exit-after-capture' -f $application,$dataDirectory,$captureDirectory
$process = Start-Process -FilePath $script:Dotnet -ArgumentList $arguments -PassThru -NoNewWindow
if (-not $process.WaitForExit(90000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw 'Window verification timed out; inspect the isolated data directory for logs.'
}
if ($process.ExitCode -ne 0) { throw "Window verification exited with code $($process.ExitCode)" }
$checks = Get-Content -LiteralPath (Join-Path $captureDirectory 'corners-check.json') -Raw | ConvertFrom-Json
$failures = @(foreach ($case in $checks.PSObject.Properties) {
    foreach ($check in $case.Value.PSObject.Properties) {
        if ($check.Value -ne $true) { "$($case.Name): $($check.Name)" }
    }
})
if (@($checks.PSObject.Properties).Count -ne 32) { throw 'Expected 32 window corner scenarios.' }
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
Write-Host 'Passed 32 window corner scenarios (native HWND region and whole-window WPF alpha).'
