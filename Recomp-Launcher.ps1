# Recomp Launcher: one console-style window for PC recomps and native ports (Tears of the Kingdom, Super Mario Odyssey,
# The Wind Waker HD, both Ratchet & Clank games, Mario Party 6, Mario Kart Wii, Super Mario Sunshine and Breath of the
# Wild). Shows the installed and latest release of each, installs a game the first time, plays it, and updates one or
# all with one click. Installs and updates run in separate hidden worker processes (the TotK Updater's
# update-worker.ps1 for TotK updates, recomp-worker.ps1 here for the rest); this window follows their logs. The window
# is WPF, built from the XAML below; it can go full screen (F11), and remembers that and the last game.
# Game folders are kept in settings.json next to this script and can be changed in the Settings panel.
# Controllers: any XInput pad (Xbox, or others through Steam Input) works while this window is in front.
# For testing: -Snapshot writes a picture of the window and closes it; -PadScript "Up,Right,A" plays controller
# presses; -Full, -OpenNotes, -OpenSettings, -Search and -Select set the window up first.
param([string]$Snapshot, [int]$SnapshotDelay = 3000, [string]$Select, [switch]$FakeUpdate, [switch]$OpenSettings,
      [string]$PadScript, [switch]$Full, [switch]$OpenNotes, [string]$Search, [string]$OpenRom, [string]$RomTry)

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class IconX {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint count, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    // Own taskbar identity, so the window isn't grouped under Windows PowerShell (its icon and pin)
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    // Windows 11 window corners (DWMWA_WINDOW_CORNER_PREFERENCE): 1 square, 2 rounded. Does nothing on Windows 10.
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    public static void WindowCorners(IntPtr h, bool round) { int v = round ? 2 : 1; DwmSetWindowAttribute(h, 33, ref v, 4); }
}
// Per-window taskbar identity: the app id plus what pinning should start and show. Docks like Seelen UI read
// these from the window's property store rather than from the process.
public static class WindowAppId {
    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid Fmtid; public uint Pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant { [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public IntPtr Ptr; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll")]
    static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    static readonly Guid Fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    static void Put(IPropertyStore store, uint pid, string text) {
        var key = new PropertyKey { Fmtid = Fmtid, Pid = pid };
        var value = new PropVariant { Vt = 31, Ptr = Marshal.StringToCoTaskMemUni(text) };   // VT_LPWSTR
        try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); }
        finally { Marshal.FreeCoTaskMem(value.Ptr); }
    }
    public static void Set(IntPtr hwnd, string appId, string relaunch, string name, string icon) {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        IPropertyStore store;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out store));
        try {
            Put(store, 2, relaunch);   // RelaunchCommand
            Put(store, 4, name);       // RelaunchDisplayNameResource
            Put(store, 3, icon);       // RelaunchIconResource
            Put(store, 5, appId);      // ID, set last
            store.Commit();
        } finally { Marshal.ReleaseComObject(store); }
    }
}
public static class Pad {
    [StructLayout(LayoutKind.Sequential)]
    public struct State { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern int Get14(int user, out State s);
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern int Get910(int user, out State s);
    static bool legacy;
    static int last = -1;
    // The first connected controller (the last one that answered is asked first)
    public static bool TryGet(out State s) {
        s = new State();
        for (int k = 0; k < 4; k++) {
            int i = last >= 0 ? (last + k) % 4 : k;
            int r;
            if (!legacy) { try { r = Get14(i, out s); } catch (DllNotFoundException) { legacy = true; r = Get910(i, out s); } }
            else r = Get910(i, out s);
            if (r == 0) { last = i; return true; }
        }
        last = -1;
        return false;
    }
}
'@
$Bullet = [string][char]0x2022
$Pk = "Pok$([char]0xE9)mon"
# The same id is on the Start Menu shortcut (src\shortcut.ps1), which gives the taskbar button its icon
[void][IconX]::SetCurrentProcessExplicitAppUserModelID('RecompLauncher.App')

# ---------- Settings ----------
$SettingsPath = Join-Path $Root 'settings.json'
# The installer ships the TotK Updater's update-worker.ps1 in totk-updater\ next to this script; use it when present
$BundledUpdater = Join-Path $Root 'totk-updater'
$DefaultUpdater = if (Test-Path (Join-Path $BundledUpdater 'update-worker.ps1')) { $BundledUpdater } else { 'C:\Recomp TOTK Updater' }
$Defaults = [ordered]@{
    TotkDir = 'C:\Recomp TOTK'; TotkUpdaterDir = $DefaultUpdater; SmoDir = 'C:\SMO'
    WwhdDir = 'C:\Recomp WWHD'; TodDir = 'C:\Recomp ToD'; Mp6Dir = 'C:\Recomp MP6'
    MkwiiDir = 'C:\Recomp MKWii'; SmsDir = 'C:\Recomp SMS'; BotwDir = 'C:\Recomp BOTW'; AcitDir = 'C:\Recomp ACiT'
    LasDir = 'C:\Recomp LAS'; IssDir = 'C:\Recomp ISS'; SmgDir = 'C:\Recomp SMG'; SpmDir = 'C:\Recomp SPM'
    TpDir = 'C:\Recomp Dusklight'; NsmbwDir = 'C:\Recomp NSMBW'; NsmbuDir = 'C:\Recomp NSMBU'
    PkgenDir = 'C:\Recomp Pokemon Gen12'; PmdDir = 'C:\Recomp Pokemon PMD'; PkblackDir = 'C:\Recomp Pokemon Black'
    PkssDir = 'C:\Recomp Pokemon SoulSilver'; PkstadiumDir = 'C:\Recomp Pokemon Stadium'; PksnapDir = 'C:\Recomp Pokemon Snap'
    PklgpeDir = 'C:\Recomp Pokemon LetsGo'; PkswshDir = 'C:\Recomp Pokemon SwSh'; PklaDir = 'C:\Recomp Pokemon Arceus'
    PokopiaDir = 'C:\Recomp Pokopia'
    MmDir = 'C:\Recomp MM'; Mm2sDir = 'C:\Recomp 2Ship'; OotDir = 'C:\Recomp SoH'; Sm64Dir = 'C:\Recomp SM64'; Mk64Dir = 'C:\Recomp MK64'; Sf64Dir = 'C:\Recomp SF64'; BkDir = 'C:\Recomp Banjo'; Dk64Dir = 'C:\Recomp DK64'; ConkerDir = 'C:\Recomp Conker'; SonicDir = 'C:\Recomp Unleashed'; Thps1Dir = 'C:\Recomp THPS1'; Thps2Dir = 'C:\Recomp THPS2'; Thps3Dir = 'C:\Recomp THPS3'; NomercyDir = 'C:\Recomp NoMercy'; Wm2kDir = 'C:\Recomp WM2000'; WcwwtDir = 'C:\Recomp WCW World Tour'; RevengeDir = 'C:\Recomp WCW Revenge'; Vpw64Dir = 'C:\Recomp VPW64'
    Fullscreen = ''; LastGame = 'totk'; SortMode = 'default'; CloseOnPlay = 'on'; AutoCheck = 'on'; GitHubToken = ''
    ViewMode = 'row'   # the library as one row of cards under the game's page, or 'grid' (the RS / grid chip switches)
    Hidden = ''   # ids of the games hidden from the library, comma-separated (Settings > Games in the library)
}
function Load-Settings {
    $s = [ordered]@{}
    foreach ($k in $Defaults.Keys) { $s[$k] = $Defaults[$k] }
    if (Test-Path $SettingsPath) {
        try {
            $j = Get-Content $SettingsPath -Raw | ConvertFrom-Json
            foreach ($k in $Defaults.Keys) { if ($j.$k) { $s[$k] = [string]$j.$k } }
        } catch {}
    }
    $s
}
function Save-Settings($s) {
    ([pscustomobject]$s | ConvertTo-Json) | Set-Content -Encoding UTF8 $SettingsPath
}
$Settings = Load-Settings

# ---------- Games ----------
# Repo / Asset: where releases come from (GitHub, or SMO's GitLab project). Keep: files of the game folder that
# a release zip also ships but that hold the player's own settings (restored after an update). Backup: what is
# copied aside before an update. Replace: folders replaced as a whole. FirstRun: started after a first install
# (each game's own setup asks for the player's dump). Needs: what the player must bring.
$Games = @(
    @{
        Id = 'totk'; Name = 'Tears of the Kingdom'; Short = 'TotK'; Source = "TOTK PC RECOMP  $Bullet  GITHUB"
        Exe = 'totk.exe'; Proc = 'totk'; SettingsKey = 'TotkDir'
        Repo = 'valentderah/totk-pc-recomp'; Asset = '*windows*.zip'
        Accent = '#D9B45A'; Accent2 = '#3FB8A0'
        Needs = 'Your own Tears of the Kingdom base game and its 1.4.3 update (NSP or XCI), and prod.keys from your Switch. Install opens the TotK setup, which asks for them; choose the folder from Settings as its install folder.'
    },
    @{
        Id = 'smo'; Name = 'Super Mario Odyssey'; Short = 'SMO'; Source = "POWER MOON RECOMP  $Bullet  GITLAB"
        Exe = 'PowerMoonRecomp.exe'; Proc = 'PowerMoonRecomp'; SettingsKey = 'SmoDir'
        Accent = '#E8604C'; Accent2 = '#D9B45A'
        Backup = 'save;powermoon.ini'; Replace = 'odysseygen'; FirstRun = 'PowerMoonRecomp.exe'
        Needs = 'Your own Super Mario Odyssey 1.0.0 (no updates), extracted to exefs and romfs folders. The game asks for that folder on its first start, then builds itself (a few minutes, about 7 GB).'
    },
    @{
        Id = 'wwhd'; Name = 'The Wind Waker HD'; Short = 'WWHD'; Source = "ZELDAWWHDRECOMP  $Bullet  GITHUB"
        Exe = 'Wind Waker HD.exe'; Proc = 'Wind Waker HD'; SettingsKey = 'WwhdDir'
        Repo = 'ZeldaWWHDRecomp/ZeldaWWHDRecomp'; Asset = '*windows-x86_64.zip'
        Accent = '#2FA7D8'; Accent2 = '#F2C14E'
        Backup = 'data\save;data\user'; FirstRun = 'Wind Waker HD.exe'; SetupArg = '--setup'
        Needs = 'Your own Wind Waker HD for Wii U (USA or Europe, version 0): a .wux/.wud disc image with its disc key and the Wii U common key, a Cemu .wua, or an extracted game folder. The game asks for it on its first start (about 2 minutes, 1.7 GB).'
    },
    @{
        Id = 'tod'; Name = 'Ratchet & Clank: Tools of Destruction'; Short = 'R&C'; Source = "RATCHET-TOD-RECOMP  $Bullet  GITHUB"
        Exe = 'tod.exe'; Proc = 'tod'; SettingsKey = 'TodDir'; Launch = 'Play.bat'
        Repo = 'Re-TeRo17/ratchet-tod-recomp'; Asset = '*win64.zip'
        Accent = '#F08A24'; Accent2 = '#4AA3DF'
        Backup = 'gamedata;tod.ini'; Keep = 'tod.ini'; FirstRun = 'Install.bat'
        Needs = 'Your own European PS3 disc (BCES00052, version 1.00), decrypted, as an .iso or a folder with PS3_GAME. About 24 GB free, a CPU with AVX2 and a Direct3D 12 GPU. Its installer copies the disc into the game folder.'
    },
    @{
        Id = 'mp6'; Name = 'Mario Party 6'; Short = 'MP6'; Source = "STARLIT PARTY  $Bullet  GITHUB"
        Exe = 'mp6native.exe'; Proc = 'mp6native'; SettingsKey = 'Mp6Dir'
        Repo = 'iirg4x/mp6-native-port'; Asset = '*win-x64.zip'
        Accent = '#F5A524'; Accent2 = '#5B6EE1'
        Backup = 'save;config.json'; FirstRun = 'mp6native.exe'
        Needs = 'Your own Mario Party 6 (USA) disc image or extracted disc folder. The game asks for it on its first start. Work in progress: all six boards, and 49 of the 81 minigames (the others are skipped).'
    },
    @{
        # WiiCompiled is installed, updated and started by Wheel Wizard, its official front end on Windows
        Id = 'mkwii'; Name = 'Mario Kart Wii'; Short = 'MKW'; Source = "WIICOMPILED VIA WHEEL WIZARD  $Bullet  GITHUB"
        Exe = 'WheelWizard.exe'; Proc = 'WheelWizard'; SettingsKey = 'MkwiiDir'
        Repo = 'TeamWheelWizard/WheelWizard'; Asset = 'WheelWizardWindows.exe'
        Accent = '#E4002B'; Accent2 = '#FFD100'
        FirstRun = 'WheelWizard.exe'
        Needs = 'Your own clean Mario Kart Wii disc image (PAL RMCP01, NTSC-U RMCE01 or NTSC-J RMCJ01; ISO, WBFS, RVZ and more). In Wheel Wizard: pick the image under Settings, turn on WiiCompiled (beta), then Install on the Home page. About 20 GB free while it builds.'
    },
    @{
        Id = 'sms'; Name = 'Super Mario Sunshine'; Short = 'SMS'; Source = "SMS LAUNCHER  $Bullet  GITHUB"
        Exe = 'SMS-Launcher-*-win-portable.exe'; Proc = 'SMS-Launcher*'; SettingsKey = 'SmsDir'
        Repo = 'chasem-dev/sms-launcher'; Asset = 'SMS-Launcher-*-win-portable.exe'
        Accent = '#1FA7E0'; Accent2 = '#F7C531'
        FirstRun = 'SMS-Launcher-*-win-portable.exe'
        Needs = 'Your own North American Super Mario Sunshine disc image (GMSE01, revision 0) as .iso, .gcm or .ciso. SMS Launcher downloads its build tools and HD textures and builds the game on your PC; its data stays in the SMS-Launcher-Portable folder here.'
    },
    @{
        Id = 'botw'; Name = 'Breath of the Wild'; Short = 'BotW'; Source = "SAVAGE WIND RECOMP  $Bullet  GITHUB"
        Exe = 'savagewind.exe'; Proc = 'savagewind'; SettingsKey = 'BotwDir'
        Repo = 'Revivedx/SavageWindRecomp'; Asset = '*-windows.zip'; Pre = $true
        Accent = '#4FB3B0'; Accent2 = '#E5C07B'
        Backup = 'save'; FirstRun = 'savagewind.exe'
        Needs = 'Your own Breath of the Wild for Switch, updated to version 1.9.0 and extracted (exefs and romfs folders). The game asks for them on its first start. 16 GB of RAM, a Vulkan 1.3 GPU and about 20 GB free. Alpha: the Great Plateau and beyond is playable, expect bugs.'
    },
    @{
        Id = 'acit'; Name = 'Ratchet & Clank: A Crack in Time'; Short = 'ACiT'; Source = "RACIT  $Bullet  GITHUB"
        Exe = 'RaCACiT Launcher.exe'; Proc = 'RaCACiT Launcher'; SettingsKey = 'AcitDir'
        Repo = 'HighDrexler/RACiT'; Asset = '*.zip'
        Accent = '#9B59D0'; Accent2 = '#F2A33A'
        FirstRun = 'RaCACiT Launcher.exe'
        Needs = 'Your own North American PS3 disc (BCUS98124), decrypted, as an .iso or the PS3_GAME folder (with a decrypted EBOOT.elf). The RaCACiT Launcher asks for it on its first start. Work in progress.'
    },
    @{
        # saves, seeds, settings and the built game stay next to lasrecomp.exe; updates unpack over them
        Id = 'las'; Name = "Link's Awakening"; Short = 'LA'; Source = "LAS RECOMP  $Bullet  GITHUB"
        Exe = 'lasrecomp.exe'; Proc = 'lasrecomp'; SettingsKey = 'LasDir'
        Repo = 'peiikai/LASRecomp'; Asset = '*-windows.zip'
        Accent = '#3FA9C9'; Accent2 = '#E9C46A'
        FirstRun = 'lasrecomp.exe'
        Needs = "Your own Link's Awakening for Switch, version 1.0.0, 1.0.1 or 1.1.0, extracted (exefs and romfs folders). The start screen's game-file setup asks for that folder, then builds the game (about 12 GB free). Beta, with a randomizer."
    },
    @{
        # PS4 port; the newest builds are all pre-releases. Saves stay in user\ next to the exe, updates unpack over them
        Id = 'iss'; Name = 'inFAMOUS Second Son'; Short = 'ISS'; Source = "ISS_PC  $Bullet  GITHUB"
        Exe = 'inFAMOUS Second Son.exe'; Proc = 'inFAMOUS Second Son'; SettingsKey = 'IssDir'
        Repo = 'HayatoG/iss_pc'; Asset = 'infamous-windows-x86_64.zip'; Pre = $true
        Accent = '#D6452E'; Accent2 = '#4FB3E8'
        Backup = 'user'
        FirstRun = 'inFAMOUS Second Son.exe'
        Needs = 'Your own US PS4 copy (CUSA00223): base game 1.00 with update 1.07 copied over it, in one folder with eboot.bin and sce_sys (about 33 GB, dumped from your PS4). Pick that folder under Game folder in its launcher. Playable but in development: 20-30 fps is typical.'
    },
    @{
        # Nebula-Setup.exe installs the game into AppData and keeps saves there; Play starts the installed game once
        # Setup has made it, and Setup itself until then
        Id = 'smg'; Name = 'Super Mario Galaxy'; Short = 'SMG'; Source = "NEBULA  $Bullet  GITHUB"
        Exe = 'Nebula-Setup.exe'; Proc = 'Nebula'; SettingsKey = 'SmgDir'
        Launch = '%LOCALAPPDATA%\Nebula\Nebula.exe;%LOCALAPPDATA%\Programs\Nebula\Nebula.exe;%LOCALAPPDATA%\Nebula\*\Nebula.exe'
        Repo = 'keepvaibin/nebula'; Asset = 'Nebula-Setup.exe'
        Accent = '#3D5BD9'; Accent2 = '#F4D35E'
        FirstRun = 'Nebula-Setup.exe'
        Needs = 'Your own North American Super Mario Galaxy disc (RMGE01, revision 0) as an .iso, .rvz or extracted folder. Nebula Setup asks for it, then translates and builds the game on your PC (about 16 GB free and internet while it installs).'
    },
    @{
        # saves, settings and mods stay in engine\UserData; updates unpack over the folder
        Id = 'spm'; Name = 'Super Paper Mario'; Short = 'SPM'; Source = "NEW SUPER PAPER BROS.  $Bullet  GITHUB"
        Exe = 'New Super Paper Bros.exe'; Proc = 'nspb-engine'; SettingsKey = 'SpmDir'
        Repo = 'josiahg2502-glitch/NewSuperPaperBros'; Asset = 'NewSuperPaperBros.zip'
        Accent = '#E94B8A'; Accent2 = '#FFD23F'
        Backup = 'engine\UserData'
        FirstRun = 'New Super Paper Bros.exe'
        Needs = 'Your own Super Paper Mario Wii disc, USA (R8PE01, revision 0) or Europe (R8PP01), as .iso, .ciso or .wbfs. Pick it on the Play page the first time; its files are copied in once.'
    },
    @{
        Id = 'tp'; Name = 'Twilight Princess'; Short = 'TP'; Source = "DUSKLIGHT  $Bullet  GITHUB"
        Exe = 'dusklight.exe'; Proc = 'dusklight'; SettingsKey = 'TpDir'
        Repo = 'TwilitRealm/dusklight'; Asset = '*-win32-x86_64.zip'
        Accent = '#C9A227'; Accent2 = '#5E8C61'
        FirstRun = 'dusklight.exe'
        Needs = 'Your own Twilight Princess disc, GameCube or Wii (any region except the Korean Wii one), as an .iso or .rvz. Dusklight asks for it on its first start.'
    },
    @{
        # saves, settings and caches live in Documents\MarioWiiSaveData, outside the folder
        Id = 'nsmbw'; Name = 'New Super Mario Bros. Wii'; Short = 'NSMBW'; Source = "NSMBW RECOMP  $Bullet  GITHUB"
        Exe = 'NSMBW.exe'; Proc = 'NSMBW'; SettingsKey = 'NsmbwDir'
        Repo = 'WitherBlitz/NSMB-Wii-Multiplayer-Recompilation'; Asset = '*-windows-x64.zip'
        Accent = '#E52521'; Accent2 = '#43B047'
        FirstRun = 'NSMBW.exe'
        Needs = 'Your own USA New Super Mario Bros. Wii disc (SMNE01, revision 1 only: Dolphin shows it under Properties > Info), extracted with Dolphin. The first start asks for it. Couch and LAN multiplayer.'
    },
    @{
        Id = 'nsmbu'; Name = 'New Super Mario Bros. U'; Short = 'NSMBU'; Source = "NSMBU-RECOMP  $Bullet  GITHUB"
        Exe = 'NSMBU.exe'; Proc = 'NSMBU'; SettingsKey = 'NsmbuDir'
        Repo = 'nsmbu-recomp/nsmbu-recomp'; Asset = '*-windows-x86_64.zip'
        Accent = '#1E88E5'; Accent2 = '#FBC02D'
        FirstRun = 'NSMBU.exe'
        Needs = 'Your own Wii U New Super Mario Bros. U, USA or Europe: a .wux / .wud disc image with its keys, or an extracted folder with code, content and meta (USA update 1.3.0 is the best match). The first start prepares the game once.'
    },
    @{
        Id = 'pkgen'; Name = "$Pk Gen 1 & 2"; Short = 'GB'; Source = "GEN2RECOMPED  $Bullet  GITHUB"
        Exe = 'Gen2Recomped.exe'; Proc = 'Gen2Recomped'; SettingsKey = 'PkgenDir'
        Repo = 'UNDERdecoded/Gen2Recomped'; Asset = '*-windows.zip'
        Accent = '#E3350D'; Accent2 = '#FFCB05'
        FirstRun = 'Gen2Recomped.exe'
        Needs = "Your own cartridge dumps: Red, Blue, Yellow, Gold, Silver, Crystal and more (.gb / .gbc, also some .gba / .nds). Only exact original dumps are accepted (it checks them). Pick or drop them on the window on its first start; it keeps them."
    },
    @{
        Id = 'pmd'; Name = "$Pk Mystery Dungeon: Red Rescue Team"; Short = 'PMD'; Source = "PMD-GREEN  $Bullet  GITHUB"
        Exe = 'pmd_green.exe'; Proc = 'pmd_green'; SettingsKey = 'PmdDir'
        Repo = 'Asphaltian/pmd-green'; Asset = '*-Windows.zip'
        Accent = '#D9632B'; Accent2 = '#7CC0E8'
        FirstRun = 'pmd_green.exe'
        Needs = 'Your own Red Rescue Team GBA cartridge dump, USA/Australia version (.gba). The game asks for it on its first start.'
    },
    @{
        # saves go to %APPDATA%\eidos\saves
        Id = 'pkblack'; Name = "$Pk Black"; Short = 'BW'; Source = "EIDOS  $Bullet  GITHUB"
        Exe = 'eidos.exe'; Proc = 'eidos'; SettingsKey = 'PkblackDir'
        Repo = 'Project-Holon/eidos'; Asset = 'eidos-*-windows.zip'
        Accent = '#3B3B3B'; Accent2 = '#9AD0F5'
        FirstRun = 'eidos.exe'
        Needs = "Your own $Pk Black DS dump, USA or Europe (.nds). Early: the opening hours (field, battles, menus, saves) work."
    },
    @{
        # a setup program that builds the game from your ROM and updates it; Play opens it
        Id = 'pkss'; Name = "$Pk SoulSilver"; Short = 'SS'; Source = "SSRECOMP  $Bullet  GITHUB"
        Exe = 'ssrecomp-setup-windows-x86_64.exe'; Proc = 'ssrecomp-setup-windows-x86_64'; SettingsKey = 'PkssDir'
        Repo = 'ClickingMaster/ssrecomp'; Asset = 'ssrecomp-setup-windows-x86_64.exe'
        Accent = '#A8B5C4'; Accent2 = '#4A6FA5'
        FirstRun = 'ssrecomp-setup-windows-x86_64.exe'
        Needs = "Your own $Pk SoulSilver DS dump, US version only (.nds; European ones don't match). Its setup compiles the game on your PC, so it also needs the free MSYS2 build tools (it shows the commands). One screen instead of the DS's two."
    },
    @{
        Id = 'pkstadium'; Name = "$Pk Stadium"; Short = 'STD'; Source = "STADIUM RECOMP  $Bullet  GITHUB"
        Exe = 'PokemonStadiumRecomp.exe'; Proc = 'PokemonStadiumRecomp'; SettingsKey = 'PkstadiumDir'
        Repo = 'mstan/PokemonStadiumRecomp'; Asset = '*-win64.zip'
        Accent = '#2A75BB'; Accent2 = '#FFCB05'
        FirstRun = 'PokemonStadiumRecomp.exe'
        Needs = "Your own $Pk Stadium N64 ROM, US version 1.0 only (.z64 / .n64 / .v64; Rev A doesn't work). Red, Blue and Yellow saves can be used through its Transfer Pak. Beta."
    },
    @{
        # saves stay in saves\ next to the program
        Id = 'pksnap'; Name = "$Pk Snap"; Short = 'SNAP'; Source = "SNAP64 RECOMP  $Bullet  GITHUB"
        Exe = 'Snap64Recomp.exe'; Proc = 'Snap64Recomp'; SettingsKey = 'PksnapDir'
        Repo = 'JackandBeans/Snap64Recomp'; Asset = '*-win64.zip'
        Accent = '#4CAF50'; Accent2 = '#FFD54F'
        Backup = 'saves'
        FirstRun = 'Snap64Recomp.exe'
        Needs = "Your own $Pk Snap N64 ROM, US cartridge only (.z64). The launcher copies it in as pokemonsnap.z64. Plays like the cartridge; every enhancement is optional."
    },
    @{
        Id = 'pklgpe'; Name = "$Pk Let's Go"; Short = 'LGPE'; Source = "LET'S GO RECOMP  $Bullet  GITHUB"
        Exe = 'LetsGoRecomp.exe'; Proc = 'LetsGoRecomp'; SettingsKey = 'PklgpeDir'
        Repo = 'raulzrrs/letsgo-recomp'; Asset = '*-windows.zip'
        Accent = '#F2C230'; Accent2 = '#C88A4A'
        Backup = 'save;saves;larecomp.ini'
        FirstRun = 'LetsGoRecomp.exe'
        Needs = "Your own Let's Go, Eevee! or Pikachu! for Switch, any region, version 1.0.0 (Eevee 1.0.2 also works), extracted (exefs and romfs folders). Pick that folder on its Game page."
    },
    @{
        Id = 'pkswsh'; Name = "$Pk Sword & Shield"; Short = 'SWSH'; Source = "SWSH RECOMP  $Bullet  GITHUB"
        Exe = 'SwShRecomp.exe'; Proc = 'SwShRecomp'; SettingsKey = 'PkswshDir'
        Repo = 'raulzrrs/swsh-recomp'; Asset = '*-windows.zip'; Pre = $true
        Accent = '#00A1E9'; Accent2 = '#E4003A'
        Backup = 'save-sword;save-shield;larecomp.ini'
        FirstRun = 'SwShRecomp.exe'
        Needs = "Your own $Pk Sword (Shield is untested) for Switch, version 1.3.2, extracted (exefs and romfs folders). Pick that folder in its window. Early test release; the Expansion Pass isn't set up yet."
    },
    @{
        # the code is recompiled once from your exefs (Iniciar_Jogo.bat asks, in Portuguese: 1 = yes); then it starts
        Id = 'pkla'; Name = "$Pk Legends: Arceus"; Short = 'PLA'; Source = "ARCEUSZADA  $Bullet  GITHUB"
        Exe = 'arceusrecomp.exe'; Proc = 'arceusrecomp'; SettingsKey = 'PklaDir'; Launch = 'Iniciar_Jogo.bat'; Console = $true
        Repo = 'CarlosExabyte/Arceuszada'; Asset = 'Arceuszada-*.zip'
        Accent = '#6E8B6B'; Accent2 = '#E6C27A'
        FirstRun = 'Iniciar_Jogo.bat'
        Needs = "Your own Legends: Arceus for Switch, extracted (exefs and romfs folders). The launcher copies both into the game's folder; then its first start asks (in Portuguese) to recompile: type 1 and Enter. Amateur project."
    },
    @{
        Id = 'pokopia'; Name = "$Pk Pokopia"; Short = 'PKP'; Source = "POKOPIA RECOMP  $Bullet  GITHUB"
        Exe = 'PokopiaLauncher.exe'; Proc = 'PokopiaLauncher'; SettingsKey = 'PokopiaDir'
        Repo = 'drakolordx7/pokopia-recomp'; Asset = '*-win64.zip'
        Accent = '#7BC96F'; Accent2 = '#F6D365'
        FirstRun = 'PokopiaLauncher.exe'
        Needs = 'Your own Pokopia for Switch 2, version 2.0.1a: an extracted folder, or NSP / XCI files with your keys. Its Set up builds the game (about 7 minutes) and then the shader cache (1 to 2 hours, can be paused).'
    },
    @{
        Id = 'mm'; Name = 'Majora''s Mask'; Short = 'MM'; Source = "ZELDA 64: RECOMPILED  $Bullet  GITHUB"
        Exe = 'Zelda64Recompiled.exe'; Proc = 'Zelda64Recompiled'; SettingsKey = 'MmDir'
        Repo = 'Zelda64Recomp/Zelda64Recomp'; Asset = '*-Windows.zip'
        Accent = '#7A4BC2'; Accent2 = '#E8B04A'
        FirstRun = 'Zelda64Recompiled.exe'
        Needs = 'Your own North American Majora''s Mask N64 ROM (.z64). Pick it in the game''s main menu; assets load straight from it.'
    },
    @{
        Id = 'mm2s'; Name = 'Majora''s Mask (2 Ship)'; Short = '2S2H'; Source = "2 SHIP 2 HARKINIAN  $Bullet  GITHUB"
        Exe = '2ship.exe'; Proc = '2ship'; SettingsKey = 'Mm2sDir'
        Repo = '2ship2harkinian/2ship2harkinian'; Asset = '*-Win64.zip'
        Accent = '#5B3F8C'; Accent2 = '#D96C3B'
        Backup = 'Save;2ship2harkinian.json'
        FirstRun = '2ship.exe'
        Needs = 'Your own Majora''s Mask N64 ROM (.z64, US; check it at 2ship.equipment). The first start asks for it and builds its asset file once. Lots of enhancements and randomizer options.'
    },
    @{
        Id = 'oot'; Name = 'Ocarina of Time'; Short = 'OoT'; Source = "SHIP OF HARKINIAN  $Bullet  GITHUB"
        Exe = 'soh.exe'; Proc = 'soh'; SettingsKey = 'OotDir'
        Repo = 'HarbourMasters/Shipwright'; Asset = '*-Win64.zip'
        Accent = '#2E8B57'; Accent2 = '#C9A227'
        Backup = 'Save;shipofharkinian.json'
        FirstRun = 'soh.exe'
        Needs = 'Your own Ocarina of Time N64 ROM (.z64; check it at ship.equipment). The first start asks for it and builds oot.o2r once. Master Quest works too.'
    },
    @{
        Id = 'sm64'; Name = 'Super Mario 64'; Short = 'SM64'; Source = "GHOSTSHIP  $Bullet  GITHUB"
        Exe = 'Ghostship.exe'; Proc = 'Ghostship'; SettingsKey = 'Sm64Dir'
        Repo = 'HarbourMasters/Ghostship'; Asset = '*-Win64.zip'
        Accent = '#E52521'; Accent2 = '#FFD700'
        Backup = 'Save;ghostship.json'
        FirstRun = 'Ghostship.exe'
        Needs = 'Your own Super Mario 64 N64 ROM, US or Japan, in .z64 format. The first start asks for it and builds its asset file once.'
    },
    @{
        Id = 'mk64'; Name = 'Mario Kart 64'; Short = 'MK64'; Source = "SPAGHETTIKART  $Bullet  GITHUB"
        Exe = 'Spaghettify.exe'; Proc = 'Spaghettify'; SettingsKey = 'Mk64Dir'
        Repo = 'HarbourMasters/SpaghettiKart'; Asset = 'spaghetti-windows.zip'
        Accent = '#2B6CE0'; Accent2 = '#E52521'
        Backup = 'Save;spaghettify.json'
        FirstRun = 'Spaghettify.exe'
        Needs = 'Your own Mario Kart 64 N64 ROM, US version only, in .z64 format. The first start asks for it and builds its asset file once.'
    },
    @{
        Id = 'sf64'; Name = 'Star Fox 64'; Short = 'SF64'; Source = "STARSHIP  $Bullet  GITHUB"
        Exe = 'Starship.exe'; Proc = 'Starship'; SettingsKey = 'Sf64Dir'
        Repo = 'HarbourMasters/Starship'; Asset = '*-Windows.zip'; NoSha = $true
        Accent = '#2F7FD1'; Accent2 = '#D8DEE6'
        Backup = 'Save;starship.json'
        FirstRun = 'Starship.exe'
        Needs = 'Your own Star Fox 64 N64 ROM, US 1.0 or 1.1 (.z64). The first start asks for it and builds its asset file once. This project publishes no checksum, so the launcher checks the size and the zip instead.'
    },
    @{
        Id = 'bk'; Name = 'Banjo-Kazooie'; Short = 'BK'; Source = "BANJO: RECOMPILED  $Bullet  GITHUB"
        Exe = 'BanjoRecompiled.exe'; Proc = 'BanjoRecompiled'; SettingsKey = 'BkDir'
        Repo = 'BanjoRecomp/BanjoRecomp'; Asset = '*-Windows.zip'
        Accent = '#E9A23B'; Accent2 = '#3B7DD8'
        FirstRun = 'BanjoRecompiled.exe'
        Needs = 'Your own North American Banjo-Kazooie N64 ROM, version 1.0 (.z64). Pick it in the main menu.'
    },
    @{
        Id = 'dk64'; Name = 'Donkey Kong 64'; Short = 'DK64'; Source = "DK64: RECOMPILED  $Bullet  GITHUB"
        Exe = 'DK64Recompiled.exe'; Proc = 'DK64Recompiled'; SettingsKey = 'Dk64Dir'
        Repo = 'Rainchus/Donkey-Kong-64-Recompiled'; Asset = '*-Windows-*.zip'
        Accent = '#8B5A2B'; Accent2 = '#F2C230'
        FirstRun = 'DK64Recompiled.exe'
        Needs = 'Your own Donkey Kong 64 N64 ROM, US version only (.z64). Pick it in the main menu.'
    },
    @{
        Id = 'conker'; Name = 'Conker''s Bad Fur Day'; Short = 'CBFD'; Source = "CONKER BFD RELOADED  $Bullet  GITHUB"
        Exe = 'ConkerBFDReloaded.exe'; Proc = 'ConkerBFDReloaded'; SettingsKey = 'ConkerDir'
        Repo = 'DahSidiAbdallah/ConkerBFDReloaded'; Asset = '*-Windows.zip'
        Accent = '#C0392B'; Accent2 = '#8D6E4A'
        FirstRun = 'ConkerBFDReloaded.exe'
        Needs = 'Your own Conker''s Bad Fur Day N64 ROM, US version (.z64). Pick it the first time; it''s remembered.'
    },
    @{
        Id = 'sonic'; Name = 'Sonic Unleashed'; Short = 'SU'; Source = "UNLEASHED RECOMPILED  $Bullet  GITHUB"
        Exe = 'UnleashedRecomp.exe'; Proc = 'UnleashedRecomp'; SettingsKey = 'SonicDir'
        Repo = 'hedge-dev/UnleashedRecomp'; Asset = '*-Windows.zip'; NoSha = $true
        Accent = '#1F5FD6'; Accent2 = '#F2C230'
        FirstRun = 'UnleashedRecomp.exe'
        Needs = 'Your own Xbox 360 Sonic Unleashed (disc dump or game files), plus its title update; the DLC is optional. Its installer asks for them on the first start (6 GB, 10 GB with DLC). No checksum published, so the launcher checks the size and the zip.'
    },
    @{
        Id = 'thps1'; Name = 'Tony Hawk''s Pro Skater'; Short = 'THPS'; Source = "THPS1 64 RECOMP  $Bullet  GITHUB"
        Exe = 'THPS1-64Recomp.exe'; Proc = 'THPS1-64Recomp'; SettingsKey = 'Thps1Dir'
        Repo = 'THPS64Recomp/THPS1-64Recomp'; Asset = '*-Windows-x64.zip'
        Accent = '#F57C00'; Accent2 = '#2B2B2B'
        FirstRun = 'THPS1-64Recomp.exe'
        Needs = 'Your own Tony Hawk''s Pro Skater N64 cartridge dump, USA 1.0 or 1.1 (.z64, .n64 or .v64). The app builds the game from it.'
    },
    @{
        Id = 'thps2'; Name = 'Tony Hawk''s Pro Skater 2'; Short = 'THPS2'; Source = "THPS2 64 RECOMP  $Bullet  GITHUB"
        Exe = 'THPS2-64Recomp.exe'; Proc = 'THPS2-64Recomp'; SettingsKey = 'Thps2Dir'
        Repo = 'THPS64Recomp/THPS2-64Recomp'; Asset = '*-Windows-x64.zip'; Pre = $true
        Accent = '#E53935'; Accent2 = '#2B2B2B'
        FirstRun = 'THPS2-64Recomp.exe'
        Needs = 'Your own Tony Hawk''s Pro Skater 2 N64 cartridge dump (.z64, .n64 or .v64). The app builds the game from it. Early preview.'
    },
    @{
        Id = 'thps3'; Name = 'Tony Hawk''s Pro Skater 3'; Short = 'THPS3'; Source = "THPS3 64 RECOMP  $Bullet  GITHUB"
        Exe = 'THPS3-64Recomp.exe'; Proc = 'THPS3-64Recomp'; SettingsKey = 'Thps3Dir'
        Repo = 'THPS64Recomp/THPS3-64Recomp'; Asset = '*-Windows-x64.zip'; Pre = $true
        Accent = '#43A047'; Accent2 = '#2B2B2B'
        FirstRun = 'THPS3-64Recomp.exe'
        Needs = 'Your own Tony Hawk''s Pro Skater 3 N64 cartridge dump (.z64, .n64 or .v64). The app builds the game from it. Early preview.'
    },
    @{
        Id = 'nomercy'; Name = 'WWF No Mercy'; Short = 'NM'; Source = "NO MERCY RECOMP  $Bullet  GITHUB"
        Exe = 'NoMercyRecompiled.exe'; Proc = 'NoMercyRecompiled'; SettingsKey = 'NomercyDir'
        Repo = 'jessetbh/WWFNoMercyRecomp'; Asset = '*-Windows.zip'; Pre = $true
        Accent = '#B71C1C'; Accent2 = '#EEEEEE'
        FirstRun = 'NoMercyRecompiled.exe'
        Needs = 'Your own WWF No Mercy N64 ROM (.z64). Assets load straight from it. Test releases.'
    },
    @{
        Id = 'wm2k'; Name = 'WWF WrestleMania 2000'; Short = 'WM2K'; Source = "WRESTLEMANIA 2000 RECOMP  $Bullet  GITHUB"
        Exe = 'Wm2kRecompiled.exe'; Proc = 'Wm2kRecompiled'; SettingsKey = 'Wm2kDir'
        Repo = 'jessetbh/WWFWrestleMania2000Recomp'; Asset = '*-Windows.zip'; Pre = $true
        Accent = '#6A1B9A'; Accent2 = '#FFC107'
        FirstRun = 'Wm2kRecompiled.exe'
        Needs = 'Your own WWF WrestleMania 2000 N64 ROM (.z64). Test releases.'
    },
    @{
        Id = 'wcwwt'; Name = 'WCW vs. nWo: World Tour'; Short = 'WT'; Source = "WORLD TOUR RECOMP  $Bullet  GITHUB"
        Exe = 'WCWRecompiled.exe'; Proc = 'WCWRecompiled'; SettingsKey = 'WcwwtDir'
        Repo = 'jessetbh/WCWvsNWOWorldTourRecomp'; Asset = '*-Windows.zip'; Pre = $true
        Accent = '#37474F'; Accent2 = '#E0E0E0'
        FirstRun = 'WCWRecompiled.exe'
        Needs = 'Your own WCW vs. nWo: World Tour N64 ROM (.z64). Test releases.'
    },
    @{
        Id = 'revenge'; Name = 'WCW/nWo Revenge'; Short = 'REV'; Source = "REVENGE RECOMP  $Bullet  GITHUB"
        Exe = 'RevengeRecompiled.exe'; Proc = 'RevengeRecompiled'; SettingsKey = 'RevengeDir'
        Repo = 'jessetbh/WCWnWoRevengeRecomp'; Asset = '*-Windows.zip'; Pre = $true
        Accent = '#212121'; Accent2 = '#D32F2F'
        FirstRun = 'RevengeRecompiled.exe'
        Needs = 'Your own WCW/nWo Revenge N64 ROM (.z64). Test releases.'
    },
    @{
        Id = 'vpw64'; Name = 'Virtual Pro Wrestling 64'; Short = 'VPW'; Source = "VPW64 RECOMP  $Bullet  GITHUB"
        Exe = 'Vpw64Recompiled.exe'; Proc = 'Vpw64Recompiled'; SettingsKey = 'Vpw64Dir'
        Repo = 'jessetbh/VPW64Recomp'; Asset = '*-Windows.zip'; Pre = $true
        Accent = '#1565C0'; Accent2 = '#FFEB3B'
        FirstRun = 'Vpw64Recompiled.exe'
        Needs = 'Your own Virtual Pro Wrestling 64 N64 ROM (Japan, .z64). Test releases.'
    }
)
function Apply-Settings {
    foreach ($g in $Games) {
        $g.Dir = $Settings[$g.SettingsKey]
        $g.Worker = Join-Path $Root 'recomp-worker.ps1'
        $g.Backups = Join-Path $Root "backups\$($g.Id)"
    }
    $t = $Games[0]
    $t.Worker = Join-Path $Settings.TotkUpdaterDir 'update-worker.ps1'
    $t.Backups = Join-Path $Settings.TotkUpdaterDir 'backups'
}
Apply-Settings

# "v0.4.1-alpha.4", "v.0.9.5", "0.3.7+0f65b4e" -> a comparable version
function To-Version($s) { try { [version]((([string]$s -replace '^[^\d]*', '') -replace '[+-].*$', '') -replace '\.$', '') } catch { $null } }

# What the launcher installed or updated last, written by recomp-worker.ps1
function Get-Marker($g) {
    $f = Join-Path $g.Dir 'recomp-launcher.json'
    if (Test-Path $f) { try { return Get-Content $f -Raw | ConvertFrom-Json } catch {} }
    $null
}

# The game's program. Exe can be a pattern (SMS Launcher's portable exe carries its version in its name): the newest
# match then. Returns the path where it would be when nothing matches.
function Resolve-Exe($g) {
    $p = Join-Path $g.Dir $g.Exe
    if ($g.Exe -notmatch '[*?]') { return $p }
    $hit = Get-ChildItem $p -File -ErrorAction SilentlyContinue | Sort-Object { To-Version $_.VersionInfo.ProductVersion }, LastWriteTime -Descending | Select-Object -First 1
    if ($hit) { $hit.FullName } else { $p }
}

function Get-Installed($g) {
    $exe = Resolve-Exe $g
    $mk = Get-Marker $g
    switch ($g.Id) {
        'totk' {
            $j = Join-Path $g.Dir 'totk-install.json'
            if (-not (Test-Path $j)) { return $null }
            try { $inst = Get-Content $j -Raw | ConvertFrom-Json } catch { return $null }
            return @{ Version = ($inst.release -replace '\+.*$', ''); Sub = "Game $($inst.version)  $Bullet  $($inst.lang)" }
        }
        'smo' {
            if (-not (Test-Path $exe)) { return $null }
            $v = (Get-Item $exe).VersionInfo.FileVersion
            $sub = if (Test-Path (Join-Path $g.Dir 'extracted')) { 'Game 1.0.0' } else { 'Game files not set up yet' }
            return @{ Version = $(if ($v) { $v } elseif ($mk) { $mk.tag -replace '^v' } else { '?' }); Sub = $sub }
        }
        'wwhd' {
            if (-not (Test-Path $exe)) { return $null }
            $v = $null
            $vt = Join-Path $g.Dir 'VERSION.txt'
            if ((Test-Path $vt) -and ((Get-Content $vt -Raw) -match 'v(\d+\.\d+(\.\d+)?)')) { $v = $Matches[1] }
            elseif ($mk) { $v = $mk.tag -replace '^v' }
            $sub = if (Test-Path (Join-Path $g.Dir 'data\game')) { 'Game set up' } else { 'Game not set up yet' }
            return @{ Version = $(if ($v) { $v } else { '?' }); Sub = $sub }
        }
        'tod' {
            if (-not (Test-Path $exe)) { return $null }
            $v = if ($mk) { $mk.tag -replace '^v' } else { (Get-Item $exe).VersionInfo.FileVersion }
            $sub = if (Test-Path (Join-Path $g.Dir 'elf\EBOOT.ELF')) { 'Disc installed' } else { 'Disc not installed yet' }
            return @{ Version = $(if ($v) { $v } else { '?' }); Sub = $sub }
        }
        'mp6' {
            if (-not (Test-Path $exe)) { return $null }
            $v = if ($mk) { $mk.tag -replace '^v' } else { (Get-Item $exe).VersionInfo.FileVersion }
            $sub = if (Test-Path (Join-Path $g.Dir 'content')) { 'Disc set up' } else { 'Disc not set up yet' }
            return @{ Version = $(if ($v) { $v } else { '?' }); Sub = $sub }
        }
        default {
            # games that run their own launcher or setup: the launcher's marker says which release is here
            if (-not (Test-Path $exe)) { return $null }
            $v = if ($mk) { $mk.tag -replace '^[^\d]*' } else { (Get-Item $exe).VersionInfo.ProductVersion }
            return @{ Version = $(if ($v) { $v } else { '?' }); Sub = '' }
        }
    }
    $null
}

# Runs in a background runspace so the window keeps animating while it waits for GitHub / GitLab
$LatestScript = {
    param($job)
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    if ($job.Repo) {
        # GitHub: the latest release, or with Pre (projects that only publish pre-releases) the newest release that
        # has the download; its asset digest is the SHA-256 the worker checks
        $hdr = @{ 'User-Agent' = 'Recomp-Launcher' }
        if ($job.Token) { $hdr.Authorization = "Bearer $($job.Token)" }   # 5,000 checks an hour instead of 60
        # a wrong or expired token makes GitHub refuse everything (401): ask again without it
        $get = {
            param($u)
            try { Invoke-RestMethod $u -Headers $hdr -TimeoutSec 20 }
            catch {
                $code = try { [int]$_.Exception.Response.StatusCode } catch { 0 }
                if ($code -eq 401 -and $hdr.Authorization) { $hdr.Remove('Authorization'); Invoke-RestMethod $u -Headers $hdr -TimeoutSec 20 }
                else { throw }
            }
        }
        if ($job.Pre) {
            $all = & $get "https://api.github.com/repos/$($job.Repo)/releases?per_page=15"
            $rel = @($all | Where-Object { -not $_.draft -and ($_.assets | Where-Object { $_.name -like $job.Asset }) } |
                Sort-Object { [datetime]$_.published_at } -Descending)[0]
        } else {
            $rel = & $get "https://api.github.com/repos/$($job.Repo)/releases/latest"
        }
        $asset = $rel.assets | Where-Object { $_.name -like $job.Asset -and $_.name -notlike $job.NotAsset } | Select-Object -First 1
        $r = @{ Tag = $rel.tag_name; Notes = $rel.body; Date = $rel.published_at }
        if ($asset) {
            $r.Url = $asset.browser_download_url; $r.Size = $asset.size
            if ($asset.digest -match '^sha256:([0-9a-f]{64})$') { $r.Sha = $Matches[1] }
        } else { $r.Problem = 'No Windows download in this release' }
        return $r
    }
    $rel = @(Invoke-RestMethod 'https://gitlab.com/api/v4/projects/88379810/releases?per_page=1' -TimeoutSec 20)[0]
    $body = [string]$rel.description
    # notes up to the downloads, with GitLab's escapes (such as "-\>") undone
    $r = @{ Tag = $rel.tag_name; Notes = (($body -split '\*\*Downloads\*\*')[0] -replace '\\([>\-\*_#\[\]\(\)!.+`])', '$1'); Date = $rel.released_at }
    # The zip is linked from the notes as /uploads/<secret>/<file>; GitLab serves it under the project id
    $m = [regex]::Match($body, '\(/uploads/([0-9a-f]{32})/([^)\s]*windows[^)\s]*\.zip)\)')
    if ($m.Success) { $r.Url = "https://gitlab.com/-/project/88379810/uploads/$($m.Groups[1].Value)/$($m.Groups[2].Value)" }
    else {
        $link = $rel.assets.links | Where-Object { $_.url -like '*windows*.zip' } | Select-Object -First 1
        if ($link) { $r.Url = $link.url }
    }
    # the hash sits on the same line as the Windows zip's name, as "- `<hash>` name" or in a code block as "<hash>  name"
    # (either order, with or without backticks)
    foreach ($line in ($body -split '\r?\n')) {
        if ($line -match 'windows[^\s]*\.zip' -and $line -match '(?<![0-9a-fA-F])([0-9a-fA-F]{64})(?![0-9a-fA-F])') { $r.Sha = $Matches[1].ToLower(); break }
    }
    if (-not $r.Url) { $r.Problem = 'No Windows download in this release' }
    elseif (-not $r.Sha) { $r.Problem = 'No SHA-256 in the notes to check the download' }
    $r
}

# ---------- Window ----------
# Console-style layout: the selected game fills the background, its title and buttons sit above a row of game cards,
# a top bar holds search, filters and the launcher's own buttons. What's new / Activity and Settings are overlays.
[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Recomp Launcher" Width="1280" Height="780" MinWidth="1040" MinHeight="660" WindowStyle="None"
        Background="#090C0F" ResizeMode="CanResize" WindowStartupLocation="CenterScreen"
        FontFamily="Segoe UI Variable Text, Segoe UI" Foreground="#E8E6DF" UseLayoutRounding="True">
  <!-- An ordinary (not see-through) window without a title bar: drawn by the graphics card, much cheaper than a
       transparent one. Windows 11 rounds its corners (WindowCorners); the edges still resize it. -->
  <WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight="0" ResizeBorderThickness="6" GlassFrameThickness="0" CornerRadius="0" UseAeroCaptionButtons="False"/>
  </WindowChrome.WindowChrome>
  <Window.Resources>
    <Style x:Key="Btn" TargetType="Button">
      <Setter Property="Foreground" Value="#E8E6DF"/>
      <Setter Property="Background" Value="#1C252C"/>
      <Setter Property="BorderBrush" Value="Transparent"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="FontFamily" Value="Segoe UI Variable Display Semibold, Segoe UI Semibold"/>
      <Setter Property="FontSize" Value="14"/>
      <Setter Property="Padding" Value="22,0"/>
      <Setter Property="Height" Value="46"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Focusable" Value="False"/>
      <Setter Property="RenderTransformOrigin" Value="0.5,0.5"/>
      <Setter Property="RenderTransform"><Setter.Value><ScaleTransform/></Setter.Value></Setter>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Grid>
              <Border x:Name="bd" CornerRadius="12" Background="{TemplateBinding Background}"
                      BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"/>
              <Border x:Name="hl" CornerRadius="12" Background="White" Opacity="0"/>
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}"/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Trigger.EnterActions><BeginStoryboard><Storyboard>
                  <DoubleAnimation Storyboard.TargetName="hl" Storyboard.TargetProperty="Opacity" To="0.12" Duration="0:0:0.15"/>
                </Storyboard></BeginStoryboard></Trigger.EnterActions>
                <Trigger.ExitActions><BeginStoryboard><Storyboard>
                  <DoubleAnimation Storyboard.TargetName="hl" Storyboard.TargetProperty="Opacity" To="0" Duration="0:0:0.25"/>
                </Storyboard></BeginStoryboard></Trigger.ExitActions>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter Property="RenderTransform"><Setter.Value><ScaleTransform ScaleX="0.96" ScaleY="0.96"/></Setter.Value></Setter>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.32"/>
                <Setter Property="Cursor" Value="Arrow"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <!-- glass icon button of the top bar and the game page -->
    <Style x:Key="Glass" TargetType="Button" BasedOn="{StaticResource Btn}">
      <Setter Property="Background" Value="#3D0E1418"/>
      <Setter Property="BorderBrush" Value="#33FFFFFF"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Height" Value="42"/>
      <Setter Property="MinWidth" Value="42"/>
      <Setter Property="Padding" Value="12,0"/>
      <Setter Property="FontSize" Value="13"/>
    </Style>
    <Style x:Key="Chrome" TargetType="Button" BasedOn="{StaticResource Btn}">
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="Height" Value="34"/>
      <Setter Property="Width" Value="40"/>
      <Setter Property="Padding" Value="0"/>
      <Setter Property="FontFamily" Value="Segoe MDL2 Assets"/>
      <Setter Property="FontSize" Value="10"/>
    </Style>
    <Style x:Key="PathBox" TargetType="TextBox">
      <Setter Property="Foreground" Value="#E8E6DF"/>
      <Setter Property="CaretBrush" Value="#E8E6DF"/>
      <Setter Property="SelectionBrush" Value="#3FB8A0"/>
      <Setter Property="FontSize" Value="13"/>
      <Setter Property="Height" Value="40"/>
      <Setter Property="VerticalContentAlignment" Value="Center"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="TextBox">
            <Border x:Name="bd" CornerRadius="10" Background="#0A0E11" BorderBrush="#26333C" BorderThickness="1" Padding="12,0">
              <ScrollViewer x:Name="PART_ContentHost" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="bd" Property="BorderBrush" Value="#3FB8A0"/></Trigger>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="bd" Property="Background" Value="#0D1215"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="BareBox" TargetType="TextBox">
      <Setter Property="Foreground" Value="#F2EFE6"/>
      <Setter Property="CaretBrush" Value="#F2EFE6"/>
      <Setter Property="SelectionBrush" Value="#3FB8A0"/>
      <Setter Property="FontSize" Value="14"/>
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="VerticalContentAlignment" Value="Center"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="TextBox">
            <ScrollViewer x:Name="PART_ContentHost" VerticalAlignment="Center"/>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType="ScrollBar">
      <Setter Property="Width" Value="6"/>
      <Setter Property="MinWidth" Value="6"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ScrollBar">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
              <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb">
                <Border CornerRadius="3" Background="#33424D"/>
              </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            </Track>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="Orientation" Value="Horizontal"><Setter Property="Visibility" Value="Collapsed"/></Trigger>
      </Style.Triggers>
    </Style>
  </Window.Resources>

