@echo off
if not exist "%LOCALAPPDATA%\LumaLyrics\logs" mkdir "%LOCALAPPDATA%\LumaLyrics\logs"
"%~dp0runtime\dotnet.exe" exec "%~dp0LumaLyrics.Probe.dll" "%LOCALAPPDATA%\LumaLyrics\logs\probe-redacted.jsonl" 180
pause
