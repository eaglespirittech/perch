; Inno Setup script for Perch: the desk app and the perch-cli command line tool.
;
;   iscc /DAppVersion=1.2.3 packaging\perch.iss
;
; Both programs are published into dist\app first, where they share one copy of the
; .NET runtime. Installs per user, so there is no UAC prompt and no admin rights are
; needed - which matches how the app behaves anyway: settings live in %APPDATA% and
; the startup entry is under HKCU.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\dist\app"
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

; perch-cli goes on PATH, so tell Windows the environment changed.
ChangesEnvironment=yes

; Perch lives in the notification area, so an old copy is probably running.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "addtopath"; Description: "Add perch-cli to PATH so it works in any terminal"; GroupDescription: "Command line:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Straight into the app list rather than a one-item folder.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\Perch.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Perch.exe"; Tasks: desktopicon

[Registry]
; Never written here - this only cleans up after the app's own "Start with Windows"
; toggle, so uninstalling does not leave a startup entry pointing at a deleted file.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Perch"; \
    Flags: dontcreatekey uninsdeletevalue

Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}"; Tasks: addtopath; Check: NeedsAddPath(ExpandConstant('{app}'))

[Run]
Filename: "{app}\Perch.exe"; Description: "Start Perch"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsAddPath(Param: string): boolean;
var
  Existing: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Existing) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Uppercase(Param) + ';', ';' + Uppercase(Existing) + ';') = 0;
end;

procedure RemoveFromPath(Folder: string);
var
  Existing, Updated: string;
  Position: Integer;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Existing) then exit;

  Updated := ';' + Existing + ';';
  Position := Pos(';' + Uppercase(Folder) + ';', Uppercase(Updated));
  if Position = 0 then exit;

  Delete(Updated, Position, Length(Folder) + 1);
  Updated := Copy(Updated, 2, Length(Updated) - 2);
  RegWriteExpandStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Updated);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromPath(ExpandConstant('{app}'));
end;
