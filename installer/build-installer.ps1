# Builds installer\Output\RecompLauncher-Setup-<version>.exe with Inno Setup 6.
# Copies only what the launcher needs into installer\stage first (an allow list, so your settings.json with its
# GitHub token, cache, play time, ROM paths, logs, backups and dumps never end up in the installer).
# Usage:  powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 [-Version 1.2.0] [-UpdaterDir <path>]
param(
    [string]$Version = (Get-Date -Format 'yyyy.M.d'),
    [string]$UpdaterDir = 'C:\Recomp TOTK Updater'   # where update-worker.ps1 (TotK updates) is copied from
)
$ErrorActionPreference = 'Stop'
$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$root  = Split-Path -Parent $here
$stage = Join-Path $here 'stage'

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $c = Get-Command ISCC.exe -ErrorAction SilentlyContinue; if ($c) { $iscc = $c.Source } }
if (-not $iscc) { throw 'Inno Setup 6 is not installed. Install it with:  winget install JRSoftware.InnoSetup' }

# ---- stage ----
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
$files = 'Recomp Launcher.exe', 'games.json', 'logo.png',
         'recomp-worker.ps1', 'nsp-worker.ps1', 'playtime-worker.ps1', 'uninstall-worker.ps1'
foreach ($f in $files) {
    $p = Join-Path $root $f
    if (-not (Test-Path $p)) { throw "Missing: $p" }
    Copy-Item $p $stage
}
# icons\ (your own game artwork) is not shipped: the launcher draws its own tiles, and players can add pictures there
foreach ($d in 'tools') {
    Copy-Item (Join-Path $root $d) (Join-Path $stage $d) -Recurse
}
$worker = Join-Path $UpdaterDir 'update-worker.ps1'
if (-not (Test-Path $worker)) { throw "Missing the TotK Updater's worker: $worker (pass -UpdaterDir)" }
New-Item -ItemType Directory (Join-Path $stage 'totk-updater') | Out-Null
Copy-Item $worker (Join-Path $stage 'totk-updater')

# Last check: nothing personal slipped in
$bad = Get-ChildItem $stage -Recurse -File | Where-Object {
    $_.Name -in 'settings.json', 'cache.json', 'playtime.json', 'roms.json', 'prod.keys', 'title.keys' -or
    $_.FullName -match '\\(logs|backups|dumps)\\' -or
    (($_.Extension -in '.json', '.ps1', '.ini', '.txt') -and (Select-String -Path $_.FullName -Pattern 'github_pat_|ghp_' -Quiet))
}
if ($bad) { throw "Refusing to build, personal files staged: $($bad.FullName -join ', ')" }

# ---- compile ----
& $iscc /Q "/DMyAppVersion=$Version" (Join-Path $here 'RecompLauncher.iss')
if ($LASTEXITCODE) { throw "ISCC failed ($LASTEXITCODE)" }
Get-ChildItem (Join-Path $here 'Output') -Filter "RecompLauncher-Setup-$Version.exe" | ForEach-Object { "Built: $($_.FullName)" }
