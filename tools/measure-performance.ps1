param([string]$ExePath = (Join-Path $PSScriptRoot '..\release\PortPilot.exe'))
$ErrorActionPreference = 'Stop'
$watch = [System.Diagnostics.Stopwatch]::StartNew()
$app = Start-Process -FilePath $ExePath -WindowStyle Hidden -PassThru
try {
    while ($app.MainWindowHandle -eq 0 -and $watch.Elapsed.TotalSeconds -lt 15) {
        [System.Threading.Tasks.Task]::Delay(50).Wait(); $app.Refresh()
    }
    $startupMs = $watch.Elapsed.TotalMilliseconds
    [System.Threading.Tasks.Task]::Delay(3000).Wait()
    $app.Refresh(); $cpuStart = $app.TotalProcessorTime.TotalSeconds
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    [System.Threading.Tasks.Task]::Delay(10000).Wait()
    $app.Refresh()
    $metrics = [ordered]@{
        Time = (Get-Date).ToString('o')
        StartupWindowMs = [math]::Round($startupMs)
        SampleSeconds = [math]::Round($timer.Elapsed.TotalSeconds,2)
        CpuPercentNormalized = [math]::Round(($app.TotalProcessorTime.TotalSeconds - $cpuStart) / $timer.Elapsed.TotalSeconds / [Environment]::ProcessorCount * 100,2)
        WorkingSetMB = [math]::Round($app.WorkingSet64/1MB,1)
        PrivateMB = [math]::Round($app.PrivateMemorySize64/1MB,1)
        LogicalProcessors = [Environment]::ProcessorCount
        Responding = $app.Responding
    }
    $metrics | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot '..\docs\performance.json')
    $metrics | ConvertTo-Json
}
finally {
    if (-not $app.HasExited) { $app.CloseMainWindow() | Out-Null; $app.WaitForExit(10000) | Out-Null }
}
