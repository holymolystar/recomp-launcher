# Recomp Launcher

One console-style window for PC recomps and native ports: shows the installed and latest release of each game,
installs a game the first time, plays it, and updates one or all with one click. Works with a keyboard, mouse or any
XInput controller.

## Install

Download `RecompLauncher-Setup-<version>.exe` from [Releases](../../releases) and run it.

- Installs for your user only (`%LOCALAPPDATA%\Programs\Recomp Launcher`), so no admin rights are needed.
- Works on a clean Windows 10/11 PC: PowerShell 5.1, .NET Framework 4 and WPF are part of Windows.
- Offers to install the Microsoft Visual C++ runtime if it's missing (most games need it; this step asks for admin).
- Uninstall from Settings > Apps. Your settings, play time, save backups and dumps are kept for a reinstall.

You need your own game files (dumps of games you own); the launcher doesn't provide any.

## Build

```powershell
src\build.ps1                                   # Recomp Launcher.exe and the logo/icon (optional: both are committed)
installer\build-installer.ps1 -Version 1.0.0    # installer\Output\RecompLauncher-Setup-1.0.0.exe (needs Inno Setup 6)
```

`build-installer.ps1` copies only the files the launcher needs into `installer\stage` and refuses to build if
anything personal (settings, keys, logs, backups, dumps or a GitHub token) would end up in the installer.

## Files

| File | What it is |
|---|---|
| `Recomp-Launcher.ps1` | The launcher (WPF window) |
| `Recomp Launcher.exe` | Starts the script without a console window (`src\Launcher.cs`) |
| `recomp-worker.ps1`, `nsp-worker.ps1`, `uninstall-worker.ps1`, `playtime-worker.ps1` | Background jobs: install/update, NSP/XCI extraction, uninstall, play time |
| `tools\` | `hactool` and `nstool`, used to extract dumps |

## Game artwork

The launcher draws its own tiles for games, and uses a game's own program icon once it's installed. No game artwork
is included. To use your own pictures, put them in `icons\` next to the launcher, named by the game's id
(for example `icons\totk.png`, `icons\smo.jpg`).
| `installer\` | Inno Setup script and build script |