  <Border x:Name="Root" CornerRadius="0" Background="#090C0F" BorderBrush="#1F2A32" BorderThickness="0"
          RenderTransformOrigin="0.5,0.5" Opacity="0">
    <Border.RenderTransform><ScaleTransform ScaleX="0.97" ScaleY="0.97"/></Border.RenderTransform>
    <Grid x:Name="Clipped">

      <!-- ===== Backdrop: the selected game's colours, light and big icon ===== -->
      <Grid x:Name="Hero" ClipToBounds="True">
        <Rectangle>
          <Rectangle.Fill>
            <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
              <GradientStop x:Name="HeroStop1" Color="#55D9B45A" Offset="0"/>
              <GradientStop x:Name="HeroStop2" Color="#223FB8A0" Offset="0.55"/>
              <GradientStop Color="#00090C0F" Offset="1"/>
            </LinearGradientBrush>
          </Rectangle.Fill>
        </Rectangle>
        <!-- soft lights: radial gradients, which look like the blurred circles they replace at a fraction of the cost -->
        <Canvas>
          <Ellipse x:Name="Glow1" Width="1160" Height="1160" Canvas.Left="360" Canvas.Top="-460" Opacity="0.55" IsHitTestVisible="False">
            <Ellipse.Fill>
              <RadialGradientBrush>
                <GradientStop x:Name="Glow1A" Color="#FFD9B45A" Offset="0"/>
                <GradientStop x:Name="Glow1M" Color="#80D9B45A" Offset="0.35"/>
                <GradientStop x:Name="Glow1B" Color="#00D9B45A" Offset="1"/>
              </RadialGradientBrush>
            </Ellipse.Fill>
          </Ellipse>
          <Ellipse x:Name="Glow2" Width="960" Height="960" Canvas.Left="-320" Canvas.Top="20" Opacity="0.32" IsHitTestVisible="False">
            <Ellipse.Fill>
              <RadialGradientBrush>
                <GradientStop x:Name="Glow2A" Color="#FF3FB8A0" Offset="0"/>
                <GradientStop x:Name="Glow2M" Color="#803FB8A0" Offset="0.35"/>
                <GradientStop x:Name="Glow2B" Color="#003FB8A0" Offset="1"/>
              </RadialGradientBrush>
            </Ellipse.Fill>
          </Ellipse>
        </Canvas>
        <Image x:Name="HeroArt" Width="640" Height="640" HorizontalAlignment="Right" VerticalAlignment="Top"
               Margin="0,-30,-90,0" Opacity="0.2" RenderTransformOrigin="0.5,0.5" RenderOptions.BitmapScalingMode="HighQuality">
          <Image.CacheMode><BitmapCache RenderAtScale="1"/></Image.CacheMode>
          <Image.RenderTransform>
            <TransformGroup><ScaleTransform/><RotateTransform Angle="-9"/></TransformGroup>
          </Image.RenderTransform>
        </Image>
        <!-- keep text readable: darker on the left and at the bottom -->
        <Rectangle HorizontalAlignment="Left" Width="980">
          <Rectangle.Fill>
            <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
              <GradientStop Color="#C0090C0F" Offset="0"/>
              <GradientStop Color="#00090C0F" Offset="1"/>
            </LinearGradientBrush>
          </Rectangle.Fill>
        </Rectangle>
        <Rectangle VerticalAlignment="Bottom" Height="460">
          <Rectangle.Fill>
            <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
              <GradientStop Color="#00090C0F" Offset="0"/>
              <GradientStop Color="#F2090C0F" Offset="0.62"/>
            </LinearGradientBrush>
          </Rectangle.Fill>
        </Rectangle>
      </Grid>

      <!-- ===== Main ===== -->
      <Grid x:Name="Main">
        <Grid.RowDefinitions>
          <RowDefinition Height="76"/>
          <RowDefinition Height="*"/>
          <RowDefinition Height="292"/>
          <RowDefinition Height="62"/>
        </Grid.RowDefinitions>

        <!-- top bar -->
        <Grid x:Name="TitleBar" Background="Transparent" Margin="34,0,14,0">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>
          <StackPanel x:Name="Logo" Orientation="Horizontal" VerticalAlignment="Center" Background="Transparent">
            <Image x:Name="LogoImg" Width="36" Height="36" RenderOptions.BitmapScalingMode="HighQuality" RenderTransformOrigin="0.5,0.5">
              <Image.RenderTransform><RotateTransform/></Image.RenderTransform>
              <Image.Effect><DropShadowEffect BlurRadius="18" ShadowDepth="0" Opacity="0.45" Color="#27D6C7"/></Image.Effect>
            </Image>
            <StackPanel Margin="12,0,0,0" VerticalAlignment="Center">
              <TextBlock Text="RECOMP" FontFamily="Segoe UI Variable Display Semibold, Segoe UI Semibold" FontSize="17"/>
              <TextBlock Text="LAUNCHER" FontSize="9.5" FontWeight="Bold" Foreground="#8A9AA5" Margin="1,-2,0,0"/>
            </StackPanel>
          </StackPanel>

          <Border x:Name="SearchPill" Grid.Column="1" Width="380" Height="42" CornerRadius="21" Margin="40,0,0,0" VerticalAlignment="Center"
                  Background="#4D0E1418" BorderBrush="#33FFFFFF" BorderThickness="1" Cursor="IBeam">
            <Grid Margin="16,0,10,0">
              <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <TextBlock Text="&#xE721;" FontFamily="Segoe MDL2 Assets" FontSize="14" Foreground="#A9B6BE" VerticalAlignment="Center"/>
              <TextBlock x:Name="SearchHint" Grid.Column="1" Text="Search games" FontSize="14" Foreground="#7F8F99" Margin="12,0,0,1"
                         VerticalAlignment="Center" IsHitTestVisible="False"/>
              <TextBox x:Name="SearchBox" Grid.Column="1" Style="{StaticResource BareBox}" Margin="10,0,0,0"/>
              <Button x:Name="BtnClearSearch" Grid.Column="2" Style="{StaticResource Chrome}" Content="&#xE894;" Width="28" Height="28"
                      Visibility="Collapsed" ToolTip="Clear search"/>
            </Grid>
          </Border>


          <StackPanel Grid.Column="4" Orientation="Horizontal" VerticalAlignment="Center">
            <TextBlock x:Name="LastCheck" Text="" FontSize="11" Foreground="#7F8F99" VerticalAlignment="Center" Margin="0,0,12,0"/>
            <Button x:Name="BtnCheck" Style="{StaticResource Glass}" Width="42" Padding="0" ToolTip="Check for updates">
              <TextBlock x:Name="CheckIcon" Text="&#xE72C;" FontFamily="Segoe MDL2 Assets" FontSize="14" RenderTransformOrigin="0.5,0.5">
                <TextBlock.RenderTransform><RotateTransform/></TextBlock.RenderTransform>
              </TextBlock>
            </Button>
            <Button x:Name="BtnAll" Style="{StaticResource Btn}" Background="#D9B45A" Foreground="#0A0E11" Height="42" Margin="8,0,0,0" Padding="16,0">
              <StackPanel Orientation="Horizontal">
                <TextBlock Text="&#xE896;" FontFamily="Segoe MDL2 Assets" FontSize="13" VerticalAlignment="Center" Margin="0,1,9,0"/>
                <TextBlock x:Name="BtnAllText" Text="Update all" VerticalAlignment="Center"/>
              </StackPanel>
            </Button>
            <Button x:Name="BtnSettings" Style="{StaticResource Glass}" Width="42" Padding="0" Margin="8,0,0,0" ToolTip="Settings">
              <TextBlock x:Name="GearIcon" Text="&#xE713;" FontFamily="Segoe MDL2 Assets" FontSize="15" RenderTransformOrigin="0.5,0.5">
                <TextBlock.RenderTransform><RotateTransform/></TextBlock.RenderTransform>
              </TextBlock>
            </Button>
            <Button x:Name="BtnFull" Style="{StaticResource Glass}" Width="42" Padding="0" Margin="8,0,0,0" ToolTip="Full screen (F11)">
              <TextBlock x:Name="FullIcon" Text="&#xE740;" FontFamily="Segoe MDL2 Assets" FontSize="14"/>
            </Button>
            <Button x:Name="BtnMin" Style="{StaticResource Chrome}" Content="&#xE921;" Margin="10,0,0,0"/>
            <Button x:Name="BtnClose" Style="{StaticResource Chrome}" Content="&#xE8BB;"/>
          </StackPanel>
        </Grid>

        <!-- the selected game -->
        <StackPanel x:Name="HeroContent" Grid.Row="1" VerticalAlignment="Bottom" HorizontalAlignment="Left" Margin="68,0,60,6" MaxWidth="960">
          <StackPanel.RenderTransform><TranslateTransform/></StackPanel.RenderTransform>
          <StackPanel Orientation="Horizontal">
            <Image x:Name="HeroIcon" Width="22" Height="22" RenderOptions.BitmapScalingMode="HighQuality" VerticalAlignment="Center"/>
            <TextBlock x:Name="HeroSource" Text="" FontSize="11.5" FontWeight="Bold" Foreground="#D9B45A" Margin="10,0,0,0" VerticalAlignment="Center">
              <TextBlock.Effect><DropShadowEffect BlurRadius="10" ShadowDepth="0" Opacity="0.9" Color="Black"/></TextBlock.Effect>
            </TextBlock>
          </StackPanel>
          <Border Visibility="Collapsed"><Border.Effect><DropShadowEffect x:Name="HeroIconGlow"/></Border.Effect></Border>
          <TextBlock x:Name="HeroTitle" Text="" FontSize="56" Margin="-3,4,0,0" TextWrapping="Wrap"
                     FontFamily="Segoe UI Variable Display Semibold, Segoe UI Semibold">
            <TextBlock.Effect><DropShadowEffect BlurRadius="30" ShadowDepth="0" Opacity="0.55" Color="Black"/></TextBlock.Effect>
          </TextBlock>
          <StackPanel Orientation="Horizontal" Margin="0,14,0,0">
            <Border CornerRadius="14" Background="#40000000" BorderBrush="#26FFFFFF" BorderThickness="1" Padding="12,5" Margin="0,0,8,0">
              <StackPanel Orientation="Horizontal">
                <TextBlock Text="INSTALLED" FontSize="9.5" FontWeight="Bold" Foreground="#8A9AA5" VerticalAlignment="Center"/>
                <TextBlock x:Name="HeroInst" Text="..." FontSize="13" Margin="8,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </Border>
            <Border CornerRadius="14" Background="#40000000" BorderBrush="#26FFFFFF" BorderThickness="1" Padding="12,5" Margin="0,0,8,0">
              <StackPanel Orientation="Horizontal">
                <TextBlock Text="LATEST" FontSize="9.5" FontWeight="Bold" Foreground="#8A9AA5" VerticalAlignment="Center"/>
                <TextBlock x:Name="HeroLatest" Text="..." FontSize="13" Margin="8,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </Border>
            <Border CornerRadius="14" Background="#40000000" BorderBrush="#26FFFFFF" BorderThickness="1" Padding="12,5">
              <StackPanel Orientation="Horizontal">
                <Ellipse x:Name="StatusDot" Width="8" Height="8" Fill="#8A9AA5" VerticalAlignment="Center"/>
                <TextBlock x:Name="HeroStatus" Text="Checking..." FontSize="13" Margin="8,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </Border>
            <Border x:Name="PlayedChip" CornerRadius="14" Background="#40000000" BorderBrush="#26FFFFFF" BorderThickness="1" Padding="12,5" Margin="8,0,0,0" Visibility="Collapsed">
              <StackPanel Orientation="Horizontal">
                <TextBlock Text="PLAYED" FontSize="9.5" FontWeight="Bold" Foreground="#8A9AA5" VerticalAlignment="Center"/>
                <TextBlock x:Name="HeroPlayed" Text="" FontSize="13" Margin="8,0,0,0" VerticalAlignment="Center"/>
              </StackPanel>
            </Border>
          </StackPanel>
          <TextBlock x:Name="HeroNote" Text="" FontSize="12.5" Foreground="#B5C0C7" TextWrapping="Wrap" MaxWidth="760"
                     HorizontalAlignment="Left" Margin="2,12,0,0" Visibility="Collapsed"/>
          <StackPanel Orientation="Horizontal" Margin="0,20,0,0">
            <Button x:Name="BtnPlay" Style="{StaticResource Btn}" Background="#F2EFE6" Foreground="#0A0E11"
                    Height="58" Padding="44,0" FontSize="17">
              <Button.Effect><DropShadowEffect x:Name="PlayGlow" BlurRadius="30" ShadowDepth="0" Opacity="0.5" Color="#FFFFFF"/></Button.Effect>
              <StackPanel Orientation="Horizontal">
                <TextBlock x:Name="PlayIcon" Text="&#xE768;" FontFamily="Segoe MDL2 Assets" FontSize="16" VerticalAlignment="Center" Margin="0,1,12,0"/>
                <TextBlock x:Name="PlayText" Text="PLAY" VerticalAlignment="Center"/>
              </StackPanel>
            </Button>
            <Button x:Name="BtnUpdate" Style="{StaticResource Glass}" Height="58" Padding="22,0" Margin="12,0,0,0" FontSize="14">
              <StackPanel Orientation="Horizontal">
                <TextBlock Text="&#xE896;" FontFamily="Segoe MDL2 Assets" FontSize="14" VerticalAlignment="Center" Margin="0,1,10,0"/>
                <TextBlock x:Name="BtnUpdateText" Text="Update" VerticalAlignment="Center"/>
              </StackPanel>
            </Button>
            <Button x:Name="BtnNotes" Style="{StaticResource Glass}" Height="58" Padding="22,0" Margin="10,0,0,0" FontSize="14">
              <StackPanel Orientation="Horizontal">
                <TextBlock Text="&#xE8A5;" FontFamily="Segoe MDL2 Assets" FontSize="14" VerticalAlignment="Center" Margin="0,1,10,0"/>
                <TextBlock Text="What's new" VerticalAlignment="Center"/>
              </StackPanel>
            </Button>
            <Button x:Name="BtnFolder" Style="{StaticResource Glass}" Height="58" Width="58" Padding="0" Margin="10,0,0,0" ToolTip="Open game folder">
              <TextBlock Text="&#xED25;" FontFamily="Segoe MDL2 Assets" FontSize="17"/>
            </Button>
            <Button x:Name="BtnBackups" Style="{StaticResource Glass}" Height="58" Width="58" Padding="0" Margin="10,0,0,0" ToolTip="Open save backups">
              <TextBlock Text="&#xE81C;" FontFamily="Segoe MDL2 Assets" FontSize="17"/>
            </Button>
            <Button x:Name="BtnUninstall" Style="{StaticResource Glass}" Height="58" Width="58" Padding="0" Margin="10,0,0,0" ToolTip="Uninstall">
              <TextBlock Text="&#xE74D;" FontFamily="Segoe MDL2 Assets" FontSize="17"/>
            </Button>
          </StackPanel>
        </StackPanel>

        <!-- the row of game cards -->
        <Grid Grid.Row="2">
          <StackPanel Orientation="Horizontal" Margin="70,8,0,0" VerticalAlignment="Top" Height="32">
            <TextBlock Text="LIBRARY" FontSize="10.5" FontWeight="Bold" Foreground="#8A9AA5" VerticalAlignment="Center"/>
            <TextBlock x:Name="RowCount" Text="" FontSize="10.5" FontWeight="Bold" Foreground="#5F717C" Margin="10,0,0,0" VerticalAlignment="Center"/>
            <StackPanel x:Name="Chips" Orientation="Horizontal" Margin="26,0,0,0" VerticalAlignment="Center"/>
          </StackPanel>
          <!-- grid view: the selected game's name and state, above the cards -->
          <StackPanel x:Name="GridInfo" Orientation="Horizontal" Margin="70,46,0,0" VerticalAlignment="Top" Height="34" Visibility="Collapsed">
            <TextBlock x:Name="GridName" FontSize="22" FontFamily="Segoe UI Variable Display Semibold, Segoe UI Semibold" VerticalAlignment="Center"/>
            <Ellipse x:Name="GridDot" Width="8" Height="8" Margin="16,2,0,0" VerticalAlignment="Center" Fill="#8A9AA5"/>
            <TextBlock x:Name="GridState" FontSize="13" Foreground="#A9B6BE" Margin="8,2,0,0" VerticalAlignment="Center"/>
          </StackPanel>
          <ScrollViewer x:Name="TileScroll" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Disabled"
                        Focusable="False" Margin="0,44,0,0">
            <StackPanel x:Name="Tiles" Orientation="Horizontal" Margin="56,24,56,0" VerticalAlignment="Top"/>
          </ScrollViewer>
          <ScrollViewer x:Name="GridScroll" HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto"
                        Focusable="False" Margin="0,86,0,0" Visibility="Collapsed">
            <WrapPanel x:Name="GridTiles" Margin="50,14,40,30"/>
          </ScrollViewer>
          <TextBlock x:Name="NoMatch" Text="No games match your search." FontSize="15" Foreground="#8A9AA5"
                     Margin="70,96,0,0" VerticalAlignment="Top" Visibility="Collapsed"/>
        </Grid>

