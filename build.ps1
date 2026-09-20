param([switch]$SkipClean, [switch]$UiSmokeTest)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = $PSScriptRoot
Set-Location -LiteralPath $projectRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
function Invoke-DotNet {
    $Arguments = $args
    & $dotnetCommand @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed ($LASTEXITCODE)." }
}
if (-not $SkipClean) { Invoke-DotNet clean PortPilot.sln -c Release --nologo -v minimal }
Invoke-DotNet restore PortPilot.sln
Invoke-DotNet build PortPilot.sln -c Release --no-restore --nologo
Invoke-DotNet test tests/PortPilot.Tests/PortPilot.Tests.csproj -c Release --no-build --logger 'trx;LogFileName=tests.trx' --results-directory TestResults
Invoke-DotNet publish src/PortPilot/PortPilot.csproj -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' -o release --nologo
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $projectRoot 'release\README.md') -Force
if ($UiSmokeTest) {
    $process = Start-Process -FilePath (Join-Path $projectRoot 'release\PortPilot.exe') -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(120000)) { throw 'UI smoke test timed out. Inspect the PortPilot window and data/logs/app.log.' }
    if ($process.ExitCode -ne 0) { throw "UI smoke test failed ($($process.ExitCode))." }
}
Get-FileHash -LiteralPath (Join-Path $projectRoot 'release\PortPilot.exe') -Algorithm SHA256
Write-Host "Ready: $projectRoot\release\PortPilot.exe"
