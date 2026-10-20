param([ValidateSet('win-x64')][string]$Runtime='win-x64')
# Preserve the original entry point while using the current version and package checks.
& "$PSScriptRoot/publish-ui.ps1" -Runtime $Runtime
