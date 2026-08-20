; Inno Setup script for Shieldsmith.
; Build the payload first:  pwsh build\publish.ps1
; Then compile this file with the Inno Setup compiler (iscc build\Shieldsmith.iss).

#define AppName "Shieldsmith"
#define AppVersion "0.4.0"
#define AppPublisher "Cameron Shields"
#define AppExeName "Shieldsmith.exe"

[Setup]
AppId={{8E0C9C1F-6C3E-4E2E-9E0B-2F0B7B2A4D11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=Shieldsmith-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Per-user install by default: no admin prompt, and the CLI PATH entry is
; per-user too, so nothing here needs elevation.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts"; Flags: unchecked
Name: "addtopath"; Description: "Add the shieldsmith command-line tool to PATH"; GroupDescription: "Command line"

[Files]
Source: "..\artifacts\publish\app\Shieldsmith.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\publish\app\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\artifacts\publish\cli\shieldsmith.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\publish\mcp\Shieldsmith.Mcp.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

; Optional: bundle Graphviz for better diagram layout. Graphviz is EPL-licensed;
; if you enable this, ship its licence alongside and keep the attribution below.
; Source: "..\third-party\graphviz\*"; DestDir: "{app}\graphviz"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Per-user PATH entry for the CLI, only when the task is selected.
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}"; Check: NeedsPathEntry(ExpandConstant('{app}')); Tasks: addtopath

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

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
