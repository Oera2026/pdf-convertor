# PDF Convertor

Windows 11 tool that batch-converts a folder (or dropped files) of Word, Excel, PowerPoint, image and text files to PDF, either as separate PDFs or as one merged PDF. One window, no settings.

## Requirements
- Windows 11 (x64 or arm64), Microsoft Office installed for Word/Excel/PowerPoint files (other types convert without Office).
- Build: .NET 10 SDK on Windows.

## Install dependencies
`run.bat` does all of this for you. To do it by hand:
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or `winget install Microsoft.DotNet.SDK.10`)
- NuGet packages, restored automatically with `dotnet restore PDFile.slnx` from [nuget.org](https://www.nuget.org):
  [Microsoft.WindowsAppSDK 2.6.5](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.6.5),
  [PDFsharp 6.2.4](https://www.nuget.org/packages/PDFsharp/6.2.4),
  [Markdig 0.45.0](https://www.nuget.org/packages/Markdig/0.45.0),
  [System.Drawing.Common 10.0.14](https://www.nuget.org/packages/System.Drawing.Common/10.0.14),
  [System.Security.Cryptography.Pkcs 10.0.14](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs/10.0.14)
- Offline: put the packages in an `offline-packages` folder next to `run.bat` and it restores from there without internet.

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
