param([ValidateSet('win-x64')][string]$Runtime='win-x64')
# Produce the stable portable package. publish-installer.ps1 wraps the same verified payload.
& "$PSScriptRoot/publish-ui.ps1" -Runtime $Runtime -Release
