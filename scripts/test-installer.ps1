param([Parameter(Mandatory=$true)][string]$Installer,[Parameter(Mandatory=$true)][string]$PackageDirectory,[switch]$FullUi,[switch]$SkipUi)
$ErrorActionPreference='Stop'
$installerFile=(Resolve-Path -LiteralPath $Installer).Path
$payload=(Resolve-Path -LiteralPath $PackageDirectory).Path
$expectedVersion=([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$registryPath='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B2DF22B2-8DE1-4C4B-91B6-DA46A7E9B109}_is1'
if(Test-Path -LiteralPath $registryPath){throw 'An existing installed LumaLyrics was found. Run this check on a clean user profile; it never replaces an existing installation.'}
$existingStartMenu=Join-Path ([Environment]::GetFolderPath('Programs')) '浮光歌词'
if(Test-Path -LiteralPath $existingStartMenu){throw 'An existing LumaLyrics Start Menu folder was found. Run this check on a clean user profile; it never replaces unrelated shortcuts.'}
$verificationRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('LumaLyrics-InstallerChecks/'+[guid]::NewGuid().ToString('N'))
$installDirectory=Join-Path $verificationRoot 'app with spaces 浮光'
$personalDirectory=Join-Path $verificationRoot 'personal with spaces 个人'
$reportDirectory=Join-Path $verificationRoot 'report'
New-Item -ItemType Directory -Path $personalDirectory,$reportDirectory -Force | Out-Null
$sentinel=Join-Path $personalDirectory 'retained-personal-fixture.txt'
Set-Content -LiteralPath $sentinel -Value 'Self-authored installation verification fixture.' -Encoding utf8
$sentinelHash=(Get-FileHash -LiteralPath $sentinel).Hash
$desktopLink=Join-Path ([Environment]::GetFolderPath('Desktop')) '浮光歌词.lnk'
$previousShortcut=if(Test-Path -LiteralPath $desktopLink){[IO.File]::ReadAllBytes($desktopLink)}else{$null}
$shell=New-Object -ComObject WScript.Shell
$checks=[ordered]@{}
$installed=$false
try
{
    $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/LANG=chinesesimplified',('/DIR="'+$installDirectory+'"'),('/LOG="'+(Join-Path $verificationRoot 'install.log')+'"'))
    $setup=Start-Process -FilePath $installerFile -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if($setup.ExitCode -ne 0){throw "Installation failed: $($setup.ExitCode)"};$installed=$true
    $checks.InstallSucceeded=$true
    $registration=Get-ItemProperty -LiteralPath $registryPath
    $checks.PerUserRegistration=$registration.DisplayName -eq ('浮光歌词 '+$expectedVersion) -and $registration.DisplayVersion -eq $expectedVersion
    $shortcut=$shell.CreateShortcut($desktopLink)
    $checks.DesktopNameAndTarget=(Test-Path -LiteralPath $desktopLink) -and $shortcut.TargetPath -eq (Join-Path $installDirectory '浮光歌词.exe') -and $shortcut.WorkingDirectory -eq $installDirectory -and $shortcut.IconLocation -like ((Join-Path $installDirectory 'luma.ico')+'*')
    $startLink=Join-Path ([Environment]::GetFolderPath('Programs')) '浮光歌词/浮光歌词.lnk'
    $checks.StartMenuShortcut=(Test-Path -LiteralPath $startLink) -and $shell.CreateShortcut($startLink).TargetPath -eq $shortcut.TargetPath
    $payloadFiles=@(Get-ChildItem -LiteralPath $payload -File -Recurse)
    $checks.AllPayloadFilesMatch=@($payloadFiles | Where-Object {
        $relative=[IO.Path]::GetRelativePath($payload,$_.FullName);$target=Join-Path $installDirectory $relative
        -not(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $_.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash
    }).Count -eq 0
    $checks.InstalledFileCount=$payloadFiles.Count
    $reinstall=Start-Process -FilePath $installerFile -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    $checks.ReinstallPreservesPersonalData=$reinstall.ExitCode -eq 0 -and (Get-FileHash -LiteralPath $sentinel).Hash -eq $sentinelHash
    if(-not $SkipUi)
    {
    $launchArguments=@('--data-dir',('"'+$personalDirectory+'"'),'--verify-ui',('"'+$reportDirectory+'"'),'--exit-after-capture')
    if(-not $FullUi){$launchArguments+='--verify-overlay-transport'}
    $launch=Start-Process -FilePath (Join-Path $installDirectory '浮光歌词.exe') -ArgumentList $launchArguments -WindowStyle Hidden -PassThru -Wait
    $checks.LauncherExitSucceeded=$launch.ExitCode -eq 0
    $deadline=[DateTimeOffset]::UtcNow.AddMinutes(9)
    $reportFile=Join-Path $reportDirectory 'ui-check.json'
    while(-not(Test-Path -LiteralPath $reportFile))
    {
        if(Test-Path -LiteralPath (Join-Path $reportDirectory 'failure.txt')){throw 'Installed UI verification reported an exception.'}
        if([DateTimeOffset]::UtcNow -gt $deadline){throw 'Installed UI verification timed out.'}
        Start-Sleep -Milliseconds 500
    }
    $ui=Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
    function Booleans($node){if($node -is [bool]){$node;return};if($node -is [pscustomobject]){foreach($property in $node.PSObject.Properties){Booleans $property.Value}}}
    $results=@(Booleans $ui)
    $checks.InstalledUiChecks=$results.Count
    $checks.InstalledUiAllPassed=$results.Count -ge 25 -and @($results|Where-Object {-not $_}).Count -eq 0
    $processDeadline=[DateTimeOffset]::UtcNow.AddSeconds(30)
    do
    {
        $owned=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -eq 'dotnet.exe' -and $_.CommandLine -like ('*'+$personalDirectory+'*')})
        if($owned.Count -eq 0){break};Start-Sleep -Milliseconds 250
    }while([DateTimeOffset]::UtcNow -lt $processDeadline)
    if($owned.Count -gt 0){throw 'Installed verification process did not exit; refusing to uninstall in-use files.'}
    }
}
finally
{
    if($installed)
    {
        $uninstaller=Join-Path $installDirectory 'unins000.exe'
        if(Test-Path -LiteralPath $uninstaller){$remove=Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru -Wait
            $checks.UninstallSucceeded=$remove.ExitCode -eq 0}
        $checks.UninstallRemovesProgramAndRegistration=-not(Test-Path -LiteralPath (Join-Path $installDirectory 'LumaLyrics.dll')) -and -not(Test-Path -LiteralPath $registryPath)
        $checks.UninstallRemovesShortcuts=-not(Test-Path -LiteralPath $desktopLink) -and -not(Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('Programs')) '浮光歌词/浮光歌词.lnk'))
        $checks.UninstallPreservesPersonalData=(Get-FileHash -LiteralPath $sentinel).Hash -eq $sentinelHash
    }
    if($null -ne $previousShortcut){[IO.File]::WriteAllBytes($desktopLink,$previousShortcut)}
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)|Out-Null
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $verificationRoot 'installer-check.json') -Encoding utf8
    [pscustomobject]@{ReportDirectory=$verificationRoot;Checks=$checks}|ConvertTo-Json -Depth 4
}
if(@($checks.Values|Where-Object {$_ -is [bool] -and -not $_}).Count -gt 0){throw 'Installer verification failed.'}
