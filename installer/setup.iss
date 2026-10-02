; GBEX Front Desk Agent — Windows installer

#ifndef AppVersion
  #define AppVersion "1.1.1"
#endif

[Setup]
AppId={{D2C0B0B6-7B09-4A3A-91E7-F5B9D6D9E7E4}
AppName=GBEX Front Desk Agent
AppVersion={#AppVersion}
AppPublisher=GBEX
AppPublisherURL=https://gbex.com.tr
DefaultDirName={autopf}\GbexFrontDeskAgent
DefaultGroupName=GBEX Front Desk Agent
DisableProgramGroupPage=yes
OutputBaseFilename=GbexFrontDeskAgentSetup
OutputDir=..\installer-output
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\GbexFrontDeskAgent.exe
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\GbexFrontDeskAgent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GBEX Front Desk Agent"; Filename: "{app}\GbexFrontDeskAgent.exe"
Name: "{group}\{cm:UninstallProgram,GBEX Front Desk Agent}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\GBEX Front Desk Agent"; Filename: "{app}\GbexFrontDeskAgent.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GbexFrontDeskAgent.exe"; Description: "{cm:LaunchProgram,GBEX Front Desk Agent}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
