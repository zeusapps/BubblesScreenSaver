<#
.SYNOPSIS
    Logs which processes are actually burning CPU, sampled over time.

.DESCRIPTION
    "The fans spin up after the screensaver kicks in" is a common suspicion and usually a
    coincidence: going idle is also when background work starts -- Windows automatic
    maintenance, indexing, backup, antivirus scans, or whatever you left running. This
    samples per-process CPU so you can see what is really responsible rather than blaming
    the thing that happened to be on screen.

    Run it, walk away, and read the log afterwards.

.EXAMPLE
    .\tools\Watch-IdleCpu.ps1 -Minutes 15
#>
param(
    [int]$Minutes = 15,
    [int]$IntervalSeconds = 20,
    [string]$LogPath = "$env:TEMP\idle-cpu.log"
)

$ErrorActionPreference = 'Continue'
"started $(Get-Date -Format s), sampling every ${IntervalSeconds}s for $Minutes minutes" |
    Set-Content $LogPath -Encoding utf8

function Snapshot {
    $map = @{}
    foreach ($p in Get-Process -ErrorAction SilentlyContinue) {
        try { $map[$p.Id] = @{ Name = $p.ProcessName; Cpu = $p.CPU } } catch { }
    }
    return $map
}

$deadline = (Get-Date).AddMinutes($Minutes)
$previous = Snapshot

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds $IntervalSeconds
    $current = Snapshot

    $rows = foreach ($id in $current.Keys) {
        if ($previous.ContainsKey($id)) {
            $delta = $current[$id].Cpu - $previous[$id].Cpu
            if ($delta -gt 0.2) {
                [pscustomobject]@{
                    Name    = $current[$id].Name
                    Percent = [math]::Round(100 * $delta / $IntervalSeconds, 1)
                }
            }
        }
    }

    $top = $rows | Sort-Object Percent -Descending | Select-Object -First 6
    $line = "{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'),
        (($top | ForEach-Object { "$($_.Name) $($_.Percent)%" }) -join '  ')

    Add-Content $LogPath $line -Encoding utf8
    Write-Host $line

    $previous = $current
}

"finished $(Get-Date -Format s)" | Add-Content $LogPath -Encoding utf8
Write-Host ""
Write-Host "log written to $LogPath"
