param(
    [string]$Executable = (Join-Path $PSScriptRoot 'release/PortPilot.exe'),
    [ValidateRange(10, 900)][int]$PhaseSeconds = 40,
    [ValidateRange(0, 2000)][int]$Endpoints = 400
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $Executable).Path
$folder = Join-Path $PSScriptRoot ('.verification/perf-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $folder 'PortPilot.exe')
$oldSeconds = $env:PORTPILOT_PERF_PHASE_SECONDS
$oldEndpoints = $env:PORTPILOT_PERF_ENDPOINTS
try {
    $env:PORTPILOT_PERF_PHASE_SECONDS = [string]$PhaseSeconds
    $env:PORTPILOT_PERF_ENDPOINTS = [string]$Endpoints
    $process = Start-Process -FilePath (Join-Path $folder 'PortPilot.exe') -ArgumentList '--perf-test' -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(($PhaseSeconds * 5 + 120) * 1000)) {
        $process.Kill()
        throw 'Performance run timed out; its isolated test process was stopped.'
    }
    if ($process.ExitCode -ne 0) { throw "Performance run failed. Inspect $folder/smoke-error.txt" }
    $reportPath = Join-Path $folder 'data/cache/performance.json'
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $report | Select-Object Version, FixtureEndpoints, CpuPercent, DispatcherP95Ms, DispatcherMaxMs
    $report.Samples | Group-Object Phase | ForEach-Object {
        [pscustomobject]@{
            Phase = $_.Name
            MeanWorkingSetMiB = [Math]::Round(($_.Group.WorkingSetMiB | Measure-Object -Average).Average, 2)
            MeanPrivateMiB = [Math]::Round(($_.Group.PrivateMiB | Measure-Object -Average).Average, 2)
            MeanManagedMiB = [Math]::Round(($_.Group.ManagedMiB | Measure-Object -Average).Average, 2)
            MeanAllocatedMiB = [Math]::Round(($_.Group.AllocatedMiB | Measure-Object -Average).Average, 2)
            MeanRefreshMs = [Math]::Round(($_.Group.RefreshMs | Measure-Object -Average).Average, 2)
        }
    } | Format-Table
    Write-Host "Report: $reportPath"
}
finally {
    $env:PORTPILOT_PERF_PHASE_SECONDS = $oldSeconds
    $env:PORTPILOT_PERF_ENDPOINTS = $oldEndpoints
}
