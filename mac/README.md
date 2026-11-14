# Recomp Launcher for macOS

The Mac version of Recomp Launcher, with the same console look as the Windows app:

- **Home** holds your installed games in a row.
- **All games** lists every port that has a Mac version.
- **A game's page** shows what you need and has the install, update, play and uninstall buttons.

The launcher downloads each port's latest Mac release from GitHub, unpacks it (zip, disk image, or a zip inside a zip) into its own folder in `~/Games`, and tells you when an update is out. It plays with the keyboard, the mouse, or a game controller.

## Games (26)

| Game | Port |
| --- | --- |
| Twilight Princess | Dusklight |
| The Wind Waker HD | ZeldaWWHDRecomp (devel build) |
| Link's Awakening | LAS Recomp (beta) |
| Majora's Mask | Zelda 64: Recompiled |
| Majora's Mask | 2 Ship 2 Harkinian |
| Ocarina of Time | Ship of Harkinian |
| Super Mario Sunshine | SMS Launcher (beta) |
| Super Mario 64 | Ghostship |
| Super Mario 64 Co-op Deluxe | sm64coopdx |
| New Super Mario Bros. U | NSMBU-Recomp |
| Mario Kart Wii | Wheel Wizard (WiiCompiled) |
| Mario Kart 64 | SpaghettiKart |
| Banjo-Kazooie | Banjo: Recompiled |
| Donkey Kong 64 | DK64: Recompiled |
| Tony Hawk's Pro Skater 1, 2 and 3 | THPS 64 Recomp |
| Pokémon Gen 1 & 2 | Gen2Recomped |
| Pokémon Mystery Dungeon: Red Rescue Team | pmd-green |
| Pokémon SoulSilver | ssrecomp |
| Pokémon Black | eidos |
| Pokémon Snap | Snap64 Recomp |
| LEGO Island | isle-portable |

The Wind Waker HD, New Super Mario Bros. U and Donkey Kong 64 only publish builds for Apple Silicon. On an Intel Mac, their tiles say so. The other ports have an Intel download, or a single download the project ships for both.

As on Windows, you need your own copy of each game; each game page says which.

### Games you build yourself

| Game | Port |
| --- | --- |
| Breath of the Wild | [MacNativeGames](https://github.com/ly05010419/MacNativeGames) (1.6.0 or 1.9.0) |
| Tears of the Kingdom | MacNativeGames (1.0.0, 1.2.1 or 1.4.3) |
| Super Mario Odyssey | MacNativeGames' community Odyssey port (1.3.0 or 1.4.1, early) |

These three have no download. You build the app yourself from your own dump, `prod.keys` and firmware, and the app you build contains the game, so it is for you only. They need an Apple Silicon Mac with macOS 26.

1. **How to build** on the game page opens the project's build steps. [switch2mac](https://github.com/azmasm/switch2mac) is a Mac app that does the build for you.
2. Put the app in Applications. The launcher finds `Breath of the Wild.app`, `Tears of the Kingdom.app` and `Odyssey.app` there, in `~/Applications`, or in the game's folder. For an app somewhere else, use **Find app**.
3. The game moves to Home, and Play starts it.

The launcher never deletes an app you built, so these have no Uninstall button.

## Build it

On a Mac with Xcode:

```bash
./build.sh
```

That makes `build/Recomp Launcher.app` and a zip of it. `ARCHS=native ./build.sh` builds for that Mac only, and works with just the command line tools.

GitHub also builds the app on every push that changes `mac/` (`.github/workflows/mac.yml`). The zip is attached to the run as **Recomp-Launcher-mac**.

## First start

The app is signed ad-hoc but not notarized, so macOS blocks it the first time. To open it:

1. Control-click the app and choose **Open**, or go to **System Settings > Privacy & Security** and click **Open Anyway**.
2. Do this once. After that it opens normally.

## Where things are

| What | Where |
| --- | --- |
| Games | `~/Games/<game name>` (change the folder in Settings) |
| Settings, your GitHub token | `~/Library/Application Support/Recomp Launcher/settings.json` |
| Your own tile pictures | `~/Library/Application Support/Recomp Launcher/icons/<game id>.png` |
| Apps you pointed at with Find app | the same settings.json |
| A newer game list | `~/Library/Application Support/Recomp Launcher/games-mac.json` (used instead of the built-in one) |

**Updates:** a .app is replaced whole, and other files from the release are copied over. Files the release doesn't have, such as saves, settings and game files a port built on its first start, stay.

**Uninstall:** the game's folder goes to the Trash, so you can put it back from there.

## Keys and buttons

| Keyboard | Controller | What it does |
| --- | --- | --- |
| ← → ↑ ↓ | D-pad or left stick | Choose |
| Return | A | Play (Home); open the page (All games); install, update or play (game page) |
| Esc | B | Back |
| ↓ on Home | | Game page |
| ↑ on Home | Y | All games |
| ⌘, | Menu | Settings |
| ⌘R | | Check for updates |
