[Setup]
AppName=Ente Auth
AppVersion=1.0.0
DefaultDirName={autopf}\EnteAuth
DefaultGroupName=Ente Auth
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
Name: "{group}\Ente Auth"; Filename: "{app}\Ente.Auth.App.exe"
Name: "{autodesktop}\Ente Auth"; Filename: "{app}\Ente.Auth.App.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\Ente.Auth.App.exe"; Description: "{cm:LaunchProgram,Ente Auth}"; Flags: nowait postinstall skipifsilent