        <!-- progress and hints -->
        <Grid Grid.Row="3" Margin="70,4,40,0">
          <Grid.ColumnDefinitions><ColumnDefinition Width="560"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
          <Grid VerticalAlignment="Top">
            <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
            <TextBlock x:Name="StepText" Text="Checking for updates..." FontSize="13"
                       FontFamily="Segoe UI Variable Display Semibold, Segoe UI Semibold"/>
            <TextBlock x:Name="PctText" Text="" FontSize="13" Foreground="#D9B45A" HorizontalAlignment="Right"
                       FontFamily="Segoe UI Variable Display Semibold, Segoe UI Semibold"/>
            <Grid x:Name="Track" Grid.Row="1" Height="5" Margin="0,8,0,0" ClipToBounds="True">
              <Border CornerRadius="3" Background="#1A242B"/>
              <Border x:Name="Fill" CornerRadius="3" HorizontalAlignment="Left" Width="0">
                <Border.Background>
                  <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                    <GradientStop Color="#3FB8A0" Offset="0"/><GradientStop Color="#D9B45A" Offset="1"/>
                  </LinearGradientBrush>
                </Border.Background>
              </Border>
              <Rectangle x:Name="Shimmer" Width="220" HorizontalAlignment="Left" Opacity="0" RadiusX="3" RadiusY="3">
                <Rectangle.Fill>
                  <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                    <GradientStop Color="#003FB8A0" Offset="0"/><GradientStop Color="#FF3FB8A0" Offset="0.4"/>
                    <GradientStop Color="#FFD9B45A" Offset="0.6"/><GradientStop Color="#00D9B45A" Offset="1"/>
                  </LinearGradientBrush>
                </Rectangle.Fill>
                <Rectangle.RenderTransform><TranslateTransform X="-220"/></Rectangle.RenderTransform>
              </Rectangle>
            </Grid>
            <TextBlock x:Name="DetailText" Grid.Row="2" Text="" FontSize="11.5" Foreground="#6E808B" Margin="0,6,0,0"/>
          </Grid>
          <StackPanel x:Name="PadHints" Grid.Column="1" Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,2,0,0"/>
        </Grid>

        <!-- controller focus ring, moved over the focused button -->
        <Border x:Name="FocusRing" Grid.RowSpan="4" HorizontalAlignment="Left" VerticalAlignment="Top" IsHitTestVisible="False"
                CornerRadius="16" BorderThickness="3.5" BorderBrush="#FFFFFF" Opacity="0" Width="60" Height="60">
          <Border.RenderTransform><TranslateTransform/></Border.RenderTransform>
          <Border.Effect><DropShadowEffect BlurRadius="22" ShadowDepth="0" Color="White" Opacity="0.6"/></Border.Effect>
        </Border>
      </Grid>

      <!-- ===== What's new / Activity panel ===== -->
      <Grid x:Name="NotesLayer" Visibility="Collapsed" Opacity="0">
        <Rectangle x:Name="NotesDim" Fill="#99050709"/>
        <Border x:Name="NotesPanel" HorizontalAlignment="Right" Width="620" Background="#F20E1418" BorderBrush="#26333C" BorderThickness="1,0,0,0">
          <Border.RenderTransform><TranslateTransform/></Border.RenderTransform>
          <Grid Margin="30,26,24,24">
            <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
            <Grid>
              <StackPanel>
                <TextBlock x:Name="NotesGame" Text="" FontSize="10.5" FontWeight="Bold" Foreground="#3FB8A0"/>
                <TextBlock Text="Release notes" FontSize="24" Margin="0,2,0,0" FontFamily="Segoe UI Variable Display Semibold, Segoe UI Semibold"/>
              </StackPanel>
              <Button x:Name="BtnNotesClose" Style="{StaticResource Chrome}" Content="&#xE8BB;" HorizontalAlignment="Right" VerticalAlignment="Top"/>
            </Grid>
            <Grid Grid.Row="1" Margin="0,16,0,8" Height="34">
              <StackPanel Orientation="Horizontal">
                <Grid x:Name="TabNotes" Cursor="Hand" Background="Transparent" Margin="0,0,24,0">
                  <TextBlock x:Name="TabNotesText" Text="WHAT'S NEW" FontSize="10.5" FontWeight="Bold" VerticalAlignment="Center"/>
                  <Border x:Name="TabNotesLine" Height="2" CornerRadius="1" VerticalAlignment="Bottom" Background="#D9B45A"/>
                </Grid>
                <Grid x:Name="TabActivity" Cursor="Hand" Background="Transparent">
                  <TextBlock x:Name="TabActivityText" Text="ACTIVITY" FontSize="10.5" FontWeight="Bold" Foreground="#5F717C" VerticalAlignment="Center"/>
                  <Border x:Name="TabActivityLine" Height="2" CornerRadius="1" VerticalAlignment="Bottom" Background="#D9B45A" Opacity="0"/>
                </Grid>
              </StackPanel>
              <TextBlock x:Name="NotesDate" Text="" FontSize="11" Foreground="#5F717C" HorizontalAlignment="Right" VerticalAlignment="Center"/>
            </Grid>
            <FlowDocumentScrollViewer x:Name="Viewer" Grid.Row="2" Margin="-12,0,0,0" VerticalScrollBarVisibility="Auto"
                                      HorizontalScrollBarVisibility="Disabled" Focusable="False" IsSelectionEnabled="False"/>
          </Grid>
        </Border>
      </Grid>

      <!-- ===== Choose your game (before a first install) ===== -->
      <Grid x:Name="RomLayer" Visibility="Collapsed" Opacity="0">
        <Rectangle x:Name="RomDim" Fill="#C0050709"/>
        <Border x:Name="RomCard" Width="700" VerticalAlignment="Center" HorizontalAlignment="Center"
                CornerRadius="18" Background="#11181D" BorderBrush="#26333C" BorderThickness="1" Padding="34,30,34,28"
                RenderTransformOrigin="0.5,0.5">
          <Border.RenderTransform><TransformGroup><ScaleTransform/><TranslateTransform/></TransformGroup></Border.RenderTransform>
          <Border.Effect><DropShadowEffect BlurRadius="60" ShadowDepth="0" Opacity="0.7" Color="Black"/></Border.Effect>
          <ScrollViewer x:Name="RomScroll" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Focusable="False" Margin="0,0,-14,0">
          <StackPanel Margin="0,0,14,0">
            <Grid>
              <StackPanel Orientation="Horizontal">
                <Image x:Name="RomIcon" Width="52" Height="52" RenderOptions.BitmapScalingMode="HighQuality"/>
                <StackPanel Margin="16,0,0,0" VerticalAlignment="Center">
                  <TextBlock x:Name="RomGame" Text="" FontSize="10.5" FontWeight="Bold" Foreground="#3FB8A0"/>
                  <TextBlock x:Name="RomTitle" Text="Choose your game" FontSize="24" Margin="0,2,0,0"
                             FontFamily="Segoe UI Variable Display Semibold, Segoe UI Semibold"/>
                </StackPanel>
              </StackPanel>
              <Button x:Name="BtnRomClose" Style="{StaticResource Chrome}" Content="&#xE8BB;" HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,-8,-14,0"/>
            </Grid>
            <TextBlock x:Name="RomNeeds" Text="" TextWrapping="Wrap" FontSize="13" Foreground="#C9CCC8" Margin="0,18,0,0" LineHeight="19"/>

            <TextBlock Text="ACCEPTED" FontSize="10" FontWeight="Bold" Foreground="#8A9AA5" Margin="0,18,0,8"/>
            <WrapPanel x:Name="RomFormats"/>

            <TextBlock x:Name="RomPickLabel" Text="YOUR GAME" FontSize="10" FontWeight="Bold" Foreground="#8A9AA5" Margin="0,18,0,8"/>
            <Grid x:Name="RomPickRow">
              <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <TextBox x:Name="RomPath" Style="{StaticResource PathBox}"/>
              <Button x:Name="BtnRomFile" Grid.Column="1" Style="{StaticResource Btn}" Height="40" Margin="8,0,0,0" Padding="16,0" FontSize="13" Content="Choose file"/>
              <Button x:Name="BtnRomFolder" Grid.Column="2" Style="{StaticResource Btn}" Height="40" Margin="8,0,0,0" Padding="16,0" FontSize="13" Content="Choose folder"/>
            </Grid>
            <TextBlock x:Name="RomCheck" Text="" FontSize="12" Margin="2,8,0,0" TextWrapping="Wrap"/>

            <!-- Switch games: turn an NSP into the exefs + romfs folders (nsp-worker.ps1) -->
            <Button x:Name="BtnNspToggle" Style="{StaticResource Btn}" Background="Transparent" Foreground="#7FD6C9" Height="32"
                    Padding="2,0" Margin="0,8,0,0" HorizontalAlignment="Left" FontSize="12.5" Visibility="Collapsed">
              <StackPanel Orientation="Horizontal">
                <TextBlock x:Name="NspToggleIcon" Text="&#xE70D;" FontFamily="Segoe MDL2 Assets" FontSize="10" VerticalAlignment="Center" Margin="0,1,8,0"/>
                <TextBlock Text="Have an NSP instead? Convert it to exefs + romfs" VerticalAlignment="Center"/>
              </StackPanel>
            </Button>
            <Border x:Name="NspPanel" Visibility="Collapsed" CornerRadius="12" Background="#0B1114" BorderBrush="#22303A" BorderThickness="1"
                    Padding="16,12,16,14" Margin="0,6,0,0">
              <StackPanel>
                <TextBlock x:Name="NspHint" Text="" FontSize="12" Foreground="#A9B6BE" TextWrapping="Wrap" Margin="0,0,0,8"/>
                <TextBlock Text="BASE GAME (.NSP)" FontSize="9.5" FontWeight="Bold" Foreground="#6E808B" Margin="0,4,0,4"/>
                <Grid>
                  <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <TextBox x:Name="NspBase" Style="{StaticResource PathBox}" Height="34" FontSize="12"/>
                  <Button x:Name="BtnNspBase" Grid.Column="1" Style="{StaticResource Btn}" Height="34" Margin="8,0,0,0" Padding="14,0" FontSize="12.5" Content="Choose"/>
                </Grid>
                <StackPanel x:Name="NspUpdateRow">
                  <TextBlock x:Name="NspUpdateLabel" Text="UPDATE (.NSP)" FontSize="9.5" FontWeight="Bold" Foreground="#6E808B" Margin="0,8,0,4"/>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                    <TextBox x:Name="NspUpdate" Style="{StaticResource PathBox}" Height="34" FontSize="12"/>
                    <Button x:Name="BtnNspUpdate" Grid.Column="1" Style="{StaticResource Btn}" Height="34" Margin="8,0,0,0" Padding="14,0" FontSize="12.5" Content="Choose"/>
                  </Grid>
                </StackPanel>
                <TextBlock Text="PROD.KEYS (FROM YOUR SWITCH)" FontSize="9.5" FontWeight="Bold" Foreground="#6E808B" Margin="0,8,0,4"/>
                <Grid>
                  <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <TextBox x:Name="NspKeys" Style="{StaticResource PathBox}" Height="34" FontSize="12"/>
                  <Button x:Name="BtnNspKeys" Grid.Column="1" Style="{StaticResource Btn}" Height="34" Margin="8,0,0,0" Padding="14,0" FontSize="12.5" Content="Choose"/>
                </Grid>
                <TextBlock Text="SAVE THE EXEFS + ROMFS IN" FontSize="9.5" FontWeight="Bold" Foreground="#6E808B" Margin="0,8,0,4"/>
                <Grid>
                  <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <TextBox x:Name="NspOut" Style="{StaticResource PathBox}" Height="34" FontSize="12"/>
                  <Button x:Name="BtnNspOut" Grid.Column="1" Style="{StaticResource Btn}" Height="34" Margin="8,0,0,0" Padding="14,0" FontSize="12.5" Content="Choose"/>
                </Grid>
                <Grid Margin="0,12,0,0">
                  <TextBlock x:Name="NspCheck" Text="" FontSize="12" TextWrapping="Wrap" VerticalAlignment="Center" Margin="2,0,140,0"/>
                  <Button x:Name="BtnNspConvert" Style="{StaticResource Btn}" Background="#7FD6C9" Foreground="#0A0E11" Height="38"
                          Padding="20,0" FontSize="13" HorizontalAlignment="Right" Content="Convert"/>
                </Grid>
              </StackPanel>
            </Border>

            <TextBlock x:Name="RomWhere" Text="" FontSize="11.5" Foreground="#6E808B" Margin="2,14,0,0" TextWrapping="Wrap"/>

            <Grid Margin="0,22,0,0">
              <Button x:Name="BtnRomSkip" Style="{StaticResource Btn}" Background="Transparent" Foreground="#8A9AA5"
                      Height="44" Padding="4,0" FontSize="13" HorizontalAlignment="Left" Content="Skip: I'll choose it in the game's setup"/>
              <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button x:Name="BtnRomCancel" Style="{StaticResource Btn}" Background="Transparent" BorderBrush="#26333C"
                        BorderThickness="1" Height="44" Content="Cancel"/>
                <Button x:Name="BtnRomInstall" Style="{StaticResource Btn}" Background="#3FB8A0" Foreground="#0A0E11"
                        Height="44" Margin="10,0,0,0" Padding="28,0">
                  <StackPanel Orientation="Horizontal">
                    <TextBlock Text="&#xE896;" FontFamily="Segoe MDL2 Assets" FontSize="13" VerticalAlignment="Center" Margin="0,1,10,0"/>
                    <TextBlock Text="Install" VerticalAlignment="Center"/>
                  </StackPanel>
                </Button>
              </StackPanel>
            </Grid>
          </StackPanel>
          </ScrollViewer>
        </Border>
      </Grid>

      <!-- ===== Uninstall ===== -->
      <Grid x:Name="UninstLayer" Visibility="Collapsed" Opacity="0">
        <Rectangle x:Name="UninstDim" Fill="#C0050709"/>
        <Border x:Name="UninstCard" Width="620" VerticalAlignment="Center" HorizontalAlignment="Center"
                CornerRadius="18" Background="#11181D" BorderBrush="#3A2A2A" BorderThickness="1" Padding="34,30,34,28"
                RenderTransformOrigin="0.5,0.5">
          <Border.RenderTransform><TransformGroup><ScaleTransform/><TranslateTransform/></TransformGroup></Border.RenderTransform>
          <Border.Effect><DropShadowEffect BlurRadius="60" ShadowDepth="0" Opacity="0.7" Color="Black"/></Border.Effect>
          <StackPanel>
            <StackPanel Orientation="Horizontal">
              <Image x:Name="UninstIcon" Width="52" Height="52" RenderOptions.BitmapScalingMode="HighQuality"/>
              <StackPanel Margin="16,0,0,0" VerticalAlignment="Center">
                <TextBlock Text="UNINSTALL" FontSize="10.5" FontWeight="Bold" Foreground="#E06C5B"/>
                <TextBlock x:Name="UninstTitle" Text="" FontSize="22" Margin="0,2,0,0" TextWrapping="Wrap"
                           FontFamily="Segoe UI Variable Display Semibold, Segoe UI Semibold"/>
              </StackPanel>
            </StackPanel>
            <TextBlock Text="DELETES THIS FOLDER" FontSize="10" FontWeight="Bold" Foreground="#8A9AA5" Margin="0,22,0,6"/>
            <TextBlock x:Name="UninstPath" Text="" FontSize="13.5" TextWrapping="Wrap"/>
            <TextBlock x:Name="UninstSize" Text="" FontSize="12" Foreground="#8A9AA5" Margin="0,4,0,0"/>
            <TextBlock x:Name="UninstWarn" Text="" FontSize="12" Foreground="#F2D27A" TextWrapping="Wrap" Margin="0,10,0,0" Visibility="Collapsed"/>
            <StackPanel x:Name="UninstToggleHost" Margin="0,18,0,0"/>
            <TextBlock x:Name="UninstNote" Text="" FontSize="11.5" Foreground="#6E808B" TextWrapping="Wrap" Margin="0,10,0,0"/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,24,0,0">
              <Button x:Name="BtnUninstCancel" Style="{StaticResource Btn}" Background="Transparent" BorderBrush="#26333C"
                      BorderThickness="1" Height="44" Content="Cancel"/>
              <Button x:Name="BtnUninstGo" Style="{StaticResource Btn}" Background="#E06C5B" Foreground="#0A0E11"
                      Height="44" Margin="10,0,0,0" Padding="24,0">
                <StackPanel Orientation="Horizontal">
                  <TextBlock Text="&#xE74D;" FontFamily="Segoe MDL2 Assets" FontSize="13" VerticalAlignment="Center" Margin="0,1,10,0"/>
                  <TextBlock Text="Uninstall" VerticalAlignment="Center"/>
                </StackPanel>
              </Button>
            </StackPanel>
          </StackPanel>
        </Border>
      </Grid>

      <!-- ===== Settings overlay ===== -->
      <Grid x:Name="SettingsLayer" Visibility="Collapsed" Opacity="0">
        <Rectangle x:Name="SettingsDim" Fill="#C0050709"/>
        <Border x:Name="SettingsCard" Width="660" VerticalAlignment="Center" HorizontalAlignment="Center"
                CornerRadius="18" Background="#11181D" BorderBrush="#26333C" BorderThickness="1" Padding="34,30,34,28"
                RenderTransformOrigin="0.5,0.5">
          <Border.RenderTransform><TransformGroup><ScaleTransform/><TranslateTransform/></TransformGroup></Border.RenderTransform>
          <Border.Effect><DropShadowEffect BlurRadius="60" ShadowDepth="0" Opacity="0.7" Color="Black"/></Border.Effect>
          <StackPanel>
            <Grid>
              <StackPanel>
                <TextBlock Text="SETTINGS" FontSize="10.5" FontWeight="Bold" Foreground="#3FB8A0"/>
                <TextBlock Text="Options and folders" FontSize="24" Margin="0,2,0,0"
                           FontFamily="Segoe UI Variable Display Semibold, Segoe UI Semibold"/>
              </StackPanel>
              <Button x:Name="BtnSetClose" Style="{StaticResource Chrome}" Content="&#xE8BB;" HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,-8,-14,0"/>
            </Grid>
            <TextBlock Text="How the launcher behaves, an optional GitHub token, and where each game is (or will be) installed."
                       TextWrapping="Wrap" FontSize="12.5" Foreground="#8A9AA5" Margin="0,8,0,10"/>
            <ScrollViewer MaxHeight="400" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Focusable="False">
              <StackPanel x:Name="SetRows" Margin="0,0,10,0"/>
            </ScrollViewer>
            <Grid Margin="0,20,0,0">
              <Button x:Name="BtnSetDefaults" Style="{StaticResource Btn}" Background="Transparent" Foreground="#8A9AA5"
                      Height="44" Padding="4,0" FontSize="13" HorizontalAlignment="Left" Content="Reset to defaults"/>
              <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button x:Name="BtnSetCancel" Style="{StaticResource Btn}" Background="Transparent" BorderBrush="#26333C"
                        BorderThickness="1" Height="44" Content="Cancel"/>
                <Button x:Name="BtnSetSave" Style="{StaticResource Btn}" Background="#3FB8A0" Foreground="#0A0E11"
                        Height="44" Margin="10,0,0,0" Padding="28,0" Content="Save"/>
              </StackPanel>
            </Grid>
          </StackPanel>
        </Border>
      </Grid>
    </Grid>
  </Border>
</Window>
'@
$W = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader $xaml))
$ui = @{}
$xaml.SelectNodes('//*[@*[local-name()="Name"]]') | ForEach-Object {
    $n = $_.Attributes | Where-Object { $_.LocalName -eq 'Name' } | Select-Object -First 1
    $ui[$n.Value] = $W.FindName($n.Value)
}

# ---------- Helpers ----------
function Col($hex) { [Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Brush($hex) { $b = New-Object Windows.Media.SolidColorBrush (Col $hex); $b }
function WithAlpha($hex, [byte]$a) { $c = Col $hex; [Windows.Media.Color]::FromArgb($a, $c.R, $c.G, $c.B) }
function Ease($mode = 'EaseOut') { $e = New-Object Windows.Media.Animation.CubicEase; $e.EasingMode = $mode; $e }
function Dur($ms) { [Windows.Duration][TimeSpan]::FromMilliseconds($ms) }
function Anim($to, $ms, $from = $null, $ease = 'EaseOut') {
    $a = New-Object Windows.Media.Animation.DoubleAnimation
    if ($null -ne $from) { $a.From = $from }
    $a.To = $to; $a.Duration = Dur $ms
    if ($ease) { $a.EasingFunction = Ease $ease }
    $a
}
function ColorAnim($to, $ms) {
    $a = New-Object Windows.Media.Animation.ColorAnimation
    $a.To = $to; $a.Duration = Dur $ms; $a.EasingFunction = Ease 'EaseInOut'
    $a
}
function Forever($a) { $a.RepeatBehavior = [Windows.Media.Animation.RepeatBehavior]::Forever; $a }
# Slow never-ending animations (drifting light, pulsing dots) at 30 frames a second: they look the same, and without
# this they redraw the window at the screen's full rate all the time (most of the launcher's CPU use)
function Calm($a) { [Windows.Media.Animation.Timeline]::SetDesiredFrameRate($a, 30); $a }
function Get-BigIcon($path, $size) {
    if (-not (Test-Path $path)) { return $null }
    $h = New-Object IntPtr[] 1; $id = New-Object int[] 1
    $n = [IconX]::PrivateExtractIcons($path, 0, $size, $size, $h, $id, 1, 0)
    if ($n -lt 1 -or $h[0] -eq [IntPtr]::Zero) { return $null }
    try {
        $src = [Windows.Interop.Imaging]::CreateBitmapSourceFromHIcon($h[0], [Windows.Int32Rect]::Empty,
            [Windows.Media.Imaging.BitmapSizeOptions]::FromEmptyOptions())
        $src.Freeze(); $src
    } finally { [void][IconX]::DestroyIcon($h[0]) }
}

$Muted = '#8A9AA5'; $Gold = '#D9B45A'; $Teal = '#3FB8A0'; $Red = '#E06C5B'; $TextC = '#E8E6DF'

# The launcher's own logo (drawn by src\build.ps1) for the sidebar, the title bar and the taskbar
$logoPath = Join-Path $Root 'logo.png'
if (Test-Path $logoPath) {
    $logo = New-Object Windows.Media.Imaging.BitmapImage
    $logo.BeginInit(); $logo.UriSource = New-Object Uri($logoPath); $logo.CacheOption = 'OnLoad'; $logo.EndInit(); $logo.Freeze()
    $ui.LogoImg.Source = $logo
    $W.Icon = $logo
}

# ---------- Game cards ----------
# One card per game in the row: its icon on a face in its colours, a status dot, and its name and state below. The
# selected card grows and lights up (Show-Game).
$tileXaml = @'
<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      Width="168" Margin="4,0" Cursor="Hand" Background="Transparent">
  <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
  <Grid x:Name="Box" Width="138" Height="138" RenderTransformOrigin="0.5,0.5">
    <Grid.RenderTransform><ScaleTransform/></Grid.RenderTransform>
    <Border x:Name="Glow" CornerRadius="32" Margin="4" Opacity="0" Visibility="Collapsed">
      <Border.Effect><DropShadowEffect x:Name="GlowFx" BlurRadius="46" ShadowDepth="0" Opacity="0.9"/></Border.Effect>
    </Border>
    <Border x:Name="Face" CornerRadius="30" ClipToBounds="True">
      <Image x:Name="Icon" RenderOptions.BitmapScalingMode="HighQuality"/>
    </Border>
    <Border x:Name="Shade" CornerRadius="30" Background="#090C0F" Opacity="0.35" IsHitTestVisible="False"/>
    <Border x:Name="Sel" CornerRadius="36" BorderThickness="3" BorderBrush="#F2EFE6" Margin="-8" Opacity="0"/>
    <Ellipse x:Name="Dot" Width="16" Height="16" HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,-2,-2,0"
             Stroke="#090C0F" StrokeThickness="3" Fill="#5F717C"/>
  </Grid>
  <TextBlock x:Name="Name" Grid.Row="1" FontSize="13.5" Margin="0,18,0,0" HorizontalAlignment="Center" TextAlignment="Center"
             TextTrimming="CharacterEllipsis" MaxWidth="164" Opacity="0.7"
             FontFamily="Segoe UI Variable Display Semibold, Segoe UI Semibold"/>
  <TextBlock x:Name="Sub" Grid.Row="2" FontSize="11.5" Foreground="#8A9AA5" Margin="0,2,0,0" HorizontalAlignment="Center"
             TextTrimming="CharacterEllipsis" MaxWidth="172"/>
</Grid>
'@
foreach ($g in $Games) {
    $t = [Windows.Markup.XamlReader]::Parse($tileXaml)
    $g.Tile = $t
    foreach ($n in 'Box', 'Glow', 'GlowFx', 'Face', 'Shade', 'Sel', 'Dot', 'Name', 'Sub', 'Icon') { $g["T$n"] = $t.FindName($n) }
    $g.TName.Text = $g.Name
    $g.TSub.Text = 'Checking...'
    $g.TGlowFx.Color = Col $g.Accent
    $g.TGlow.Background = Brush $g.Accent
    $g.TFace.Background = New-Object Windows.Media.LinearGradientBrush((Col $g.Accent), (Col $g.Accent2), 45)
    $t.Tag = $g
    $t.Add_MouseEnter({ param($s, $e) if ($s.Tag -ne $script:sel) { $s.Tag.TShade.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 0.1 150)) } })
    $t.Add_MouseLeave({ param($s, $e) if ($s.Tag -ne $script:sel) { $s.Tag.TShade.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 0.35 250)) } })
    $t.Add_MouseLeftButtonUp({ param($s, $e) if ($s.Tag -ne $script:sel) { Show-Game $s.Tag } })
    [void]$ui.Tiles.Children.Add($t)
}

# A game whose program has no icon (or isn't installed yet) gets a drawn one: a tile in its colours with a white
# symbol for the game. Symbols are drawn on a 256 x 256 grid, as filled shapes (Fill) and lines (Line, with a width).
function Fill($data) { @{ Geo = $(if ($data -is [string]) { [Windows.Media.Geometry]::Parse($data) } else { $data }); W = 0 } }
function Line($data, $w) { @{ Geo = $(if ($data -is [string]) { [Windows.Media.Geometry]::Parse($data) } else { $data }); W = $w } }
function Circle($x, $y, $r) { New-Object Windows.Media.EllipseGeometry((New-Object Windows.Point($x, $y)), $r, $r) }
function Cut($a, $b) { New-Object Windows.Media.CombinedGeometry([Windows.Media.GeometryCombineMode]::Exclude, $a, $b) }
# short sun rays around (cx, cy), from radius r1 to r2
function Rays($cx, $cy, $r1, $r2, $n) {
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $d = for ($i = 0; $i -lt $n; $i++) {
        $a = 2 * [Math]::PI * $i / $n
        [string]::Format($inv, 'M{0:0.#},{1:0.#} L{2:0.#},{3:0.#}', ($cx + $r1 * [Math]::Cos($a)), ($cy + $r1 * [Math]::Sin($a)),
            ($cx + $r2 * [Math]::Cos($a)), ($cy + $r2 * [Math]::Sin($a)))
    }
    [Windows.Media.Geometry]::Parse(($d -join ' '))
}
function Game-Symbol($id) {
    switch ($id) {
        'totk' {   # a swirl
            @((Line 'M128,58 A70,70 0 1 1 58,128' 18), (Line 'M128,96 A32,32 0 1 0 160,128' 14), (Fill (Circle 128 128 9)))
        }
        'smo' {    # a crescent moon and a star
            @((Fill (Cut (Circle 122 136 70) (Circle 152 110 60))),
              (Fill 'M184,52 L192,74 L215,74 L196,88 L203,110 L184,96 L165,110 L172,88 L153,74 L176,74 Z'))
        }
        'wwhd' {   # a sailboat on the waves
            @((Fill 'M130,44 L130,160 L74,160 Z'), (Fill 'M140,70 L140,160 L188,160 Z'),
              (Fill 'M52,170 L204,170 L184,196 L72,196 Z'),
              (Line 'M46,222 Q66,208 86,222 T126,222 T166,222 T206,222' 10))
        }
        'tod' {    # a wrench
            @((Line 'M152,104 L82,174' 36), (Fill (Cut (Circle 166 90 46) ([Windows.Media.Geometry]::Parse('M183.7,93.5 L162.5,72.3 L204.9,30.1 L226.1,51.3 Z')))))
        }
        'acit' {   # a cracked clock
            @((Line (Circle 128 132 70) 16), (Line 'M128,132 L128,90' 12), (Line 'M128,132 L160,152' 12),
              (Fill (Circle 128 132 10)), (Line 'M172,77 L158,98 L174,110 L162,128' 8))
        }
        'mp6' {    # the sun and the moon
            @((Fill (Circle 98 100 34)), (Line (Rays 98 100 46 62 8) 10),
              (Fill (Cut (Circle 166 162 50) (Circle 190 140 44))))
        }
        'mkwii' {  # a steering wheel
            @((Line (Circle 128 128 76) 20), (Fill (Circle 128 128 24)),
              (Line 'M52,128 L104,128 M152,128 L204,128 M128,152 L128,204' 18))
        }
        'sms' {    # the sun over the sea
            @((Fill (Circle 128 100 36)), (Line (Rays 128 100 48 64 10) 10),
              (Line 'M44,174 Q64,160 86,174 T128,174 T170,174 T212,174' 11),
              (Line 'M64,206 Q84,192 106,206 T148,206 T190,206' 11))
        }
        'botw' {   # a paraglider over the mountains
            @((Fill 'M30,212 L96,118 L132,166 L158,134 L226,212 Z'),
              (Line 'M112,62 Q156,30 200,62' 14), (Line 'M112,62 L156,100 L200,62' 4), (Fill (Circle 156 104 9)))
        }
        { $_ -in 'nsmbu', 'nsmbw' } {   # a goal flag on a hill
            @((Fill 'M28,214 Q128,150 228,214 Z'), (Line 'M150,184 L150,48' 12),
              (Fill 'M156,52 L214,74 L156,98 Z'), (Fill (Circle 150 44 11)))
        }
        { $_ -in 'pkla', 'pokopia', 'pkstadium', 'pkss', 'pkgen', 'pmd', 'pkblack', 'pksnap', 'pklgpe', 'pkswsh' } {   # a paw print
            $pad = [Windows.Media.Geometry]::Parse('M128,128 C164,128 196,166 190,190 C184,212 160,204 128,204 C96,204 72,212 66,190 C60,166 92,128 128,128 Z')
            $toes = foreach ($t in @(@(66, 112, 0), @(102, 76, 0), @(154, 76, 0), @(190, 112, 0))) {
                Fill (New-Object Windows.Media.EllipseGeometry((New-Object Windows.Point($t[0], $t[1])), 20, 26))
            }
            @((Fill $pad)) + @($toes)
        }
        'smg' {    # a star
            @((Fill 'M128,36 L152,100 L220,102 L166,144 L186,210 L128,172 L70,210 L90,144 L36,102 L104,100 Z'))
        }
        default { $null }
    }
}
function New-GameIcon($g) {
    $dv = New-Object Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $tile = New-Object Windows.Rect(8, 8, 240, 240)
    $grad = New-Object Windows.Media.LinearGradientBrush((Col $g.Accent), (Col $g.Accent2), 45)
    $dc.DrawRoundedRectangle($grad, $null, $tile, 56, 56)
    $shine = New-Object Windows.Media.LinearGradientBrush((WithAlpha '#FFFFFF' 0x48), (WithAlpha '#FFFFFF' 0), 90)
    $dc.DrawRoundedRectangle($shine, $null, $tile, 56, 56)
    $parts = Game-Symbol $g.Id
    if ($parts) {
        # a soft shadow under the symbol, then the symbol
        foreach ($pass in @(@{ C = (WithAlpha '#000000' 0x38); Dy = 6 }, @{ C = [Windows.Media.Colors]::White; Dy = 0 })) {
            $br = New-Object Windows.Media.SolidColorBrush($pass.C)
            $dc.PushTransform((New-Object Windows.Media.TranslateTransform(0, $pass.Dy)))
            foreach ($p in $parts) {
                if ($p.W) {
                    $pen = New-Object Windows.Media.Pen($br, $p.W)
                    $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'; $pen.LineJoin = 'Round'
                    $dc.DrawGeometry($null, $pen, $p.Geo)
                } else { $dc.DrawGeometry($br, $null, $p.Geo) }
            }
            $dc.Pop()
        }
    } else {
        $ft = New-Object Windows.Media.FormattedText($g.Short, [Globalization.CultureInfo]::InvariantCulture, 'LeftToRight',
            (New-Object Windows.Media.Typeface('Segoe UI Black')), $(if ($g.Short.Length -gt 3) { 58 } else { 92 }), (Brush '#FFFFFF'), 1.0)
        $dc.DrawText($ft, (New-Object Windows.Point((128 - $ft.Width / 2), (128 - $ft.Height / 2))))
    }
    $dc.Close()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap(256, 256, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv); $bmp.Freeze()
    $bmp
}
# A picture of the player's own for a game (icons\<id>.png or .jpg next to the launcher), as a card face: scaled to
# fill 256 x 256, with the card's rounded corners
function Get-CustomIcon($g) {
    $file = foreach ($ext in 'png', 'jpg', 'jpeg') { $p = Join-Path $Root "icons\$($g.Id).$ext"; if (Test-Path $p) { $p; break } }
    if (-not $file) { return $null }
    try {
        $img = New-Object Windows.Media.Imaging.BitmapImage
        $img.BeginInit(); $img.UriSource = New-Object Uri($file); $img.CacheOption = 'OnLoad'
        $img.DecodePixelWidth = 512; $img.EndInit(); $img.Freeze()
        $dv = New-Object Windows.Media.DrawingVisual
        $dc = $dv.RenderOpen()
        $rect = New-Object Windows.Rect(0, 0, 256, 256)
        $dc.PushClip((New-Object Windows.Media.RectangleGeometry($rect, 56, 56)))
        $dc.DrawImage($img, $rect)
        $dc.Pop()
        $dc.Close()
        $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap(256, 256, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
        $bmp.Render($dv); $bmp.Freeze()
        $bmp
    } catch { $null }
}
function Load-Icons {
    foreach ($g in $Games) {
        # a picture in icons\ first, then the program's own icon, then a drawn one
        $g.Icon = Get-CustomIcon $g
        $drawn = [bool]$g.Icon
        if (-not $g.Icon) { $g.Icon = Get-BigIcon (Resolve-Exe $g) 256; $drawn = -not $g.Icon }
        if (-not $g.Icon) { $g.Icon = New-GameIcon $g }
        $g.TIcon.Source = $g.Icon
        # a drawn icon is a whole card face; a program's own icon sits on the game's colours with some room
        $g.TIcon.Margin = New-Object Windows.Thickness($(if ($drawn) { 0 } else { 26 }))
    }
}
Load-Icons

# ---------- Notes and activity documents ----------
function New-Doc {
    $d = New-Object Windows.Documents.FlowDocument
    $d.FontFamily = New-Object Windows.Media.FontFamily('Segoe UI Variable Text, Segoe UI')
    $d.FontSize = 13; $d.Foreground = Brush '#C9CCC8'; $d.PagePadding = New-Object Windows.Thickness(12, 0, 12, 6)
    $d.TextAlignment = 'Left'
    $d
}
$docNotes = New-Doc
$docActivity = New-Doc

function Add-Inlines($para, $line, $color = $null) {
    $line = $line -replace '\[([^\]]+)\]\([^)]+\)', '$1' -replace '`', ''
    $parts = $line -split '\*\*'
    for ($i = 0; $i -lt $parts.Count; $i++) {
        if (-not $parts[$i]) { continue }
        $r = New-Object Windows.Documents.Run($parts[$i])
        if ($i % 2) { $r.FontWeight = 'SemiBold'; $r.Foreground = Brush $TextC }
        if ($color) { $r.Foreground = Brush $color }
        $para.Inlines.Add($r)
    }
}
$script:notesFor = $null   # the game whose notes are laid out now
function Fill-Notes($g) {
    $script:notesFor = $g
    $docNotes.Blocks.Clear()
    $ui.NotesDate.Text = ''
    if (-not $g.Latest) {
        $p = New-Object Windows.Documents.Paragraph
        Add-Inlines $p $(if ($g.State -eq 'checking') { 'Loading the release notes...' } else { "Couldn't load the release notes." }) $Muted
        $docNotes.Blocks.Add($p); return
    }
    try { $ui.NotesDate.Text = "$($g.Latest.Tag)  $Bullet  " + ([datetime]$g.Latest.Date).ToLocalTime().ToString('MMM d, yyyy') } catch { $ui.NotesDate.Text = $g.Latest.Tag }
    foreach ($raw in (([string]$g.Latest.Notes) -replace "`r", '') -split "`n") {
        $line = $raw.TrimEnd()
        if (-not $line.Trim() -or $line -match '^>\s*\[!') { continue }
        $p = New-Object Windows.Documents.Paragraph
        $p.Margin = New-Object Windows.Thickness(0, 0, 0, 6)
        if ($line -match '^#{1,6}\s+(.*)') {
            $p.Margin = New-Object Windows.Thickness(0, 10, 0, 6)
            $p.FontSize = 14.5; $p.FontWeight = 'SemiBold'
            Add-Inlines $p $Matches[1] $g.Accent
        } elseif ($line -match '^\s*[-*]\s+(.*)') {
            $p.Margin = New-Object Windows.Thickness(16, 0, 0, 6); $p.TextIndent = -14
            $dot = New-Object Windows.Documents.Run("$Bullet   "); $dot.Foreground = Brush $g.Accent
            $p.Inlines.Add($dot)
            Add-Inlines $p $Matches[1]
        } elseif ($line -match '^>\s?(.*)') {
            Add-Inlines $p $Matches[1] $Muted
        } else {
            Add-Inlines $p $line
        }
        $docNotes.Blocks.Add($p)
    }
}
function Log($text, $color = $null) {
    $p = New-Object Windows.Documents.Paragraph
    $p.Margin = New-Object Windows.Thickness(0, 0, 0, 4)
    $t = New-Object Windows.Documents.Run((Get-Date -Format 'HH:mm:ss') + '    ')
    $t.Foreground = Brush '#5F717C'; $t.FontSize = 11.5
    $p.Inlines.Add($t)
    $r = New-Object Windows.Documents.Run($text)
    if ($color) { $r.Foreground = Brush $color }
    $p.Inlines.Add($r)
    $docActivity.Blocks.Add($p)
    if ($script:tab -eq 'activity') { $p.BringIntoView() }
}
function Set-Tab($which) {
    $script:tab = $which
    $on = $which -eq 'notes'
    $ui.TabNotesText.Foreground = Brush $(if ($on) { $TextC } else { '#5F717C' })
    $ui.TabActivityText.Foreground = Brush $(if ($on) { '#5F717C' } else { $TextC })
    $ui.TabNotesLine.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim ([double][int]$on) 200))
    $ui.TabActivityLine.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim ([double][int](-not $on)) 200))
    $ui.NotesDate.Visibility = if ($on) { 'Visible' } else { 'Collapsed' }
    $ui.Viewer.Document = if ($on) { $docNotes } else { $docActivity }
    $ui.Viewer.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 250 0))
    if (-not $on -and $docActivity.Blocks.LastBlock) { $docActivity.Blocks.LastBlock.BringIntoView() }
}

