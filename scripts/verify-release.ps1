param([Parameter(Mandatory=$true)][string]$PackageDirectory,[Parameter(Mandatory=$true)][string]$Installer,[string]$Report)
$ErrorActionPreference='Stop'
$package=(Resolve-Path -LiteralPath $PackageDirectory).Path
$installerFile=(Resolve-Path -LiteralPath $Installer).Path
& "$PSScriptRoot/verify-distribution.ps1" -PackageDirectory $package
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archiveFile=$package+'.zip'
$archive=[IO.Compression.ZipFile]::OpenRead($archiveFile)
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$count=0;[long]$bytes=0
try
{
    foreach($entry in $archive.Entries)
    {
        if($entry.Name.Length -eq 0){continue}
        $target=[IO.Path]::GetFullPath((Join-Path $package $entry.FullName))
        if(-not $target.StartsWith($package+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or -not $seen.Add($target)){throw 'Unsafe or duplicate archive entry.'}
        $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$zipHash=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLower()}finally{$stream.Dispose();$sha.Dispose()}
        $file=Get-Item -LiteralPath $target
        if($file.Length -ne $entry.Length -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLower() -ne $zipHash){throw ('Archive mismatch: '+$entry.FullName)}
        $count++;$bytes+=$entry.Length
    }
    if($count -ne @(Get-ChildItem -LiteralPath $package -Recurse -File).Count){throw 'Archive file count mismatch.'}
}
finally{$archive.Dispose()}
$assets=@(foreach($file in @($archiveFile,$installerFile))
{
    $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLower()
    $record=(Get-Content -LiteralPath ($file+'.sha256') -Raw).Trim()
    if($record -ne ($hash+'  '+[IO.Path]::GetFileName($file))){throw ('Sidecar mismatch: '+[IO.Path]::GetFileName($file))}
    [pscustomobject]@{File=[IO.Path]::GetFileName($file);Bytes=(Get-Item -LiteralPath $file).Length;Sha256=$hash}
})
$versions=@(foreach($file in @((Join-Path $package 'LumaLyrics.dll'),(Join-Path $package '浮光歌词.exe'),$installerFile))
{[pscustomobject]@{File=[IO.Path]::GetFileName($file);Version=(Get-Item -LiteralPath $file).VersionInfo.FileVersion}})
if(@($versions.Version | Select-Object -Unique).Count -ne 1){throw 'Application, launcher and installer versions differ.'}
$result=[pscustomobject]@{EveryZipFileMatched=$true;Files=$count;UncompressedBytes=$bytes;AssetSidecarsMatched=$true;Versions=$versions;Assets=$assets}
if($Report){$result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $Report -Encoding utf8}
$result|ConvertTo-Json -Depth 5
