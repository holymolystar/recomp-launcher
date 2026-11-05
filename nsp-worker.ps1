# Turns a Switch NSP (and, for games that need a newer version, its update NSP) into the exefs and romfs folders the
# Switch recomps read, for Recomp-Launcher.ps1. Uses the player's own prod.keys and two tools, each downloaded once
# from its GitHub release into tools\ and checked against a pinned SHA-256:
#   base only:     hactool (SciresM, 1.4.0) extracts exefs + romfs from the base game's program NCA
#   with update:   nstool (jakcron, 1.9.2) applies the update to the base game while extracting: hactool can't read
#                  the patch layout of current updates ("Invalid BKTR layout")
# nstool gets its keys from a temporary home folder (.switch\prod.keys, and title.keys with the player's title.keys
# plus the NSPs' own ticket keys), so the player's own home folder is not touched. A base game NSP without a ticket
# needs -TitleKeys (its key is only there); nstool then extracts it too. Every event goes to $Log as one JSON line, as in the other workers.
param(
    [Parameter(Mandatory)][string]$Base,
    [string]$Update = '',
    [Parameter(Mandatory)][string]$Keys,
    [string]$TitleKeys = '',
    [Parameter(Mandatory)][string]$Out,
    [Parameter(Mandatory)][string]$Log
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$work = Join-Path $root 'work-nsp'
$Tools = @{
    hactool = @{ Dir = 'tools\hactool'; Exe = 'hactool.exe'; Version = '1.4.0'
                 Url = 'https://github.com/SciresM/hactool/releases/download/1.4.0/hactool-1.4.0-win.zip'
                 Sha = '36e9a221c8a7949c86ada9388eb703c90663aedfe9f65b6032429614c5e1abe8' }
    nstool  = @{ Dir = 'tools\nstool'; Exe = 'nstool.exe'; Version = '1.9.2'
                 Url = 'https://github.com/jakcron/nstool/releases/download/v1.9.2/nstool-v1.9.2-win_x64.zip'
                 Sha = '34659dc47249a3c00f7fc88a11f4e5ba14dfbec0a6a96b4f3f71368a7e1c9374' }
}
# Windows paths stop at 260 characters; the deepest game files sit about 170 below the romfs folder
$MaxOut = 80

$fs  = [IO.File]::Open($Log, 'Create', 'Write', 'ReadWrite')
$logw = New-Object IO.StreamWriter($fs, (New-Object Text.UTF8Encoding($false)))
$logw.AutoFlush = $true
function Emit([hashtable]$e) { $logw.WriteLine(($e | ConvertTo-Json -Compress)) }
function Stage($name, $text) { Emit @{ type = 'stage'; name = $name; text = $text } }

# The tool's exe, downloaded and checked the first time
function Get-Tool($name) {
    $t = $Tools[$name]
    $dir = Join-Path $root $t.Dir
    $exe = Join-Path $dir $t.Exe
    if (Test-Path $exe) { return $exe }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $zip = Join-Path $dir "$name.zip"
    Invoke-WebRequest $t.Url -OutFile $zip -UseBasicParsing
    if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLower() -ne $t.Sha) { Remove-Item $zip -Force; throw "The $name download doesn't match its known SHA-256." }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $dir)
    Remove-Item $zip -Force
    $found = Get-ChildItem $dir -Recurse -Filter $t.Exe | Select-Object -First 1
    if (-not $found) { throw "The $name download has no $($t.Exe) in it." }
    if ($found.FullName -ne $exe) { Move-Item $found.FullName $exe }
    Emit @{ type = 'text'; text = "$name $($t.Version) downloaded (SHA-256 checked)" }
    $exe
}
# Runs a tool, passing its file lines on as progress (both print "Saving <file>..." per file) and failing on errors.
# $homeDir: a home folder for the tool (nstool reads its keys from .switch there).
function Run-Tool($exe, [string[]]$argList, [string]$phase, $homeDir = $null) {
    $name = [IO.Path]::GetFileNameWithoutExtension($exe)
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = ($argList | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    if ($homeDir) { $psi.EnvironmentVariables['HOME'] = $homeDir; $psi.EnvironmentVariables['USERPROFILE'] = $homeDir }
    $p = [Diagnostics.Process]::Start($psi)
    $errTask = $p.StandardError.ReadToEndAsync()
    $n = 0; $lastErr = ''
    while ($null -ne ($line = $p.StandardOutput.ReadLine())) {
        if ($line -match '^Saving ') {
            $n++
            if ($n % 25 -eq 0) { Emit @{ type = 'progress'; step = 'extract'; phase = "$phase ($('{0:N0}' -f $n) files)"; done = 0; total = 0 } }
        } elseif ($line -notmatch '^\[WARN(ING)?\]|match key' -and $line -match '(?i)\berror\b|failed|invalid|must be') { $lastErr = $line.Trim() }
    }
    $p.WaitForExit()
    $err = ($errTask.Result -split "`n" | Where-Object { $_ -notmatch 'Failed to match key|^\s*$' } | Select-Object -Last 3) -join ' '
    if ($p.ExitCode -ne 0 -or $lastErr) {
        $why = if ($lastErr) { $lastErr } else { $err }
        if ($why -match 'cannot find the path') { $why += ' (a path may be too long: choose a shorter folder to save in)' }
        throw "${name}: $why (exit $($p.ExitCode))"
    }
    $n
}
# nstool's home folder for this run: .switch\prod.keys, and .switch\title.keys with the player's title.keys (if given)
# plus $extra lines (title keys from the NSPs' own tickets, "rightsid = key")
function New-NstoolHome([string[]]$extra) {
    $homeDir = Join-Path $work 'home'
    $sw = Join-Path $homeDir '.switch'
    New-Item -ItemType Directory -Force $sw | Out-Null
    Copy-Item -LiteralPath $Keys (Join-Path $sw 'prod.keys') -Force
    $lines = @()
    if ($TitleKeys -and (Test-Path -LiteralPath $TitleKeys)) { $lines += Get-Content -LiteralPath $TitleKeys }
    $lines += $extra
    if ($lines) { Set-Content -Encoding ascii (Join-Path $sw 'title.keys') $lines }
    $homeDir
}
# The program NCA (the game itself: the largest NCA) of an unpacked NSP, and its ticket's rights ID and title key
function Read-Nsp($dir, $label) {
    $nca = Get-ChildItem $dir -Filter '*.nca' | Where-Object { $_.Name -notlike '*.cnmt.nca' } | Sort-Object Length -Descending | Select-Object -First 1
    if (-not $nca) { throw "The $label NSP has no game data (NCA) in it." }
    $r = @{ Nca = $nca.FullName; TitleKey = $null; RightsId = $null }
    $tik = Get-ChildItem $dir -Filter '*.tik' | Select-Object -First 1
    if ($tik) {
        $b = [IO.File]::ReadAllBytes($tik.FullName)
        $r.TitleKey = -join ($b[0x180..0x18F] | ForEach-Object { $_.ToString('x2') })
        $r.RightsId = -join ($b[0x2A0..0x2AF] | ForEach-Object { $_.ToString('x2') })
    }
    $r
}

try {
    foreach ($f in @($Base, $Keys) + @(if ($Update) { $Update })) { if (-not (Test-Path -LiteralPath $f)) { throw "Not found: $f" } }
    if ($Out.Length -gt $MaxOut) { throw "The folder to save in is too long for Windows' path limit ($($Out.Length) characters, $MaxOut at most): choose a shorter one, such as C:\Games\<game>." }

    # 1. Room: the unpacked NSPs and the output
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory -Force $work, $Out | Out-Null
    $size = (Get-Item -LiteralPath $Base).Length + $(if ($Update) { (Get-Item -LiteralPath $Update).Length } else { 0 })
    $need = $size * 2.1
    $drive = Get-PSDrive -Name ((Resolve-Path $Out).Path.Substring(0, 1)) -ErrorAction SilentlyContinue
    if ($drive -and $drive.Free -lt $need) { throw ('Not enough free space: about {0:N0} GB is needed, {1:N0} GB is free.' -f ($need / 1GB), ($drive.Free / 1GB)) }
    $exefs = Join-Path $Out 'exefs'; $romfs = Join-Path $Out 'romfs'
    foreach ($d in $exefs, $romfs) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }

    if (-not $Update) {
        # 2a. Base game only: hactool
        Stage 'tools' 'Getting hactool'
        $hactool = Get-Tool 'hactool'
        Stage 'keys' 'Reading your keys'
        # hactool 1.4.0 stops at key entries it doesn't know the length of, so it gets a copy without those
        $keyCopy = Join-Path $work 'prod.keys'
        Get-Content -LiteralPath $Keys | Where-Object { $_ -match '^\s*\w+\s*=\s*([0-9a-fA-F]+)\s*$' -and ($Matches[1].Length -in 32, 64) } |
            Set-Content -Encoding ascii $keyCopy
        Stage 'unpack' 'Unpacking the game'
        $bdir = Join-Path $work 'base'; New-Item -ItemType Directory -Force $bdir | Out-Null
        [void](Run-Tool $hactool @('-k', $keyCopy, '-t', 'pfs0', "--outdir=$bdir", $Base) 'unpacking')
        $b = Read-Nsp $bdir 'base game'
        Stage 'extract' 'Extracting exefs and romfs'
        if ($b.TitleKey -or -not $TitleKeys) {
            $a = @('-k', $keyCopy); if ($b.TitleKey) { $a += "--titlekey=$($b.TitleKey)" }
            [void](Run-Tool $hactool ($a + @("--exefsdir=$exefs", "--romfsdir=$romfs", $b.Nca)) 'extracting')
        } else {
            # no ticket in the NSP: its title key can only come from title.keys, which nstool looks up by itself
            $nstool = Get-Tool 'nstool'
            $homeDir = New-NstoolHome @()
            [void](Run-Tool $nstool @('--part0', $exefs, '--part1', $romfs, $b.Nca) 'extracting' $homeDir)
        }
    } else {
        # 2b. Base game + update: nstool, with keys and both title keys in a home folder of its own
        Stage 'tools' 'Getting nstool'
        $nstool = Get-Tool 'nstool'
        Stage 'keys' 'Reading your keys'
        $homeDir = New-NstoolHome @()
        Stage 'unpack' 'Unpacking the base game and the update'
        $bdir = Join-Path $work 'base'; $udir = Join-Path $work 'update'
        [void](Run-Tool $nstool @('-x', $bdir, $Base) 'unpacking the base game' $homeDir)
        [void](Run-Tool $nstool @('-x', $udir, $Update) 'unpacking the update' $homeDir)
        $b = Read-Nsp $bdir 'base game'; $u = Read-Nsp $udir 'update'
        [void](New-NstoolHome @(@($b, $u) | Where-Object { $_.RightsId } | ForEach-Object { "$($_.RightsId) = $($_.TitleKey)" }))
        Stage 'extract' 'Applying the update and extracting exefs and romfs'
        New-Item -ItemType Directory -Force $Out | Out-Null
        [void](Run-Tool $nstool @('--basenca', $b.Nca, '--part0', $exefs, '--part1', $romfs, $u.Nca) 'extracting' $homeDir)
    }

    # 3. Check the result
    Stage 'verify' 'Checking the result'
    if (-not (Test-Path (Join-Path $exefs 'main'))) { throw 'No exefs\main came out: the keys may not fit this game (a newer game needs newer keys).' }
    $files = @(Get-ChildItem $romfs -Recurse -File -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { throw 'The romfs came out empty.' }
    Emit @{ type = 'text'; text = ('{0:N0} files, {1:N1} GB in {2}' -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1GB), $Out) }

    Stage 'cleanup' 'Removing temporary files'
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    Emit @{ type = 'done'; result = 'ok'; out = $Out }
}
catch {
    $msg = $_.Exception.Message
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    Emit @{ type = 'done'; result = 'failed'; message = $msg }
}
finally {
    $logw.Close()
}
