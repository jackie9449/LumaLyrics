param([ValidateSet('win-x64')][string]$Runtime='win-x64')
. "$PSScriptRoot/common.ps1"
$releaseVersion = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$packageName = "LumaLyrics-$releaseVersion-ui-preview-$Runtime"
$destination = Join-Path $projectRoot "artifacts/$packageName"
Invoke-Dotnet publish src/LumaLyrics.App -c Release -r $Runtime --self-contained true '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' --no-restore -o $destination
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
Copy-Item -LiteralPath docs/screenshots-ui-v041 -Destination $guide -Recurse -Force
$archive = Join-Path $projectRoot "artifacts/$packageName.zip"
Compress-Archive -Path "$destination/*" -DestinationPath $archive -Force
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
"$($hash.Hash.ToLower())  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
Write-Output "Ready: $archive"
