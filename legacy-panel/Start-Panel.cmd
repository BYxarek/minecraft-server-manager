@echo off
cd /d "%~dp0"
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0panel\Panel.ps1" -OpenBrowser
