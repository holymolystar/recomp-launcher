# Uninstalls one game for Recomp Launcher.exe: copies the -Saves paths (saves and settings) into -BackupDir first, if
# asked, then deletes the game's folder. Refuses folders that can't be a game's own: a drive root, Windows, Program
# Files, the user folder itself, or a folder without the game's program (-Exe, a name or pattern) in it. Every event
# goes to $Log as one JSON line, as in the other workers.
param(
    [Parameter(Mandatory)][string]$Game,
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Log,
    [string]$Saves = '',
    [string]$BackupDir = '',
    [switch]$KeepSaves
)
$ErrorActionPreference = 'Stop'
function List($s) { @($s -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

$fs  = [IO.File]::Open($Log, 'Create', 'Write', 'ReadWrite')
$logw = New-Object IO.StreamWriter($fs, (New-Object Text.UTF8Encoding($false)))
$logw.AutoFlush = $true
function Emit([hashtable]$e) { $logw.WriteLine(($e | ConvertTo-Json -Compress)) }
function Stage($name, $text) { Emit @{ type = 'stage'; name = $name; text = $text } }

$bk = $null
try {
    if (-not (Test-Path -LiteralPath $Game)) { throw "The folder doesn't exist: $Game" }
    $dir = (Resolve-Path -LiteralPath $Game).Path.TrimEnd('\')
    $protected = @($env:SystemRoot, $env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramData, $env:USERPROFILE,
        [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('MyDocuments'), (Join-Path $env:USERPROFILE 'Downloads')) |
        Where-Object { $_ } | ForEach-Object { $_.TrimEnd('\') }
    if ($dir.Length -le 3 -or $protected -contains $dir) { throw "Won't delete ${dir}: it isn't a game's own folder." }
    if (-not (Test-Path (Join-Path $dir $Exe))) { throw "$Exe isn't in $dir, so it doesn't look like the game's folder. Nothing was deleted." }

    # 1. Saves and settings
    if ($KeepSaves -and (List $Saves).Count) {
        Stage 'backup' 'Backing up saves and settings'
        $bk = Join-Path $BackupDir ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-before-uninstall')
        New-Item -ItemType Directory -Force $bk | Out-Null
        $n = 0
        foreach ($p in List $Saves) {
            # a path can be outside the game folder (%USERPROFILE%\Games\Hookshot\user\save): kept under its last name
            $p = [Environment]::ExpandEnvironmentVariables($p)
            $outside = [IO.Path]::IsPathRooted($p)
            $src = if ($outside) { $p } else { Join-Path $dir $p }
            if (Test-Path -LiteralPath $src) {
                $dst = if ($outside) { Join-Path $bk (Split-Path $p -Leaf) } else { Join-Path $bk $p }
                New-Item -ItemType Directory -Force (Split-Path -Parent $dst) | Out-Null
                Copy-Item -LiteralPath $src $dst -Recurse -Force
                $n++
            }
        }
        Emit @{ type = 'text'; text = "Saves and settings ($n of $((List $Saves).Count)) copied to $bk" }
    }

    # 2. The folder (rd handles long paths and many files faster than Remove-Item)
    Stage 'remove' "Deleting $dir"
    & cmd.exe /d /c rd /s /q "`"$dir`"" 2>&1 | Out-Null
    if (Test-Path -LiteralPath $dir) {
        $left = @(Get-ChildItem -LiteralPath $dir -Recurse -File -Force -ErrorAction SilentlyContinue).Count
        throw "Some files couldn't be deleted ($left left in $dir); a program may still have them open. Close the game and try again."
    }
    Emit @{ type = 'done'; result = 'ok'; backup = $bk }
}
catch {
    Emit @{ type = 'done'; result = 'failed'; message = $_.Exception.Message; backup = $bk }
}
finally {
    $logw.Close()
}
