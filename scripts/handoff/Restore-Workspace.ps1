param([Parameter(Mandatory=$true)][string]$Destination,[switch]$Build,[string]$PackageRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $PackageRoot).Path
$target=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $target){throw 'Destination already exists. Choose a new directory.'}
Get-Command git -ErrorAction Stop | Out-Null
& (Join-Path $PSScriptRoot 'Verify-Handoff.ps1') -PackageRoot $root
$manifest=Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
git clone --branch main (Join-Path $root 'history/LumaLyrics.bundle') $target
if($LASTEXITCODE -ne 0){throw 'Local clone failed.'}
$previousEnvironment=@{}
Push-Location -LiteralPath $target
try{
    if((git rev-parse HEAD) -ne $manifest.SourceCommit -or (git rev-parse 'HEAD^{tree}') -ne $manifest.SourceTree){throw 'Restored commit or tree differs.'}
    git remote remove origin
    if($LASTEXITCODE -ne 0){throw 'Could not remove local bundle origin.'}
    git fsck --full
    if($LASTEXITCODE -ne 0){throw 'Git object verification failed.'}
    New-Item -ItemType Directory -Path '.tools' | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'toolchain/dotnet') -Destination '.tools/dotnet' -Recurse
    Copy-Item -LiteralPath (Join-Path $root 'toolchain/nuget-feed') -Destination '.tools/nuget-feed' -Recurse
    if(Test-Path -LiteralPath (Join-Path $root 'toolchain/locale-audit-tool')){Copy-Item -LiteralPath (Join-Path $root 'toolchain/locale-audit-tool') -Destination '.tools/locale-audit-tool' -Recurse}
    $sdk=Join-Path $target '.tools/dotnet/dotnet.exe'
    if((& $sdk --version).Trim() -ne $manifest.SDKVersion){throw 'SDK version mismatch.'}
    if($Build){
        foreach($name in @('DOTNET_CLI_HOME','DOTNET_CLI_TELEMETRY_OPTOUT','DOTNET_SKIP_FIRST_TIME_EXPERIENCE','NUGET_PACKAGES')){$previousEnvironment[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
        $env:DOTNET_CLI_HOME=Join-Path $target '.tools/dotnet-home'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
        $env:NUGET_PACKAGES=Join-Path $target '.tools/nuget-packages'
        $feed=Join-Path $target '.tools/nuget-feed'
        & $sdk restore --locked-mode --source $feed --packages $env:NUGET_PACKAGES -p:NuGetAudit=false
        if($LASTEXITCODE -ne 0){throw 'Offline locked restore failed.'}
        & $sdk build -c Release --no-restore
        if($LASTEXITCODE -ne 0){throw 'Release build failed.'}
        & $sdk test -c Release --no-build --no-restore --logger 'trx;LogFilePrefix=migration' --results-directory TestResults
        if($LASTEXITCODE -ne 0){throw 'Tests failed.'}
    }
    if(git status --porcelain){throw 'Restored tracked files changed.'}
    if(git remote){throw 'Unexpected remote remains.'}
    [ordered]@{SourceCommit=$manifest.SourceCommit;SourceTree=$manifest.SourceTree;SDKVersion=$manifest.SDKVersion;BuildAndTests=[bool]$Build;RemoteCount=0;Clean=$true} | ConvertTo-Json | Set-Content -LiteralPath '.tools/handoff-restore.json' -Encoding UTF8
    Write-Output "Ready: $target"
}
finally{
    foreach($name in $previousEnvironment.Keys){[Environment]::SetEnvironmentVariable($name,$previousEnvironment[$name],'Process')}
    Pop-Location
}
