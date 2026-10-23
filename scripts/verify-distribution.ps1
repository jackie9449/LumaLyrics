param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference='Stop'
$distributionRoot=(Resolve-Path -LiteralPath $PackageDirectory).Path
if(((Get-Item -LiteralPath $distributionRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw '软件输出目录不能是链接或重定向目录。'}
$links=@(Get-ChildItem -LiteralPath $distributionRoot -Recurse -Force | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0})
if($links.Count -gt 0){throw '软件输出中存在链接或重定向文件，已停止打包。'}
$privateFiles=@(Get-ChildItem -LiteralPath $distributionRoot -Recurse -Force -File | Where-Object {
    $relative=[IO.Path]::GetRelativePath($distributionRoot,$_.FullName).Replace('\','/')
    $segments=$relative.Split('/')
    $privateDirectory=@($segments | Where-Object {$_ -in @('cache','logs','personal-library','profiles','translations','models','个人资料')}).Count -gt 0
    $privateName=$_.Name -match '^(settings|data-location|personal-library)\.json$|^(library|artwork)-(prefetch|import).*\.(json|lock|pause)$|^translation-preparation.*\.(json|lock|pause)$|^translations?([-.].*)?\.json$|^(model\.bin|sentencepiece\.bpe\.model|shared_vocabulary\.(json|txt))$'
    $privateQueue=$_.Name -match '^(playback[-_.]?queue|current[-_.]?queue|queue)([-_.].*)?\.json$|^native-(playback|queue).*\.(json|jsonl)$'
    $privateExtension=$_.Name -match '\.db([-.].*)?$|\.sqlite3?(-(wal|shm|journal))?$|\.(musicdb|backup|bak|lrc|ttml|img|jsonl|zip|onnx|safetensors)$'
    $privateText=$_.Extension -eq '.txt' -and $relative -notmatch '^licenses/' -and $_.Name -notin @('LICENSE.txt','ThirdPartyNotices.txt')
    $unexpectedImage=$_.Extension -match '^\.(png|jpe?g|webp|avif|bmp)$' -and $relative -notmatch '^docs/screenshots-ui-v(041|050|051|060|061|070|071|080|090|091|0100|0101|0110|0120)/[^/]+\.png$'
    $privateDirectory -or $privateName -or $privateQueue -or $privateExtension -or $privateText -or $unexpectedImage
})
if($privateFiles.Count -gt 0){
    $names=$privateFiles | ForEach-Object {[IO.Path]::GetRelativePath($distributionRoot,$_.FullName)}
    throw "软件包检测到个人文件，已停止打包；请将它们保留在独立的个人目录：`n$($names -join "`n")"
}
Write-Output 'Distribution privacy check passed.'
