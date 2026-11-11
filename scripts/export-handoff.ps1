param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$ApplicationZip,
    [Parameter(Mandatory=$true)][string]$ApplicationCommit,
    [switch]$NoArchive
)
. "$PSScriptRoot/common.ps1"
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Check-Git([string]$Message){if($LASTEXITCODE -ne 0){throw $Message}}
if(git status --porcelain){throw 'Commit all intended changes before exporting.'}
$commit=(git rev-parse HEAD).Trim();Check-Git 'Cannot read HEAD.'
$tree=(git rev-parse 'HEAD^{tree}').Trim();Check-Git 'Cannot read source tree.'
$appCommit=(git rev-parse "$ApplicationCommit^{commit}").Trim();Check-Git 'Cannot read application commit.'
git diff --quiet $appCommit $commit -- src tests tools launcher installer Directory.Build.props global.json
Check-Git 'Application package is older than current application source.'
$output=[IO.Path]::GetFullPath($OutputDirectory)
if((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath "$output.zip")){throw 'Output already exists. Choose a new directory.'}
$appZip=(Resolve-Path -LiteralPath $ApplicationZip).Path
$appHash=(Get-FileHash -LiteralPath $appZip -Algorithm SHA256).Hash
$declared=(Get-Content -LiteralPath "$appZip.sha256" -Raw).Trim().Split(' ')[0]
if($appHash -ne $declared){throw 'Application archive checksum mismatch.'}
$appCheck=Join-Path $projectRoot ('.tools/handoff-appcheck-'+[guid]::NewGuid().ToString('N'))
[IO.Compression.ZipFile]::ExtractToDirectory($appZip,$appCheck)
& "$PSScriptRoot/verify-distribution.ps1" -PackageDirectory $appCheck
foreach($part in @('source','history','app','scripts','toolchain','toolchain/nuget-feed')){New-Item -ItemType Directory -Path (Join-Path $output $part) -Force | Out-Null}
$snapshot=Join-Path $projectRoot ('.tools/handoff-source-'+[guid]::NewGuid().ToString('N')+'.zip')
git archive --format=zip "--output=$snapshot" $commit;Check-Git 'Source archive failed.'
[IO.Compression.ZipFile]::ExtractToDirectory($snapshot,(Join-Path $output 'source'))
git bundle create (Join-Path $output 'history/LumaLyrics.bundle') --branches --tags;Check-Git 'Git bundle failed.'
git log --all --format=fuller --stat | Set-Content -LiteralPath (Join-Path $output 'history/commits.txt') -Encoding UTF8
git show-ref | Set-Content -LiteralPath (Join-Path $output 'history/refs.txt') -Encoding UTF8
Copy-Item -LiteralPath $appZip,"$appZip.sha256" -Destination (Join-Path $output 'app')
Copy-Item -LiteralPath "$PSScriptRoot/handoff/Verify-Handoff.ps1","$PSScriptRoot/handoff/Restore-Workspace.ps1" -Destination (Join-Path $output 'scripts')
Copy-Item -LiteralPath (Join-Path $output 'source/CODEX_HANDOFF.md') -Destination (Join-Path $output 'CODEX_HANDOFF.md')
$sdkRoot=Split-Path -Parent $script:Dotnet
$sdkVersion=(& $script:Dotnet --version).Trim()
$required=(Get-Content global.json -Raw | ConvertFrom-Json).sdk.version
if($sdkVersion -ne $required){throw 'Use the exact SDK pinned in global.json for a portable handoff.'}
Copy-Item -LiteralPath $sdkRoot -Destination (Join-Path $output 'toolchain/dotnet') -Recurse
$packages=@{}
function Add-Package([string]$Id,[string]$Version,$Folders){
    $idLower=$Id.ToLowerInvariant();$versionLower=$Version.ToLowerInvariant()
    $key="$idLower.$versionLower.nupkg"
    if($packages.ContainsKey($key)){return}
    foreach($folder in $Folders){
        $candidate=Join-Path $folder "$idLower/$versionLower/$key"
        if(Test-Path -LiteralPath $candidate){$packages[$key]=$candidate;return}
    }
    throw "Missing offline package: $key. Restore/build first."
}
foreach($project in (git ls-files '*.csproj')){
    $assets=Join-Path (Split-Path -Parent $project) 'obj/project.assets.json'
    if(-not (Test-Path -LiteralPath $assets)){throw "Restore first: $project"}
    $data=Get-Content -LiteralPath $assets -Raw | ConvertFrom-Json
    $folders=@($data.packageFolders.PSObject.Properties.Name)
    foreach($library in $data.libraries.PSObject.Properties){if($library.Value.type -eq 'package'){$parts=$library.Name.Split('/');Add-Package $parts[0] $parts[1] $folders}}
    foreach($framework in $data.project.frameworks.PSObject.Properties.Value){
        foreach($dependency in $framework.downloadDependencies){
            $version=$dependency.version.Trim('[',']').Split(',')[0].Trim()
            Add-Package $dependency.name $version $folders
        }
    }
}
foreach($path in $packages.Values){Copy-Item -LiteralPath $path -Destination (Join-Path $output 'toolchain/nuget-feed')}
$locale=Join-Path $projectRoot '.tools/locale-audit-tool'
if(Test-Path -LiteralPath $locale){
    $localeTarget=Join-Path $output 'toolchain/locale-audit-tool';New-Item -ItemType Directory -Path $localeTarget | Out-Null
    Copy-Item -LiteralPath (Join-Path $locale 'Program.cs'),(Join-Path $locale 'Tool.csproj') -Destination $localeTarget
    if(Test-Path -LiteralPath (Join-Path $locale 'packages.lock.json')){Copy-Item -LiteralPath (Join-Path $locale 'packages.lock.json') -Destination $localeTarget}
}
Copy-Item -LiteralPath "$PSScriptRoot/handoff/START_HERE.md" -Destination (Join-Path $output 'START_HERE.md')
if(Test-Path -LiteralPath (Join-Path $projectRoot 'docs/migration-validation.md')){Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/migration-validation.md') -Destination (Join-Path $output 'MIGRATION_CHECKS.md')}
if(@(Get-ChildItem -LiteralPath $output -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Package contains links.'}
$prefix=$output.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
$files=@(Get-ChildItem -LiteralPath $output -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
    [ordered]@{Path=$_.FullName.Substring($prefix.Length).Replace('\','/');Size=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$manifest=[ordered]@{FormatVersion=1;CreatedUtc=[DateTime]::UtcNow.ToString('o');SourceCommit=$commit;SourceTree=$tree;Branch='main';CommitCount=[int](git rev-list --count HEAD);ApplicationCommit=$appCommit;ApplicationArchive=('app/'+[IO.Path]::GetFileName($appZip));SDKVersion=$sdkVersion;Runtime='win-x64';NuGetPackageCount=$packages.Count;PersonalDataIncluded=$false;Files=$files}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding UTF8
& "$PSScriptRoot/handoff/Verify-Handoff.ps1" -PackageRoot $output
if(-not $NoArchive){
    [IO.Compression.ZipFile]::CreateFromDirectory($output,"$output.zip",[IO.Compression.CompressionLevel]::Optimal,$false)
    $hash=(Get-FileHash -LiteralPath "$output.zip" -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($output)).zip" | Set-Content -LiteralPath "$output.zip.sha256" -Encoding ASCII
}
Write-Output "Ready: $output"
