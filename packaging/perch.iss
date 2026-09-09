; Inno Setup script for Perch.
;
;   iscc /DAppVersion=1.2.3 /DSourceExe=..\dist\Perch.exe packaging\perch.iss
;
; Installs per user, so there is no UAC prompt and no admin rights needed. That
; matches how the app behaves anyway: settings live in %APPDATA% and the startup
; entry is under HKCU.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceExe
  #define SourceExe "..\dist\Perch.exe"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "Perch"
#define Publisher "Eagle Spirit"
#define RepoUrl "https://github.com/eaglespirittech/perch"

[Setup]
; Keep this GUID stable forever: it is how Windows recognises an upgrade.
AppId={{8E4B1C22-7F3A-4B6E-9C1D-2A5F0E7D3B91}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir={#OutputDir}
OutputBaseFilename=Perch-{#AppVersion}-setup
SetupIconFile=..\assets\perch.ico
UninstallDisplayIcon={app}\Perch.exe
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

; Perch lives in the notification area, so an old copy is probably running.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "Perch.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Perch.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Perch.exe"; Tasks: desktopicon

[Registry]
; Never written here - this only cleans up after the app's own "Start with Windows"
; toggle, so uninstalling does not leave a startup entry pointing at a deleted file.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Perch"; \
    Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\Perch.exe"; Description: "Start Perch"; Flags: nowait postinstall skipifsilent
