# PDFile

Windows 11 tool that batch-converts a folder (or dropped files) of Word, Excel, PowerPoint, image and text files to PDF, either as separate PDFs or as one merged PDF. One window, no settings.

## Requirements
- Windows 11 (x64 or arm64), Microsoft Office installed for Word/Excel/PowerPoint files (other types convert without Office).
- Build: .NET 10 SDK on Windows.

## Build and run
Easiest: double-click `run.bat` (installs the .NET 10 SDK via winget if missing, restores packages, builds and launches the app). Office is not installed by it.

Manual:
```
dotnet run --project src/PDFile.App
dotnet publish src/PDFile.App -c Release -r win-x64 --self-contained   # or -r win-arm64
```
The app is unpackaged and self-contained (Windows App SDK 2.6.5 is bundled); run `PDFile.exe` from the publish folder. Elevated (admin) instances cannot receive drag and drop.

## Logs and crash reports
`%LOCALAPPDATA%\PDFile\logs` (daily logs, 14 days) and `%LOCALAPPDATA%\PDFile\crashes` (one full report per crash). The footer link "Open logs" opens the log folder. Any unhandled error writes a report, shows it in a dialog with Copy, then closes the app. If the previous run ended abnormally, a banner says so on the next start.

## Testing the crash path
Set `PDFILE_TEST_CRASH` before launching: `startup` (before the window exists, MessageBox), `ui` (UI-thread exception), `safe` (async handler), `thread` (background thread), `task` (unobserved task exception).
```
set PDFILE_TEST_CRASH=ui && src\PDFile.App\bin\Debug\net10.0-windows10.0.22621.0\win-x64\PDFile.exe
```
