; Recomp Launcher installer (Inno Setup 6). Build it with build-installer.ps1 in this folder, which stages only the
; files the launcher needs into installer\stage (never settings.json, cache.json, roms.json, playtime.json, logs,
; backups or dumps: those are your own and settings.json holds your GitHub token) and then compiles this script.
;
; Installs per user into %LOCALAPPDATA%\Programs\Recomp Launcher, so no admin rights are needed and the launcher can
; keep writing its settings, logs and backups next to itself as it does now. Works on a clean Windows 10/11 PC:
; PowerShell 5.1, .NET Framework 4 and WPF are part of Windows. Most games need the Microsoft Visual C++ runtime,
; so setup offers to download and install it when it's missing (that one step asks for admin).

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "Recomp Launcher"
#define MyAppExe "Recomp Launcher.exe"
#define Stage "stage"

[Setup]
AppId={{6E0B3C52-8F4A-4C7B-9C1D-2A7E5B3F9D41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Recomp Launcher
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=Output
OutputBaseFilename=RecompLauncher-Setup-{#MyAppVersion}
SetupIconFile=..\src\launcher.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "vcredist"; Description: "Download and install the Microsoft Visual C++ runtime (most games need it)"; GroupDescription: "Needed by games:"; Check: VCRedistMissing

[Files]
Source: "{#Stage}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; AppUserModelID matches the one Recomp-Launcher.ps1 sets, so the taskbar shows the launcher's icon, not PowerShell's
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; AppUserModelID: "RecompLauncher.App"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; AppUserModelID: "RecompLauncher.App"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Start {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Things the launcher makes and can make again. settings.json, playtime.json, roms.json, backups\ (game saves) and
; dumps\ are kept, so reinstalling picks up where you left off; delete the folder by hand to remove them too.
Type: filesandordirs; Name: "{app}\logs"
Type: files; Name: "{app}\cache.json"
Type: filesandordirs; Name: "{app}\totk-updater\work"
Type: filesandordirs; Name: "{app}\totk-updater\logs"

[Code]
var
  DownloadPage: TDownloadWizardPage;

function VCRedistMissing: Boolean;
var
  Installed: Cardinal;
begin
  Result := not (RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed)
                 and (Installed = 1));
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID = wpReady) and WizardIsTaskSelected('vcredist') then begin
    DownloadPage.Clear;
    DownloadPage.Add('https://aka.ms/vs/17/release/vc_redist.x64.exe', 'vc_redist.x64.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        // "runas" shows the Windows admin prompt; the runtime installs for the whole PC
        if not ShellExec('runas', ExpandConstant('{tmp}\vc_redist.x64.exe'), '/install /quiet /norestart', '',
                         SW_SHOW, ewWaitUntilTerminated, ResultCode) then
          MsgBox('The Visual C++ runtime was not installed. You can get it later from' + #13#10 +
                 'https://aka.ms/vs/17/release/vc_redist.x64.exe', mbInformation, MB_OK);
      except
        MsgBox('Could not download the Visual C++ runtime: ' + GetExceptionMessage + #13#10 +
               'Setup carries on; you can install it later from https://aka.ms/vs/17/release/vc_redist.x64.exe',
               mbInformation, MB_OK);
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;
