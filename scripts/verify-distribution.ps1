param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference='Stop'
$distributionRoot=(Resolve-Path -LiteralPath $PackageDirectory).Path
$distributionPrefix=$distributionRoot.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
function Get-DistributionRelative([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path)
    if(-not $full.StartsWith($distributionPrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Distribution boundary mismatch.'}
    return $full.Substring($distributionPrefix.Length)
}
if(((Get-Item -LiteralPath $distributionRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw '软件输出目录不能是链接或重定向目录。'}
$links=@(Get-ChildItem -LiteralPath $distributionRoot -Recurse -Force | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0})
if($links.Count -gt 0){throw '软件输出中存在链接或重定向文件，已停止打包。'}
$privateFiles=@(Get-ChildItem -LiteralPath $distributionRoot -Recurse -Force -File | Where-Object {
    $relative=(Get-DistributionRelative $_.FullName).Replace('\','/')
    $segments=$relative.Split('/')
    $privateDirectory=@($segments | Where-Object {$_ -in @('cache','logs','personal-library','profiles','translations','originals','models','lyrics-review','ocr-models','instruments','instrument-models','个人资料')}).Count -gt 0
    $privateName=$_.Name -match '^(settings|data-location|personal-library)\.json$|^(library|artwork)-(prefetch|import).*\.(json|lock|pause)$|^translation-preparation.*\.(json|lock|pause)$|^translations?([-.].*)?\.json$|^(model\.bin|sentencepiece\.bpe\.model|shared_vocabulary\.(json|txt))$'
    $privateQueue=$_.Name -match '^(playback[-_.]?queue|current[-_.]?queue|queue)([-_.].*)?\.json$|^native-(playback|queue|read-safety|memory-safety|hang).*\.(json|jsonl)$'
    $privateReview=$_.Name -match '^(review|lyrics-review|ocr|native-ocr|native-lyrics|native-ttml|apple-original|coverage-audit|library-coverage)([-_.].*)?\.(json|png|jpe?g)$'
    $privateInstrument=$_.Name -match '^instrument([-.].*)?\.(json|jsonl|log)$|^(yamnet|panns)([-_.].*)?\.(json|h5|pth|pt)$'
    $privateExtension=$_.Name -match '\.db([-.].*)?$|\.sqlite3?(-(wal|shm|journal))?$|\.(musicdb|backup|bak|lrc|ttml|img|jsonl|zip|onnx|pb|safetensors|h5|pth|pt|f32|npz|npy|pcm|wav|flac|m4a|mp3)$'
    $privateText=$_.Extension -eq '.txt' -and $relative -notmatch '^licenses/' -and $_.Name -notin @('LICENSE.txt','ThirdPartyNotices.txt')
    $unexpectedImage=$_.Extension -match '^\.(png|jpe?g|webp|avif|bmp)$' -and $relative -notmatch '^docs/screenshots-ui-v(041|050|051|060|061|070|071|080|090|091|0100|0101|0110|0120|0130|0140|0150|0160|0161|0162|0164|0165|0166|0167|0170|0180)/[^/]+\.png$|^docs/verification-v100/floating-playback-controls\.png$|^docs/verification-seek-motion/ranges-(liquid|frost)\.png$'
    $privateDirectory -or $privateName -or $privateQueue -or $privateReview -or $privateInstrument -or $privateExtension -or $privateText -or $unexpectedImage
})
if($privateFiles.Count -gt 0){
    $names=$privateFiles | ForEach-Object {Get-DistributionRelative $_.FullName}
    throw "软件包检测到个人文件，已停止打包；请将它们保留在独立的个人目录：`n$($names -join "`n")"
}
Write-Output 'Distribution privacy check passed.'
