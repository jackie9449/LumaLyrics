param([ValidateSet('win-x64')][string]$Runtime='win-x64')
. "$PSScriptRoot\common.ps1"
$packageName = "LumaLyrics-0.3.2-preview-$Runtime"
$destination = Join-Path $projectRoot "artifacts/$packageName"
$probeDestination = Join-Path $projectRoot "artifacts/Probe-0.3.2-$Runtime"
Invoke-Dotnet publish src/LumaLyrics.App -c Release -r $Runtime --self-contained true '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' -o $destination
Invoke-Dotnet publish tools/LumaLyrics.Probe -c Release -r $Runtime --self-contained true -o $probeDestination
# A standard Microsoft dotnet muxer is an alternate entry point for diagnostics.
# The published application's runtime is still entirely app-local; no SDK is shipped.
if ($Runtime -eq 'win-x64') {
    $dotnetRoot = Split-Path -Parent $script:Dotnet
    $fxr = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'host\fxr') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $runtimeDir = Join-Path $destination 'runtime'
    $fxrDir = Join-Path $runtimeDir "host\fxr\$($fxr.Name)"
    New-Item -ItemType Directory -Force -Path $fxrDir | Out-Null
    Copy-Item -LiteralPath $script:Dotnet -Destination (Join-Path $runtimeDir 'dotnet.exe') -Force
    Copy-Item -LiteralPath (Join-Path $fxr.FullName 'hostfxr.dll') -Destination $fxrDir -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Launch.vbs') -Destination (Join-Path $destination 'Launch.vbs') -Force
}
Copy-Item -LiteralPath README.md,THIRD-PARTY-NOTICES.md -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Probe.cmd') -Destination $destination -Force
Get-ChildItem -LiteralPath $probeDestination -Filter 'LumaLyrics.Probe.*' -File | Copy-Item -Destination $destination -Force
foreach ($file in Get-ChildItem -LiteralPath docs -Recurse -File) {
    if ($file.Extension -notin @('.md','.png') -and $file.Name -notin @('runtime-check.json','verification.json','appearance-check.json','motion-check.json','corners-check.json','probe-redacted.jsonl')) { continue }
    $relative = [IO.Path]::GetRelativePath((Join-Path $projectRoot 'docs'), $file.FullName)
    $target = Join-Path $destination "docs/$relative"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
Copy-Item -LiteralPath licenses -Destination $destination -Recurse -Force
$archive = Join-Path $projectRoot "artifacts/LumaLyrics-0.3.2-preview-$Runtime.zip"
Compress-Archive -Path "$destination\*" -DestinationPath $archive -Force
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
"$($hash.Hash.ToLower())  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
