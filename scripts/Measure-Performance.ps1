[CmdletBinding()]
param(
    [int]$TargetProcessId,
    [int]$Seconds = 60,
    [string]$OutputPath = '.\artifacts\performance.csv',
    [string]$PhaseFile
)
$ErrorActionPreference = 'Stop'
$outputFile = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFile)) | Out-Null
$rows = [Collections.Generic.List[object]]::new()
$processorCount = [Environment]::ProcessorCount
$previousCpu = $null
$previousTime = $null
# PDH expands wildcards when the query opens. Wait until the chosen process has registered its engines.
if ($PhaseFile -and $TargetProcessId -le 0) {
    $registrationDeadline = [DateTimeOffset]::UtcNow.AddSeconds(40)
    do {
        if (Test-Path -LiteralPath $PhaseFile) {
            $phaseParts = (Get-Content -LiteralPath $PhaseFile -Raw).Trim().Split(' ')
            $candidateId = [int]$phaseParts[0]
            $probe = Get-Counter '\GPU Engine(*)\Utilization Percentage' -MaxSamples 1 -ErrorAction SilentlyContinue
            if ($probe.CounterSamples | Where-Object { $_.InstanceName -match "^pid_${candidateId}_" -and $_.Status -eq 0 }) { $TargetProcessId = $candidateId; break }
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $registrationDeadline)
}
try {
    # GPU instances come and go as other processes exit. Keep valid samples from our process.
    Get-Counter '\GPU Engine(*)\Utilization Percentage' -SampleInterval 1 -MaxSamples $Seconds -ErrorAction SilentlyContinue | ForEach-Object {
        $phase = 'unspecified'
        if ($PhaseFile -and (Test-Path -LiteralPath $PhaseFile)) {
            $parts = (Get-Content -LiteralPath $PhaseFile -Raw).Trim().Split(' ')
            $TargetProcessId = [int]$parts[0]
            $phase = $parts[1]
        }
        $process = if ($TargetProcessId -gt 0) { Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue } else { $null }
        if ($process) {
            $now = [DateTimeOffset]::UtcNow
            $cpu = $process.TotalProcessorTime.TotalSeconds
            $cpuPercent = if ($null -ne $previousCpu) { 100 * ($cpu - $previousCpu) / ($now - $previousTime).TotalSeconds / $processorCount } else { 0 }
            $gpu = $_.CounterSamples | Where-Object { $_.InstanceName -match "^pid_${TargetProcessId}_" -and $_.Status -eq 0 }
            $decode = ($gpu | Where-Object InstanceName -Match 'engtype_VideoDecode' | Measure-Object CookedValue -Sum).Sum
            $render = ($gpu | Where-Object InstanceName -Match 'engtype_3D' | Measure-Object CookedValue -Sum).Sum
            $processing = ($gpu | Where-Object InstanceName -Match 'engtype_VideoProcessing' | Measure-Object CookedValue -Sum).Sum
            $rows.Add([pscustomobject]@{Utc=$now.ToString('o'); Phase=$phase; ProcessId=$TargetProcessId; CpuPercentTotalCapacity=$cpuPercent; PrivateMiB=$process.PrivateMemorySize64/1MB; WorkingSetMiB=$process.WorkingSet64/1MB; Handles=$process.HandleCount; ValidGpuCounters=@($gpu).Count; Gpu3DPercent=$render; GpuDecodePercent=$decode; GpuVideoProcessingPercent=$processing})
            $previousCpu=$cpu; $previousTime=$now
        }
    }
} finally {
    $rows | Export-Csv -LiteralPath $outputFile -NoTypeInformation
    Write-Host "Saved $($rows.Count) samples to $outputFile"
}
# GPU counters are process-specific engine sums, not whole-GPU utilization.
# Localized Windows counter names may require adjusting the path above.
