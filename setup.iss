[Setup]
AppName=Ente Auth Community
AppVersion=1.0.4
DefaultDirName={autopf}\EnteAuth
DefaultGroupName=Ente Auth Community
OutputDir=Output
OutputBaseFilename=EnteAuth-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
PrivilegesRequired=lowest

[Files]
Source: "publish_output\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Ente Auth Community"; Filename: "{app}\Ente.Auth.App.exe"
Name: "{autodesktop}\Ente Auth Community"; Filename: "{app}\Ente.Auth.App.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\Ente.Auth.App.exe"; Description: "{cm:LaunchProgram,Ente Auth Community}"; Flags: nowait postinstall skipifsilent