# ---------- Progress bar ----------
function Set-Indeterminate($on) {
    $tt = $ui.Shimmer.RenderTransform
    if ($on) {
        $ui.Fill.BeginAnimation([Windows.FrameworkElement]::WidthProperty, (Anim 0 200))
        $ui.Shimmer.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 200))
        $a = Forever (Anim ($ui.Track.ActualWidth + 220) 1400 -220 'EaseInOut')
        $tt.BeginAnimation([Windows.Media.TranslateTransform]::XProperty, $a)
    } else {
        $ui.Shimmer.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 0 200))
        $tt.BeginAnimation([Windows.Media.TranslateTransform]::XProperty, $null)
    }
    $script:indet = $on
}
function Set-Progress($frac) {
    if ($script:indet) { Set-Indeterminate $false }
    $ui.Fill.BeginAnimation([Windows.FrameworkElement]::WidthProperty, (Anim ($ui.Track.ActualWidth * [Math]::Max(0, [Math]::Min(1, $frac))) 300))
}
function Set-Step($text, $color = $TextC, $detail = '', $pct = '') {
    $ui.StepText.Text = $text; $ui.StepText.Foreground = Brush $color
    $ui.DetailText.Text = $detail; $ui.PctText.Text = $pct
}

# ---------- State and views ----------
$script:sel = $null
$script:tab = 'notes'
$script:running = $false
$script:checking = $false
$script:current = $null
$script:queue = New-Object System.Collections.ArrayList
$script:results = @()
$script:pending = New-Object System.Collections.ArrayList
$script:indet = $false
$script:checkGen = 0
$script:filter = 'all'
$script:isFull = $false

function Status-Of($g) {
    switch ($g.State) {
        'checking' { @{ Text = 'Checking...'; Color = $Muted; Tile = 'Checking...' } }
        'missing'  { @{ Text = 'Not installed'; Color = $Muted; Tile = 'Not installed' } }
        'installing' { @{ Text = 'Installing...'; Color = $Gold; Tile = 'Installing...' } }
        'uninstalling' { @{ Text = 'Uninstalling...'; Color = $Gold; Tile = 'Uninstalling...' } }
        'converting' { @{ Text = 'Converting the NSP...'; Color = $Gold; Tile = 'Converting NSP...' } }
        'offline'  { @{ Text = "Couldn't check"; Color = $Red; Tile = "v$($g.Installed.Version)  $Bullet  offline" } }
        'problem'  { @{ Text = $g.Latest.Problem; Color = $Red; Tile = "v$($g.Installed.Version)  $Bullet  can't update" } }
        'update'   { @{ Text = 'Update available'; Color = $Gold; Tile = "Update  $Bullet  $($g.Latest.Tag)" } }
        'updating' { @{ Text = 'Updating...'; Color = $Gold; Tile = 'Updating...' } }
        default    { @{ Text = 'Up to date'; Color = $Teal; Tile = "v$($g.Installed.Version)" } }
    }
}

function Pulse($el, $on) {
    if ($on) {
        $a = Calm (Forever (Anim 0.25 900 1 'EaseInOut')); $a.AutoReverse = $true
        $el.BeginAnimation([Windows.UIElement]::OpacityProperty, $a)
    } else { $el.BeginAnimation([Windows.UIElement]::OpacityProperty, $null); $el.Opacity = 1 }
}

function Update-Views {
    foreach ($g in $Games) {
        $st = Status-Of $g
        $g.TSub.Text = $st.Tile
        $g.TSub.Foreground = Brush $(if ($g.State -in 'update', 'updating', 'installing', 'converting') { $Gold } else { $Muted })
        $g.TDot.Fill = Brush $st.Color
        $g.TDot.Visibility = if ($g.State -eq 'missing') { 'Collapsed' } else { 'Visible' }
        Pulse $g.TDot ($g.State -in 'update', 'updating', 'installing', 'converting')
    }
    Update-Hero
    $n = @(Listed-Games | Where-Object { $_.State -eq 'update' }).Count
    $ui.BtnAll.IsEnabled = ($n -gt 0) -and -not $script:running
    $ui.BtnAll.Visibility = if ($n -gt 0 -or $script:running) { 'Visible' } else { 'Collapsed' }
    $ui.BtnAllText.Text = if ($n -gt 1) { "Update all  ($n)" } else { 'Update all' }
    Update-Chips
    Apply-Sort
    Apply-Filter
}
# The selected game's page (all that changes when stepping from game to game)
function Update-Hero {
    $g = $script:sel
    if ($g) {
        $st = Status-Of $g
        $ui.HeroInst.Text = if ($g.Installed) { "v$($g.Installed.Version)" } else { '-' }
        $ui.HeroLatest.Text = if ($g.Latest) { $g.Latest.Tag } elseif ($g.State -eq 'checking') { '...' } else { '-' }
        $ui.HeroStatus.Text = $st.Text
        $pt = Get-Playtime $g.Id
        if ($pt -and ($pt.total -gt 30 -or $pt.last)) {
            $ui.HeroPlayed.Text = (Format-Playtime $pt.total) + $(if ($pt.last) { "  $Bullet  " + (Format-When $pt.last) } else { '' })
            $ui.PlayedChip.Visibility = 'Visible'
        } else { $ui.PlayedChip.Visibility = 'Collapsed' }
        $ui.StatusDot.Fill = Brush $st.Color
        Pulse $ui.StatusDot ($g.State -in 'checking', 'update', 'updating', 'installing', 'converting')
        $exists = Test-Path (Resolve-Exe $g)
        # not installed yet: the main button installs instead of playing
        $installMode = $g.State -in 'missing', 'installing', 'converting'
        $ui.PlayIcon.Text = if ($installMode) { [string][char]0xE896 } else { [string][char]0xE768 }
        $ui.PlayText.Text = if ($g.State -eq 'converting') { 'CONVERTING...' } elseif ($g.State -eq 'installing') { 'INSTALLING...' } elseif ($installMode) { 'INSTALL' } else { 'PLAY' }
        if ($installMode) {
            # without a known download, Install looks it up first (Start-Install)
            $ui.BtnPlay.IsEnabled = ($g.State -eq 'missing') -and -not $script:running
            $ui.HeroNote.Text = "You need: $($g.Needs)"
            $ui.HeroNote.Visibility = 'Visible'
        } else {
            $ui.BtnPlay.IsEnabled = $exists -and $g.State -ne 'updating'
            $ui.HeroNote.Visibility = 'Collapsed'
        }
        $ui.BtnFolder.IsEnabled = Test-Path $g.Dir
        $ui.BtnUninstall.Visibility = if ($g.Installed -and $g.State -notin 'missing', 'installing', 'converting', 'uninstalling') { 'Visible' } else { 'Collapsed' }
        $ui.BtnUninstall.IsEnabled = -not $script:running
        $ui.BtnUpdate.IsEnabled = ($g.State -eq 'update') -and -not $script:running
        $ui.BtnUpdate.Visibility = if ($g.State -in 'missing', 'installing', 'converting') { 'Collapsed' } else { 'Visible' }
        $ui.BtnUpdateText.Text = switch ($g.State) {
            'update'   { if ($g.Latest.Size) { 'Update  ({0:N0} MB)' -f ($g.Latest.Size / 1MB) } else { 'Update' } }
            'updating' { 'Updating...' }
            default    { 'Up to date' }
        }
        $ui.BtnUpdate.BorderBrush = Brush $(if ($g.State -eq 'update') { $Gold } else { '#33FFFFFF' })
        $ui.BtnUpdate.Foreground = Brush $(if ($g.State -eq 'update') { '#F2D27A' } else { $TextC })
    }
    $ui.BtnCheck.IsEnabled = -not $script:running -and -not $script:checking
    $ui.BtnSettings.IsEnabled = -not $script:running
    if ($g -and $script:view -eq 'grid') {
        $ui.GridName.Text = $g.Name; $ui.GridName.Foreground = Brush $TextC
        $ui.GridState.Text = "$($st.Text)$(if ($g.Installed) { "  $Bullet  v$($g.Installed.Version)" })"
        $ui.GridDot.Fill = Brush $st.Color
    }
    if ($script:padUsed) { Fix-Focus }
}

# ---------- Search and filters ----------
$FilterDefs = @(@{ Id = 'all'; Label = 'All' }, @{ Id = 'installed'; Label = 'Installed' }, @{ Id = 'updates'; Label = 'Updates' }, @{ Id = 'new'; Label = 'Not installed' })
foreach ($f in $FilterDefs) {
    $b = New-Object Windows.Controls.Border
    $b.CornerRadius = New-Object Windows.CornerRadius(15); $b.Height = 30; $b.Cursor = 'Hand'
    $b.Padding = New-Object Windows.Thickness(14, 0, 14, 0); $b.Margin = New-Object Windows.Thickness(0, 0, 6, 0)
    $b.BorderThickness = New-Object Windows.Thickness(1)
    $t = New-Object Windows.Controls.TextBlock
    $t.Text = $f.Label; $t.FontSize = 12; $t.VerticalAlignment = 'Center'
    $t.FontFamily = New-Object Windows.Media.FontFamily('Segoe UI Variable Display Semibold, Segoe UI Semibold')
    $b.Child = $t
    $f.Chip = $b; $f.Text = $t
    $b.Tag = $f.Id
    $b.Add_MouseLeftButtonUp({ param($s, $e) Set-Filter $s.Tag })
    [void]$ui.Chips.Children.Add($b)
}
# ---- Play time (playtime.json, written by playtime-worker.ps1 while a game runs) ----
$PlaytimePath = Join-Path $Root 'playtime.json'
$script:playtime = @{}; $script:playtimeRead = [datetime]::MinValue
function Get-Playtime($id) {
    # read again when the file changed (a game was played since)
    $t = if (Test-Path $PlaytimePath) { (Get-Item $PlaytimePath).LastWriteTime } else { [datetime]::MinValue }
    if ($t -ne $script:playtimeRead) {
        $script:playtimeRead = $t; $script:playtime = @{}
        try { (Get-Content $PlaytimePath -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $script:playtime[$_.Name] = $_.Value } } catch {}
    }
    $script:playtime[$id]
}
function Format-Playtime($secs) {
    $m = [int][Math]::Round([double]$secs / 60)
    if ($m -lt 1) { 'just started' } elseif ($m -lt 60) { "$m min" } else { '{0} h {1} min' -f [Math]::Floor($m / 60), ($m % 60) }
}
function Format-When($iso) {
    try { $d = [datetime]$iso } catch { return '' }
    $days = ((Get-Date).Date - $d.Date).Days
    if ($days -le 0) { 'today' } elseif ($days -eq 1) { 'yesterday' } elseif ($days -lt 7) { "$days days ago" } else { $d.ToString('MMM d') }
}

# Sort chip: Default (the library's own order), Installed first, Recently played, A-Z. Clicking (or LS on a controller)
# goes to the next.
$SortDefs = @(@{ Id = 'default'; Label = 'Series' }, @{ Id = 'installed'; Label = 'Installed first' }, @{ Id = 'recent'; Label = 'Recently played' },
    @{ Id = 'name'; Label = 'A' + [char]0x2013 + 'Z' })
$script:sort = if ($Settings.SortMode -in @($SortDefs | ForEach-Object { $_.Id })) { $Settings.SortMode } else { 'default' }
$script:order = @($Games)
$ui.SortChip = New-Object Windows.Controls.Border
$ui.SortChip.CornerRadius = New-Object Windows.CornerRadius(15); $ui.SortChip.Height = 30; $ui.SortChip.Cursor = 'Hand'
$ui.SortChip.Padding = New-Object Windows.Thickness(12, 0, 14, 0); $ui.SortChip.Margin = New-Object Windows.Thickness(10, 0, 0, 0)
$ui.SortChip.BorderThickness = New-Object Windows.Thickness(1)
$ui.SortChip.Background = Brush '#3D0E1418'; $ui.SortChip.BorderBrush = Brush '#33FFFFFF'
$sortRow = New-Object Windows.Controls.StackPanel; $sortRow.Orientation = 'Horizontal'
$sortIcon = New-Object Windows.Controls.TextBlock
$sortIcon.Text = [string][char]0xE8CB; $sortIcon.FontFamily = New-Object Windows.Media.FontFamily('Segoe MDL2 Assets')
$sortIcon.FontSize = 12; $sortIcon.VerticalAlignment = 'Center'; $sortIcon.Margin = New-Object Windows.Thickness(0, 1, 8, 0); $sortIcon.Foreground = Brush '#A9B6BE'
$ui.SortText = New-Object Windows.Controls.TextBlock
$ui.SortText.FontSize = 12; $ui.SortText.VerticalAlignment = 'Center'; $ui.SortText.Foreground = Brush $TextC
$ui.SortText.FontFamily = New-Object Windows.Media.FontFamily('Segoe UI Variable Display Semibold, Segoe UI Semibold')
[void]$sortRow.Children.Add($sortIcon); [void]$sortRow.Children.Add($ui.SortText)
$ui.SortChip.Child = $sortRow
$ui.SortChip.ToolTip = 'Sort the library'
$ui.SortChip.Add_MouseLeftButtonUp({ Cycle-Sort })
[void]$ui.Chips.Children.Add($ui.SortChip)
# Grid / row chip (RS on a controller, Ctrl+G on the keyboard)
$ui.ViewChip = New-Object Windows.Controls.Border
$ui.ViewChip.CornerRadius = New-Object Windows.CornerRadius(15); $ui.ViewChip.Height = 30; $ui.ViewChip.Cursor = 'Hand'
$ui.ViewChip.Padding = New-Object Windows.Thickness(12, 0, 14, 0); $ui.ViewChip.Margin = New-Object Windows.Thickness(6, 0, 0, 0)
$ui.ViewChip.BorderThickness = New-Object Windows.Thickness(1)
$ui.ViewChip.Background = Brush '#3D0E1418'; $ui.ViewChip.BorderBrush = Brush '#33FFFFFF'
$viewRow = New-Object Windows.Controls.StackPanel; $viewRow.Orientation = 'Horizontal'
$ui.ViewIcon = New-Object Windows.Controls.TextBlock
$ui.ViewIcon.Text = [string][char]0xF0E2; $ui.ViewIcon.FontFamily = New-Object Windows.Media.FontFamily('Segoe MDL2 Assets')
$ui.ViewIcon.FontSize = 12; $ui.ViewIcon.VerticalAlignment = 'Center'; $ui.ViewIcon.Margin = New-Object Windows.Thickness(0, 1, 8, 0); $ui.ViewIcon.Foreground = Brush '#A9B6BE'
$ui.ViewText = New-Object Windows.Controls.TextBlock
$ui.ViewText.Text = 'Grid'; $ui.ViewText.FontSize = 12; $ui.ViewText.VerticalAlignment = 'Center'; $ui.ViewText.Foreground = Brush $TextC
$ui.ViewText.FontFamily = New-Object Windows.Media.FontFamily('Segoe UI Variable Display Semibold, Segoe UI Semibold')
[void]$viewRow.Children.Add($ui.ViewIcon); [void]$viewRow.Children.Add($ui.ViewText)
$ui.ViewChip.Child = $viewRow
$ui.ViewChip.ToolTip = 'Grid or row view (RS on a controller, Ctrl+G)'
$ui.ViewChip.Add_MouseLeftButtonUp({ Toggle-View })
[void]$ui.Chips.Children.Add($ui.ViewChip)

# The games in the chosen order; the cards are put in that order and left / right follow it
# Games of one series always stay together (Zelda, then Mario, other Nintendo, PlayStation); the sort chip orders
# the games inside each series. Within a series the default order keeps related games side by side (BotW + TotK).
$SeriesOf = @{
    botw = 'zelda'; totk = 'zelda'; las = 'zelda'; wwhd = 'zelda'
    smo = 'mario'; sms = 'mario'; mkwii = 'mario'; mp6 = 'mario'
    tod = 'playstation'; acit = 'playstation'; iss = 'playstation'
    tp = 'zelda'; smg = 'mario'; spm = 'mario'; nsmbw = 'mario'; nsmbu = 'mario'
    pkgen = 'pokemon'; pmd = 'pokemon'; pkblack = 'pokemon'; pkss = 'pokemon'; pkstadium = 'pokemon'; pksnap = 'pokemon'
    pklgpe = 'pokemon'; pkswsh = 'pokemon'; pkla = 'pokemon'; pokopia = 'pokemon'
    mm = 'zelda'; mm2s = 'zelda'; oot = 'zelda'; sm64 = 'mario'; mk64 = 'mario'; sf64 = 'nintendo'; bk = 'nintendo'; dk64 = 'nintendo'; conker = 'nintendo'; sonic = 'sega'; thps1 = 'sports'; thps2 = 'sports'; thps3 = 'sports'; nomercy = 'sports'; wm2k = 'sports'; wcwwt = 'sports'; revenge = 'sports'; vpw64 = 'sports'
}
$SeriesRank = @{ zelda = 0; mario = 1; pokemon = 2; nintendo = 3; sega = 4; sports = 5; playstation = 6 }
$InSeries = @('botw', 'totk', 'tp', 'wwhd', 'las', 'mm', 'mm2s', 'oot', 'smo', 'smg', 'sms', 'sm64', 'nsmbu', 'nsmbw', 'spm', 'mkwii', 'mk64', 'mp6',
    'sf64', 'bk', 'dk64', 'conker', 'sonic', 'thps1', 'thps2', 'thps3', 'nomercy', 'wm2k', 'wcwwt', 'revenge', 'vpw64',
    'pkgen', 'pmd', 'pkss', 'pkblack', 'pkstadium', 'pksnap', 'pklgpe', 'pkswsh', 'pkla', 'pokopia', 'tod', 'acit', 'iss')
