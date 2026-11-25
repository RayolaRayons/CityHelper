; Inno Setup 6 script for the installed version of CityHelper. Built by build\build.ps1, which passes
; AppVersion, SourceExe and OutputDir. Installed copies keep their settings in %APPDATA%\CityHelper.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\dist\publish\CityHelper.exe"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{6D1C3B7A-4E52-4B7E-9C0B-5C1F0C7E2A11}
AppName=CityHelper
AppVersion={#AppVersion}
AppPublisher=RayolaRayons
DefaultDirName={autopf}\CityHelper
DefaultGroupName=CityHelper
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\CityHelper.exe
OutputDir={#OutputDir}
OutputBaseFilename=CityHelper-{#AppVersion}-setup-win-x64
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Installs for all users when run as administrator, or just for the current user when the person declines
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\CityHelper"; Filename: "{app}\CityHelper.exe"
Name: "{group}\Uninstall CityHelper"; Filename: "{uninstallexe}"
Name: "{autodesktop}\CityHelper"; Filename: "{app}\CityHelper.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CityHelper.exe"; Description: "Start CityHelper"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings in %APPDATA%\CityHelper are kept on purpose; the uninstaller asks (below) whether to remove them

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\CityHelper');
    if DirExists(DataDir) then
      if MsgBox('Also delete your CityHelper settings (' + DataDir + ')?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
