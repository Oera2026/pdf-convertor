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

$feed = 'https://api.nuget.org/v3/index.json'
# A local copy of every package (folder next to this script) means no internet is needed.
$offline = Join-Path $PSScriptRoot 'offline-packages'
if (Test-Path $offline) { $feed = $offline; Write-Host "Using local packages in $offline" }
$log = Join-Path $PSScriptRoot 'run.log'
Set-Content $log "PDF Convertor run $(Get-Date -Format o)"

# Run a native command, show + log its output, return its exit code.
# (Native stderr must not be turned into PowerShell errors, so relax the preference around it.)
function Invoke-Logged([string]$exe, [string[]]$arguments) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $exe @arguments 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $log -Append | Out-Host
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prev
    return $code
}

Write-Host 'Checking connection to nuget.org...'
if ($feed -ne $offline) { try {
    Invoke-WebRequest $feed -UseBasicParsing -TimeoutSec 20 | Out-Null
    Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/markdig/index.json' -UseBasicParsing -TimeoutSec 20 | Out-Null
} catch {
    throw "Cannot reach nuget.org ($($_.Exception.Message)). Check internet, VPN, proxy, firewall or antivirus HTTPS scanning, then run again."
} }

# Drop cached "package not found" results from earlier failed restores.
Invoke-Logged dotnet @('nuget', 'locals', 'all', '--clear') | Out-Null

Write-Host 'Restoring NuGet packages (Windows App SDK, analyzers)...'
$code = Invoke-Logged dotnet @('restore', 'PDFile.slnx', '--source', $feed, '--force')
if ($code) {
    Write-Host 'Restore failed. Retrying with certificate revocation checks off and detailed logging...'
    $env:NUGET_CERT_REVOCATION_MODE = 'offline'
    $code = Invoke-Logged dotnet @('restore', 'PDFile.slnx', '--source', $feed, '--force', '-v', 'detailed')
}
if ($code) {
    Add-Content $log "`n=== DIAGNOSTICS ==="
    Invoke-Logged dotnet @('--info') | Out-Null
    Invoke-Logged dotnet @('nuget', 'list', 'source') | Out-Null
    Add-Content $log ("Proxy: " + [Net.WebRequest]::GetSystemWebProxy().GetProxy($feed))
    Add-Content $log ("HTTP(S)_PROXY env: $env:HTTP_PROXY / $env:HTTPS_PROXY")
    throw "dotnet restore failed. Send me the contents of $log"
}

Write-Host 'Building and starting PDF Convertor...'
$code = Invoke-Logged dotnet @('run', '--project', 'src/PDFile.App', '-c', 'Release', '--no-restore')
if ($code) { throw "PDF Convertor exited with an error. Full output is in $log; app logs are in %LOCALAPPDATA%\PDFile\logs." }