function Series-Of($g) { if ($SeriesOf[$g.Id]) { $SeriesOf[$g.Id] } else { 'nintendo' } }
function Game-Rank($g) { $i = [array]::IndexOf($InSeries, $g.Id); if ($i -lt 0) { 100 + [array]::IndexOf($Games, $g) } else { $i } }
function Last-Played($g) { $p = Get-Playtime $g.Id; if ($p -and $p.last) { try { ([datetime]$p.last).Ticks } catch { 0 } } else { 0 } }
function Sorted-Games {
    $series = @{ E = { $SeriesRank[(Series-Of $_)] } }
    $rank = @{ E = { Game-Rank $_ } }
    switch ($script:sort) {
        'installed' { @($Games | Sort-Object $series, @{ E = { [int](-not $_.Installed) } }, $rank) }
        'name'      { @($Games | Sort-Object $series, @{ E = { $_.Name -replace '^The ', '' } }) }
        # the series played most recently first, and inside each the most recently played game first
        'recent'    {
            $best = @{}
            foreach ($g in $Games) { $s = Series-Of $g; $t = Last-Played $g; if (-not $best[$s] -or $t -gt $best[$s]) { $best[$s] = $t } }
            @($Games | Sort-Object @{ E = { $best[(Series-Of $_)] }; Descending = $true }, $series,
                                   @{ E = { Last-Played $_ }; Descending = $true }, $rank)
        }
        default     { @($Games | Sort-Object $series, $rank) }
    }
}
$script:view = if ("$($Settings.ViewMode)" -eq 'grid') { 'grid' } else { 'row' }
function Tile-Panel { if ($script:view -eq 'grid') { $ui.GridTiles } else { $ui.Tiles } }
function Apply-Sort([switch]$Force) {
    $order = Sorted-Games
    $ui.SortText.Text = 'Sort: ' + ($SortDefs | Where-Object { $_.Id -eq $script:sort }).Label
    $panel = Tile-Panel
    if (-not $Force -and (($order | ForEach-Object { $_.Id }) -join ',') -eq (($script:order | ForEach-Object { $_.Id }) -join ',') -and $panel.Children.Count) { return }
    $script:order = $order
    $ui.Tiles.Children.Clear(); $ui.GridTiles.Children.Clear()
    $prev = $null
    foreach ($g in $order) {
        # row: a wider gap where a new series starts; grid: even spacing (room for the bigger selected card)
        $s = Series-Of $g
        $g.Tile.Margin = if ($script:view -eq 'grid') { New-Object Windows.Thickness(12, 10, 12, 14) }
                         else { New-Object Windows.Thickness($(if ($prev -and $s -ne $prev) { 36 } else { 4 }), 0, 4, 0) }
        $prev = $s
        [void]$panel.Children.Add($g.Tile)
    }
    Queue-Scroll
}
# Row view (the game's page above one row of cards) or grid view (cards filling the window)
function Set-View($v) {
    $script:view = $v
    $grid = $v -eq 'grid'
    $lib = $ui.TileScroll.Parent
    [Windows.Controls.Grid]::SetRow($lib, $(if ($grid) { 1 } else { 2 }))
    [Windows.Controls.Grid]::SetRowSpan($lib, $(if ($grid) { 2 } else { 1 }))
    $ui.HeroContent.Visibility = if ($grid) { 'Collapsed' } else { 'Visible' }
    $ui.TileScroll.Visibility = if ($grid) { 'Collapsed' } else { 'Visible' }
    $ui.GridScroll.Visibility = if ($grid) { 'Visible' } else { 'Collapsed' }
    $ui.GridInfo.Visibility = if ($grid) { 'Visible' } else { 'Collapsed' }
    $ui.ViewIcon.Text = [string][char]$(if ($grid) { 0xE8FD } else { 0xF0E2 })
    $ui.ViewText.Text = if ($grid) { 'Row' } else { 'Grid' }
    if ($grid -and $script:zone -ne 'cards') { $script:zone = 'cards'; Hide-Ring }
    Apply-Sort -Force
    Apply-Filter
    Update-Hero
    Set-Hints
    if (-not $Snapshot) { $Settings.ViewMode = $v; try { Save-Settings $Settings } catch {} }
}
function Toggle-View { Set-View $(if ($script:view -eq 'grid') { 'row' } else { 'grid' }) }
# Grid view: up / down go to the card above or below (the nearest one in the row before or after)
function Grid-Move([int]$dir) {   # [int]: "Grid-Move -1" would otherwise pass the text "-1"
    $list = Shown-Games; $cur = $script:sel
    if (-not $cur -or -not $list.Count) { return }
    # layout positions in the grid (not where an animation is drawing the card right now)
    $pos = { param($t) $o = [Windows.Media.VisualTreeHelper]::GetOffset($t); New-Object Windows.Point($o.X, $o.Y) }
    $p0 = & $pos $cur.Tile
    $best = $null; $bestRow = $null; $bestDx = [double]::MaxValue
    foreach ($o in $list) {
        if ($o -eq $cur) { continue }
        $p = & $pos $o.Tile
        $dy = $p.Y - $p0.Y
        if (($dir -gt 0 -and $dy -le 20) -or ($dir -lt 0 -and $dy -ge -20)) { continue }
        # the nearest row in that direction, then the nearest card in it
        if ($null -eq $bestRow -or [Math]::Abs($dy) -lt [Math]::Abs($bestRow) - 20) { $bestRow = $dy; $bestDx = [double]::MaxValue; $best = $null }
        if ([Math]::Abs($dy - $bestRow) -le 20) {
            $dx = [Math]::Abs($p.X - $p0.X)
            if ($dx -lt $bestDx) { $bestDx = $dx; $best = $o }
        }
    }
    if ($best) { Show-Game $best }
}
function Cycle-Sort {
    $ids = @($SortDefs | ForEach-Object { $_.Id })
    $script:sort = $ids[([array]::IndexOf($ids, $script:sort) + 1) % $ids.Count]
    Apply-Sort
    $Settings.SortMode = $script:sort
    try { Save-Settings $Settings } catch {}
    # a little nudge on the chip, so the change is noticed
    $st = New-Object Windows.Media.ScaleTransform(1, 1); $ui.SortChip.RenderTransformOrigin = New-Object Windows.Point(0.5, 0.5); $ui.SortChip.RenderTransform = $st
    $a = Anim 1 260 0.9
    $st.BeginAnimation([Windows.Media.ScaleTransform]::ScaleXProperty, $a); $st.BeginAnimation([Windows.Media.ScaleTransform]::ScaleYProperty, $a)
}
# Games hidden in Settings stay out of the library, its counts and Update all
function Is-Hidden($g) { (",$($Settings.Hidden),") -like "*,$($g.Id),*" }
function Listed-Games { @($Games | Where-Object { -not (Is-Hidden $_) }) }
function Count-Filter($id) {
    $list = Listed-Games
    switch ($id) {
        'installed' { @($list | Where-Object { $_.Installed }).Count }
        'updates'   { @($list | Where-Object { $_.State -eq 'update' }).Count }
        'new'       { @($list | Where-Object { -not $_.Installed }).Count }
        default     { $list.Count }
    }
}
function Update-Chips {
    foreach ($f in $FilterDefs) {
        $on = $f.Id -eq $script:filter
        $c = Count-Filter $f.Id
        $f.Text.Text = if ($f.Id -eq 'all') { $f.Label } else { "$($f.Label)  $c" }
        $f.Chip.Background = Brush $(if ($on) { '#F2EFE6' } else { '#3D0E1418' })
        $f.Chip.BorderBrush = Brush $(if ($on) { '#F2EFE6' } else { '#33FFFFFF' })
        $f.Text.Foreground = Brush $(if ($on) { '#0A0E11' } elseif ($c -eq 0) { '#5F717C' } else { $TextC })
    }
}
function Test-Shown($g) {
    if (Is-Hidden $g) { return $false }
    $q = $ui.SearchBox.Text.Trim()
    if ($q -and -not (("$($g.Name) $($g.Short) $($g.Source) $($g.Id)") -like "*$q*")) { return $false }
    switch ($script:filter) {
        'installed' { [bool]$g.Installed }
        'updates'   { $g.State -in 'update', 'updating' }
        'new'       { -not $g.Installed }
        default     { $true }
    }
}
function Shown-Games { @($script:order | Where-Object { $_.Tile.Visibility -eq 'Visible' }) }
function Apply-Filter {
    $shown = 0
    foreach ($g in $script:order) {
        $on = Test-Shown $g
        $g.Tile.Visibility = if ($on) { 'Visible' } else { 'Collapsed' }
        if ($on) { $shown++ }
    }
    $total = (Listed-Games).Count
    $ui.RowCount.Text = if ($shown -eq $total) { "$Bullet  $total GAMES" } else { "$Bullet  $shown OF $total" }
    $ui.NoMatch.Visibility = if ($shown) { 'Collapsed' } else { 'Visible' }
    $ui.NoMatch.Text = if ($ui.SearchBox.Text.Trim()) { "No games match `"$($ui.SearchBox.Text.Trim())`"." } else { 'No games here.' }
    # keep a visible game selected
    if ($shown -and $script:sel -and $script:sel.Tile.Visibility -ne 'Visible') {
        $first = (Shown-Games)[0]
        if ($first) { Show-Game $first }
    }
}
function Set-Filter($id) {
    $script:filter = $id
    Update-Chips
    Apply-Filter
    Queue-Scroll
}
function Cycle-Filter([int]$dir) {
    $ids = @($FilterDefs | ForEach-Object { $_.Id })
    $i = [array]::IndexOf($ids, $script:filter) + $dir
    if ($i -lt 0) { $i = $ids.Count - 1 } elseif ($i -ge $ids.Count) { $i = 0 }
    Set-Filter $ids[$i]
}

# ---------- Card row scrolling: the selected card glides to the middle ----------
$script:scrollTo = $null
$scrollTimer = New-Object Windows.Threading.DispatcherTimer
$scrollTimer.Interval = [TimeSpan]::FromMilliseconds(15)
$scrollTimer.Add_Tick({
    $sv = $ui.TileScroll
    if ($null -eq $script:scrollTo) { $scrollTimer.Stop(); return }
    $cur = $sv.HorizontalOffset
    $next = $cur + ($script:scrollTo - $cur) * 0.2
    if ([Math]::Abs($script:scrollTo - $next) -lt 0.5) { $next = $script:scrollTo; $script:scrollTo = $null }
    $sv.ScrollToHorizontalOffset($next)
})
function Scroll-ToSelected {
    $g = $script:sel
    if ($script:view -eq 'grid') { if ($g -and $g.Tile.Visibility -eq 'Visible') { $g.Tile.BringIntoView() }; return }
    if (-not $g -or $g.Tile.Visibility -ne 'Visible' -or $ui.TileScroll.ViewportWidth -le 0) { return }
    $x = $g.Tile.TransformToAncestor($ui.Tiles).Transform((New-Object Windows.Point(0, 0))).X + $ui.Tiles.Margin.Left
    $target = $x + $g.Tile.ActualWidth / 2 - $ui.TileScroll.ViewportWidth / 2
    $target = [Math]::Max(0, [Math]::Min($target, $ui.TileScroll.ScrollableWidth))
    $script:scrollTo = $target
    $scrollTimer.Start()
}
function Queue-Scroll { [void]$W.Dispatcher.BeginInvoke([Action] { Scroll-ToSelected }, [Windows.Threading.DispatcherPriority]::Loaded) }

function Show-Game($g) {
    $prev = $script:sel
    $script:sel = $g
    # only the card that loses the selection and the one that gets it change (all of them the first time)
    $touch = if ($prev -and $prev -ne $g) { @($prev, $g) } elseif ($prev) { @($g) } else { $Games }
    foreach ($o in $touch) {
        $on = $o -eq $g
        $s = if ($on) { 1.12 } else { 1.0 }
        $o.TBox.RenderTransform.BeginAnimation([Windows.Media.ScaleTransform]::ScaleXProperty, (Anim $s 320))
        $o.TBox.RenderTransform.BeginAnimation([Windows.Media.ScaleTransform]::ScaleYProperty, (Anim $s 320))
        $o.TSel.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim ([double][int]$on) 250))
        # the glow (a blurred shadow) is only drawn for the selected card: hidden, not just see-through, on the others
        if ($on) {
            $o.TGlow.Visibility = 'Visible'
            $o.TGlow.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 0.75 400))
        } elseif ($o.TGlow.Visibility -eq 'Visible') {
            $ga = Anim 0 300; $tg = $o.TGlow
            $ga.Add_Completed({ if ($script:sel.TGlow -ne $tg) { $tg.Visibility = 'Collapsed' } }.GetNewClosure())
            $o.TGlow.BeginAnimation([Windows.UIElement]::OpacityProperty, $ga)
        }
        $o.TShade.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim $(if ($on) { 0 } else { 0.35 }) 250))
        $o.TName.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim $(if ($on) { 1 } else { 0.7 }) 250))
    }
    # backdrop colours
    $ui.HeroStop1.BeginAnimation([Windows.Media.GradientStop]::ColorProperty, (ColorAnim (WithAlpha $g.Accent 0x70) 700))
    $ui.HeroStop2.BeginAnimation([Windows.Media.GradientStop]::ColorProperty, (ColorAnim (WithAlpha $g.Accent2 0x26) 700))
    foreach ($l in @(@('Glow1', $g.Accent), @('Glow2', $g.Accent2))) {
        $ui["$($l[0])A"].BeginAnimation([Windows.Media.GradientStop]::ColorProperty, (ColorAnim (WithAlpha $l[1] 0xFF) 700))
        $ui["$($l[0])M"].BeginAnimation([Windows.Media.GradientStop]::ColorProperty, (ColorAnim (WithAlpha $l[1] 0x80) 700))
        $ui["$($l[0])B"].BeginAnimation([Windows.Media.GradientStop]::ColorProperty, (ColorAnim (WithAlpha $l[1] 0x00) 700))
    }
    # content
    $ui.HeroTitle.Text = $g.Name
    $ui.HeroTitle.FontSize = if ($g.Name.Length -gt 26) { 42 } else { 56 }
    $ui.HeroSource.Text = $g.Source; $ui.HeroSource.Foreground = Brush $g.Accent
    $ui.HeroIcon.Source = $g.Icon
    $ui.HeroArt.Source = $g.Icon
    $ui.NotesGame.Text = $g.Name.ToUpper(); $ui.NotesGame.Foreground = Brush $g.Accent
    $ui.TabNotesLine.Background = Brush $g.Accent; $ui.TabActivityLine.Background = Brush $g.Accent
    # the release notes are laid out when the panel is open, or when it opens (Open-Notes)
    if ($ui.NotesLayer.Visibility -eq 'Visible') { Fill-Notes $g } else { $script:notesFor = $null }
    Update-Hero
    Queue-Scroll
    # slide in
    $ui.HeroContent.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 420 0))
    $ui.HeroContent.RenderTransform.BeginAnimation([Windows.Media.TranslateTransform]::XProperty, (Anim 0 520 40))
    $ui.HeroArt.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 0.2 900 0))
    $sc = $ui.HeroArt.RenderTransform.Children[0]
    $sc.BeginAnimation([Windows.Media.ScaleTransform]::ScaleXProperty, (Anim 1 1100 1.16))
    $sc.BeginAnimation([Windows.Media.ScaleTransform]::ScaleYProperty, (Anim 1 1100 1.16))
}
function Step-Game([int]$dir) {
    $list = Shown-Games
    if (-not $list.Count) { return }
    $i = [array]::IndexOf($list, $script:sel)
    $j = [Math]::Max(0, [Math]::Min($list.Count - 1, $i + $dir))
    if ($i -lt 0) { $j = 0 }
    if ($list[$j] -ne $script:sel) { Show-Game $list[$j] }
}

# ---------- What's new / Activity panel ----------
function Open-Notes($which = $null) {
    if ($script:notesFor -ne $script:sel) { Fill-Notes $script:sel }
    if ($which) { Set-Tab $which }
    if ($ui.NotesLayer.Visibility -eq 'Visible') { return }
    $ui.NotesLayer.Visibility = 'Visible'
    $ui.NotesLayer.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 200 0))
    $ui.NotesPanel.RenderTransform.BeginAnimation([Windows.Media.TranslateTransform]::XProperty, (Anim 0 380 ($ui.NotesPanel.Width)))
    Hide-Ring
    Set-Hints
}
function Close-Notes {
    if ($ui.NotesLayer.Visibility -ne 'Visible') { return }
    $a = Anim 0 200 $null 'EaseIn'
    $a.Add_Completed({ $ui.NotesLayer.Visibility = 'Collapsed'; Set-Hints; Queue-Ring })
    $ui.NotesLayer.BeginAnimation([Windows.UIElement]::OpacityProperty, $a)
    $ui.NotesPanel.RenderTransform.BeginAnimation([Windows.Media.TranslateTransform]::XProperty, (Anim ($ui.NotesPanel.Width) 260 $null 'EaseIn'))
}

# ---------- Full screen ----------
# Full screen covers the whole monitor the window is on, taskbar and docks (Seelen UI) included: maximizing would
# only fill the space those bars leave free. The window stays on top only while it is the active window, so a game
# started from it comes to the front normally.
function Set-Fullscreen($on) {
    if ([bool]$on -eq [bool]$script:isFull -and $script:fullInit) { return }
    $script:fullInit = $true
    $script:isFull = [bool]$on
    if ($on) {
        if ($W.WindowState -eq 'Normal') { $script:normalRect = New-Object Windows.Rect($W.Left, $W.Top, $W.Width, $W.Height) }
        $hwnd = (New-Object Windows.Interop.WindowInteropHelper($W)).Handle
        $px = [System.Windows.Forms.Screen]::FromHandle($hwnd).Bounds
        # monitor pixels to WPF's device-independent units (display scaling)
        $src = [Windows.PresentationSource]::FromVisual($W)
        $m = if ($src) { $src.CompositionTarget.TransformFromDevice } else { [Windows.Media.Matrix]::Identity }
        $tl = $m.Transform((New-Object Windows.Point($px.Left, $px.Top)))
        $br = $m.Transform((New-Object Windows.Point($px.Right, $px.Bottom)))
        $W.WindowState = 'Normal'
        $W.ResizeMode = 'NoResize'
        $W.Left = $tl.X; $W.Top = $tl.Y; $W.Width = $br.X - $tl.X; $W.Height = $br.Y - $tl.Y
        $W.Topmost = $W.IsActive
        try { [IconX]::WindowCorners($hwnd, $false) } catch {}
        $ui.FullIcon.Text = [string][char]0xE73F
        $ui.BtnFull.ToolTip = 'Leave full screen (F11)'
    } else {
        $W.Topmost = $false
        $W.WindowState = 'Normal'
        if ($script:normalRect) {
            $r = $script:normalRect
            $W.Left = $r.X; $W.Top = $r.Y; $W.Width = $r.Width; $W.Height = $r.Height
        }
        $W.ResizeMode = 'CanResize'
        try { [IconX]::WindowCorners((New-Object Windows.Interop.WindowInteropHelper($W)).Handle, $true) } catch {}
        $ui.FullIcon.Text = [string][char]0xE740
        $ui.BtnFull.ToolTip = 'Full screen (F11)'
    }
    Queue-Scroll
    Queue-Ring
}
function Toggle-Fullscreen {
    Set-Fullscreen (-not $script:isFull)
    $Settings.Fullscreen = $script:isFull
    try { Save-Settings $Settings } catch {}
}

# ---------- Background jobs (release checks) ----------
function Start-Async($script, $arg, $onDone) {
    $ps = [PowerShell]::Create()
    [void]$ps.AddScript($script).AddArgument($arg)
    [void]$script:pending.Add(@{ PS = $ps; Handle = $ps.BeginInvoke(); Done = $onDone; Arg = $arg; Gen = $script:checkGen })
    $asyncTimer.Start()
}
$asyncTimer = New-Object Windows.Threading.DispatcherTimer
$asyncTimer.Interval = [TimeSpan]::FromMilliseconds(100)
$asyncTimer.Add_Tick({
    foreach ($job in @($script:pending)) {
        if (-not $job.Handle.IsCompleted) { continue }
        $script:pending.Remove($job)
        $res = $null; $err = $null
        try { $res = $job.PS.EndInvoke($job.Handle) | Select-Object -Last 1 } catch { $err = $_ }
        $job.PS.Dispose()
        if ($job.Gen -eq $script:checkGen) { & $job.Done $job.Arg $res $err }
    }
    if ($script:pending.Count -eq 0) { $asyncTimer.Stop() }
})

# The last release info of each game, kept in cache.json: used for 20 minutes instead of asking again (GitHub
# allows 60 questions an hour without an account), and whenever a release page can't be reached.
$CachePath = Join-Path $Root 'cache.json'
$CacheMinutes = 20
function Load-Cache {
    $c = @{}
    if (Test-Path $CachePath) {
        try {
            $j = Get-Content $CachePath -Raw | ConvertFrom-Json
            foreach ($p in $j.PSObject.Properties) {
                $h = @{}; foreach ($q in $p.Value.PSObject.Properties) { $h[$q.Name] = $q.Value }
                $c[$p.Name] = $h
            }
        } catch {}
    }
    $c
}
function Save-Cache($c) { try { $c | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $CachePath } catch {} }
$script:cache = Load-Cache

function Check-All([switch]$Force) {
    $script:checkGen++
    $script:checking = $true
    $a = Forever (Anim 360 900 0 $null)
    $ui.CheckIcon.RenderTransform.BeginAnimation([Windows.Media.RotateTransform]::AngleProperty, $a)
    $ui.LogoImg.RenderTransform.BeginAnimation([Windows.Media.RotateTransform]::AngleProperty, (Anim 360 1200 0 'EaseInOut'))
    Set-Step 'Checking for updates...' $TextC
    Set-Indeterminate $true
    $script:checksLeft = $Games.Count
    $script:usedOld = $false
    foreach ($g in $Games) {
        $g.Installed = Get-Installed $g
        $g.Latest = $null
        $g.State = if ($g.Installed) { 'checking' } else { 'missing' }
        $done = {
            param($job, $res, $err)
            $g = $Games | Where-Object { $_.Id -eq $job.Id }
            if ($res -and $res.Tag) {
                if (-not $job.FromCache) {
                    $res.Checked = (Get-Date).ToString('o')
                    $script:cache[$g.Id] = $res
                    Save-Cache $script:cache
                }
            } elseif ($script:cache[$g.Id]) {
                $res = $script:cache[$g.Id]; $script:usedOld = $true   # unreachable: the last known release
            }
            if ($res -and $res.Tag) {
                $g.Latest = $res
                if ($FakeUpdate -and $g.Installed) { $g.Installed.Version = '0.0.1' }
                if ($g.Installed) {
                    $newer = (To-Version $res.Tag) -gt (To-Version $g.Installed.Version)
                    $g.State = if (-not $newer) { 'current' } elseif ($res.Problem) { 'problem' } else { 'update' }
                }
            } elseif ($g.Installed) { $g.State = 'offline' }
            if ($g -eq $script:sel) { Fill-Notes $g; if ($script:tab -eq 'notes') { $ui.Viewer.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 350 0)) } }
            $script:checksLeft--
            if ($script:checksLeft -le 0) { Finish-Check }
            Update-Views
        }
        $job = @{ Id = $g.Id; Repo = $g.Repo; Asset = $g.Asset; NotAsset = $g.NotAsset; Pre = [bool]$g.Pre; Token = [string]$Settings.GitHubToken }
        $c = $script:cache[$g.Id]
        $fresh = $c -and $c.Checked -and ((Get-Date) - [datetime]$c.Checked).TotalMinutes -lt $CacheMinutes
        if ($fresh -and -not $Force) { $job.FromCache = $true; & $done $job $c $null }
        else { Start-Async $LatestScript $job $done }
    }
    Update-Views
}
function Finish-Check {
    $script:checking = $false
    $ui.CheckIcon.RenderTransform.BeginAnimation([Windows.Media.RotateTransform]::AngleProperty, (Anim 360 400 $null))
    Set-Indeterminate $false
    $ui.LastCheck.Text = 'Checked ' + (Get-Date -Format 'h:mm tt')
    $n = @(Listed-Games | Where-Object { $_.State -eq 'update' }).Count
    $off = @(Listed-Games | Where-Object { $_.State -eq 'offline' }).Count
    $old = if ($script:usedOld) { 'Some release pages could not be reached (GitHub allows 60 checks an hour); showing the last known versions.' } else { '' }
    if ($n -eq 1) { Set-Step '1 update available' $Gold $old; Set-Progress 0 }
    elseif ($n -gt 1) { Set-Step "$n updates available" $Gold $old; Set-Progress 0 }
    elseif ($off) { Set-Step "Couldn't reach the release pages" $Red; Set-Progress 0 }
    else { Set-Step 'Everything is up to date' $Teal $old; Set-Progress 1 }
}

# ---------- Settings panel ----------
# One row per folder: label, settings key, the file that shows the folder is right, and whether an empty folder is
# fine (a game that isn't installed yet goes there)
$SetRowDefs = @(
    @{ Label = 'Tears of the Kingdom'; Key = 'TotkDir'; File = 'totk.exe'; New = $true },
    @{ Label = 'TotK Updater (installs TotK updates)'; Key = 'TotkUpdaterDir'; File = 'update-worker.ps1'; New = $false },
    @{ Label = 'Super Mario Odyssey'; Key = 'SmoDir'; File = 'PowerMoonRecomp.exe'; New = $true },
    @{ Label = 'The Wind Waker HD'; Key = 'WwhdDir'; File = 'Wind Waker HD.exe'; New = $true },
    @{ Label = 'Ratchet & Clank: Tools of Destruction'; Key = 'TodDir'; File = 'tod.exe'; New = $true },
    @{ Label = 'Mario Party 6'; Key = 'Mp6Dir'; File = 'mp6native.exe'; New = $true },
    @{ Label = 'Mario Kart Wii (Wheel Wizard)'; Key = 'MkwiiDir'; File = 'WheelWizard.exe'; New = $true },
    @{ Label = 'Super Mario Sunshine (SMS Launcher)'; Key = 'SmsDir'; File = 'SMS-Launcher-*-win-portable.exe'; New = $true },
    @{ Label = 'Breath of the Wild'; Key = 'BotwDir'; File = 'savagewind.exe'; New = $true },
    @{ Label = 'Ratchet & Clank: A Crack in Time'; Key = 'AcitDir'; File = 'RaCACiT Launcher.exe'; New = $true },
    @{ Label = "Link's Awakening"; Key = 'LasDir'; File = 'lasrecomp.exe'; New = $true },
    @{ Label = 'inFAMOUS Second Son'; Key = 'IssDir'; File = 'inFAMOUS Second Son.exe'; New = $true },
    @{ Label = 'Super Mario Galaxy'; Key = 'SmgDir'; File = 'Nebula-Setup.exe'; New = $true },
    @{ Label = 'Super Paper Mario'; Key = 'SpmDir'; File = 'New Super Paper Bros.exe'; New = $true },
    @{ Label = 'Twilight Princess (Dusklight)'; Key = 'TpDir'; File = 'dusklight.exe'; New = $true },
    @{ Label = 'New Super Mario Bros. Wii'; Key = 'NsmbwDir'; File = 'NSMBW.exe'; New = $true },
    @{ Label = 'New Super Mario Bros. U'; Key = 'NsmbuDir'; File = 'NSMBU.exe'; New = $true },
    @{ Label = "$Pk Gen 1 & 2"; Key = 'PkgenDir'; File = 'Gen2Recomped.exe'; New = $true },
    @{ Label = "$Pk Mystery Dungeon"; Key = 'PmdDir'; File = 'pmd_green.exe'; New = $true },
    @{ Label = "$Pk Black"; Key = 'PkblackDir'; File = 'eidos.exe'; New = $true },
    @{ Label = "$Pk SoulSilver"; Key = 'PkssDir'; File = 'ssrecomp-setup-windows-x86_64.exe'; New = $true },
    @{ Label = "$Pk Stadium"; Key = 'PkstadiumDir'; File = 'PokemonStadiumRecomp.exe'; New = $true },
    @{ Label = "$Pk Snap"; Key = 'PksnapDir'; File = 'Snap64Recomp.exe'; New = $true },
    @{ Label = "$Pk Let's Go"; Key = 'PklgpeDir'; File = 'LetsGoRecomp.exe'; New = $true },
    @{ Label = "$Pk Sword & Shield"; Key = 'PkswshDir'; File = 'SwShRecomp.exe'; New = $true },
    @{ Label = "$Pk Legends: Arceus"; Key = 'PklaDir'; File = 'arceusrecomp.exe'; New = $true },
    @{ Label = "$Pk Pokopia"; Key = 'PokopiaDir'; File = 'PokopiaLauncher.exe'; New = $true },
    @{ Label = 'Majora''s Mask'; Key = 'MmDir'; File = 'Zelda64Recompiled.exe'; New = $true },
    @{ Label = 'Majora''s Mask (2 Ship)'; Key = 'Mm2sDir'; File = '2ship.exe'; New = $true },
    @{ Label = 'Ocarina of Time'; Key = 'OotDir'; File = 'soh.exe'; New = $true },
    @{ Label = 'Super Mario 64'; Key = 'Sm64Dir'; File = 'Ghostship.exe'; New = $true },
    @{ Label = 'Mario Kart 64'; Key = 'Mk64Dir'; File = 'Spaghettify.exe'; New = $true },
    @{ Label = 'Star Fox 64'; Key = 'Sf64Dir'; File = 'Starship.exe'; New = $true },
    @{ Label = 'Banjo-Kazooie'; Key = 'BkDir'; File = 'BanjoRecompiled.exe'; New = $true },
    @{ Label = 'Donkey Kong 64'; Key = 'Dk64Dir'; File = 'DK64Recompiled.exe'; New = $true },
    @{ Label = 'Conker''s Bad Fur Day'; Key = 'ConkerDir'; File = 'ConkerBFDReloaded.exe'; New = $true },
    @{ Label = 'Sonic Unleashed'; Key = 'SonicDir'; File = 'UnleashedRecomp.exe'; New = $true },
    @{ Label = 'Tony Hawk''s Pro Skater'; Key = 'Thps1Dir'; File = 'THPS1-64Recomp.exe'; New = $true },
    @{ Label = 'Tony Hawk''s Pro Skater 2'; Key = 'Thps2Dir'; File = 'THPS2-64Recomp.exe'; New = $true },
    @{ Label = 'Tony Hawk''s Pro Skater 3'; Key = 'Thps3Dir'; File = 'THPS3-64Recomp.exe'; New = $true },
    @{ Label = 'WWF No Mercy'; Key = 'NomercyDir'; File = 'NoMercyRecompiled.exe'; New = $true },
    @{ Label = 'WWF WrestleMania 2000'; Key = 'Wm2kDir'; File = 'Wm2kRecompiled.exe'; New = $true },
    @{ Label = 'WCW vs. nWo: World Tour'; Key = 'WcwwtDir'; File = 'WCWRecompiled.exe'; New = $true },
    @{ Label = 'WCW/nWo Revenge'; Key = 'RevengeDir'; File = 'RevengeRecompiled.exe'; New = $true },
    @{ Label = 'Virtual Pro Wrestling 64'; Key = 'Vpw64Dir'; File = 'Vpw64Recompiled.exe'; New = $true }
)
foreach ($r in $SetRowDefs) {
    $row = New-Object Windows.Controls.StackPanel
    $row.Margin = New-Object Windows.Thickness(0, 8, 0, 0)
    $head = New-Object Windows.Controls.Grid
    $lab = New-Object Windows.Controls.TextBlock
    $lab.Text = $r.Label; $lab.FontSize = 13
    $lab.FontFamily = New-Object Windows.Media.FontFamily('Segoe UI Variable Display Semibold, Segoe UI Semibold')
    $r.Check = New-Object Windows.Controls.TextBlock
    $r.Check.FontSize = 11.5; $r.Check.HorizontalAlignment = 'Right'; $r.Check.VerticalAlignment = 'Bottom'
    [void]$head.Children.Add($lab); [void]$head.Children.Add($r.Check)
    $line = New-Object Windows.Controls.Grid
    $line.Margin = New-Object Windows.Thickness(0, 6, 0, 0)
    $c0 = New-Object Windows.Controls.ColumnDefinition; $c0.Width = New-Object Windows.GridLength(1, 'Star')
    $c1 = New-Object Windows.Controls.ColumnDefinition; $c1.Width = [Windows.GridLength]::Auto
    $line.ColumnDefinitions.Add($c0); $line.ColumnDefinitions.Add($c1)
    $r.Box = New-Object Windows.Controls.TextBox
    $r.Box.Style = $W.FindResource('PathBox'); $r.Box.Height = 38
    $b = New-Object Windows.Controls.Button
    $b.Style = $W.FindResource('Btn'); $b.Content = 'Browse'; $b.Height = 38; $b.FontSize = 13
    $b.Padding = New-Object Windows.Thickness(16, 0, 16, 0); $b.Margin = New-Object Windows.Thickness(8, 0, 0, 0)
    [Windows.Controls.Grid]::SetColumn($b, 1)
    $b.Tag = $r; $r.Btn = $b
    # a controller gets the launcher's own folder browser (Windows' dialog can't be used with one)
    $b.Add_Click({ param($s, $e) if ($script:padUsed) { Open-FbFolder $s.Tag } else { Browse-Into $s.Tag.Box "$($s.Tag.Label) folder (the one with $($s.Tag.File))" } })
    $r.Box.Add_TextChanged({ Validate-Settings })
    [void]$line.Children.Add($r.Box); [void]$line.Children.Add($b)
    [void]$row.Children.Add($head); [void]$row.Children.Add($line)
    [void]$ui.SetRows.Children.Add($row)
}
function Check-PathLabel($r) {
    $dir = $r.Box.Text.Trim(); $label = $r.Check
    if (-not $dir) { $label.Text = 'Enter a folder'; $label.Foreground = Brush $Red; return }
    if (Test-Path (Join-Path $dir $r.File)) { $label.Text = [string][char]0x2713 + "  $($r.File) found"; $label.Foreground = Brush $Teal }
    elseif ($r.New) { $label.Text = 'Not installed here yet: Install puts it here'; $label.Foreground = Brush $Muted }
    elseif (Test-Path $dir) { $label.Text = "$($r.File) isn't in this folder"; $label.Foreground = Brush $Gold }
    else { $label.Text = "This folder doesn't exist"; $label.Foreground = Brush $Red }
}
function Validate-Settings { foreach ($r in $SetRowDefs) { if ($r.Box) { Check-PathLabel $r } } }
function Open-Settings {
    foreach ($r in $SetRowDefs) { $r.Box.Text = $Settings[$r.Key] }
    Validate-Settings
    Load-Options
    if ($ui.Fb) { $ui.Fb.Visibility = 'Collapsed' }
    foreach ($st in $script:setStops) { Mark-SetStop $st $false }
    $script:setStops = Get-SetStops; $script:setIdx = -1
    if ($script:padUsed) { Select-SetStop 0 }
    $ui.SettingsLayer.Visibility = 'Visible'
    $ui.SettingsLayer.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 220 0))
    $tg = $ui.SettingsCard.RenderTransform
    $tg.Children[0].BeginAnimation([Windows.Media.ScaleTransform]::ScaleXProperty, (Anim 1 380 0.94))
    $tg.Children[0].BeginAnimation([Windows.Media.ScaleTransform]::ScaleYProperty, (Anim 1 380 0.94))
    $tg.Children[1].BeginAnimation([Windows.Media.TranslateTransform]::YProperty, (Anim 0 380 18))
    $ui.GearIcon.RenderTransform.BeginAnimation([Windows.Media.RotateTransform]::AngleProperty, (Anim 90 500 0))
    Hide-Ring
    Set-Hints
}
function Close-Settings {
    $a = Anim 0 180 $null 'EaseIn'
    $a.Add_Completed({ $ui.SettingsLayer.Visibility = 'Collapsed'; Set-Hints; Queue-Ring })
    $ui.SettingsLayer.BeginAnimation([Windows.UIElement]::OpacityProperty, $a)
    $tg = $ui.SettingsCard.RenderTransform
    $tg.Children[1].BeginAnimation([Windows.Media.TranslateTransform]::YProperty, (Anim 12 180 $null 'EaseIn'))
    $ui.GearIcon.RenderTransform.BeginAnimation([Windows.Media.RotateTransform]::AngleProperty, (Anim 0 500))
}
function Browse-Into($box, $title) {
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = $title
    if (Test-Path $box.Text) { $d.SelectedPath = $box.Text }
    if ($d.ShowDialog() -eq 'OK') { $box.Text = $d.SelectedPath }
}
function Save-SettingsPanel {
    foreach ($r in $SetRowDefs) { $Settings[$r.Key] = $r.Box.Text.Trim().TrimEnd('\') }
    try { Save-Settings $Settings } catch {
        [Windows.MessageBox]::Show($W, "Couldn't save the settings: $($_.Exception.Message)", 'Recomp Launcher', 'OK', 'Error') | Out-Null
        return
    }
    Save-Options
    Apply-Settings
    Load-Icons
    Close-Settings
    Show-Game $script:sel
    Check-All
}

# ---------- Toggle switches (Settings options, Uninstall's keep-saves) ----------
function New-Toggle($label, $desc) {
    $row = New-Object Windows.Controls.Grid
    $row.Margin = New-Object Windows.Thickness(0, 6, 0, 6); $row.Background = Brush '#00000000'; $row.Cursor = 'Hand'
    $c0 = New-Object Windows.Controls.ColumnDefinition; $c0.Width = New-Object Windows.GridLength(1, 'Star')
    $c1 = New-Object Windows.Controls.ColumnDefinition; $c1.Width = [Windows.GridLength]::Auto
    $row.ColumnDefinitions.Add($c0); $row.ColumnDefinitions.Add($c1)
    $text = New-Object Windows.Controls.StackPanel; $text.VerticalAlignment = 'Center'; $text.Margin = New-Object Windows.Thickness(0, 0, 16, 0)
    $l = New-Object Windows.Controls.TextBlock; $l.Text = $label; $l.FontSize = 13; $l.TextTrimming = 'CharacterEllipsis'; $l.ToolTip = $label
    $l.FontFamily = New-Object Windows.Media.FontFamily('Segoe UI Variable Display Semibold, Segoe UI Semibold')
    [void]$text.Children.Add($l)
    if ($desc) { $d = New-Object Windows.Controls.TextBlock; $d.Text = $desc; $d.FontSize = 11.5; $d.Foreground = Brush $Muted; $d.TextWrapping = 'Wrap'; [void]$text.Children.Add($d) }
    $track = New-Object Windows.Controls.Border
    $track.Width = 44; $track.Height = 24; $track.CornerRadius = New-Object Windows.CornerRadius(12); $track.VerticalAlignment = 'Center'
    $knob = New-Object Windows.Shapes.Ellipse
    $knob.Width = 18; $knob.Height = 18; $knob.Fill = Brush '#F2EFE6'; $knob.HorizontalAlignment = 'Left'; $knob.Margin = New-Object Windows.Thickness(3, 0, 0, 0)
    $knob.RenderTransform = New-Object Windows.Media.TranslateTransform
    $track.Child = $knob
    [Windows.Controls.Grid]::SetColumn($track, 1)
    [void]$row.Children.Add($text); [void]$row.Children.Add($track)
    $t = @{ Root = $row; Track = $track; Knob = $knob; On = $false }
    $row.Tag = $t
    $row.Add_MouseLeftButtonUp({ param($s, $e) Set-Toggle $s.Tag (-not $s.Tag.On) })
    Set-Toggle $t $false $true
    $t
}
function Set-Toggle($t, [bool]$on, [switch]$Now) {
    $t.On = $on
    $ms = if ($Now) { 0 } else { 180 }
    $t.Track.Background = Brush $(if ($on) { $Teal } else { '#2A3640' })
    $t.Knob.RenderTransform.BeginAnimation([Windows.Media.TranslateTransform]::XProperty, (Anim $(if ($on) { 20 } else { 0 }) $ms))
}

# ---------- Options and GitHub token (top of Settings) ----------
$StartupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'Recomp Launcher.lnk'
function Add-SetHeading($text, $index) {
    $h = New-Object Windows.Controls.TextBlock
    $h.Text = $text; $h.FontSize = 10.5; $h.FontWeight = 'Bold'; $h.Foreground = Brush '#3FB8A0'
    $h.Margin = New-Object Windows.Thickness(0, $(if ($index -eq 0) { 4 } else { 18 }), 0, 4)
    $ui.SetRows.Children.Insert($index, $h)
}
$Opt = [ordered]@{
    CloseOnPlay = New-Toggle 'Close the launcher when a game starts' 'Play time is still counted while you play.'
    Fullscreen  = New-Toggle 'Open in full screen' 'F11, the full screen button or View on a controller switch it any time.'
    Startup     = New-Toggle 'Open when Windows starts' 'Adds Recomp Launcher to your Startup folder.'
    AutoCheck   = New-Toggle 'Check for updates every 3 hours' 'While the launcher is open.'
}
$i = 0
Add-SetHeading 'OPTIONS' $i; $i++
foreach ($t in $Opt.Values) { $ui.SetRows.Children.Insert($i, $t.Root); $i++ }

# GitHub token: raises GitHub's limit from 60 checks an hour to 5,000. Stored in settings.json next to the launcher.
Add-SetHeading 'GITHUB TOKEN (OPTIONAL)' $i; $i++
$tokRow = New-Object Windows.Controls.StackPanel
$tokHelp = New-Object Windows.Controls.TextBlock
$tokHelp.TextWrapping = 'Wrap'; $tokHelp.FontSize = 11.5; $tokHelp.Foreground = Brush $Muted; $tokHelp.Margin = New-Object Windows.Thickness(0, 0, 0, 8)
$tokHelp.Text = "Without one, GitHub allows 60 update checks an hour, and some games then show `"Couldn't check`". With a token it's 5,000. Make one at github.com: your picture > Settings > Developer settings > Personal access tokens > Fine-grained tokens > Generate new token. Give it any name, Repository access: Public repositories, no permissions. Generate it, copy it, paste it here. It can't change anything on your account."
$ui.TokenBox = New-Object Windows.Controls.PasswordBox
$ui.TokenBox.Height = 38; $ui.TokenBox.FontSize = 13; $ui.TokenBox.Padding = New-Object Windows.Thickness(10, 0, 10, 0)
$ui.TokenBox.VerticalContentAlignment = 'Center'; $ui.TokenBox.Background = Brush '#0A0E11'; $ui.TokenBox.Foreground = Brush $TextC
$ui.TokenBox.BorderBrush = Brush '#26333C'; $ui.TokenBox.CaretBrush = Brush $TextC
$tokLine = New-Object Windows.Controls.Grid
$tc0 = New-Object Windows.Controls.ColumnDefinition; $tc0.Width = New-Object Windows.GridLength(1, 'Star')
$tc1 = New-Object Windows.Controls.ColumnDefinition; $tc1.Width = [Windows.GridLength]::Auto
$tokLine.ColumnDefinitions.Add($tc0); $tokLine.ColumnDefinitions.Add($tc1)
$ui.BtnTokenSite = New-Object Windows.Controls.Button
$ui.BtnTokenSite.Style = $W.FindResource('Btn'); $ui.BtnTokenSite.Content = 'Open GitHub'; $ui.BtnTokenSite.Height = 38; $ui.BtnTokenSite.FontSize = 13
$ui.BtnTokenSite.Padding = New-Object Windows.Thickness(16, 0, 16, 0); $ui.BtnTokenSite.Margin = New-Object Windows.Thickness(8, 0, 0, 0)
[Windows.Controls.Grid]::SetColumn($ui.BtnTokenSite, 1)
[void]$tokLine.Children.Add($ui.TokenBox); [void]$tokLine.Children.Add($ui.BtnTokenSite)
$ui.TokenCheck = New-Object Windows.Controls.TextBlock
$ui.TokenCheck.FontSize = 11.5; $ui.TokenCheck.Margin = New-Object Windows.Thickness(2, 6, 0, 0); $ui.TokenCheck.TextWrapping = 'Wrap'
[void]$tokRow.Children.Add($tokHelp); [void]$tokRow.Children.Add($tokLine); [void]$tokRow.Children.Add($ui.TokenCheck)
$ui.SetRows.Children.Insert($i, $tokRow); $i++

# Games in the library: a switch per game (on = shown), two columns, in the library's series order
Add-SetHeading 'GAMES IN THE LIBRARY' $i; $i++
$showHelp = New-Object Windows.Controls.TextBlock
$showHelp.TextWrapping = 'Wrap'; $showHelp.FontSize = 11.5; $showHelp.Foreground = Brush $Muted; $showHelp.Margin = New-Object Windows.Thickness(0, 0, 0, 6)
$showHelp.Text = 'Turn a game off to hide it from the library. Nothing is uninstalled: turn it back on any time.'
$ui.SetRows.Children.Insert($i, $showHelp); $i++
$showBtns = New-Object Windows.Controls.StackPanel; $showBtns.Orientation = 'Horizontal'; $showBtns.Margin = New-Object Windows.Thickness(0, 0, 0, 4)
foreach ($sb in @(@('Show all', 'all'), @('Only installed', 'installed'))) {
    $bt = New-Object Windows.Controls.Button
    $bt.Style = $W.FindResource('Btn'); $bt.Content = $sb[0]; $bt.Height = 32; $bt.FontSize = 12.5
    $bt.Padding = New-Object Windows.Thickness(14, 0, 14, 0); $bt.Margin = New-Object Windows.Thickness(0, 0, 8, 0); $bt.Tag = $sb[1]
    $bt.Add_Click({ param($s, $e)
        foreach ($g in $Games) { Set-Toggle $ShowToggles[$g.Id] $(if ($s.Tag -eq 'all') { $true } else { [bool](Get-Installed $g) }) }
    })
    [void]$showBtns.Children.Add($bt)
}
$ui.SetRows.Children.Insert($i, $showBtns); $i++
$showGrid = New-Object Windows.Controls.Primitives.UniformGrid; $showGrid.Columns = 2
$ShowToggles = @{}
foreach ($g in @($Games | Sort-Object @{ E = { $SeriesRank[(Series-Of $_)] } }, @{ E = { Game-Rank $_ } })) {
    $t = New-Toggle $g.Name $null
    $t.Root.Margin = New-Object Windows.Thickness(0, 4, 18, 4)
    $ShowToggles[$g.Id] = $t
    [void]$showGrid.Children.Add($t.Root)
}
$ui.SetRows.Children.Insert($i, $showGrid); $i++
Add-SetHeading 'GAME FOLDERS' $i

# ---- Controller in Settings: a list of stops (switches, buttons, the folders' Browse buttons) to move through ----
$script:setStops = @(); $script:setIdx = -1
$SetGlow = New-Object Windows.Media.Effects.DropShadowEffect
$SetGlow.Color = [Windows.Media.ColorConverter]::ConvertFromString('#3FB8A0'); $SetGlow.BlurRadius = 18; $SetGlow.ShadowDepth = 0; $SetGlow.Opacity = 1
function Get-SetStops {
    $s = New-Object System.Collections.ArrayList
    foreach ($t in $Opt.Values) { [void]$s.Add(@{ El = $t.Root; T = $t }) }
    foreach ($b in $showBtns.Children) { [void]$s.Add(@{ El = $b; Btn = $b }) }
    foreach ($c in $showGrid.Children) { [void]$s.Add(@{ El = $c; T = $c.Tag; Grid = $true }) }
    foreach ($r in $SetRowDefs) { [void]$s.Add(@{ El = $r.Btn; Row = $r }) }
    foreach ($b in $ui.BtnSetDefaults, $ui.BtnSetCancel, $ui.BtnSetSave) { [void]$s.Add(@{ El = $b; Btn = $b }) }
    @($s)
}
function Mark-SetStop($st, $on) {
    if (-not $st) { return }
    if ($st.T) { $st.El.Background = Brush $(if ($on) { '#203039' } else { '#00000000' }) }
    else { $st.El.Effect = if ($on) { $SetGlow } else { $null } }
}
function Select-SetStop($i) {
    $n = $script:setStops.Count; if ($n -eq 0) { return }
    $i = [Math]::Max(0, [Math]::Min($n - 1, $i))
    if ($script:setIdx -ge 0) { Mark-SetStop $script:setStops[$script:setIdx] $false }
    $script:setIdx = $i
    Mark-SetStop $script:setStops[$i] $true
    $script:setStops[$i].El.BringIntoView()
}
function Load-Options {
    Set-Toggle $Opt.CloseOnPlay ("$($Settings.CloseOnPlay)" -ne 'off') -Now
    Set-Toggle $Opt.Fullscreen ("$($Settings.Fullscreen)" -eq 'True') -Now
    Set-Toggle $Opt.Startup (Test-Path $StartupLink) -Now
    Set-Toggle $Opt.AutoCheck ("$($Settings.AutoCheck)" -ne 'off') -Now
    $ui.TokenBox.Password = [string]$Settings.GitHubToken
    $ui.TokenCheck.Text = if ($Settings.GitHubToken) { 'A token is saved.' } else { '' }
    $ui.TokenCheck.Foreground = Brush $Muted
    foreach ($g in $Games) { Set-Toggle $ShowToggles[$g.Id] (-not (Is-Hidden $g)) -Now }
}
function Save-Options {
    $Settings.CloseOnPlay = if ($Opt.CloseOnPlay.On) { 'on' } else { 'off' }
    $Settings.AutoCheck = if ($Opt.AutoCheck.On) { 'on' } else { 'off' }
    $wantFull = $Opt.Fullscreen.On
    $Settings.Fullscreen = if ($wantFull) { 'True' } else { '' }
    if ($wantFull -ne [bool]$script:isFull) { Set-Fullscreen $wantFull }
    $Settings.GitHubToken = $ui.TokenBox.Password.Trim()
    # Startup folder shortcut
    if ($Opt.Startup.On -and -not (Test-Path $StartupLink)) {
        try {
            $exe = Join-Path $Root 'Recomp Launcher.exe'
            $sc = (New-Object -ComObject WScript.Shell).CreateShortcut($StartupLink)
            $sc.TargetPath = $exe; $sc.WorkingDirectory = $Root; $sc.IconLocation = "$exe,0"; $sc.Description = 'Recomp Launcher'
            $sc.Save()
        } catch { Log "Couldn't add the launcher to the Startup folder: $($_.Exception.Message)" $Red }
    } elseif (-not $Opt.Startup.On -and (Test-Path $StartupLink)) { Remove-Item $StartupLink -Force -ErrorAction SilentlyContinue }
    $Settings.Hidden = (@($Games | Where-Object { -not $ShowToggles[$_.Id].On } | ForEach-Object { $_.Id }) -join ',')
    try { Save-Settings $Settings } catch {}
    Set-AutoCheck
    Update-Chips; Apply-Filter
}
# Updates every 3 hours while open (the launcher's own check, ignoring the 20-minute memory)
$autoTimer = New-Object Windows.Threading.DispatcherTimer
$autoTimer.Interval = [TimeSpan]::FromHours(3)
$autoTimer.Add_Tick({ if (-not $script:running -and -not $script:checking) { Check-All -Force } })
function Set-AutoCheck { if ("$($Settings.AutoCheck)" -ne 'off') { $autoTimer.Start() } else { $autoTimer.Stop() } }
# Asks GitHub how many checks an hour this token gets (background, so Settings doesn't wait)
function Test-Token {
    $tok = $ui.TokenBox.Password.Trim()
    if (-not $tok) { $ui.TokenCheck.Text = ''; return }
    $ui.TokenCheck.Text = 'Checking the token...'; $ui.TokenCheck.Foreground = Brush $Muted
    Start-Async {
        param($tok)
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        (Invoke-RestMethod 'https://api.github.com/rate_limit' -Headers @{ 'User-Agent' = 'Recomp-Launcher'; Authorization = "Bearer $tok" } -TimeoutSec 15).resources.core
    } $tok {
        param($tok, $res, $err)
        if ($res -and $res.limit -gt 60) {
            $ui.TokenCheck.Text = "$([char]0x2713)  The token works: {0:N0} checks an hour ({1:N0} left)." -f $res.limit, $res.remaining
            $ui.TokenCheck.Foreground = Brush $Teal
        } else {
            $ui.TokenCheck.Text = "GitHub didn't accept this token: check that it was copied whole, or make a new one."
            $ui.TokenCheck.Foreground = Brush $Red
        }
    }
}

# ---------- Uninstall ----------
# The saves and settings in each game's folder, kept (copied to the launcher's backups) when uninstalling
$SavePaths = @{
    totk = 'profile;totk.ini'; smo = 'save;powermoon.ini'; wwhd = 'data\save;data\user'; tod = 'gamedata;tod.ini'
    mp6 = 'save;config.json'; botw = 'save;savagewind.ini'
    las = 'save;saves;seeds;settings.ini;settings.json;config.json'
    iss = 'user'; spm = 'engine\UserData'; nsmbu = 'user;save;saves;settings.json;config.json'
    pksnap = 'saves'; pklgpe = 'save;saves;larecomp.ini'; pkswsh = 'save-sword;save-shield;larecomp.ini'
    pkla = 'save;saves;larecomp.ini'; pkstadium = 'saves;save'; pmd = 'saves;save;settings.json'
    mm2s = 'Save;2ship2harkinian.json'; oot = 'Save;shipofharkinian.json'; sm64 = 'Save;ghostship.json'; mk64 = 'Save;spaghettify.json'; sf64 = 'Save;starship.json'
    sms = 'SMS-Launcher-Portable\saves;SMS-Launcher-Portable\Save Backups;SMS-Launcher-Portable\preferences.json'
}
$ui.KeepSaves = New-Toggle 'Keep a backup of my saves and settings' ''
[void]$ui.UninstToggleHost.Children.Add($ui.KeepSaves.Root)
$script:uninstGame = $null
function Open-Uninstall($g) {
    $script:uninstGame = $g
    $ui.UninstIcon.Source = $g.Icon
    $ui.UninstTitle.Text = "Uninstall $($g.Name)?"
    $ui.UninstPath.Text = $g.Dir
    $ui.UninstSize.Text = 'Working out the size...'
    $saves = $SavePaths[$g.Id]
    $ui.KeepSaves.Root.Visibility = if ($saves) { 'Visible' } else { 'Collapsed' }
    Set-Toggle $ui.KeepSaves ([bool]$saves) -Now
    $ui.UninstNote.Text = if ($saves) {
        "Kept: $(($saves -split ';' | ForEach-Object { Split-Path $_ -Leaf }) -join ', '), copied to $($g.Backups) (the Backups button opens it). Play time is kept too."
    } elseif ($g.Id -eq 'mkwii') { 'This removes Wheel Wizard. Its data and the Mario Kart Wii it built are kept elsewhere (Wheel Wizard manages them): uninstall those in Wheel Wizard first if you want them gone.' }
    else { 'Play time is kept.' }
    $warn = if ($g.Dir -match '\\Quiver\\') { "This folder belongs to Quiver: Quiver will see the game as missing." } elseif ($g.Id -eq 'totk') { 'The TotK Updater and its backups stay; only the game folder goes.' } else { '' }
    $ui.UninstWarn.Text = $warn; $ui.UninstWarn.Visibility = if ($warn) { 'Visible' } else { 'Collapsed' }
    $ui.BtnUninstGo.IsEnabled = $true
    Start-Async {
        param($d)
        $s = (Get-ChildItem -LiteralPath $d -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum)
        @{ Bytes = [double]$s.Sum; Files = $s.Count }
    } $g.Dir {
        param($d, $res, $err)
        if ($res -and $script:uninstGame -and $script:uninstGame.Dir -eq $d) { $ui.UninstSize.Text = '{0:N1} GB in {1:N0} files' -f ($res.Bytes / 1GB), $res.Files }
    }
    $ui.UninstLayer.Visibility = 'Visible'
    $ui.UninstLayer.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 220 0))
    $tg = $ui.UninstCard.RenderTransform
    $tg.Children[0].BeginAnimation([Windows.Media.ScaleTransform]::ScaleXProperty, (Anim 1 380 0.94))
    $tg.Children[0].BeginAnimation([Windows.Media.ScaleTransform]::ScaleYProperty, (Anim 1 380 0.94))
    $tg.Children[1].BeginAnimation([Windows.Media.TranslateTransform]::YProperty, (Anim 0 380 18))
    Hide-Ring
    Set-Hints
}
function Close-Uninstall {
    $a = Anim 0 180 $null 'EaseIn'
    $a.Add_Completed({ $ui.UninstLayer.Visibility = 'Collapsed'; Set-Hints; Queue-Ring })
    $ui.UninstLayer.BeginAnimation([Windows.UIElement]::OpacityProperty, $a)
    $ui.UninstCard.RenderTransform.Children[1].BeginAnimation([Windows.Media.TranslateTransform]::YProperty, (Anim 12 180 $null 'EaseIn'))
}
function Confirm-Uninstall {
    $g = $script:uninstGame
    if (-not $g) { return }
    if (Get-Process -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -and $_.Path.StartsWith($g.Dir.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) } catch { $false } }) {
        [Windows.MessageBox]::Show($W, "Close $($g.Name) first: it's still running.", 'Recomp Launcher', 'OK', 'Information') | Out-Null
        return
    }
    Close-Uninstall
    Run-Jobs @(@{ G = $g; Job = 'uninstall'; KeepSaves = [bool]($ui.KeepSaves.On -and $SavePaths[$g.Id]) })
}

