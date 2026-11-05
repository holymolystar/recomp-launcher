# Counts how long a game is played, for Recomp-Launcher.ps1 (which closes itself when a game starts). Started hidden
# next to the game, it is built to cost next to nothing while you play:
#   - records the start at once (for "Recently played")
#   - while the game starts, looks every 10 seconds for programs running from the game's folder (the game itself, or
#     what its own launcher starts, such as Play.bat's tod.exe or SMS Launcher's game), for up to $StartGrace seconds
#   - then just waits for them to close (Windows wakes it up: no checking in between), saving the time played so far
#     every 5 minutes, so a crash or power cut loses at most that
#   - when they're gone it looks once more after 15 seconds (a launcher may have started the game itself) and ends
# Play time goes to playtime.json next to the launcher: { "<game id>": { "total": seconds, "last": "ISO time", "sessions": n } }
param(
    [Parameter(Mandatory)][string]$Id,
    [Parameter(Mandatory)][string]$Dir,
    [Parameter(Mandatory)][string]$Store,
    [int]$StartGrace = 120,
    [int]$SaveEvery = 300
)
$ErrorActionPreference = 'Stop'

# one writer at a time (two games played at once share the file)
function Update-Store([scriptblock]$change) {
    $m = New-Object Threading.Mutex($false, 'Local\RecompLauncherPlaytime')
    [void]$m.WaitOne(10000)
    try {
        $data = @{}
        if (Test-Path $Store) {
            try { (Get-Content $Store -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object {
                $data[$_.Name] = @{ total = [double]$_.Value.total; last = [string]$_.Value.last; sessions = [int]$_.Value.sessions } } } catch {}
        }
        if (-not $data[$Id]) { $data[$Id] = @{ total = 0.0; last = ''; sessions = 0 } }
        & $change $data[$Id]
        $tmp = "$Store.tmp"
        ([pscustomobject]$data | ConvertTo-Json -Depth 3) | Set-Content -Encoding UTF8 $tmp
        Move-Item $tmp $Store -Force
    } finally { $m.ReleaseMutex(); $m.Dispose() }
}
# programs running from the game's folder (the paths of protected processes can't be read: skipped)
$root = (Resolve-Path -LiteralPath $Dir).Path.TrimEnd('\') + '\'
function Find-Game {
    @(Get-Process | Where-Object { try { $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) } catch { $false } })
}

$start = Get-Date
Update-Store { param($r) $r.last = $start.ToString('o'); $r.sessions++ }
$saved = 0.0          # seconds of this session already added to the store
$playStart = $start   # set to when the game shows up: its start-up wait doesn't count as playing
function Save-Progress($until) {
    $now = ($until - $script:playStart).TotalSeconds
    $add = $now - $script:saved
    if ($add -gt 0) { Update-Store { param($r) $r.total += $add; $r.last = $until.ToString('o') }.GetNewClosure(); $script:saved = $now }
}

# 1. the game starting
$procs = @()
while (((Get-Date) - $start).TotalSeconds -lt $StartGrace) {
    $procs = Find-Game
    if ($procs.Count) { $playStart = Get-Date; break }
    Start-Sleep -Seconds 10
}

# 2. playing: wait on the game's processes, waking every 5 minutes only to save
while ($procs.Count) {
    foreach ($p in $procs) {
        while (-not $p.WaitForExit($SaveEvery * 1000)) { Save-Progress (Get-Date) }
    }
    $end = Get-Date
    Start-Sleep -Seconds 15
    $procs = Find-Game   # anything still (or newly) running from the game's folder keeps the session going
}
if ($end) { Save-Progress $end }
