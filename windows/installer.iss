#define ProductName "TikTok-Bot"
[Setup]
AppId=TikTokBot.Windows
AppName={#ProductName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\TikTok-Bot
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=build
OutputBaseFilename=TikTok-Bot-Setup
SetupIconFile=TikTok-Bot.ico
UninstallDisplayIcon={app}\TikTok-Bot.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "build\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Tasks]
Name: "desktopicon"; Description: "Desktop-Symbol erstellen"; Flags: unchecked

[Icons]
Name: "{userprograms}\TikTok-Bot"; Filename: "{app}\TikTok-Bot.exe"
Name: "{userdesktop}\TikTok-Bot"; Filename: "{app}\TikTok-Bot.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TikTok-Bot.exe"; Flags: nowait; Check: RestartAfterUpdate

[Code]
function RestartAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATEBOT|0}') = '1';
end;