# ---------- Choose your game (before a first install) ----------
# What each game's setup reads, so the launcher can ask for it up front and check it. Kind: file, folder, both, or
# none (the setup asks for several files itself). Ext: accepted file types. FolderNeeds: sets of names that make a
# folder right (any one set, all its names inside). Handoff 'tod': the disc goes straight to tod_setup.exe; for the
# others the path is put on the clipboard to paste into the game's own file box.
$RomSpecs = @{
    totk  = @{ Kind = 'none'; Formats = @('.nsp', '.xci', '.nsz', '.xcz', 'prod.keys') }
    smo   = @{ Kind = 'folder'; Formats = @('Folder with exefs + romfs'); FolderNeeds = @(, @('exefs', 'romfs')); TitleId = '0100000000010000'
               Nsp = @{ Update = $false; Hint = 'Your Super Mario Odyssey NSP, version 1.0.0 (the base game, [v0], no update). hactool unpacks it with your keys: about 5 GB, a minute or two.' } }
    wwhd  = @{ Kind = 'both'; Formats = @('.wux', '.wud', '.wua', 'Folder with code + content + meta'); Ext = @('.wux', '.wud', '.wua')
               FolderNeeds = @(, @('code', 'content', 'meta')); Filter = 'Wii U game (*.wux;*.wud;*.wua)|*.wux;*.wud;*.wua' }
    tod   = @{ Kind = 'both'; Formats = @('.iso (decrypted)', 'Folder with PS3_GAME'); Ext = @('.iso')
               FolderNeeds = @(@('PS3_GAME'), @('USRDIR', 'PARAM.SFO')); Filter = 'PS3 disc image (*.iso)|*.iso'; Handoff = 'tod' }
    mp6   = @{ Kind = 'both'; Formats = @('.iso', '.gcm', '.rvz', '.ciso', 'Extracted disc folder'); Ext = @('.iso', '.gcm', '.rvz', '.ciso')
               FolderNeeds = @(@('sys', 'files'), @('files')); Filter = 'GameCube disc image (*.iso;*.gcm;*.rvz;*.ciso)|*.iso;*.gcm;*.rvz;*.ciso' }
    mkwii = @{ Kind = 'file'; Formats = @('.iso', '.wbfs', '.rvz', '.wia', '.gcz', '.ciso', '.gcm'); Ext = @('.iso', '.wbfs', '.rvz', '.wia', '.gcz', '.ciso', '.gcm')
               Filter = 'Wii disc image (*.iso;*.wbfs;*.rvz;*.wia;*.gcz;*.ciso;*.gcm)|*.iso;*.wbfs;*.rvz;*.wia;*.gcz;*.ciso;*.gcm' }
    sms   = @{ Kind = 'file'; Formats = @('.iso', '.gcm', '.ciso'); Ext = @('.iso', '.gcm', '.ciso')
               Filter = 'GameCube disc image (*.iso;*.gcm;*.ciso)|*.iso;*.gcm;*.ciso' }
    botw  = @{ Kind = 'folder'; Formats = @('Folder with exefs + romfs'); FolderNeeds = @(, @('exefs', 'romfs')); TitleId = '01007EF00011E000'
               # the 1.9.0 update is applied to the base game while converting (nstool in nsp-worker.ps1)
               Nsp = @{ Update = $true; UpdateId = '01007EF00011E800'; Hint = 'Your Breath of the Wild base game NSP and its version 1.9.0 update NSP. nstool applies the update and unpacks both with your keys: needs about twice the size of both NSPs free while it works, and several minutes.' } }
    acit  = @{ Kind = 'both'; Formats = @('.iso (decrypted)', 'Folder with PS3_GAME'); Ext = @('.iso')
               FolderNeeds = @(@('PS3_GAME'), @('USRDIR', 'PARAM.SFO')); Filter = 'PS3 disc image (*.iso)|*.iso' }
    las   = @{ Kind = 'folder'; Formats = @('Folder with exefs + romfs'); FolderNeeds = @(, @('exefs', 'romfs')); TitleId = '01006BB00C6F0000'
               Nsp = @{ Update = $false; Hint = "Your Link's Awakening NSP: the base game (1.0.0) works as it is. hactool unpacks it with your keys, in a minute or two." } }
    iss   = @{ Kind = 'folder'; Formats = @('PS4 game folder (1.00 + update 1.07, with eboot.bin)'); FolderNeeds = @(, @('eboot.bin', 'sce_sys')) }
    smg   = @{ Kind = 'both'; Formats = @('.iso', '.rvz', 'Extracted disc folder'); Ext = @('.iso', '.rvz')
               FolderNeeds = @(@('sys', 'files'), @('DATA')); Filter = 'Wii disc image (*.iso;*.rvz)|*.iso;*.rvz' }
    spm   = @{ Kind = 'file'; Formats = @('.iso', '.ciso', '.wbfs'); Ext = @('.iso', '.ciso', '.wbfs')
               Filter = 'Wii disc image (*.iso;*.ciso;*.wbfs)|*.iso;*.ciso;*.wbfs' }
    tp    = @{ Kind = 'file'; Formats = @('.iso', '.rvz'); Ext = @('.iso', '.rvz', '.gcm')
               Filter = 'GameCube / Wii disc image (*.iso;*.rvz;*.gcm)|*.iso;*.rvz;*.gcm' }
    nsmbw = @{ Kind = 'folder'; Formats = @('Disc folder extracted with Dolphin'); FolderNeeds = @(@('sys', 'files'), @('DATA')) }
    nsmbu = @{ Kind = 'both'; Formats = @('.wux / .wud (with its keys)', 'Folder with code, content, meta'); Ext = @('.wux', '.wud')
               FolderNeeds = @(, @('code', 'content', 'meta')); Filter = 'Wii U disc image (*.wux;*.wud)|*.wux;*.wud' }
    pkgen = @{ Kind = 'file'; Formats = @('.gb', '.gbc', '.gba', '.nds'); Ext = @('.gb', '.gbc', '.gba', '.nds')
               Filter = 'Cartridge dump (*.gb;*.gbc;*.gba;*.nds)|*.gb;*.gbc;*.gba;*.nds' }
    pmd   = @{ Kind = 'file'; Formats = @('.gba'); Ext = @('.gba'); Filter = 'GBA cartridge dump (*.gba)|*.gba' }
    pkblack = @{ Kind = 'file'; Formats = @('.nds'); Ext = @('.nds'); Filter = 'DS cartridge dump (*.nds)|*.nds' }
    pkss  = @{ Kind = 'file'; Formats = @('.nds'); Ext = @('.nds'); Filter = 'DS cartridge dump (*.nds)|*.nds' }
    pkstadium = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    pksnap = @{ Kind = 'file'; Formats = @('.z64'); Ext = @('.z64'); Filter = 'N64 ROM (*.z64)|*.z64'; CopyAs = 'pokemonsnap.z64' }
    pklgpe = @{ Kind = 'folder'; Formats = @('Folder with exefs + romfs'); FolderNeeds = @(, @('exefs', 'romfs')) }
    pkswsh = @{ Kind = 'folder'; Formats = @('Folder with exefs + romfs'); FolderNeeds = @(, @('exefs', 'romfs')) }
    pkla  = @{ Kind = 'folder'; Formats = @('Folder with exefs + romfs'); FolderNeeds = @(, @('exefs', 'romfs')); CopyIn = @('exefs', 'romfs') }
    pokopia = @{ Kind = 'none'; Formats = @('Extracted game folder', '.nsp / .xci + your keys') }
    mm = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    mm2s = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    oot = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    sm64 = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    mk64 = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    sf64 = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    bk = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    dk64 = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    conker = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    sonic = @{ Kind = 'none'; Formats = @('Xbox 360 game files', 'Title update', 'DLC (optional)') }
    thps1 = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    thps2 = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    thps3 = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    nomercy = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    wm2k = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    wcwwt = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    revenge = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
    vpw64 = @{ Kind = 'file'; Formats = @('.z64', '.n64', '.v64'); Ext = @('.z64', '.n64', '.v64'); Filter = 'N64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64' }
}
# The games picked before, offered again next time (roms.json next to the launcher)
$RomsPath = Join-Path $Root 'roms.json'
function Load-Roms { $h = @{}; if (Test-Path $RomsPath) { try { (Get-Content $RomsPath -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $h[$_.Name] = [string]$_.Value } } catch {} }; $h }
function Save-Roms($h) { try { [pscustomobject]$h | ConvertTo-Json | Set-Content -Encoding UTF8 $RomsPath } catch {} }

