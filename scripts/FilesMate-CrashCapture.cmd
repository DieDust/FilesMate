@echo off
setlocal
set "FilesMatePowerShell=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "FilesMatePowerShell=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%FilesMatePowerShell%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0capture-startup-crash.ps1"
echo.
pause
