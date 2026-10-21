$ErrorActionPreference='Stop'
$fixtureParent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot=Join-Path $fixtureParent ('Luma-distribution-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
try {
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'LumaLyrics.dll'),'original fixture')
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'Microsoft.Data.Sqlite.dll'),'original dependency fixture')
    & "$PSScriptRoot/verify-distribution.ps1" -PackageDirectory $fixtureRoot
    $cases=@('lyrics.db','artwork.db-wal','backup.sqlite3','Library.musicdb','settings.json','data-location.json','personal-library.json','own.lrc','own.txt','own-cover.jpg','personal.lumalibrary.zip','library-import-worker.json','probe-redacted.jsonl')
    foreach($name in $cases){
        $fixtureFile=Join-Path $fixtureRoot $name
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
