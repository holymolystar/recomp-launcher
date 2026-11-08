# Installs or updates one recomp release in the background for Recomp Launcher.exe (Super Mario Odyssey, Wind Waker
# HD, Ratchet & Clank: Tools of Destruction, and the TotK setup download). Every event goes to $Log as one JSON line,
# in the same format as the TotK Updater's worker.
#   -Mode install  unpack the release into an empty or new game folder
#   -Mode update   back up the -Backup paths, unpack over the game folder, restore the -Keep files
#   -Mode setup    unpack into -Game as a fresh folder (the TotK setup program, which then installs the game)
# The download must match -Sha256. -Replace folders are removed before unpacking when the release has them.
param(
    [Parameter(Mandatory)][ValidateSet('install', 'update', 'setup')][string]$Mode,
    [Parameter(Mandatory)][string]$Game,
    [Parameter(Mandatory)][string]$Url,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Log,
    [string]$Sha256 = '',
    [string]$Exe = '',
    [string]$BackupDir = '',
    [string]$Backup = '',
    [string]$Keep = '',
    [string]$Replace = '',
    [string]$RemoveOld = '',
    [switch]$NeedsSetup,
    # for the few projects that publish no checksum: the download is checked for its full size and a readable zip
    [switch]$NoSha
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$work = Join-Path $root ('work-' + [IO.Path]::GetFileName($Game.TrimEnd('\')))
$keepBackups = 3
function List($s) { @($s -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

$fs  = [IO.File]::Open($Log, 'Create', 'Write', 'ReadWrite')
$out = New-Object IO.StreamWriter($fs, (New-Object Text.UTF8Encoding($false)))
$out.AutoFlush = $true
function Emit([hashtable]$e) { $out.WriteLine(($e | ConvertTo-Json -Compress)) }
function Stage($name, $text) { Emit @{ type = 'stage'; name = $name; text = $text } }

$bk = $null
try {
    if (-not ($Sha256 -match '^[0-9a-fA-F]{64}$') -and -not $NoSha) { throw 'The release has no SHA-256 to check the download against.' }

    # 1. Back up saves and settings (updates)
    if ($Mode -eq 'update' -and (List $Backup).Count) {
        Stage 'backup' 'Backing up saves and settings'
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $bk = Join-Path $BackupDir "$stamp-before-$Tag"
        New-Item -ItemType Directory -Force $bk | Out-Null
        foreach ($p in List $Backup) {
            # a path can be outside the game folder (%USERPROFILE%\Games\Hookshot\user\save): kept under its last name
            $p = [Environment]::ExpandEnvironmentVariables($p)
            $outside = [IO.Path]::IsPathRooted($p)
            $src = if ($outside) { $p } else { Join-Path $Game $p }
            if (Test-Path -LiteralPath $src) {
                $dst = if ($outside) { Join-Path $bk (Split-Path $p -Leaf) } else { Join-Path $bk $p }
                New-Item -ItemType Directory -Force (Split-Path -Parent $dst) | Out-Null
                Copy-Item $src $dst -Recurse -Force
            }
        }
        Get-ChildItem $BackupDir -Directory | Sort-Object Name -Descending |
            Select-Object -Skip $keepBackups | Remove-Item -Recurse -Force
        Emit @{ type = 'text'; text = "Backup saved to $bk" }
    }

    # 2. Download the release
    Stage 'download' "Downloading $Tag"
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory -Force $work | Out-Null
    $zip = Join-Path $work 'release.zip'
    Add-Type -AssemblyName System.Net.Http
    $hc = New-Object System.Net.Http.HttpClient
    $hc.DefaultRequestHeaders.UserAgent.ParseAdd('Recomp-Launcher')
    $resp = $hc.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).Result
    $resp.EnsureSuccessStatusCode() | Out-Null
    $total = [long]$resp.Content.Headers.ContentLength
    $in  = $resp.Content.ReadAsStreamAsync().Result
    $dst = [IO.File]::Create($zip)
    $buf = New-Object byte[] (1MB)
    $done = 0L; $last = 0L
    while (($n = $in.Read($buf, 0, $buf.Length)) -gt 0) {
        $dst.Write($buf, 0, $n); $done += $n
        if ($done - $last -ge 2MB) {
            Emit @{ type = 'progress'; step = 'download'; phase = 'download'; done = $done; total = $total; unit = 'bytes' }
            $last = $done
        }
    }
    $dst.Close(); $in.Close(); $hc.Dispose()

    # 3. Check it
    Stage 'verify' 'Checking the download'
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    if ($Sha256 -match '^[0-9a-fA-F]{64}$') {
        if ($hash -ne $Sha256.ToLower()) { throw "The download doesn't match the release's SHA-256 ($hash)." }
        Emit @{ type = 'text'; text = 'SHA-256 matches the release' }
    } else {
        if ($total -gt 0 -and $done -ne $total) { throw "The download is incomplete ($done of $total bytes)." }
        try { Add-Type -AssemblyName System.IO.Compression.FileSystem; ([IO.Compression.ZipFile]::OpenRead($zip)).Dispose() }
        catch { throw "The download isn't a readable zip: $($_.Exception.Message)" }
        Emit @{ type = 'text'; text = "This project publishes no SHA-256: the download is complete and a readable zip (SHA-256 $hash)" }
    }

    # 4. Unpack it; the release is the folder in the zip that holds the game's exe (or the zip itself). A release that
    #    is a single program (a portable .exe) is put in place as $Exe.
    Stage 'unpack' 'Unpacking'
    $x = Join-Path $work 'x'
    New-Item -ItemType Directory -Force $x | Out-Null
    $isZip = $false
    $sig = New-Object byte[] 2
    $zf = [IO.File]::OpenRead($zip); [void]$zf.Read($sig, 0, 2); $zf.Close()
    if ($sig[0] -eq 0x50 -and $sig[1] -eq 0x4B) { $isZip = $true }
    if ($isZip) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $x)
        Remove-Item $zip -Force
    } else {
        if (-not $Exe) { throw 'The download is not a zip file.' }
        Move-Item $zip (Join-Path $x $Exe)
    }
    $release = $x
    if ($Exe -and -not (Test-Path (Join-Path $x $Exe))) {
        $hit = Get-ChildItem $x -Directory | Where-Object { Test-Path (Join-Path $_.FullName $Exe) } | Select-Object -First 1
        if (-not $hit) { throw "The download does not contain $Exe." }
        $release = $hit.FullName
    }

    # 5. Put it in place
    Stage 'install' $(if ($Mode -eq 'update') { 'Installing over the game folder' } else { "Installing into $Game" })
    if ($Mode -eq 'setup' -and (Test-Path $Game)) { Remove-Item $Game -Recurse -Force }
    New-Item -ItemType Directory -Force $Game | Out-Null
    $kept = @{}
    foreach ($k in List $Keep) {
        $p = Join-Path $Game $k
        if (Test-Path $p) { $kept[$k] = [IO.File]::ReadAllBytes($p) }
    }
    foreach ($d in List $Replace) {
        if ((Test-Path (Join-Path $release $d)) -and (Test-Path (Join-Path $Game $d))) { Remove-Item (Join-Path $Game $d) -Recurse -Force }
    }
    Copy-Item (Join-Path $release '*') $Game -Recurse -Force
    # a single-file release named after its version: the older copies go (-RemoveOld is their name pattern)
    if ($RemoveOld) {
        Get-ChildItem (Join-Path $Game $RemoveOld) -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne $Exe } | ForEach-Object {
            Remove-Item $_.FullName -Force
            Emit @{ type = 'text'; text = "Removed the old $($_.Name)" }
        }
    }
    foreach ($k in $kept.Keys) {
        [IO.File]::WriteAllBytes((Join-Path $Game $k), $kept[$k])
        Emit @{ type = 'text'; text = "Kept your $k" }
    }
    if ($Mode -ne 'setup') {
        @{ tag = $Tag; installed = (Get-Date).ToString('o'); needsSetup = [bool]$NeedsSetup } | ConvertTo-Json |
            Set-Content -Encoding UTF8 (Join-Path $Game 'recomp-launcher.json')
    }

    # 6. Clean up
    Stage 'cleanup' 'Removing the download'
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

    Emit @{ type = 'done'; result = 'ok'; backup = $bk }
}
catch {
    $msg = $_.Exception.Message
    if ($_.Exception.InnerException) { $msg += ' ' + $_.Exception.InnerException.Message }
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    Emit @{ type = 'done'; result = 'failed'; message = $msg }
}
finally {
    $out.Close()
}
