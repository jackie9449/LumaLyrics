param([ValidateSet('win-x64')][string]$Runtime='win-x64')
. "$PSScriptRoot/common.ps1"
$releaseVersion = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$packageName = "LumaLyrics-$releaseVersion-ui-preview-$Runtime"
$destination = Join-Path $projectRoot "artifacts/$packageName"
Invoke-Dotnet publish src/LumaLyrics.App -c Release -r $Runtime --self-contained true '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' --no-restore -o $destination
$probeDestination=Join-Path $projectRoot ".tools/publish-probe-$releaseVersion"
Invoke-Dotnet publish tools/LumaLyrics.Probe -c Release -r $Runtime --self-contained true --no-restore -o $probeDestination
Get-ChildItem -LiteralPath $probeDestination -Filter 'LumaLyrics.Probe.*' -File | Copy-Item -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Probe.cmd') -Destination $destination -Force
# Keep the existing app-local Microsoft host entry point for machines where apphost launch stalls.
$dotnetRoot = Split-Path -Parent $script:Dotnet
$fxr = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'host/fxr') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$hostDirectory = Join-Path $destination 'runtime'
$fxrDirectory = Join-Path $hostDirectory "host/fxr/$($fxr.Name)"
New-Item -ItemType Directory -Force -Path $fxrDirectory | Out-Null
Copy-Item -LiteralPath $script:Dotnet -Destination (Join-Path $hostDirectory 'dotnet.exe') -Force
Copy-Item -LiteralPath (Join-Path $fxr.FullName 'hostfxr.dll') -Destination $fxrDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Launch.vbs') -Destination $destination -Force
Copy-Item -LiteralPath README.md,THIRD-PARTY-NOTICES.md -Destination $destination -Force
Copy-Item -LiteralPath licenses -Destination $destination -Recurse -Force
$guide = Join-Path $destination 'docs'
New-Item -ItemType Directory -Force -Path $guide | Out-Null
Copy-Item -LiteralPath docs/ui-themes.md -Destination $guide -Force
Copy-Item -LiteralPath docs/library-assets.md -Destination $guide -Force
Copy-Item -LiteralPath docs/personal-library.md -Destination $guide -Force
Copy-Item -LiteralPath docs/glass-materials.md -Destination $guide -Force
Copy-Item -LiteralPath docs/theme-memory.md -Destination $guide -Force
Copy-Item -LiteralPath docs/music-library.md -Destination $guide -Force
Copy-Item -LiteralPath docs/chinese-translations.md -Destination $guide -Force
Copy-Item -LiteralPath docs/lyrics-review.md -Destination $guide -Force
Copy-Item -LiteralPath docs/apple-original-lyrics.md -Destination $guide -Force
Copy-Item -LiteralPath docs/release-notes.md -Destination $guide -Force
Copy-Item -LiteralPath docs/screenshots-ui-v041 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v050 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v051 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v060 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v061 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v070 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v071 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v080 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v090 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v091 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0100 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0101 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0110 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0120 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0130 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0140 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0150 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0160 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0161 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0162 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0164 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0165 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0166 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0167 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/verification-v0163 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/verification-v0164 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/verification-v0165 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/verification-v0166 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/verification-v0167 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/screenshots-ui-v0170 -Destination $guide -Recurse -Force
Copy-Item -LiteralPath docs/verification-v0170 -Destination $guide -Recurse -Force
& "$PSScriptRoot/verify-distribution.ps1" -PackageDirectory $destination
$archive = Join-Path $projectRoot "artifacts/$packageName.zip"
Compress-Archive -Path "$destination/*" -DestinationPath $archive -Force
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
"$($hash.Hash.ToLower())  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
Write-Output "Ready: $archive"
