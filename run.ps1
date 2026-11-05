$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Test-Dotnet10 {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $cmd) { return $false }
    return [bool](& dotnet --list-sdks | Select-String '^10\.')
}

if (-not (Test-Dotnet10)) {
    Write-Host 'Installing .NET 10 SDK via winget...'
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'winget not found. Install the .NET 10 SDK manually from https://dotnet.microsoft.com/download/dotnet/10.0 and run this again.'
    }
    winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements
    # Pick up the new PATH in this session.
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not (Test-Dotnet10)) { throw '.NET 10 SDK install finished but dotnet was not found. Open a new terminal and run again.' }
}

Write-Host 'Restoring NuGet packages (Windows App SDK, analyzers)...'
dotnet restore PDFile.slnx 2>&1 | Tee-Object -FilePath run.log
if ($LASTEXITCODE) { throw 'dotnet restore failed. Full output is in run.log.' }

Write-Host 'Building and starting PDF Convertor...'
dotnet run --project src/PDFile.App -c Release 2>&1 | Tee-Object -FilePath run.log -Append
if ($LASTEXITCODE) { throw 'PDF Convertor exited with an error. Full output is in run.log; app logs are in %LOCALAPPDATA%\PDFile\logs.' }