# A Switch game's title ID, from exefs\main.npdm (the program ID in its ACI0 section)
function Get-NpdmTitle($dir) {
    $p = Join-Path $dir 'exefs\main.npdm'
    if (-not (Test-Path -LiteralPath $p)) { return $null }
    try {
        $b = [IO.File]::ReadAllBytes($p)
        $i = [Text.Encoding]::ASCII.GetString($b).IndexOf('ACI0')
        if ($i -lt 0 -or $i + 0x18 -gt $b.Length) { return $null }
        '{0:X16}' -f [BitConverter]::ToUInt64($b, $i + 0x10)
    } catch { $null }
}
# Whether a folder holds this game: its names are there, and for a Switch game its title ID matches
function Test-RomFolder($spec, $dir) {
    foreach ($set in $spec.FolderNeeds) {
        if (@($set | Where-Object { -not (Test-Path -LiteralPath (Join-Path $dir $_)) }).Count -eq 0) {
            if ($spec.TitleId) {
                $id = Get-NpdmTitle $dir
                if ($id -and $id -ne $spec.TitleId) { return @{ Match = $false; Other = $id } }
            }
            return @{ Match = $true; Set = $set }
        }
    }
    @{ Match = $false }
}
# Checks a picked file or folder against the game's spec: Ok (it can be used), Text (what to show), Color
function Test-Rom($spec, $path) {
    $path = "$path".Trim().Trim('"')
    if (-not $path) { return @{ Ok = $false; Text = 'Choose your game file or folder.'; Color = $Muted } }
    if (-not (Test-Path -LiteralPath $path)) { return @{ Ok = $false; Text = "That file or folder doesn't exist."; Color = $Red } }
    $item = Get-Item -LiteralPath $path
    if (-not $item.PSIsContainer) {
        if ($spec.Kind -eq 'folder') { return @{ Ok = $false; Text = "This game needs a folder, not a file: $($spec.Formats -join ', ')."; Color = $Red } }
        $ext = $item.Extension.ToLower()
        if ($ext -notin $spec.Ext) { return @{ Ok = $false; Text = "$ext files aren't accepted. Use $($spec.Ext -join ', ')$(if ($spec.Kind -eq 'both') { ' or a game folder' })."; Color = $Red } }
        $size = '{0:N1} GB' -f ($item.Length / 1GB)
        if ($ext -in '.wux', '.wud' -and -not (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($item.FullName, '.key')))) {
            return @{ Ok = $true; Text = "$([char]0x2713)  $($item.Name) ($size). Its setup also asks for the disc key (put it next to the image as $($item.BaseName).key to have it found) and the Wii U common key."; Color = $Gold }
        }
        return @{ Ok = $true; Text = "$([char]0x2713)  $($item.Name) ($size)"; Color = $Teal }
    }
    if ($spec.Kind -eq 'file') { return @{ Ok = $false; Text = "This game needs a disc image file: $($spec.Ext -join ', ')."; Color = $Red } }
    $here = Test-RomFolder $spec $path
    if ($here.Match) {
        $idText = if ($spec.TitleId) { ", title ID $($spec.TitleId)" } else { '' }
        return @{ Ok = $true; Text = "$([char]0x2713)  Folder with $($here.Set -join ', ')$idText"; Color = $Teal }
    }
    if ($here.Other) { return @{ Ok = $false; Text = "This is a different game (title ID $($here.Other)); this one is $($spec.TitleId)."; Color = $Red } }
    # a folder one level up from the right one: say which folder inside to use
    foreach ($sub in Get-ChildItem -LiteralPath $path -Directory -ErrorAction SilentlyContinue) {
        if ((Test-RomFolder $spec $sub.FullName).Match) {
            return @{ Ok = $false; Text = "The game is one folder deeper, in `"$($sub.Name)`". Choose that folder."; Color = $Gold; Better = $sub.FullName }
        }
    }
    $want = ($spec.FolderNeeds | ForEach-Object { $_ -join ' + ' }) -join ', or '
    @{ Ok = $false; Text = "This folder doesn't have the game in it: it needs $want."; Color = $Red }
}

# ---- NSP to exefs + romfs ----
# prod.keys: the one used last time, else the usual places Switch emulators keep it
function Find-Keys {
    $roms = Load-Roms
    $c = @($roms['_keys'], "$env:SystemDrive\Eden\prod.keys", "$env:APPDATA\eden\keys\prod.keys", "$env:APPDATA\yuzu\keys\prod.keys",
        "$env:APPDATA\suyu\keys\prod.keys", "$env:APPDATA\citron\keys\prod.keys", "$env:APPDATA\sudachi\keys\prod.keys",
        "$env:APPDATA\Ryujinx\system\prod.keys", "$env:USERPROFILE\.switch\prod.keys")
    foreach ($p in $c) { if ($p -and (Test-Path -LiteralPath $p)) { return $p } }
    ''
}
# Whether a file really is an NSP: every NSP starts with "PFS0". Returns why not (archives renamed to .nsp are
# common in downloads), or nothing when it is one.
function Test-NspFile($path) {
    try {
        $fs = [IO.File]::OpenRead($path); $b = New-Object byte[] 8; [void]$fs.Read($b, 0, 8); $fs.Close()
    } catch { return "can't be read: $($_.Exception.Message)" }
    $magic = [Text.Encoding]::ASCII.GetString($b, 0, 4)
    if ($magic -eq 'PFS0') { return $null }
    $kind = if ($b[0] -eq 0x37 -and $b[1] -eq 0x7A -and $b[2] -eq 0xBC -and $b[3] -eq 0xAF) { '7-Zip archive' }
            elseif ($magic -eq "PK$([char]3)$([char]4)") { 'ZIP archive' }
            elseif ($magic -eq 'Rar!') { 'RAR archive' }
            elseif ($magic -eq 'NCZS' -or $magic -eq 'NCZB') { 'NSZ (compressed NSP)' }
            else { $null }
    if ($kind -eq '7-Zip archive') {
        # a 7z start header says how long the archive is (its table of contents sits at the end)
        try {
            $fs = [IO.File]::OpenRead($path); $h = New-Object byte[] 32; [void]$fs.Read($h, 0, 32); $len = $fs.Length; $fs.Close()
            $want = 32 + [BitConverter]::ToUInt64($h, 12) + [BitConverter]::ToUInt64($h, 20)
            if ($len -lt $want) { return ('is a 7-Zip archive renamed to .nsp, and an incomplete download: {0:N0} of {1:N0} MB. Download it again, then unpack it and choose the .nsp inside.' -f ($len / 1MB), ($want / 1MB)) }
        } catch {}
    }
    if ($kind) { "is a $kind renamed to .nsp, not an NSP. Unpack it first (with 7-Zip, for example) and choose the .nsp that comes out." }
    else { "isn't an NSP (an NSP starts with PFS0)." }
}
# title.keys (title keys the emulator collected) next to prod.keys, if there is one
function Get-TitleKeys($keys) {
    if (-not $keys) { return '' }
    $p = Join-Path (Split-Path $keys -Parent) 'title.keys'
    if (Test-Path -LiteralPath $p) { $p } else { '' }
}
function Show-NspPanel($on) {
    $ui.NspPanel.Visibility = if ($on) { 'Visible' } else { 'Collapsed' }
    $ui.NspToggleIcon.Text = if ($on) { [string][char]0xE70E } else { [string][char]0xE70D }
    if ($on) { Check-Nsp }
}
# What the file name says about an NSP: the title ID and version in brackets, if it has them
function Nsp-Name($path) {
    $n = [IO.Path]::GetFileName($path)
    @{ Id = $(if ($n -match '\[(0100[0-9A-Fa-f]{12})\]') { $Matches[1].ToUpper() }); Ver = $(if ($n -match '\[v(\d+)\]') { [int]$Matches[1] }) }
}
function Check-Nsp {
    $g = $script:romGame; if (-not $g) { return }
    $nsp = $RomSpecs[$g.Id].Nsp; if (-not $nsp) { return }
    $base = $ui.NspBase.Text.Trim().Trim('"'); $upd = $ui.NspUpdate.Text.Trim().Trim('"'); $keys = $ui.NspKeys.Text.Trim().Trim('"'); $out = $ui.NspOut.Text.Trim().Trim('"')
    $bad = $null; $warn = $null
    if (-not $base) { $bad = 'Choose the base game NSP.' }
    elseif (-not (Test-Path -LiteralPath $base)) { $bad = "The base game NSP doesn't exist." }
    elseif ([IO.Path]::GetExtension($base) -ne '.nsp') { $bad = 'Only .nsp works here (not .nsz or .xci).' }
    elseif ($why = Test-NspFile $base) { $bad = "The base game file $why" }
    elseif ($nsp.Update -and -not $upd) { $bad = 'Choose the update NSP too.' }
    elseif ($nsp.Update -and -not (Test-Path -LiteralPath $upd)) { $bad = "The update NSP doesn't exist." }
    elseif ($nsp.Update -and ($why = Test-NspFile $upd)) { $bad = "The update file $why" }
    elseif (-not $keys -or -not (Test-Path -LiteralPath $keys)) { $bad = 'Choose your prod.keys.' }
    elseif (-not $out) { $bad = 'Choose where to save the exefs and romfs.' }
    elseif ($out.Length -gt 80) { $bad = "That folder's path is too long for Windows ($($out.Length) characters, 80 at most): choose a shorter one, such as C:\Games\$($g.Id)." }
    else {
        $bn = Nsp-Name $base
        if ($bn.Id -and $bn.Id -ne $RomSpecs[$g.Id].TitleId) { $warn = "The base NSP's name says [$($bn.Id)]; $($g.Name) is [$($RomSpecs[$g.Id].TitleId)]." }
        elseif ($bn.Ver) { $warn = "The base NSP's name says [v$($bn.Ver)]: that looks like an update, not the base game." }
        if ($nsp.Update -and -not $warn) {
            $un = Nsp-Name $upd
            if ($un.Id -and $un.Id -ne $nsp.UpdateId) { $warn = "The update NSP's name says [$($un.Id)]; the $($g.Name) update is [$($nsp.UpdateId)]." }
        }
    }
    $ui.BtnNspConvert.IsEnabled = -not $bad
    $tkNote = if (-not $bad -and (Get-TitleKeys $keys)) { ', with title.keys from the same folder' } else { '' }
    $ui.NspCheck.Text = if ($bad) { $bad } elseif ($warn) { $warn } else { "$([char]0x2713)  Ready$tkNote" }
    $ui.NspCheck.Foreground = Brush $(if ($bad) { $Muted } elseif ($warn) { $Gold } else { $Teal })
}
function Pick-Into($box, $title, $filter) {
    $d = New-Object System.Windows.Forms.OpenFileDialog
    $d.Title = $title; $d.Filter = "$filter|All files (*.*)|*.*"
    $cur = $box.Text.Trim('"')
    if ($cur -and (Test-Path -LiteralPath $cur)) { $d.InitialDirectory = Split-Path $cur }
    if ($d.ShowDialog() -eq 'OK') { $box.Text = $d.FileName }
}
function Start-Convert {
    $g = $script:romGame
    $job = @{ G = $g; Job = 'convert'; Nsp = @{
        Base = $ui.NspBase.Text.Trim().Trim('"'); Update = $(if ($RomSpecs[$g.Id].Nsp.Update) { $ui.NspUpdate.Text.Trim().Trim('"') } else { '' })
        Keys = $ui.NspKeys.Text.Trim().Trim('"'); Out = $ui.NspOut.Text.Trim().Trim('"') } }
    $job.Nsp.TitleKeys = Get-TitleKeys $job.Nsp.Keys
    $roms = Load-Roms; $roms['_keys'] = $job.Nsp.Keys; Save-Roms $roms
    Close-RomPicker
    Run-Jobs @($job)
}

$script:romGame = $null
function Open-RomPicker($g) {
    $spec = $RomSpecs[$g.Id]
    $script:romGame = $g
    $ui.RomGame.Text = "$($g.Name.ToUpper())  $Bullet  $($g.Latest.Tag)"; $ui.RomGame.Foreground = Brush $g.Accent
    $ui.RomIcon.Source = $g.Icon
    $ui.RomTitle.Text = if ($spec.Kind -eq 'none') { 'Before you install' } else { 'Choose your game' }
    $ui.RomNeeds.Text = $g.Needs
    $ui.RomFormats.Children.Clear()
    foreach ($f in $spec.Formats) {
        $b = New-Object Windows.Controls.Border
        $b.CornerRadius = New-Object Windows.CornerRadius(8); $b.Padding = New-Object Windows.Thickness(10, 4, 10, 5)
        $b.Margin = New-Object Windows.Thickness(0, 0, 6, 6)
        $b.Background = New-Object Windows.Media.SolidColorBrush((WithAlpha $g.Accent 0x2E))
        $b.BorderBrush = New-Object Windows.Media.SolidColorBrush((WithAlpha $g.Accent 0x80)); $b.BorderThickness = New-Object Windows.Thickness(1)
        $t = New-Object Windows.Controls.TextBlock
        $t.Text = if ($f -like '.*') { $f.ToUpper() } else { $f }
        $t.FontSize = 12; $t.FontFamily = New-Object Windows.Media.FontFamily('Segoe UI Variable Display Semibold, Segoe UI Semibold')
        $b.Child = $t
        [void]$ui.RomFormats.Children.Add($b)
    }
    $pick = $spec.Kind -ne 'none'
    foreach ($el in $ui.RomPickLabel, $ui.RomPickRow, $ui.RomCheck, $ui.BtnRomSkip) { $el.Visibility = if ($pick) { 'Visible' } else { 'Collapsed' } }
    $ui.BtnRomFile.Visibility = if ($spec.Kind -in 'file', 'both') { 'Visible' } else { 'Collapsed' }
    $ui.BtnRomFolder.Visibility = if ($spec.Kind -in 'folder', 'both') { 'Visible' } else { 'Collapsed' }
    $ui.RomWhere.Text = if ($g.Id -eq 'totk') {
        "Install downloads the TotK setup and opens it: choose your files there, and $($g.Dir) as the install folder (or change it in Settings)."
    } else {
        $how = if ($spec.Handoff -eq 'tod') { 'Your disc then goes straight to its installer.' } else { "Then the game's setup opens: paste the path there with Ctrl+V (it's copied for you)." }
        "Installs into $($g.Dir) (change it in Settings). The download is checked against its SHA-256 first. $how"
    }
    $roms = Load-Roms
    $ui.RomPath.Text = if ($roms[$g.Id]) { $roms[$g.Id] } else { '' }
    Check-RomPath
    # Switch games: the NSP converter, folded away until asked for
    $nsp = $spec.Nsp
    $ui.BtnNspToggle.Visibility = if ($nsp) { 'Visible' } else { 'Collapsed' }
    if ($nsp) {
        $ui.NspHint.Text = $nsp.Hint
        $ui.NspUpdateRow.Visibility = if ($nsp.Update) { 'Visible' } else { 'Collapsed' }
        $ui.NspUpdateLabel.Text = "UPDATE (.NSP)$(if ($nsp.UpdateId) { "  $Bullet  [$($nsp.UpdateId)]" })"
        if (-not $ui.NspKeys.Text) { $ui.NspKeys.Text = Find-Keys }
        $ui.NspOut.Text = Join-Path $Root "dumps\$($g.Id)"
    }
    Show-NspPanel $false
    if ($ui.Fb) { $ui.Fb.Visibility = 'Collapsed' }
    $ui.RomLayer.Visibility = 'Visible'
    $ui.RomLayer.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 220 0))
    $tg = $ui.RomCard.RenderTransform
    $tg.Children[0].BeginAnimation([Windows.Media.ScaleTransform]::ScaleXProperty, (Anim 1 380 0.94))
    $tg.Children[0].BeginAnimation([Windows.Media.ScaleTransform]::ScaleYProperty, (Anim 1 380 0.94))
    $tg.Children[1].BeginAnimation([Windows.Media.TranslateTransform]::YProperty, (Anim 0 380 18))
    Hide-Ring
    Set-Hints
}
function Close-RomPicker {
    $a = Anim 0 180 $null 'EaseIn'
    $a.Add_Completed({ $ui.RomLayer.Visibility = 'Collapsed'; Set-Hints; Queue-Ring })
    $ui.RomLayer.BeginAnimation([Windows.UIElement]::OpacityProperty, $a)
    $ui.RomCard.RenderTransform.Children[1].BeginAnimation([Windows.Media.TranslateTransform]::YProperty, (Anim 12 180 $null 'EaseIn'))
}
function Check-RomPath {
    $g = $script:romGame
    if (-not $g) { return }
    $spec = $RomSpecs[$g.Id]
    if ($spec.Kind -eq 'none') { $ui.BtnRomInstall.IsEnabled = $true; return }
    $r = Test-Rom $spec $ui.RomPath.Text
    $ui.RomCheck.Text = $r.Text; $ui.RomCheck.Foreground = Brush $r.Color
    $ui.BtnRomInstall.IsEnabled = $r.Ok
    $script:romBetter = $r.Better
}
function Pick-RomFile {
    if ($script:padUsed) { Open-Fb; return }   # a controller can't use Windows' dialog: the launcher's own browser
    $spec = $RomSpecs[$script:romGame.Id]
    $d = New-Object System.Windows.Forms.OpenFileDialog
    $d.Title = "Choose your $($script:romGame.Name) disc image"
    $d.Filter = "$($spec.Filter)|All files (*.*)|*.*"
    $cur = $ui.RomPath.Text.Trim('"')
    if ($cur -and (Test-Path -LiteralPath $cur)) { $d.InitialDirectory = $(if ((Get-Item -LiteralPath $cur).PSIsContainer) { $cur } else { Split-Path $cur }) }
    if ($d.ShowDialog() -eq 'OK') { $ui.RomPath.Text = $d.FileName }
}
function Pick-RomFolder {
    if ($script:padUsed) { Open-Fb; return }
    $spec = $RomSpecs[$script:romGame.Id]
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = "Choose the $($script:romGame.Name) folder: $(($spec.Formats | Where-Object { $_ -notlike '.*' }) -join ', ')"
    $cur = $ui.RomPath.Text.Trim('"')
    if ($cur -and (Test-Path -LiteralPath $cur)) { $d.SelectedPath = $(if ((Get-Item -LiteralPath $cur).PSIsContainer) { $cur } else { Split-Path $cur }) }
    if ($d.ShowDialog() -eq 'OK') {
        $ui.RomPath.Text = $d.SelectedPath
        if ($script:romBetter) { $ui.RomPath.Text = $script:romBetter }   # the game is one folder deeper
    }
}
# ---------- File browser (inside the launcher, for a controller) ----------
# Windows' own file dialogs can't be used with a controller, so a controller (or keyboard) opens this one instead:
# This PC (recent game folders, Downloads, Desktop, Documents, every drive), then folders and the files the game
# accepts. Up / Down move, A opens or chooses, B or Left goes back, X uses the folder you're in, Y closes.
$fbXaml = @'
<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      Visibility="Collapsed">
  <Rectangle x:Name="FbDim" Fill="#B0050709"/>
  <Border Width="780" Height="580" VerticalAlignment="Center" HorizontalAlignment="Center" CornerRadius="18"
          Background="#11181D" BorderBrush="#26333C" BorderThickness="1" Padding="28,24,28,20">
    <Border.Effect><DropShadowEffect BlurRadius="60" ShadowDepth="0" Opacity="0.7" Color="Black"/></Border.Effect>
    <DockPanel>
      <StackPanel DockPanel.Dock="Top">
        <TextBlock x:Name="FbKind" FontSize="10.5" FontWeight="Bold" Foreground="#3FB8A0"/>
        <TextBlock x:Name="FbTitle" FontSize="22" Margin="0,2,0,0" Foreground="#F2EFE6"
                   FontFamily="Segoe UI Variable Display Semibold, Segoe UI Semibold"/>
        <Border Background="#0A0E11" CornerRadius="8" Padding="12,8" Margin="0,12,0,0" BorderBrush="#26333C" BorderThickness="1">
          <StackPanel Orientation="Horizontal">
            <TextBlock Text="&#xED25;" FontFamily="Segoe MDL2 Assets" FontSize="13" Foreground="#8A9AA5" VerticalAlignment="Center"/>
            <TextBlock x:Name="FbPath" FontSize="13" Foreground="#C9CCC8" Margin="10,0,0,0" TextTrimming="CharacterEllipsis" MaxWidth="660"/>
          </StackPanel>
        </Border>
        <TextBlock x:Name="FbCheck" FontSize="12.5" Margin="2,8,0,8" TextWrapping="Wrap"/>
      </StackPanel>
      <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
        <Button x:Name="FbUse" Height="40" Padding="18,0" Margin="0,0,8,0" FontSize="13" Content="Use this folder"/>
        <Button x:Name="FbCancel" Height="40" Padding="18,0" FontSize="13" Content="Cancel"/>
      </StackPanel>
      <ScrollViewer x:Name="FbScroll" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Focusable="False">
        <StackPanel x:Name="FbList"/>
      </ScrollViewer>
    </DockPanel>
  </Border>
</Grid>
'@
$ui.Fb = [Windows.Markup.XamlReader]::Parse($fbXaml)
foreach ($n in 'FbDim', 'FbKind', 'FbTitle', 'FbPath', 'FbCheck', 'FbUse', 'FbCancel', 'FbScroll', 'FbList') { $ui[$n] = $ui.Fb.FindName($n) }
$ui.FbUse.Style = $W.FindResource('Btn'); $ui.FbUse.Background = Brush '#3FB8A0'; $ui.FbUse.Foreground = Brush '#0A0E11'
$ui.FbCancel.Style = $W.FindResource('Btn')
[void]$ui.RomLayer.Children.Add($ui.Fb)
$script:fb = @{ Dir = $null; Items = @(); Rows = @(); Idx = 0 }

function Fb-Visible { $ui.Fb.Visibility -eq 'Visible' }
# the browser serves the game picker ('rom') and the game folders in Settings ('setfolder': folders only)
$script:fbMode = 'rom'; $script:fbRow = $null
function Fb-Files { $script:fbMode -eq 'rom' -and $RomSpecs[$script:romGame.Id].Kind -in 'file', 'both' }
function Fb-Folders { $script:fbMode -eq 'setfolder' -or $RomSpecs[$script:romGame.Id].Kind -in 'folder', 'both' }
function Fb-Accent { if ($script:fbMode -eq 'rom') { $script:romGame.Accent } else { '#3FB8A0' } }
# the browser sits in whichever panel opened it
function Move-Fb($layer) {
    if ($ui.Fb.Parent -ne $layer) { $ui.Fb.Parent.Children.Remove($ui.Fb); [void]$layer.Children.Add($ui.Fb) }
}
# Settings: a game folder ($r: its row), chosen with the browser
function Open-FbFolder($r) {
    $script:fbMode = 'setfolder'; $script:fbRow = $r
    Move-Fb $ui.SettingsLayer
    $ui.FbKind.Text = "SETTINGS  $Bullet  $($r.Label.ToUpper())"
    $ui.FbTitle.Text = 'Choose the game folder'
    $ui.FbUse.Visibility = 'Visible'
    $cur = $r.Box.Text.Trim().Trim('"').TrimEnd('\'); $start = $null; $pick = $null
    # start next to the folder now set (it may not exist yet: then where it would be)
    while ($cur -and -not (Test-Path -LiteralPath $cur)) { $cur = Split-Path $cur }
    if ($cur) { $start = Split-Path $cur; $pick = Split-Path $cur -Leaf; if (-not $start) { $start = $cur; $pick = $null } }
    $ui.Fb.Visibility = 'Visible'
    $ui.Fb.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 200 0))
    Load-Fb $start $pick
    Set-Hints
}
function Open-Fb {
    $script:fbMode = 'rom'
    Move-Fb $ui.RomLayer
    $g = $script:romGame; $spec = $RomSpecs[$g.Id]
    $ui.FbKind.Text = "$($g.Name.ToUpper())  $Bullet  $(($spec.Formats) -join ', ')"
    $ui.FbTitle.Text = if (Fb-Files) { 'Choose your game file' } else { 'Choose your game folder' }
    $ui.FbUse.Visibility = if (Fb-Folders) { 'Visible' } else { 'Collapsed' }
    # start where the game was picked before, or at This PC
    $cur = $ui.RomPath.Text.Trim().Trim('"'); $start = $null
    if ($cur -and (Test-Path -LiteralPath $cur)) { $start = if ((Get-Item -LiteralPath $cur).PSIsContainer) { Split-Path $cur } else { Split-Path $cur } }
    $ui.Fb.Visibility = 'Visible'
    $ui.Fb.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 200 0))
    Load-Fb $start $(if ($cur) { Split-Path $cur -Leaf })
    Set-Hints
}
function Close-Fb { $ui.Fb.Visibility = 'Collapsed'; Set-Hints }
# One row: an icon, the name, and a note on the right
function New-FbRow($it) {
    $b = New-Object Windows.Controls.Border
    $b.CornerRadius = New-Object Windows.CornerRadius(8); $b.Padding = New-Object Windows.Thickness(12, 7, 12, 7); $b.Margin = New-Object Windows.Thickness(0, 0, 6, 2)
    $b.Background = Brush '#00000000'; $b.BorderThickness = New-Object Windows.Thickness(1); $b.BorderBrush = Brush '#00000000'; $b.Cursor = 'Hand'
    $gr = New-Object Windows.Controls.Grid
    foreach ($w in @('Auto', '*', 'Auto')) { $c = New-Object Windows.Controls.ColumnDefinition; $c.Width = $(if ($w -eq '*') { New-Object Windows.GridLength(1, 'Star') } else { [Windows.GridLength]::Auto }); $gr.ColumnDefinitions.Add($c) }
    $ic = New-Object Windows.Controls.TextBlock
    $ic.FontFamily = New-Object Windows.Media.FontFamily('Segoe MDL2 Assets'); $ic.FontSize = 15; $ic.Width = 26; $ic.VerticalAlignment = 'Center'
    $ic.Text = [string][char]$(switch ($it.Kind) { 'up' { 0xE74A } 'drive' { 0xEDA2 } 'recent' { 0xE81C } 'place' { 0xED25 } 'dir' { 0xED25 } default { 0xE958 } })
    $ic.Foreground = Brush $(switch ($it.Kind) { { $_ -in 'file', 'nsp' } { Fb-Accent } 'up' { '#8A9AA5' } default { '#D9B45A' } })
    $nm = New-Object Windows.Controls.TextBlock
    $nm.Text = $it.Name; $nm.FontSize = 13.5; $nm.Foreground = Brush '#F2EFE6'; $nm.TextTrimming = 'CharacterEllipsis'; $nm.VerticalAlignment = 'Center'
    if ($it.Kind -in 'file', 'nsp') { $nm.FontFamily = New-Object Windows.Media.FontFamily('Segoe UI Variable Display Semibold, Segoe UI Semibold') }
    [Windows.Controls.Grid]::SetColumn($nm, 1)
    $dt = New-Object Windows.Controls.TextBlock
    $dt.Text = $it.Detail; $dt.FontSize = 11.5; $dt.Foreground = Brush $Muted; $dt.VerticalAlignment = 'Center'; $dt.Margin = New-Object Windows.Thickness(12, 0, 0, 0)
    [Windows.Controls.Grid]::SetColumn($dt, 2)
    [void]$gr.Children.Add($ic); [void]$gr.Children.Add($nm); [void]$gr.Children.Add($dt)
    $b.Child = $gr
    $b.Tag = $it
    $b.Add_MouseLeftButtonDown({ param($s, $e)
        $i = [array]::IndexOf($script:fb.Items, $s.Tag)
        Select-Fb $i
        if ($e.ClickCount -ge 2) { Enter-Fb }
        $e.Handled = $true
    })
    $b
}
function Format-Size($n) { if ($n -ge 1GB) { '{0:N1} GB' -f ($n / 1GB) } elseif ($n -ge 1MB) { '{0:N0} MB' -f ($n / 1MB) } else { '{0:N0} KB' -f ($n / 1KB) } }
# Fills the list for a folder ($dir), or This PC ($null); $pick: the name to put the cursor on
function Load-Fb($dir, $pick) {
    $spec = if ($script:fbMode -eq 'rom') { $RomSpecs[$script:romGame.Id] } else { @{ Kind = 'folder' } }
    $items = New-Object System.Collections.ArrayList
    $note = ''; $noteColor = $Muted
    if (-not $dir) {
        $seen = @{}
        $roms = Load-Roms
        foreach ($v in $roms.Values) {
            if (-not $v -or $v -like '*.keys') { continue }
            $p = if (Test-Path -LiteralPath $v -PathType Leaf) { Split-Path $v } elseif (Test-Path -LiteralPath $v) { Split-Path $v } else { $null }
            if ($p -and -not $seen[$p] -and (Test-Path -LiteralPath $p)) { $seen[$p] = 1; [void]$items.Add(@{ Kind = 'recent'; Name = $p; Path = $p; Detail = 'used before' }) }
        }
        foreach ($pl in @(@('Downloads', (Join-Path $env:USERPROFILE 'Downloads')), @('Desktop', [Environment]::GetFolderPath('Desktop')), @('Documents', [Environment]::GetFolderPath('MyDocuments')))) {
            if (Test-Path -LiteralPath $pl[1]) { [void]$items.Add(@{ Kind = 'place'; Name = $pl[0]; Path = $pl[1]; Detail = '' }) }
        }
        foreach ($d in [IO.DriveInfo]::GetDrives()) {
            if (-not $d.IsReady) { continue }
            $label = if ($d.VolumeLabel) { "$($d.Name)  $($d.VolumeLabel)" } else { $d.Name }
            [void]$items.Add(@{ Kind = 'drive'; Name = $label; Path = $d.RootDirectory.FullName; Detail = "$(Format-Size $d.AvailableFreeSpace) free" })
        }
        $ui.FbPath.Text = 'This PC'
        $note = 'Pick a drive or folder.'
    } else {
        $parent = Split-Path $dir
        [void]$items.Add(@{ Kind = 'up'; Name = '..'; Path = $(if ($parent) { $parent } else { $null }); Detail = $(if ($parent) { 'up a folder' } else { 'This PC' }) })
        $ui.FbPath.Text = $dir
        try {
            $all = @(Get-ChildItem -LiteralPath $dir -Force -ErrorAction Stop | Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::Hidden) -and -not ($_.Attributes -band [IO.FileAttributes]::System) })
            foreach ($f in @($all | Where-Object { $_.PSIsContainer } | Sort-Object Name | Select-Object -First 400)) { [void]$items.Add(@{ Kind = 'dir'; Name = $f.Name; Path = $f.FullName; Detail = '' }) }
            $files = @()
            if (Fb-Files) {
                $files = @($all | Where-Object { -not $_.PSIsContainer -and $_.Extension.ToLower() -in $spec.Ext } | Sort-Object Name)
                foreach ($f in $files) { [void]$items.Add(@{ Kind = 'file'; Name = $f.Name; Path = $f.FullName; Detail = (Format-Size $f.Length) }) }
            }
            # Switch games with the converter: their NSPs too (choosing one sets up the conversion)
            $nsps = @()
            if ($spec.Nsp) {
                $nsps = @($all | Where-Object { -not $_.PSIsContainer -and $_.Extension -eq '.nsp' } | Sort-Object Name)
                foreach ($f in $nsps) { [void]$items.Add(@{ Kind = 'nsp'; Name = $f.Name; Path = $f.FullName; Detail = "$(Format-Size $f.Length)  $Bullet  converts first" }) }
            }
            if ($script:fbMode -eq 'setfolder') {
                $r = $script:fbRow
                if (Test-Path (Join-Path $dir $r.File)) { $note = "$([char]0x2713)  $($r.File) is in this folder. X uses it."; $noteColor = $Teal }
                else { $note = "X uses this folder for $($r.Label)$(if ($r.New) { ' (Install puts the game here)' } else { " (it should have $($r.File))" })."; $noteColor = $Muted }
                $ui.FbUse.IsEnabled = $true
            }
            elseif (Fb-Folders) {
                $r = Test-Rom $spec $dir
                if ($r.Ok) { $note = "$([char]0x2713)  This folder has the game. Press X (or Use this folder) to choose it."; $noteColor = $Teal }
                elseif ($r.Better) { $note = "The game is one folder deeper, in `"$(Split-Path $r.Better -Leaf)`". X uses that one."; $noteColor = $Gold }
                elseif ($nsps.Count) { $note = "There's an NSP here: choose it (A) and the launcher turns it into exefs + romfs with your keys first."; $noteColor = $Teal }
                else { $note = "Open the folder that has: $(($spec.FolderNeeds | ForEach-Object { $_ -join ' + ' }) -join ', or ')$(if (Fb-Files) { ", or pick a $($spec.Ext -join ' / ') file" })."; $noteColor = $Muted }
                $ui.FbUse.IsEnabled = $r.Ok -or [bool]$r.Better
            }
            if (-not (Fb-Folders)) {
                $note = if ($files.Count) { "$($files.Count) matching $(if ($files.Count -eq 1) { 'file' } else { 'files' }) here: A chooses one." } else { "No $($spec.Ext -join ' / ') files here: open another folder." }
                $noteColor = if ($files.Count) { $Teal } else { $Muted }
            }
        } catch { $note = "Can't open this folder: $($_.Exception.Message)"; $noteColor = $Red }
    }
    $script:fb.Dir = $dir
    $script:fb.Items = @($items)
    $ui.FbCheck.Text = $note; $ui.FbCheck.Foreground = Brush $noteColor
    $ui.FbList.Children.Clear()
    $script:fb.Rows = @(foreach ($it in $script:fb.Items) { $r = New-FbRow $it; [void]$ui.FbList.Children.Add($r); $r })
    $ui.FbScroll.ScrollToTop()
    # the cursor: on the folder we came from (or the game picked before), else the first file, else the first row
    $i = 0
    if ($pick) { for ($k = 0; $k -lt $script:fb.Items.Count; $k++) { if ($script:fb.Items[$k].Name -eq $pick -or $script:fb.Items[$k].Path -eq $pick) { $i = $k; break } } }
    elseif ($dir) { for ($k = 0; $k -lt $script:fb.Items.Count; $k++) { if ($script:fb.Items[$k].Kind -eq 'file') { $i = $k; break } } }
    if ($i -eq 0 -and $dir -and $script:fb.Items.Count -gt 1 -and -not $pick) { $i = 1 }
    $script:fb.Idx = -1
    Select-Fb $i
}
function Select-Fb($i) {
    $n = $script:fb.Rows.Count
    if ($n -eq 0) { return }
    $i = [Math]::Max(0, [Math]::Min($n - 1, $i))
    if ($script:fb.Idx -ge 0 -and $script:fb.Idx -lt $n) { $o = $script:fb.Rows[$script:fb.Idx]; $o.Background = Brush '#00000000'; $o.BorderBrush = Brush '#00000000' }
    $script:fb.Idx = $i
    $r = $script:fb.Rows[$i]
    $r.Background = Brush '#22303A'; $r.BorderBrush = New-Object Windows.Media.SolidColorBrush((WithAlpha (Fb-Accent) 0xA0))
    [void]$W.Dispatcher.BeginInvoke([Action] { $script:fb.Rows[$script:fb.Idx].BringIntoView() }, [Windows.Threading.DispatcherPriority]::Loaded)
}
function Enter-Fb {
    $it = $script:fb.Items[$script:fb.Idx]
    if (-not $it) { return }
    switch ($it.Kind) {
        'up'   { Back-Fb }
        'file' { $ui.RomPath.Text = $it.Path; Check-RomPath; Close-Fb }
        'nsp'  {
            # an update NSP (version above 0, or the update's title ID) goes in the update box, anything else is the base game
            $nsp = $RomSpecs[$script:romGame.Id].Nsp; $n = Nsp-Name $it.Path
            $isUpd = $nsp.Update -and (($n.Ver -gt 0) -or ($nsp.UpdateId -and $n.Id -eq $nsp.UpdateId))
            if ($isUpd) { $ui.NspUpdate.Text = $it.Path } else { $ui.NspBase.Text = $it.Path }
            if (-not $ui.NspKeys.Text) { $ui.NspKeys.Text = Find-Keys }
            Show-NspPanel $true; Check-NspStatus
            Close-Fb
            [void]$W.Dispatcher.BeginInvoke([Action] { $ui.NspPanel.BringIntoView() }, [Windows.Threading.DispatcherPriority]::Loaded)
        }
        default { Load-Fb $it.Path $null }
    }
}
function Back-Fb {
    if (-not $script:fb.Dir) { Close-Fb; return }
    $from = $script:fb.Dir
    $parent = Split-Path $from
    if ($parent) { Load-Fb $parent (Split-Path $from -Leaf) } else { Load-Fb $null $from }
}
function Use-FbFolder {
    if (-not (Fb-Folders) -or -not $script:fb.Dir -or -not $ui.FbUse.IsEnabled) { return }
    if ($script:fbMode -eq 'setfolder') { $script:fbRow.Box.Text = $script:fb.Dir.TrimEnd('\'); Close-Fb; return }
    $r = Test-Rom $RomSpecs[$script:romGame.Id] $script:fb.Dir
    $ui.RomPath.Text = if ($r.Ok) { $script:fb.Dir } elseif ($r.Better) { $r.Better } else { $script:fb.Dir }
    Check-RomPath; Close-Fb
}
function Check-NspStatus { Check-Nsp; Set-Hints }
$ui.FbUse.Add_Click({ Use-FbFolder })
$ui.FbCancel.Add_Click({ Close-Fb })
$ui.FbDim.Add_MouseLeftButtonDown({ param($s, $e) Close-Fb; $e.Handled = $true })

# Install pressed in the picker ($skip: no game chosen, the game's setup asks)
function Confirm-Rom([switch]$Skip) {
    $g = $script:romGame
    if (-not $g) { return }
    $g.RomPath = $null
    if (-not $Skip -and $RomSpecs[$g.Id].Kind -ne 'none') {
        $g.RomPath = $ui.RomPath.Text.Trim().Trim('"')
        $roms = Load-Roms; $roms[$g.Id] = $g.RomPath; Save-Roms $roms
    }
    Close-RomPicker
    Run-Jobs @(@{ G = $g; Job = $(if ($g.Id -eq 'totk') { 'setup' } else { 'install' }) })
}
# After a first install: the game's own setup, with the chosen game handed over where it can be
function Start-FirstRun($g) {
    $rom = $g.RomPath
    if ($rom -and $RomSpecs[$g.Id].Handoff -eq 'tod') {
        $setup = Join-Path $g.Dir 'tod_setup.exe'
        if (Test-Path $setup) {
            Log "Installing the game from $rom (tod_setup.exe --install). This copies the disc, about 24 GB." $Muted
            Start-Process $setup -ArgumentList @('--install', '--base', "`"$($g.Dir)`"", "`"$rom`"") -WorkingDirectory $g.Dir
            return
        }
    }
    $first = if ($g.FirstRun -eq $g.Exe) { Resolve-Exe $g } else { Join-Path $g.Dir $g.FirstRun }
    if (-not ($g.FirstRun -and (Test-Path $first))) { return }
    $spec = $RomSpecs[$g.Id]
    # CopyAs: the game looks for its ROM next to itself under one name
    if ($rom -and $spec.CopyAs -and (Test-Path -LiteralPath $rom -PathType Leaf)) {
        try {
            Copy-Item -LiteralPath $rom (Join-Path $g.Dir $spec.CopyAs) -Force
            Log "Copied your game in as $($spec.CopyAs)." $Muted
            Start-Process $first -WorkingDirectory $g.Dir
            return
        } catch { Log "Couldn't copy your game in: $($_.Exception.Message)" $Red }
    }
    # CopyIn: the game wants these folders (exefs, romfs) inside its own folder: copied there in a console window,
    # then the game's first-start script runs in the same window
    if ($rom -and $spec.CopyIn -and (Test-Path -LiteralPath $rom -PathType Container)) {
        $cmds = foreach ($f in $spec.CopyIn) { "robocopy `"$(Join-Path $rom $f)`" `"$(Join-Path $g.Dir $f)`" /E /NFL /NDL /NJH /NP" }
        $line = ($cmds -join ' & ') + " & cd /d `"$($g.Dir)`" & call `"$first`""
        Log "Copying $($spec.CopyIn -join ' and ') from $rom into the game's folder, then starting its setup." $Muted
        Start-Process cmd.exe -ArgumentList "/c `"$line`"" -WorkingDirectory $g.Dir
        return
    }
    if ($rom) {
        try { [Windows.Clipboard]::SetText($rom) } catch {}
        Log "Starting its setup. Your game is $rom; that path is on the clipboard: paste it (Ctrl+V) into the setup's file box." $Muted
        Set-Step "$($g.Name): paste your game's path into its setup (Ctrl+V)" $Teal
    } else {
        Log "Starting its setup: choose your game there. $($g.Needs)" $Muted
    }
    $style = if ($first -like '*.bat' -and -not $g.Console) { 'Hidden' } else { 'Normal' }
    Start-Process $first -WorkingDirectory $g.Dir -WindowStyle $style
}

# ---------- Updates (worker processes) ----------
$Names = @{
    backup = 'Backing up saves'; download = 'Downloading'; unpack = 'Unpacking'; install = 'Starting installer'
    check = 'Checking game files'; extract = 'Extracting game files'; lift = 'Translating game code'
    compile = 'Building totk.exe'; prepare = 'Building shaders'; cleanup = 'Finishing up'; verify = 'Checking the download'; tools = 'Getting hactool'; keys = 'Reading your keys'
}

function Start-Queue($list) {
    $busy = @($list | Where-Object { Get-Process $_.Proc -ErrorAction SilentlyContinue })
    if ($busy) {
        $names = ($busy | ForEach-Object { $_.Name }) -join ' and '
        [Windows.MessageBox]::Show($W, "Close $names before updating.", 'Recomp Launcher', 'OK', 'Information') | Out-Null
        return
    }
    Run-Jobs @($list | ForEach-Object { @{ G = $_; Job = 'update' } })
}

# A first install: the release goes into the game's folder from Settings, then the game's own setup starts and
# asks for the player's dump. TotK has a setup program instead: it is downloaded and started, and installs itself.
$TotkSetupDir = Join-Path $env:LOCALAPPDATA 'RecompLauncher\totk-setup'
function Start-Install($g) {
    # no download known yet (the release page couldn't be reached at the last check): look it up first
    if (-not ($g.Latest -and $g.Latest.Url)) {
        Set-Step "Looking up the $($g.Name) download..." $TextC
        Set-Indeterminate $true
        Start-Async $LatestScript @{ Id = $g.Id; Repo = $g.Repo; Asset = $g.Asset; NotAsset = $g.NotAsset; Pre = [bool]$g.Pre; Token = [string]$Settings.GitHubToken } {
            param($job, $res, $err)
            Set-Indeterminate $false
            $g = $Games | Where-Object { $_.Id -eq $job.Id }
            if ($res -and $res.Tag -and $res.Url) {
                $res.Checked = (Get-Date).ToString('o')
                $script:cache[$g.Id] = $res; Save-Cache $script:cache
                $g.Latest = $res
                if ($g -eq $script:sel) { Fill-Notes $g }
                Set-Step "Found $($g.Name) $($res.Tag)" $Teal
                Update-Views
                Start-Install $g
            } else {
                $why = if ("$err" -match '403|rate limit') { "GitHub's limit of 60 checks an hour is used up; try again in a few minutes." } elseif ($res.Problem) { $res.Problem } else { "the release page couldn't be reached. Check your internet connection and try again." }
                Set-Step "Can't install $($g.Name) yet: $why" $Red
                Log "Can't install $($g.Name): $why $err" $Red
            }
        }
        return
    }
    if ($g.Latest.Problem) {
        Set-Step "Can't install $($g.Name): $($g.Latest.Problem)" $Red
        return
    }
    if ($g.Id -ne 'totk' -and $g.Dir -match '^[A-Za-z]:\\Program Files') {
        [Windows.MessageBox]::Show($W, "Program Files isn't writable for games. Pick another folder in Settings.", 'Recomp Launcher', 'OK', 'Information') | Out-Null
        return
    }
    # asks for the player's game first; its Install button starts the download (Confirm-Rom)
    Open-RomPicker $g
}

function Run-Jobs($jobs) {
    $script:queue.Clear()
    foreach ($j in $jobs) { [void]$script:queue.Add($j) }
    $script:results = @()
    $script:running = $true
    $docActivity.Blocks.Clear()
    Set-Tab 'activity'
    Start-Next
}

function Start-Next {
    if ($script:queue.Count -eq 0) { Finish-Queue; return }
    $j = $script:queue[0]; $script:queue.RemoveAt(0)
    $g = $j.G
    $script:current = $g; $script:currentJob = $j.Job
    $script:currentNsp = $j.Nsp
    $g.State = switch ($j.Job) { 'update' { 'updating' } 'convert' { 'converting' } 'uninstall' { 'uninstalling' } default { 'installing' } }
    if ($script:sel -ne $g) { Show-Game $g } else { Update-Views }
    $logs = Join-Path $Root 'logs'
    New-Item -ItemType Directory -Force $logs | Out-Null
    Get-ChildItem $logs -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -Skip 10 | Remove-Item -Force
    $script:logPath = Join-Path $logs ("$($g.Id)-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
    $script:logPos = 0L; $script:partial = ''; $script:finished = $false
    if ($j.Job -eq 'update') { Log "$($g.Name): updating v$($g.Installed.Version) to $($g.Latest.Tag)" $g.Accent }
    elseif ($j.Job -eq 'convert') { Log "$($g.Name): converting $([IO.Path]::GetFileName($j.Nsp.Base))$(if ($j.Nsp.Update) { " with $([IO.Path]::GetFileName($j.Nsp.Update))" }) to exefs + romfs" $g.Accent }
    elseif ($j.Job -ne 'uninstall') { Log "$($g.Name): installing $($g.Latest.Tag)" $g.Accent }
    Set-Step "$($g.Name): starting..." $TextC
    Set-Indeterminate $true
    if ($j.Job -eq 'uninstall') {
        Log "$($g.Name): uninstalling from $($g.Dir)" $g.Accent
        $argLine = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$(Join-Path $Root 'uninstall-worker.ps1')`" -Game `"$($g.Dir)`" " +
                "-Exe `"$($g.Exe)`" -Log `"$($script:logPath)`" -Saves `"$($SavePaths[$g.Id])`" -BackupDir `"$($g.Backups)`""
        if ($j.KeepSaves) { $argLine += ' -KeepSaves' }
        $script:worker = Start-Process powershell.exe -ArgumentList $argLine -WindowStyle Hidden -PassThru
        $poll.Start()
        return
    }
    if ($j.Job -eq 'convert') {
        $n = $j.Nsp
        $argLine = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$(Join-Path $Root 'nsp-worker.ps1')`" -Base `"$($n.Base)`" " +
                "-Keys `"$($n.Keys)`" -Out `"$($n.Out)`" -Log `"$($script:logPath)`""
        if ($n.Update) { $argLine += " -Update `"$($n.Update)`"" }
        if ($n.TitleKeys) { $argLine += " -TitleKeys `"$($n.TitleKeys)`"" }
        $script:worker = Start-Process powershell.exe -ArgumentList $argLine -WindowStyle Hidden -PassThru
        $poll.Start()
        return
    }
    $useTotkWorker = $g.Id -eq 'totk' -and $j.Job -eq 'update'
    $worker = if ($useTotkWorker) { $g.Worker } else { Join-Path $Root 'recomp-worker.ps1' }
    if (-not (Test-Path $worker)) {
        Finish-Game ([pscustomobject]@{ result = 'failed'; message = "The update helper is missing: $worker. Check the TotK Updater folder in Settings." })
        return
    }
    if ($useTotkWorker) {
        $argLine = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$worker`" -Game `"$($g.Dir)`" " +
                "-Url `"$($g.Latest.Url)`" -Tag `"$($g.Latest.Tag)`" -Log `"$($script:logPath)`""
    } else {
        $dest = if ($j.Job -eq 'setup') { $TotkSetupDir } else { $g.Dir }
        # a pattern Exe is a single-file release: it keeps the download's own name, and older copies are removed
        $exe = if ($j.Job -eq 'setup') { 'TotK-Setup.exe' } elseif ($g.Exe -match '[*?]') { [IO.Path]::GetFileName(([uri]$g.Latest.Url).LocalPath) } else { $g.Exe }
        $argLine = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$worker`" -Mode $($j.Job) -Game `"$dest`" " +
                "-Url `"$($g.Latest.Url)`" -Tag `"$($g.Latest.Tag)`" -Sha256 `"$($g.Latest.Sha)`" -Log `"$($script:logPath)`" " +
                "-Exe `"$exe`" -BackupDir `"$($g.Backups)`" -Backup `"$($g.Backup)`" -Keep `"$($g.Keep)`" -Replace `"$($g.Replace)`""
        if ($g.Exe -match '[*?]') { $argLine += " -RemoveOld `"$($g.Exe)`"" }
        if ($g.NoSha -and -not $g.Latest.Sha) { $argLine += ' -NoSha' }
        if ($j.Job -eq 'update' -and $g.SetupArg) { $argLine += ' -NeedsSetup' }
    }
    $script:worker = Start-Process powershell.exe -ArgumentList $argLine -WindowStyle Hidden -PassThru
    $poll.Start()
}

# Starts a game: its own launcher file if it has one (Play.bat), and its setup once after an update that needs it.
# Then the launcher closes itself, unless an install or update is still running.
function Start-Game($g) {
    # Launch: what to start instead of Exe, as ';'-separated choices (relative to the game folder, or full paths
    # with %VARIABLES% and wildcards); the first one that exists wins, otherwise Exe (such as an installer that
    # puts the game somewhere else, and is run again until it has)
    $file = $null
    if ($g.Launch) {
        foreach ($c in ($g.Launch -split ';')) {
            $p = [Environment]::ExpandEnvironmentVariables($c.Trim())
            if (-not [IO.Path]::IsPathRooted($p)) { $p = Join-Path $g.Dir $p }
            $hit = @(Resolve-Path -Path $p -ErrorAction SilentlyContinue | Select-Object -First 1)
            if ($hit.Count) { $file = $hit[0].Path; break }
        }
    }
    if (-not $file) { $file = Resolve-Exe $g }
    if (-not (Test-Path $file)) { return }
    # play time watches the folder the game runs from
    $playDir = if ($file.StartsWith($g.Dir.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { $g.Dir } else { Split-Path -Parent $file }
    $mk = Get-Marker $g
    $args = @()
    if ($g.SetupArg -and $mk -and $mk.needsSetup) {
        $args = @($g.SetupArg)
        $mk.needsSetup = $false
        try { $mk | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $g.Dir 'recomp-launcher.json') } catch {}
    }
    # a .bat runs hidden, unless the game's script talks to you (Console)
    $style = if ($file -like '*.bat' -and -not $g.Console) { 'Hidden' } else { 'Normal' }
    if ($args) { Start-Process $file -ArgumentList $args -WorkingDirectory $playDir -WindowStyle $style }
    else { Start-Process $file -WorkingDirectory $playDir -WindowStyle $style }
    Set-Step "Starting $($g.Name)..." $Teal
    # play time: a hidden helper watches the game's folder until the game is closed
    $tracker = Join-Path $Root 'playtime-worker.ps1'
    if (Test-Path $tracker) {
        Start-Process powershell.exe -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$tracker`" -Id $($g.Id) -Dir `"$playDir`" -Store `"$PlaytimePath`""
    }
    if (-not $script:running -and "$($Settings.CloseOnPlay)" -ne 'off') { Close-Launcher }
}
# Fades the window out and closes it (remembering the selected game, like any close)
function Close-Launcher {
    $W.Topmost = $false
    $a = Anim 0 300 $null 'EaseIn'
    $a.Add_Completed({ $W.Close() })
    $ui.Root.BeginAnimation([Windows.UIElement]::OpacityProperty, $a)
    $ui.Root.RenderTransform.BeginAnimation([Windows.Media.ScaleTransform]::ScaleXProperty, (Anim 0.97 300 $null 'EaseIn'))
    $ui.Root.RenderTransform.BeginAnimation([Windows.Media.ScaleTransform]::ScaleYProperty, (Anim 0.97 300 $null 'EaseIn'))
}

function Format-Count($e) {
    if ($e.unit -eq 'bytes') { '{0:N0} / {1:N0} MB' -f ($e.done / 1MB), ($e.total / 1MB) }
    else { '{0:N0} / {1:N0} {2}' -f $e.done, $e.total, $e.unit }
}

function Handle-Event($e) {
    $who = $script:current.Name
    switch ($e.type) {
        'stage' { Set-Step "${who}: $($e.text)"; Set-Indeterminate $true; Log $e.text }
        'text'  { Log $e.text $Muted }
        'start' { Log "Installer $($e.release) in $($e.mode) mode" $Muted }
        'step' {
            $name = if ($Names[$e.step]) { $Names[$e.step] } else { $e.step }
            switch ($e.state) {
                'started' { Set-Step "${who}: $name"; Set-Indeterminate $true; Log $name }
                'done'    { Log ("  done in {0:N0} s" -f [double]$e.seconds) $Teal }
                'skipped' { Log "$name  -  skipped ($($e.reason))" $Muted }
                default   { Log "$name  -  $($e.state)" $Muted }
            }
        }
        'progress' {
            $phase = ($e.phase -replace '_', ' ')
            if ($e.total) {
                $f = [double]$e.done / [double]$e.total
                Set-Progress $f
                $ui.PctText.Text = '{0:N0}%' -f (100 * $f)
                $ui.DetailText.Text = "$phase   $(Format-Count $e)"
            } else {
                if (-not $script:indet) { Set-Indeterminate $true }
                $ui.PctText.Text = ''; $ui.DetailText.Text = $phase
            }
        }
        'end' { }
        'plan' { }
        'done' { Finish-Game $e }
        default {
            $color = if ($e.type -match 'error|fail') { $Red } elseif ($e.type -match 'warn') { $Gold } else { $Muted }
            Log ($e | ConvertTo-Json -Compress) $color
        }
    }
}

function Finish-Game($e) {
    $script:finished = $true
    $poll.Stop()
    $g = $script:current
    $job = $script:currentJob
    if ($e.result -eq 'ok') {
        switch ($job) {
            'update' {
                Log "$($g.Name) updated to $($g.Latest.Tag). Saves and settings were kept." $Teal
                if ($e.backup) { Log "Backup: $($e.backup)" $Muted }
                if ($g.Id -eq 'smo') { Log 'The first start after an update can rebuild the game code (a few minutes).' $Muted }
                if ($g.SetupArg) { Log "The next Play runs the game's setup once, to rebuild the game for this version." $Muted }
            }
            'install' {
                Log "$($g.Name) $($g.Latest.Tag) is in $($g.Dir)." $Teal
                Start-FirstRun $g
            }
            'uninstall' {
                Log "$($g.Name) is uninstalled." $Teal
                if ($e.backup) { Log "Saves and settings kept in $($e.backup)" $Muted }
            }
            'convert' {
                Log "$($g.Name): exefs and romfs are in $($e.out)." $Teal
                $roms = Load-Roms; $roms[$g.Id] = [string]$e.out; Save-Roms $roms
                $script:reopenRom = $g   # back to Choose your game, with the new folder filled in
            }
            'setup' {
                $exe = Get-ChildItem $TotkSetupDir -Recurse -Filter 'TotK-Setup.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($exe) {
                    Log "Started the TotK setup. Choose your game files, and $($g.Dir) as the install folder (or change it in Settings to match)." $Teal
                    Start-Process $exe.FullName -WorkingDirectory $exe.DirectoryName
                } else { Log "The TotK download has no TotK-Setup.exe in it." $Red }
            }
        }
        $g.Installed = Get-Installed $g
        Load-Icons
        if ($script:sel -eq $g) { $ui.HeroIcon.Source = $g.Icon; $ui.HeroArt.Source = $g.Icon }
        $g.State = if (-not $g.Installed) { 'missing' } elseif ((To-Version $g.Latest.Tag) -gt (To-Version $g.Installed.Version)) { 'update' } else { 'current' }
        $script:results += @{ Game = $g; Ok = $true; Job = $job }
    } else {
        $what = switch ($job) { 'update' { 'update' } 'convert' { 'NSP conversion' } default { 'install' } }
        Log "$($g.Name) $what failed: $($e.message)" $Red
        Log "Log: $($script:logPath)" $Muted
        $g.State = if ($job -eq 'update') { 'update' } else { 'missing' }
        if ($job -eq 'convert') { $script:reopenRom = $g; $script:reopenNsp = $true }
        $script:results += @{ Game = $g; Ok = $false; Job = $job }
    }
    Update-Views
    Start-Next
}

function Finish-Queue {
    $script:current = $null
    $script:running = $false
    Set-Indeterminate $false
    $failed = @($script:results | Where-Object { -not $_.Ok })
    $jobs = @($script:results | ForEach-Object { $_.Job })
    $word = if ($jobs -contains 'uninstall') { 'Uninstall' } elseif ($jobs -contains 'convert') { 'Conversion' } elseif (@($jobs | Where-Object { $_ -ne 'update' }).Count) { 'Install' } else { 'Update' }
    if ($failed.Count -eq 0) {
        Set-Progress 1
        Set-Step "$word complete" $Teal '' '100%'
        [Media.SystemSounds]::Asterisk.Play()
    } else {
        Set-Progress 0
        Set-Step ("$word failed: " + (($failed | ForEach-Object { $_.Game.Name }) -join ', ') + " $Bullet  details in What's new > Activity") $Red
        [Media.SystemSounds]::Hand.Play()
    }
    Update-Views
    # after an NSP conversion: Choose your game again, with the new folder (or the converter, to try again)
    if ($script:reopenRom) {
        $g = $script:reopenRom; $script:reopenRom = $null
        Show-Game $g
        Open-RomPicker $g
        if ($script:reopenNsp) { $script:reopenNsp = $false; Show-NspPanel $true }
    }
}

# Follows the current worker's log file
$poll = New-Object Windows.Threading.DispatcherTimer
$poll.Interval = [TimeSpan]::FromMilliseconds(200)
$poll.Add_Tick({
    try {
        if ($script:logPath -and (Test-Path $script:logPath)) {
            $fs = [IO.File]::Open($script:logPath, 'Open', 'Read', 'ReadWrite')
            try {
                [void]$fs.Seek($script:logPos, 'Begin')
                $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
                $chunk = $sr.ReadToEnd()
                $script:logPos = $fs.Position
            } finally { $fs.Close() }
            if ($chunk) {
                $lines = ($script:partial + $chunk) -split "`n"
                $script:partial = $lines[-1]
                foreach ($l in $lines[0..($lines.Count - 2)]) {
                    $l = $l.Trim()
                    if (-not $l) { continue }
                    try { $ev = $l | ConvertFrom-Json } catch { Log $l $Muted; continue }
                    Handle-Event $ev
                    if ($script:finished) { return }
                }
            }
        }
        if ($script:worker -and $script:worker.HasExited -and -not $script:finished) {
            Finish-Game ([pscustomobject]@{ result = 'failed'; message = 'The updater stopped unexpectedly.' })
        }
    } catch {
        Log "Launcher error: $($_.Exception.Message)" $Red
    }
})

# ---------- Controller ----------
# Three rows the controller moves between: the game cards (left / right picks a game), the game's buttons and the top
# bar (up / down moves between them, left / right between their buttons).
#   A plays or installs the game (or presses the focused button), X updates it, Y opens What's new, B goes back
#   (buttons -> cards, then clears the search and filter), LB / RB switch the filter, a left stick click (LS) the
#   sort order, Menu opens Settings, View switches full screen, the right stick scrolls the release notes.
# In What's new: B or Y closes, LB / RB switch tabs. In Settings: A saves, B (or Menu) cancels, X resets.
$PadBits = [ordered]@{ Up = 0x1; Down = 0x2; Left = 0x4; Right = 0x8; Start = 0x10; Back = 0x20; LS = 0x40; RS = 0x80; LB = 0x100; RB = 0x200; A = 0x1000; B = 0x2000; X = 0x4000; Y = 0x8000 }
$Zones = @{
    actions = @($ui.BtnPlay, $ui.BtnUpdate, $ui.BtnNotes, $ui.BtnFolder, $ui.BtnBackups, $ui.BtnUninstall)
    top     = @($ui.BtnCheck, $ui.BtnAll, $ui.BtnSettings, $ui.BtnFull)
}
$script:zone = 'cards'
$script:focusIdx = 0
$script:padUsed = $false
$script:padOn = $false
$script:padPrev = 0
$script:padHeld = @{}
$script:padNextScan = [DateTime]::MinValue
$script:notesScroll = $null

# Hints are collected first (New-Hint) and only drawn again when they differ from what's on screen
function New-Hint($key, $label, $color) { [void]$script:hintSpecs.Add(@($key, $label, $color)) }
function Set-Hints {
    $script:hintSpecs = New-Object System.Collections.ArrayList
    Build-Hints
    $k = ($script:hintSpecs | ForEach-Object { $_ -join '|' }) -join ';'
    if ($k -eq $script:hintKey -and $ui.PadHints.Children.Count) { return }
    $script:hintKey = $k
    $ui.PadHints.Children.Clear()
    foreach ($h in $script:hintSpecs) { Draw-Hint $h[0] $h[1] $h[2] }
}
function Draw-Hint($key, $label, $color) {
    $sp = New-Object Windows.Controls.StackPanel
    $sp.Orientation = 'Horizontal'; $sp.Margin = New-Object Windows.Thickness(18, 0, 0, 0)
    $badge = New-Object Windows.Controls.Border
    $badge.MinWidth = 22; $badge.Height = 22; $badge.CornerRadius = New-Object Windows.CornerRadius(11)
    $badge.Padding = New-Object Windows.Thickness(6, 0, 6, 0)
    $badge.Background = if ($color) { Brush $color } else { Brush '#2A3640' }
    $t = New-Object Windows.Controls.TextBlock
    $t.Text = $key; $t.FontSize = 10.5; $t.FontWeight = 'Bold'
    $t.Foreground = Brush $(if ($color) { '#0A0E11' } else { $TextC })
    $t.HorizontalAlignment = 'Center'; $t.VerticalAlignment = 'Center'
    $badge.Child = $t
    $l = New-Object Windows.Controls.TextBlock
    $l.Text = $label; $l.FontSize = 12; $l.Foreground = Brush '#A9B6BE'; $l.Margin = New-Object Windows.Thickness(8, 0, 0, 0)
    $l.VerticalAlignment = 'Center'
    [void]$sp.Children.Add($badge); [void]$sp.Children.Add($l)
    [void]$ui.PadHints.Children.Add($sp)
}
# The hints at the bottom right follow what the controller (or keyboard) can do right now
function Build-Hints {
    $settings = $ui.SettingsLayer.Visibility -eq 'Visible'
    $notes = $ui.NotesLayer.Visibility -eq 'Visible'
    $rom = $ui.RomLayer.Visibility -eq 'Visible'
    $uninst = $ui.UninstLayer.Visibility -eq 'Visible'
    if ($script:padOn) {
        if ($uninst) {
            New-Hint 'Y' 'Uninstall' '#F2C230'
            if ($ui.KeepSaves.Root.Visibility -eq 'Visible') { New-Hint 'X' 'Keep saves' '#3A86E8' }
            New-Hint 'B' 'Cancel' '#E5473B'; return
        }
        if (Fb-Visible) {
            $it = $script:fb.Items[$script:fb.Idx]
            New-Hint 'A' $(if ($it.Kind -in 'file', 'nsp') { 'Choose' } elseif ($it.Kind -eq 'up') { 'Back' } else { 'Open' }) '#5DC14A'
            if ((Fb-Folders) -and $script:fb.Dir -and $ui.FbUse.IsEnabled) { New-Hint 'X' 'Use this folder' '#3A86E8' }
            New-Hint 'B' 'Back' '#E5473B'; New-Hint 'Y' 'Close' '#F2C230'; New-Hint 'LB RB' 'Page' $null
            return
        }
        if ($rom) {
            if ($RomSpecs[$script:romGame.Id].Kind -ne 'none') { New-Hint 'A' 'Find your game' '#5DC14A' }
            if ($ui.BtnRomSkip.Visibility -eq 'Visible') { New-Hint 'X' 'Skip' '#3A86E8' }
            $conv = $ui.NspPanel.Visibility -eq 'Visible' -and $ui.BtnNspConvert.IsEnabled
            New-Hint 'Y' $(if ($conv) { 'Convert NSP' } else { 'Install' }) '#F2C230'; New-Hint 'B' 'Cancel' '#E5473B'; return
        }
        if ($settings) {
            $st = if ($script:setIdx -ge 0) { $script:setStops[$script:setIdx] }
            New-Hint 'A' $(if ($st.T) { 'Switch' } elseif ($st.Row) { 'Choose folder' } else { 'Select' }) '#5DC14A'
            New-Hint 'Y' 'Save' '#F2C230'; New-Hint 'B' 'Cancel' '#E5473B'; New-Hint 'LB RB' 'Page' $null; return
        }
        if ($notes) { New-Hint 'LB RB' 'Tabs' $null; New-Hint 'R' 'Scroll' $null; New-Hint 'B' 'Close' '#E5473B'; return }
        $main = if ($script:zone -eq 'cards') { $(if ($script:sel.State -eq 'missing') { 'Install' } else { 'Play' }) } else { 'Select' }
        New-Hint 'A' $main '#5DC14A'
        if ($script:sel.State -eq 'update') { New-Hint 'X' 'Update' '#3A86E8' }   # only when there is one (keeps the row short)
        New-Hint 'Y' "What's new" '#F2C230'
        New-Hint 'LB RB' 'Filter' $null
        New-Hint 'LS' 'Sort' $null
        New-Hint 'RS' $(if ($script:view -eq 'grid') { 'Row' } else { 'Grid' }) $null
        New-Hint ([string][char]0x2261) 'Settings' $null
        # (View switches full screen too; its hint is left out so the row fits next to the progress text)
    } else {
        if ($settings -or $notes -or $rom -or $uninst) { New-Hint 'Esc' 'Close' $null; return }
        New-Hint ([string][char]0x2190 + ' ' + [string][char]0x2192) 'Game' $null
        New-Hint 'Enter' $(if ($script:sel.State -eq 'missing') { 'Install' } else { 'Play' }) $null
        New-Hint 'Type' 'Search' $null
        New-Hint 'F11' 'Full screen' $null
    }
}
function Set-PadConnected($on) {
    if ($on -eq $script:padOn) { return }
    $script:padOn = $on
    Set-Hints
    $ui.PadHints.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 350 0))
    if (-not $on) { Hide-Ring; $script:zone = 'cards' }
}

function Place-Ring {
    if (-not $script:padUsed -or $script:zone -eq 'cards') { Hide-Ring; return }
    if ($ui.SettingsLayer.Visibility -eq 'Visible' -or $ui.NotesLayer.Visibility -eq 'Visible') { Hide-Ring; return }
    $btn = $Zones[$script:zone][$script:focusIdx]
    if (-not $btn -or -not $btn.IsVisible -or $btn.ActualWidth -le 0) { return }
    if ($script:zone -eq 'actions') {
        # layout position, ignoring the slide-in animation of the game page
        $off = [Windows.Media.VisualTreeHelper]::GetOffset($ui.HeroContent)
        $p = $btn.TransformToAncestor($ui.HeroContent).Transform((New-Object Windows.Point(0, 0)))
        $x = $off.X + $p.X; $y = $off.Y + $p.Y
    } else {
        $p = $btn.TransformToAncestor($ui.Main).Transform((New-Object Windows.Point(0, 0)))
        $x = $p.X; $y = $p.Y
    }
    $pad = 5
    $tt = $ui.FocusRing.RenderTransform
    $first = $ui.FocusRing.Opacity -lt 0.05
    $ms = if ($first) { 0 } else { 240 }
    $tt.BeginAnimation([Windows.Media.TranslateTransform]::XProperty, (Anim ($x - $pad) $ms))
    $tt.BeginAnimation([Windows.Media.TranslateTransform]::YProperty, (Anim ($y - $pad) $ms))
    $ui.FocusRing.BeginAnimation([Windows.FrameworkElement]::WidthProperty, (Anim ($btn.ActualWidth + 2 * $pad) $ms))
    $ui.FocusRing.BeginAnimation([Windows.FrameworkElement]::HeightProperty, (Anim ($btn.ActualHeight + 2 * $pad) $ms))
    if ($first) {
        $b = Calm (Forever (Anim 0.55 1100 1 'EaseInOut')); $b.AutoReverse = $true
        $ui.FocusRing.BeginAnimation([Windows.UIElement]::OpacityProperty, $b)
    }
}
function Hide-Ring { $ui.FocusRing.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 0 200)) }
function Queue-Ring { [void]$W.Dispatcher.BeginInvoke([Action] { Place-Ring }, [Windows.Threading.DispatcherPriority]::Loaded) }
# Keeps the focus on a button that can be pressed, or goes back to the cards when the row has none
function Fix-Focus {
    if ($script:zone -ne 'cards') {
        $row = $Zones[$script:zone]
        if (-not ($row[$script:focusIdx].IsEnabled -and $row[$script:focusIdx].IsVisible)) {
            $ok = @(0..($row.Count - 1) | Where-Object { $row[$_].IsEnabled -and $row[$_].Visibility -eq 'Visible' })
            if ($ok) { $script:focusIdx = $ok[0] } else { $script:zone = 'cards' }
        }
    }
    Queue-Ring
}
function Set-Zone($z) {
    $script:zone = $z; $script:focusIdx = 0
    Fix-Focus
    Set-Hints
}
function Move-Focus([int]$dir) {
    $row = $Zones[$script:zone]
    $i = $script:focusIdx
    while ($true) {
        $i += $dir
        if ($i -lt 0 -or $i -ge $row.Count) { return }
        if ($row[$i].IsEnabled -and $row[$i].Visibility -eq 'Visible') { break }
    }
    $script:focusIdx = $i
    Place-Ring
}
function Press($btn) {
    if (-not $btn.IsEnabled -or $btn.Visibility -ne 'Visible') { return }
    $st = New-Object Windows.Media.ScaleTransform(1, 1)
    $btn.RenderTransform = $st
    $a = Anim 1 260 0.92
    $st.BeginAnimation([Windows.Media.ScaleTransform]::ScaleXProperty, $a)
    $st.BeginAnimation([Windows.Media.ScaleTransform]::ScaleYProperty, $a)
    $btn.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Primitives.ButtonBase]::ClickEvent, $btn)))
}
function Pad-Press($name) {
    $script:padUsed = $true
    if (-not $script:padOn) { Set-PadConnected $true }
    if ($ui.UninstLayer.Visibility -eq 'Visible') {
        # Y uninstalls (on purpose not A), X switches keeping the saves, B cancels
        switch ($name) { 'Y' { Press $ui.BtnUninstGo } 'X' { if ($ui.KeepSaves.Root.Visibility -eq 'Visible') { Set-Toggle $ui.KeepSaves (-not $ui.KeepSaves.On) } } 'B' { Close-Uninstall } }
        return
    }
    if (Fb-Visible) {
        # the file browser: Up / Down move (LB / RB a page), A opens or chooses, B / Left back, Right opens,
        # X uses the folder you're in, Y closes
        switch ($name) {
            'Up'    { Select-Fb ($script:fb.Idx - 1) }
            'Down'  { Select-Fb ($script:fb.Idx + 1) }
            'LB'    { Select-Fb ($script:fb.Idx - 8) }
            'RB'    { Select-Fb ($script:fb.Idx + 8) }
            'A'     { Enter-Fb }
            'Right' { $it = $script:fb.Items[$script:fb.Idx]; if ($it -and $it.Kind -notin 'file', 'nsp', 'up') { Enter-Fb } }
            'B'     { Back-Fb }
            'Left'  { if ($script:fb.Dir) { Back-Fb } }
            'X'     { Use-FbFolder }
            'Y'     { Close-Fb }
            'Back'  { Close-Fb }
        }
        Set-Hints
        return
    }
    if ($ui.RomLayer.Visibility -eq 'Visible') {
        # A opens the file browser, X lets the game's own setup ask instead, Y or Menu installs, B cancels
        switch ($name) {
            'A' { if ($RomSpecs[$script:romGame.Id].Kind -ne 'none') { Open-Fb } else { Press $ui.BtnRomInstall } }
            'X' { Press $ui.BtnRomSkip }
            # with the NSP converter open and ready, Y converts (and then installs)
            'Y' { if ($ui.NspPanel.Visibility -eq 'Visible' -and $ui.BtnNspConvert.IsEnabled) { Press $ui.BtnNspConvert } else { Press $ui.BtnRomInstall } }
            'Start' { if ($ui.NspPanel.Visibility -eq 'Visible' -and $ui.BtnNspConvert.IsEnabled) { Press $ui.BtnNspConvert } else { Press $ui.BtnRomInstall } }
            'B' { Close-RomPicker }
        }
        return
    }
    if ($ui.SettingsLayer.Visibility -eq 'Visible') {
        # move through the switches and buttons; A switches or presses, Y (or Menu) saves, B cancels
        $i = $script:setIdx; $st = if ($i -ge 0) { $script:setStops[$i] }
        if ($i -lt 0 -and $name -in 'Up', 'Down', 'Left', 'Right', 'A', 'LB', 'RB') { Select-SetStop 0; Set-Hints; return }
        switch ($name) {
            'Up'    { $j = if ($st.Grid -and $i -ge 2 -and $script:setStops[$i - 2].Grid) { $i - 2 } else { $i - 1 }; Select-SetStop $j }
            'Down'  { $j = if ($st.Grid -and $i + 2 -lt $script:setStops.Count -and $script:setStops[$i + 2].Grid) { $i + 2 } else { $i + 1 }; Select-SetStop $j }
            'Left'  { Select-SetStop ($i - 1) }
            'Right' { Select-SetStop ($i + 1) }
            'LB'    { Select-SetStop ($i - 6) }
            'RB'    { Select-SetStop ($i + 6) }
            'A'     { if ($st.T) { Set-Toggle $st.T (-not $st.T.On) } elseif ($st.Row) { Open-FbFolder $st.Row } else { Press $st.Btn } }
            'Y'     { Press $ui.BtnSetSave }
            'Start' { Press $ui.BtnSetSave }
            'B'     { Press $ui.BtnSetCancel }
        }
        Set-Hints
        return
    }
    if ($ui.NotesLayer.Visibility -eq 'Visible') {
        switch ($name) {
            'B' { Close-Notes }
            'Y' { Close-Notes }
            'LB' { Set-Tab 'notes' }
            'RB' { Set-Tab 'activity' }
        }
        return
    }
    switch ($name) {
        'Left'  { if ($script:zone -eq 'cards') { Step-Game -1 } else { Move-Focus -1 } }
        'Right' { if ($script:zone -eq 'cards') { Step-Game 1 } else { Move-Focus 1 } }
        'Up'    { if ($script:view -eq 'grid' -and $script:zone -eq 'cards') { Grid-Move -1 } elseif ($script:zone -eq 'cards') { Set-Zone 'actions' } elseif ($script:zone -eq 'actions') { Set-Zone 'top' } }
        'Down'  { if ($script:view -eq 'grid' -and $script:zone -eq 'cards') { Grid-Move 1 } elseif ($script:zone -eq 'top') { Set-Zone 'actions' } elseif ($script:zone -eq 'actions') { Set-Zone 'cards' } }
        'RS'    { Toggle-View }
        'A'     { if ($script:zone -eq 'cards') { Press $ui.BtnPlay } else { Press $Zones[$script:zone][$script:focusIdx] } }
        'X'     { Press $ui.BtnUpdate }
        'Y'     { Open-Notes 'notes' }
        'B'     {
            if ($script:zone -ne 'cards') { Set-Zone 'cards' }
            elseif ($ui.SearchBox.Text) { $ui.SearchBox.Text = '' }
            elseif ($script:filter -ne 'all') { Set-Filter 'all' }
        }
        'LB'    { Cycle-Filter -1 }
        'RB'    { Cycle-Filter 1 }
        'LS'    { Cycle-Sort }
        'Start' { if ($ui.BtnSettings.IsEnabled) { Open-Settings } }
        'Back'  { Toggle-Fullscreen }
    }
    Set-Hints
}
function Find-Child($el, $type) {
    for ($i = 0; $i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($el); $i++) {
        $c = [Windows.Media.VisualTreeHelper]::GetChild($el, $i)
        if ($c -is $type) { return $c }
        $r = Find-Child $c $type
        if ($r) { return $r }
    }
    $null
}

