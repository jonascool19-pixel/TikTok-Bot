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

[InstallDelete]
Type: files; Name: "{app}\ai\llama-*.exe"

[Tasks]
Name: "desktopicon"; Description: "Desktop-Symbol erstellen"; Flags: unchecked

[Icons]
Name: "{userprograms}\TikTok-Bot"; Filename: "{app}\TikTok-Bot.exe"
Name: "{userdesktop}\TikTok-Bot"; Filename: "{app}\TikTok-Bot.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TikTok-Bot.exe"; Flags: nowait; Check: RestartAfterUpdate

[Code]
var
  InstallStarted: LongWord;
  LastProgressUpdate: LongWord;
  ProgressDetails: TNewStaticText;

function GetTickCount: LongWord;
  external 'GetTickCount@kernel32.dll stdcall';

procedure InitializeWizard;
begin
  ProgressDetails := TNewStaticText.Create(WizardForm);
  ProgressDetails.Parent := WizardForm.ProgressGauge.Parent;
  ProgressDetails.Left := WizardForm.ProgressGauge.Left;
  ProgressDetails.Top := WizardForm.ProgressGauge.Top + WizardForm.ProgressGauge.Height + ScaleY(10);
  ProgressDetails.Caption := 'Installation wird vorbereitet ...';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    InstallStarted := GetTickCount;
    LastProgressUpdate := InstallStarted;
  end;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  NowTick: LongWord;
  Percent, Remaining: Integer;
  TimeText: String;
begin
  if (MaxProgress <= 0) or (CurProgress <= 0) then Exit;
  NowTick := GetTickCount;
  if (NowTick - LastProgressUpdate < 250) and (CurProgress < MaxProgress) then Exit;
  LastProgressUpdate := NowTick;
  Percent := Trunc(100.0 * CurProgress / MaxProgress);
  if CurProgress >= MaxProgress then
    TimeText := 'Installation wird abgeschlossen ...'
  else if (NowTick - InstallStarted < 2000) or (Percent < 3) then
    TimeText := 'Restzeit wird berechnet ...'
  else
  begin
    Remaining := Trunc(((NowTick - InstallStarted) / 1000.0) * (MaxProgress - CurProgress) / CurProgress);
    TimeText := Format('Noch ca. %d Min. %d Sek.', [Remaining div 60, Remaining mod 60]);
  end;
  ProgressDetails.Caption := Format('%d %%  |  %s', [Percent, TimeText]);
end;

function RestartAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATEBOT|0}') = '1';
end;
