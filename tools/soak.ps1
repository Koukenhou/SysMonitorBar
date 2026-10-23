# Long-run check for SysMonitorBar (design v1.0 T14 / T17).
# Samples the running process every few seconds and appends a CSV row:
# time, working set MB, private MB, handles, threads, CPU % of the whole machine since the last row.
# Run while the app is running, e.g. overnight:
#   powershell -ExecutionPolicy Bypass -File tools\soak.ps1 -Minutes 480
param(
    [int]$Minutes = 60,
    [int]$IntervalSeconds = 60,
    [string]$Out = "soak.csv"
)

$proc = Get-Process -Name SysMonitorBar -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $proc) {
    Write-Error "SysMonitorBar is not running."
    exit 1
}

"time,ws_mb,private_mb,handles,threads,cpu_pct" | Out-File -FilePath $Out -Encoding ascii
$cores = [Environment]::ProcessorCount
$lastCpu = $proc.TotalProcessorTime
$lastTime = Get-Date
$end = (Get-Date).AddMinutes($Minutes)

while ((Get-Date) -lt $end) {
    Start-Sleep -Seconds $IntervalSeconds
    $proc.Refresh()
    if ($proc.HasExited) {
        Write-Warning "SysMonitorBar exited."
        break
    }
    $now = Get-Date
    $cpu = ($proc.TotalProcessorTime - $lastCpu).TotalSeconds / ($now - $lastTime).TotalSeconds / $cores * 100
    $lastCpu = $proc.TotalProcessorTime
    $lastTime = $now
    # Invariant culture and F formats: no thousands separators or locale decimal commas in the CSV.
    $row = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0},{1:F1},{2:F1},{3},{4},{5:F2}",
        $now.ToString("yyyy-MM-dd HH:mm:ss"), ($proc.WorkingSet64 / 1MB), ($proc.PrivateMemorySize64 / 1MB),
        $proc.HandleCount, $proc.Threads.Count, $cpu)
    $row | Out-File -FilePath $Out -Append -Encoding ascii
}
Write-Output "Wrote $Out"