$padTimer = New-Object Windows.Threading.DispatcherTimer
$padTimer.Interval = [TimeSpan]::FromMilliseconds(33)   # 30 checks a second: as quick to the hand, half the work
$padTimer.Add_Tick({
    if ($PadScript) { return }   # scripted presses (testing) stand in for a controller
    $now = [DateTime]::Now
    # with no controller, look for one only twice a second (asking empty slots is slow)
    if (-not $script:padOn -and $now -lt $script:padNextScan) { return }
    $st = New-Object Pad+State
    $ok = [Pad]::TryGet([ref]$st)
    if (-not $ok) { Set-PadConnected $false; $script:padNextScan = $now.AddMilliseconds(500); $script:padPrev = 0; return }
    Set-PadConnected $true
    if (-not $W.IsActive) { $script:padPrev = 0xFFFF; return }   # the game is in front: ignore the pad
    $b = [int]$st.Buttons
    if ($st.LY -gt 16000) { $b = $b -bor 0x1 }
    if ($st.LY -lt -16000) { $b = $b -bor 0x2 }
    if ($st.LX -lt -16000) { $b = $b -bor 0x4 }
    if ($st.LX -gt 16000) { $b = $b -bor 0x8 }
    $prev = $script:padPrev
    $script:padPrev = $b
    if ($prev -eq 0xFFFF) { return }   # coming back to the window: wait until everything is let go once
    foreach ($k in $PadBits.Keys) {
        $bit = $PadBits[$k]
        $down = ($b -band $bit) -ne 0
        if ($down -and -not ($prev -band $bit)) {
            $script:padHeld[$k] = $now.AddMilliseconds(420)
            Pad-Press $k
        } elseif ($down -and $bit -le 0x8 -and $now -ge $script:padHeld[$k]) {
            $script:padHeld[$k] = $now.AddMilliseconds(130)   # held direction repeats
            Pad-Press $k
        }
    }
    # right stick scrolls the release notes
    if ([Math]::Abs([int]$st.RY) -gt 8000 -and $ui.NotesLayer.Visibility -eq 'Visible') {
        if (-not $script:notesScroll) { $script:notesScroll = Find-Child $ui.Viewer ([Windows.Controls.ScrollViewer]) }
        if ($script:notesScroll) { $script:notesScroll.ScrollToVerticalOffset($script:notesScroll.VerticalOffset - [int]$st.RY / 32767.0 * 16) }
    }
})
# the mouse takes over again
$W.Add_MouseMove({
    param($s, $e)
    $p = $e.GetPosition($W)
    if ($script:lastMouse -and $script:padUsed -and ([Math]::Abs($p.X - $script:lastMouse.X) + [Math]::Abs($p.Y - $script:lastMouse.Y)) -gt 6) {
        $script:padUsed = $false; $script:zone = 'cards'; Hide-Ring
    }
    $script:lastMouse = $p
})

# ---------- Wiring ----------
$ui.TitleBar.Add_MouseLeftButtonDown({
    param($s, $e)
    if ($e.ClickCount -eq 2) { Toggle-Fullscreen } elseif (-not $script:isFull) { $W.DragMove() }
})
$ui.BtnMin.Add_Click({ $W.WindowState = 'Minimized' })
$ui.BtnClose.Add_Click({ $W.Close() })
$ui.BtnFull.Add_Click({ Toggle-Fullscreen })
# A button's error shows in the status line and the Activity log (WPF would otherwise drop it silently)
function Try-Action($what, [scriptblock]$sb) {
    try { & $sb } catch {
        $msg = "$what failed: $($_.Exception.Message)"
        Set-Indeterminate $false
        Set-Step $msg $Red
        Log "$msg (line $($_.InvocationInfo.ScriptLineNumber))" $Red
        $script:running = $false
        Update-Views
    }
}
$ui.BtnPlay.Add_Click({
    $g = $script:sel
    if ($g.State -eq 'missing') { Try-Action "Installing $($g.Name)" { Start-Install $g } }
    else { Try-Action "Starting $($g.Name)" { Start-Game $g } }
})
$ui.BtnPlay.Add_MouseEnter({ $ui.PlayGlow.BeginAnimation([Windows.Media.Effects.DropShadowEffect]::BlurRadiusProperty, (Anim 50 250)) })
$ui.BtnPlay.Add_MouseLeave({ $ui.PlayGlow.BeginAnimation([Windows.Media.Effects.DropShadowEffect]::BlurRadiusProperty, (Anim 30 400)) })
$ui.BtnUpdate.Add_Click({ Try-Action "Updating $($script:sel.Name)" { Start-Queue @($script:sel) } })
$ui.BtnAll.Add_Click({ Try-Action 'Updating' { Start-Queue @(Listed-Games | Where-Object { $_.State -eq 'update' }) } })
$ui.BtnCheck.Add_Click({ Check-All -Force })
$ui.BtnNotes.Add_Click({ Open-Notes 'notes' })
$ui.BtnNotesClose.Add_Click({ Close-Notes })
$ui.NotesDim.Add_MouseLeftButtonDown({ Close-Notes })
$ui.BtnFolder.Add_Click({ Start-Process explorer.exe $script:sel.Dir })
$ui.BtnBackups.Add_Click({
    $b = $script:sel.Backups
    New-Item -ItemType Directory -Force $b | Out-Null
    Start-Process explorer.exe $b
})
$ui.TabNotes.Add_MouseLeftButtonUp({ if ($script:tab -ne 'notes') { Set-Tab 'notes' } })
$ui.TabActivity.Add_MouseLeftButtonUp({ if ($script:tab -ne 'activity') { Set-Tab 'activity' } })

$ui.BtnSettings.Add_Click({ Open-Settings })
$ui.BtnSetClose.Add_Click({ Close-Settings })
$ui.BtnSetCancel.Add_Click({ Close-Settings })
$ui.SettingsDim.Add_MouseLeftButtonDown({ Close-Settings })
$ui.BtnSetSave.Add_Click({ Save-SettingsPanel })
$ui.BtnSetDefaults.Add_Click({ foreach ($r in $SetRowDefs) { $r.Box.Text = $Defaults[$r.Key] } })

$ui.BtnUninstall.Add_Click({ Open-Uninstall $script:sel })
$ui.BtnUninstCancel.Add_Click({ Close-Uninstall })
$ui.UninstDim.Add_MouseLeftButtonDown({ Close-Uninstall })
$ui.BtnUninstGo.Add_Click({ Try-Action "Uninstalling $($script:uninstGame.Name)" { Confirm-Uninstall } })
$ui.BtnTokenSite.Add_Click({ Start-Process 'https://github.com/settings/personal-access-tokens/new' })
$ui.TokenBox.Add_LostKeyboardFocus({ Test-Token })
$ui.BtnRomFile.Add_Click({ Pick-RomFile })
$ui.BtnRomFolder.Add_Click({ Pick-RomFolder })
$ui.RomPath.Add_TextChanged({ Check-RomPath })
$ui.BtnRomInstall.Add_Click({ Try-Action "Installing $($script:romGame.Name)" { Confirm-Rom } })
$ui.BtnRomSkip.Add_Click({ Try-Action "Installing $($script:romGame.Name)" { Confirm-Rom -Skip } })
$ui.BtnRomCancel.Add_Click({ Close-RomPicker })
$ui.BtnRomClose.Add_Click({ Close-RomPicker })
$ui.RomDim.Add_MouseLeftButtonDown({ Close-RomPicker })
$ui.BtnNspToggle.Add_Click({
    $open = $ui.NspPanel.Visibility -ne 'Visible'
    Show-NspPanel $open
    if ($open) { [void]$W.Dispatcher.BeginInvoke([Action] { $ui.NspPanel.BringIntoView() }, [Windows.Threading.DispatcherPriority]::Loaded) }
})
$ui.BtnNspBase.Add_Click({ Pick-Into $ui.NspBase "Choose the $($script:romGame.Name) base game NSP" 'Switch package (*.nsp)|*.nsp' })
$ui.BtnNspUpdate.Add_Click({ Pick-Into $ui.NspUpdate "Choose the $($script:romGame.Name) update NSP" 'Switch package (*.nsp)|*.nsp' })
$ui.BtnNspKeys.Add_Click({ Pick-Into $ui.NspKeys 'Choose prod.keys (dumped from your Switch)' 'Switch keys (prod.keys)|prod.keys;*.keys' })
$ui.BtnNspOut.Add_Click({
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = 'Where to save the exefs and romfs folders'
    if ($d.ShowDialog() -eq 'OK') { $ui.NspOut.Text = Join-Path $d.SelectedPath $script:romGame.Id }
})
foreach ($b in $ui.NspBase, $ui.NspUpdate, $ui.NspKeys, $ui.NspOut) { $b.Add_TextChanged({ Check-Nsp }) }
$ui.BtnNspConvert.Add_Click({ Try-Action "Converting the $($script:romGame.Name) NSP" { Start-Convert } })

# Search: typing anywhere goes into the search box
$ui.SearchPill.Add_MouseLeftButtonDown({ $ui.SearchBox.Focus() | Out-Null })
$ui.SearchBox.Add_TextChanged({
    $has = [bool]$ui.SearchBox.Text
    $ui.SearchHint.Visibility = if ($has) { 'Collapsed' } else { 'Visible' }
    $ui.BtnClearSearch.Visibility = if ($has) { 'Visible' } else { 'Collapsed' }
    Apply-Filter
    $first = (Shown-Games)[0]
    if ($has -and $first -and $first -ne $script:sel) { Show-Game $first } else { Queue-Scroll }
})
$ui.SearchBox.Add_GotKeyboardFocus({ $ui.SearchPill.BorderBrush = Brush '#99FFFFFF' })
$ui.SearchBox.Add_LostKeyboardFocus({ $ui.SearchPill.BorderBrush = Brush '#33FFFFFF' })
$ui.BtnClearSearch.Add_Click({ $ui.SearchBox.Text = ''; [Windows.Input.Keyboard]::ClearFocus() })
$W.Add_PreviewTextInput({
    param($s, $e)
    if ($ui.SearchBox.IsKeyboardFocused -or $ui.SettingsLayer.Visibility -eq 'Visible' -or $ui.RomLayer.Visibility -eq 'Visible' -or $ui.UninstLayer.Visibility -eq 'Visible') { return }
    if ($e.Text -match '^[\w&\-\. ]$' -and $e.Text -ne ' ') {
        $ui.SearchBox.Focus() | Out-Null
        $ui.SearchBox.Text += $e.Text
        $ui.SearchBox.CaretIndex = $ui.SearchBox.Text.Length
        $e.Handled = $true
    }
})
$W.Add_PreviewKeyDown({
    param($s, $e)
    $k = $e.Key
    if ($ui.UninstLayer.Visibility -eq 'Visible') {
        if ($k -eq 'Escape') { Close-Uninstall; $e.Handled = $true }
        return
    }
    if (Fb-Visible) {
        switch ($k) {
            'Up'       { Select-Fb ($script:fb.Idx - 1) }
            'Down'     { Select-Fb ($script:fb.Idx + 1) }
            'PageUp'   { Select-Fb ($script:fb.Idx - 8) }
            'PageDown' { Select-Fb ($script:fb.Idx + 8) }
            'Enter'    { Enter-Fb }
            'Right'    { $it = $script:fb.Items[$script:fb.Idx]; if ($it -and $it.Kind -notin 'file', 'nsp', 'up') { Enter-Fb } }
            'Back'     { Back-Fb }
            'Left'     { if ($script:fb.Dir) { Back-Fb } }
            'Escape'   { Close-Fb }
            default    { return }
        }
        Set-Hints; $e.Handled = $true
        return
    }
    if ($ui.RomLayer.Visibility -eq 'Visible') {
        if ($k -eq 'Escape') { Close-RomPicker; $e.Handled = $true }
        elseif ($k -eq 'Enter' -and $ui.BtnRomInstall.IsEnabled) { Press $ui.BtnRomInstall; $e.Handled = $true }
        return
    }
    if ($ui.SettingsLayer.Visibility -eq 'Visible') {
        if ($k -eq 'Escape') { Close-Settings; $e.Handled = $true }
        return
    }
    if ($ui.SearchBox.IsKeyboardFocused) {
        if ($k -eq 'Escape') { if ($ui.SearchBox.Text) { $ui.SearchBox.Text = '' } else { [Windows.Input.Keyboard]::ClearFocus() }; $e.Handled = $true }
        elseif ($k -in 'Enter', 'Down') { [Windows.Input.Keyboard]::ClearFocus(); $e.Handled = $true }
        return
    }
    switch ($k) {
        'F11'    { Toggle-Fullscreen; $e.Handled = $true }
        'Escape' {
            if ($ui.NotesLayer.Visibility -eq 'Visible') { Close-Notes }
            elseif ($script:isFull) { Toggle-Fullscreen }
            $e.Handled = $true
        }
        'Up'     { if ($script:view -eq 'grid' -and $ui.NotesLayer.Visibility -ne 'Visible') { Grid-Move -1; $e.Handled = $true } }
        'Down'   { if ($script:view -eq 'grid' -and $ui.NotesLayer.Visibility -ne 'Visible') { Grid-Move 1; $e.Handled = $true } }
        'G'      { if ([Windows.Input.Keyboard]::Modifiers -eq 'Control') { Toggle-View; $e.Handled = $true } }
        'Left'   { if ($ui.NotesLayer.Visibility -ne 'Visible') { Step-Game -1; $e.Handled = $true } }
        'Right'  { if ($ui.NotesLayer.Visibility -ne 'Visible') { Step-Game 1; $e.Handled = $true } }
        'Enter'  { if ($ui.NotesLayer.Visibility -ne 'Visible') { Press $ui.BtnPlay; $e.Handled = $true } }
        'F5'     { if ($ui.BtnCheck.IsEnabled) { Check-All -Force }; $e.Handled = $true }
        'F'      { if ([Windows.Input.Keyboard]::Modifiers -eq 'Control') { $ui.SearchBox.Focus() | Out-Null; $e.Handled = $true } }
    }
})

$W.Add_Closing({
    param($s, $e)
    if ($script:running -and -not $Snapshot) {
        $r = [Windows.MessageBox]::Show($W,
            "An update is still running. Closing this window won't stop the current game's update, but any game after it in the queue won't be updated.`n`nClose anyway?",
            'Recomp Launcher', 'YesNo', 'Warning')
        if ($r -ne 'Yes') { $e.Cancel = $true; return }
    }
    if (-not $Snapshot -and $script:sel) {
        $Settings.LastGame = $script:sel.Id
        try { Save-Settings $Settings } catch {}
    }
})
# In full screen the launcher is on top of the taskbar and docks only while it is the active window
$W.Add_Activated({ if ($script:isFull) { $W.Topmost = $true } })
$W.Add_Deactivated({ $W.Topmost = $false })
# Taskbar / dock identity of this window (see WindowAppId)
$W.Add_SourceInitialized({
    try {
        $hwnd = (New-Object Windows.Interop.WindowInteropHelper($W)).Handle
        $exe = Join-Path $Root 'Recomp Launcher.exe'
        [WindowAppId]::Set($hwnd, 'RecompLauncher.App', "`"$exe`"", 'Recomp Launcher', "$exe,0")
    } catch {}
    try { [IconX]::WindowCorners($hwnd, $true) } catch {}
})
# On a big screen everything is scaled up together, like a console menu on a TV: the layout is made for about 800
# pixels of height. (The window's rounded corners come from Windows itself.)
$ui.Root.Add_SizeChanged({
    $k = [Math]::Max(1.0, $ui.Root.ActualHeight / 800.0)
    foreach ($el in $ui.Main, $ui.NotesLayer, $ui.SettingsLayer, $ui.RomLayer) { $el.LayoutTransform = New-Object Windows.Media.ScaleTransform($k, $k) }
    $ui.RomCard.MaxHeight = [Math]::Max(300, $ui.Root.ActualHeight / $k - 40)   # Choose your game scrolls when it doesn't fit
    $ui.HeroArt.Width = $ui.Root.ActualHeight * 0.85; $ui.HeroArt.Height = $ui.HeroArt.Width
    Queue-Scroll
})

$W.Add_ContentRendered({
    # open animation
    $ui.Root.BeginAnimation([Windows.UIElement]::OpacityProperty, (Anim 1 350 0))
    $ui.Root.RenderTransform.BeginAnimation([Windows.Media.ScaleTransform]::ScaleXProperty, (Anim 1 450 0.97))
    $ui.Root.RenderTransform.BeginAnimation([Windows.Media.ScaleTransform]::ScaleYProperty, (Anim 1 450 0.97))
    # cards rise in one after another
    $i = 0
    foreach ($g in $Games) {
        $tt = New-Object Windows.Media.TranslateTransform; $g.Tile.RenderTransform = $tt
        $a = Anim 1 420 0; $a.BeginTime = [TimeSpan]::FromMilliseconds(120 + 60 * $i)
        $b = Anim 0 560 40; $b.BeginTime = $a.BeginTime
        $g.Tile.Opacity = 0
        $g.Tile.BeginAnimation([Windows.UIElement]::OpacityProperty, $a)
        $tt.BeginAnimation([Windows.Media.TranslateTransform]::YProperty, $b)
        $i++
    }
    # slowly drifting light behind the game
    $d1 = Calm (Forever (Anim 220 9000 400 'EaseInOut')); $d1.AutoReverse = $true
    $d2 = Calm (Forever (Anim -40 12000 -340 'EaseInOut')); $d2.AutoReverse = $true
    $d3 = Calm (Forever (Anim -340 11000 -480 'EaseInOut')); $d3.AutoReverse = $true
    $ui.Glow1.BeginAnimation([Windows.Controls.Canvas]::LeftProperty, $d1)
    $ui.Glow2.BeginAnimation([Windows.Controls.Canvas]::LeftProperty, $d2)
    $ui.Glow1.BeginAnimation([Windows.Controls.Canvas]::TopProperty, $d3)

    $pick = if ($Select) { $Select } else { $Settings.LastGame }
    $first = $Games | Where-Object { $_.Id -eq $pick } | Select-Object -First 1
    if (-not $first) { $first = $Games[0] }
    Set-Tab 'notes'
    Check-All
    Show-Game $first
    Set-Hints
    if ($Full -or (-not $Snapshot -and "$($Settings.Fullscreen)" -eq 'True')) { Set-Fullscreen $true }
    if ($script:view -eq 'grid') { Set-View 'grid' }
    if ($OpenSettings) { Open-Settings }
    if ($OpenNotes) { Open-Notes 'notes' }
    if ($OpenRom) { $rg = $Games | Where-Object { $_.Id -eq $OpenRom }; if ($rg) { $rg.Latest = $script:cache[$rg.Id]; Open-RomPicker $rg; if ($RomTry) { $ui.RomPath.Text = $RomTry }; if ($RomTry -like '*.nsp') { $ui.RomPath.Text = ''; Show-NspPanel $true; $ui.NspBase.Text = $RomTry } } }
    $padTimer.Start()
    Set-AutoCheck
    if ($PadScript) {
        $script:padSteps = New-Object System.Collections.Queue
        foreach ($s in $PadScript -split ',') { $script:padSteps.Enqueue($s.Trim()) }
        $script:padStepTimer = New-Object Windows.Threading.DispatcherTimer
        $script:padStepTimer.Interval = [TimeSpan]::FromMilliseconds(500)
        $script:padStepTimer.Add_Tick({
            if ($script:padSteps.Count -eq 0) { $script:padStepTimer.Stop(); return }
            Pad-Press $script:padSteps.Dequeue()
        })
        $script:padStepTimer.Start()
    }
    if ($Search) { $ui.SearchBox.Text = $Search }

    if ($Snapshot) {
        $script:snap = New-Object Windows.Threading.DispatcherTimer
        $script:snap.Interval = [TimeSpan]::FromMilliseconds($SnapshotDelay)
        $script:snap.Add_Tick({
            $script:snap.Stop()
            $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$ui.Root.ActualWidth, [int]$ui.Root.ActualHeight, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
            $rtb.Render($ui.Root)
            $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
            $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
            $fs = [IO.File]::Create($Snapshot); $enc.Save($fs); $fs.Close()
            $W.Close()
        })
        $script:snap.Start()
    }
})

[void]$W.ShowDialog()
