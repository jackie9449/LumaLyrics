$ErrorActionPreference='Stop'
$fixtureParent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot=Join-Path $fixtureParent ('Luma-distribution-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
try {
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'LumaLyrics.dll'),'original fixture')
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'Microsoft.Data.Sqlite.dll'),'original dependency fixture')
    & "$PSScriptRoot/verify-distribution.ps1" -PackageDirectory $fixtureRoot
    $cases=@('lyrics.db','artwork.db-wal','backup.sqlite3','Library.musicdb','settings.json','data-location.json','personal-library.json','own.lrc','own.txt','own-cover.jpg','personal.lumalibrary.zip','library-import-worker.json','probe-redacted.jsonl','translations.db','translation-preparation-progress.json','translation-preparation.lock','translation-preparation.pause','translation-ai-drafts-v0100.json','translation-draft-sources-v0100.json','translation-ai-preparation-report.json','encoder_model.onnx','model.bin')
    $cases+=@('playback-queue.json','queue-restore-input.json','native-queue.json','current-queue.json')
    $cases+=@('review.json','native-ocr.json','native-lyrics.png','coverage-audit.json','lyrics-review.json','ocr-result.json')
    $cases+=@('own.ttml','native-ttml.json','apple-original.json')
    $cases+=@('originals/source.json')
    $cases+=@('native-read-safety.json','native-memory-safety.json','native-hang.json')
    $cases+=@('instrument-audio-probe.json','instrument-corpus.json','instrument-review-dataset.json','instrument-stage.log','instrument-model-export.json','yamnet.h5','parity-inputs.f32','capture.pcm','private.wav','private.flac','private.m4a','private.mp3','instruments/timeline.json','instrument-models/profile.json')
    $cases+=@('cnn6.pth','weights.pt','private.safetensors','embedding.pb')
    $cases+=@('matpac-gate.npz','instrument-features.npy')
    foreach($name in $cases){
        $fixtureFile=Join-Path $fixtureRoot $name
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fixtureFile)) | Out-Null
        [IO.File]::WriteAllText($fixtureFile,'original private fixture')
        $rejected=$false
        try { & "$PSScriptRoot/verify-distribution.ps1" -PackageDirectory $fixtureRoot | Out-Null } catch { $rejected=$true }
        if(-not $rejected){throw "Privacy guard accepted $name"}
        Remove-Item -LiteralPath $fixtureFile
    }
    Write-Output "Distribution tests passed: 2 software fixtures and $($cases.Count) private-file cases."
} finally {
    $resolved=[IO.Path]::GetFullPath($fixtureRoot)
    if(-not $resolved.StartsWith($fixtureParent,[StringComparison]::OrdinalIgnoreCase) -or -not [IO.Path]::GetFileName($resolved).StartsWith('Luma-distribution-test-')){throw 'Fixture cleanup boundary mismatch'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
