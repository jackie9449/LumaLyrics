param([string]$Compiler,[string]$PackageDirectory)
. "$PSScriptRoot/common.ps1"
$releaseVersion=([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if(-not $PackageDirectory){$PackageDirectory=Join-Path $projectRoot "artifacts/LumaLyrics-$releaseVersion-portable-win-x64"}
$payload=(Resolve-Path -LiteralPath $PackageDirectory).Path
& "$PSScriptRoot/verify-distribution.ps1" -PackageDirectory $payload
if(-not $Compiler){
    $candidates=@((Join-Path $projectRoot '.tools/inno/ISCC.exe'),(Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),(Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe'))
    $Compiler=$candidates | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
}
if(-not $Compiler){throw 'Install Inno Setup 6.7+ or pass -Compiler with the absolute path to ISCC.exe.'}
$output=Join-Path $projectRoot 'artifacts'
& $Compiler "/DAppVersion=$releaseVersion" "/DPayloadDirectory=$payload" "/DArtifactDirectory=$output" (Join-Path $projectRoot 'installer/LumaLyrics.iss')
if($LASTEXITCODE -ne 0){throw "Installer compilation failed: $LASTEXITCODE"}
$installer=Join-Path $output "LumaLyrics-$releaseVersion-setup-win-x64.exe"
$hash=Get-FileHash -LiteralPath $installer -Algorithm SHA256
"$($hash.Hash.ToLower())  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath "$installer.sha256" -Encoding ascii
Write-Output "Ready: $installer"
