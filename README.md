# Recomp Launcher

One console-style window for PC recomps and native ports: shows the installed and latest release of each game,
installs a game the first time, plays it, and updates one or all with one click. Works with a keyboard, mouse or any
XInput controller.

## Library views

Switch with the four buttons next to the sort chip, Ctrl+G (or Ctrl+1 to Ctrl+4), or RS on a controller:

| View | What it shows |
|---|---|
| Row | The selected game's page, with one row of cards below it |
| Large grid | Big cards filling the window |
| Compact grid | Small cards: the whole library at a glance |
| List | One line per game: source, installed and latest version, status, play time, last played. Click a heading to sort |

Double-click a game (or press Enter / A) to play it, or to install it when it isn't installed yet.

## Update checks

At start (and every 3 hours, or with the Check button / F5) the launcher asks GitHub for the latest release of the
games you have **installed** only. A release found in the last 20 minutes is reused instead of asking again. Games
that aren't installed are looked up only when you press Install or open their release notes.

## Install

Download `RecompLauncher-Setup-<version>.exe` from [Releases](../../releases) and run it.

- Installs for your user only (`%LOCALAPPDATA%\Programs\Recomp Launcher`), so no admin rights are needed.
- Works on a clean Windows 10/11 PC: .NET Framework 4.8, WPF and PowerShell 5.1 are part of Windows.
- Offers to install the Microsoft Visual C++ runtime if it's missing (most games need it; this step asks for admin).
- Uninstall from Settings > Apps. Your settings, play time, save backups and dumps are kept for a reinstall.

You need your own game files (dumps of games you own); the launcher doesn't provide any.

## Build

```powershell
src\build.ps1                                   # Recomp Launcher.exe (C# compiler built into Windows) and the logo/icon
installer\build-installer.ps1 -Version 2.0.0    # installer\Output\RecompLauncher-Setup-2.0.0.exe (needs Inno Setup 6)
```

`build.ps1` needs nothing to be installed: it uses the C# compiler that comes with .NET Framework 4.8.
`build-installer.ps1` copies only the files the launcher needs into `installer\stage` and refuses to build if
anything personal (settings, keys, logs, backups, dumps or a GitHub token) would end up in the installer.

## Files

| File | What it is |
|---|---|
| `Recomp Launcher.exe` | The launcher (WPF), built from `src\App` |
| `games.json` | The games: names, folders, where their releases are, what each game's setup needs |
| `recomp-worker.ps1`, `nsp-worker.ps1`, `uninstall-worker.ps1`, `playtime-worker.ps1` | Background jobs: install/update, NSP/XCI extraction, uninstall, play time |
| `tools\` | `hactool` and `nstool`, used to extract dumps |
| `installer\` | Inno Setup script and build script |

Adding a game is an entry in `games.json` (its `Asset` can list several download names, `;`-separated, the first
that a release has wins).

## Game artwork

The launcher draws its own tiles for games, and uses a game's own program icon once it's installed. No game artwork
is included. To use your own pictures, put them in `icons\` next to the launcher, named by the game's id
(for example `icons\totk.png`, `icons\smo.jpg`).
