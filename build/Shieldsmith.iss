; Inno Setup script for Shieldsmith.
;
; Build the payload first:  powershell -File build\publish.ps1
; Then compile this file:   iscc build\Shieldsmith.iss
;
; publish.ps1 writes version.iss, so the installer version is read from
; Directory.Build.props rather than typed here. It said 0.4.0 against a 0.9.0
; application for four phases, which is exactly what hand-maintained duplicates
; do.

#include "version.iss"

#define AppName "Shieldsmith"
#define AppPublisher "Cameron Shields"
#define AppUrl "https://www.cameronshields.co.uk/"
#define AppExeName "Shieldsmith.exe"
#define PayloadDir "..\artifacts\Shieldsmith"

[Setup]
AppId={{8E0C9C1F-6C3E-4E2E-9E0B-2F0B7B2A4D11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\artifacts
OutputBaseFilename=Shieldsmith-{#AppVersion}-setup
SetupIconFile=..\src\Shieldsmith.App\Assets\shieldsmith.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
DisableDirPage=auto
ShowLanguageDialog=no

; Per-user install by default: no admin prompt, no UAC dialogue, and the PATH
; entry is per-user too. A stranger can install this on a locked-down work
; machine without asking anyone.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts"; Flags: unchecked
Name: "addtopath"; Description: "Add the shieldsmith command line tool to PATH"; GroupDescription: "Command line"
Name: "associate"; Description: "Open solution .zip exports with Shieldsmith from the right-click menu"; GroupDescription: "File handling"; Flags: unchecked

[Files]
; The whole self-contained payload: three executables sharing one copy of the
; .NET and WPF runtime, plus the licence and documentation.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Shieldsmith on the web"; Filename: "{#AppUrl}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Per-user PATH entry for the CLI, only when the task is selected.
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}"; \
    Check: NeedsPathEntry(ExpandConstant('{app}')); Tasks: addtopath; \
    Flags: preservestringtype

; "Document with Shieldsmith" on any .zip. Deliberately not a file association:
; taking over .zip wholesale would be hostile, and a solution export is just a
; zip, so there is no distinct extension to claim.
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\Shieldsmith"; \
    ValueType: string; ValueName: ""; ValueData: "Document with Shieldsmith"; \
    Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\Shieldsmith"; \
    ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExeName},0"; \
    Tasks: associate
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\Shieldsmith\command"; \
    ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; \
    Flags: uninsdeletekey; Tasks: associate

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The app writes its working files here; leaving them behind after an uninstall
; would be litter. API keys live in %APPDATA%\Shieldsmith and are deliberately
; NOT removed: an uninstall should not silently destroy the user's credentials.
Type: filesandordirs; Name: "{localappdata}\Temp\Shieldsmith"

[Code]
function NeedsPathEntry(Dir: string): Boolean;
var
  Existing: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Existing) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Existing) + ';') = 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Existing: string;
  AppDir: string;
  P: Integer;
begin
  { Remove our PATH entry on uninstall, otherwise every install and uninstall
    cycle leaves another dead directory behind in the user's PATH. }
  if CurUninstallStep = usUninstall then
  begin
    if RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Existing) then
    begin
      AppDir := ExpandConstant('{app}');
      P := Pos(Uppercase(';' + AppDir), Uppercase(Existing));
      if P > 0 then
      begin
        Delete(Existing, P, Length(AppDir) + 1);
        RegWriteExpandStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Existing);
      end;
    end;
  end;
end;
