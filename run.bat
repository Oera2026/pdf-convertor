@echo off
rem One-click: installs the .NET 10 SDK if missing, restores all NuGet packages, builds and launches PDFile.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1"
if errorlevel 1 pause
