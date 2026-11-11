param([string]$PackageRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $PackageRoot).Path
$prefix=$root.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
if((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Package root must not be a link.'}
$entries=@(Get-ChildItem -LiteralPath $root -Recurse -Force)
if(@($entries | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Package contains links.'}
$manifest=Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$seen=@{}
foreach($file in $manifest.Files){
    if([IO.Path]::IsPathRooted($file.Path) -or $file.Path -match '(^|[\\/])\.\.([\\/]|$)' -or $file.Path.Contains(':')){throw 'Unsafe manifest path.'}
    $path=[IO.Path]::GetFullPath((Join-Path $root $file.Path))
    if(-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $seen.ContainsKey($path)){throw 'Invalid or duplicate manifest entry.'}
    $seen[$path]=$true
    $item=Get-Item -LiteralPath $path -Force
    if($item.PSIsContainer -or $item.Length -ne $file.Size -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.SHA256){throw "File mismatch: $($file.Path)"}
}
$actual=@($entries | Where-Object {-not $_.PSIsContainer -and $_.FullName -ne (Join-Path $root 'manifest.json')})
if($actual.Count -ne $seen.Count -or @($actual | Where-Object {-not $seen.ContainsKey($_.FullName)}).Count){throw 'Package file set differs from manifest.'}
Write-Output "Verified $($seen.Count) files. Source: $($manifest.SourceCommit)"
