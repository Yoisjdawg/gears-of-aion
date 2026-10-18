# A real launch, as the player starts it (visible window, sound unless -Mute), under a memory watchdog.
# Reports peak memory, whether it kept responding, how it ended, and any new crash report.
#   powershell -ExecutionPolicy Bypass -File tools\smoke.ps1 [-Exe <path>] [-Seconds 90] [-Autoplay] [-Mute] [-Map historic] [-LimitGB 8]
param(
    [string]$Exe = "$env:LOCALAPPDATA\Programs\GearsOfAion\GearsOfAion.exe",
    [int]$Seconds = 90,
    [switch]$Autoplay,
    [switch]$Mute,
    [string]$Map = "",
    [double]$LimitGB = 8
)
$ErrorActionPreference = "Stop"
if (Get-Process GearsOfAion -ErrorAction SilentlyContinue) { Write-Output "Gears of Aion is already running: close it first (one copy at a time)."; exit 2 }
$logs = "$env:LOCALAPPDATA\GearsOfAion\logs"
$started = Get-Date
Remove-Item Env:DAEVAS_NO_TEST, Env:DAEVAS_HIDE, Env:DAEVAS_BACKGROUND -ErrorAction SilentlyContinue
$env:DAEVAS_MEMLOG = "1"
if ($Map) { $env:DAEVAS_MAP = $Map } else { Remove-Item Env:DAEVAS_MAP -ErrorAction SilentlyContinue }
$gameArgs = @("--quit-after", "$Seconds")
if ($Autoplay) { $gameArgs += "--autoplay" }
if ($Mute) { $gameArgs += "--mute" }
$p = Start-Process -FilePath $Exe -ArgumentList $gameArgs -WorkingDirectory (Split-Path $Exe) -PassThru
$peak = 0.0; $stalls = 0; $note = "ran to the end"
for ($i = 0; $i -lt ($Seconds + 40) / 2; $i++) {
    Start-Sleep -Seconds 2
    try {
        $p.Refresh()
        if ($p.HasExited) { $note = "exited after $($i * 2) s (code $($p.ExitCode))"; break }
        $gb = $p.PrivateMemorySize64 / 1GB
        if ($gb -gt $peak) { $peak = $gb }
        if (-not $p.Responding) { $stalls++ }
        if ($gb -gt $LimitGB) { Stop-Process -Id $p.Id -Force; $note = "KILLED at $($i * 2) s: over $LimitGB GB"; break }
    } catch { break }
}
if (-not $p.HasExited) { $p.WaitForExit(15000) | Out-Null }
$crash = Get-ChildItem $logs -Filter "crash-*.txt" -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $started }
"peak memory {0:N2} GB, not responding in {1} checks, {2}" -f $peak, $stalls, $note
if ($crash) { "NEW CRASH REPORT: $($crash[0].FullName)"; Get-Content $crash[0].FullName | Select-Object -First 8 }
else { "no crash report" }
$ok = (-not $crash) -and ($note -notlike "KILLED*") -and ($stalls -lt 3)
if ($ok) { "SMOKE TEST PASSED" ; exit 0 } else { "SMOKE TEST FAILED"; exit 1 }
