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
dotnet restore PDFile.slnx
if ($LASTEXITCODE) { throw 'dotnet restore failed.' }

Write-Host 'Building and starting PDFile...'
dotnet run --project src/PDFile.App -c Release
if ($LASTEXITCODE) { throw 'PDFile exited with an error. See %LOCALAPPDATA%\PDFile\logs.' }
