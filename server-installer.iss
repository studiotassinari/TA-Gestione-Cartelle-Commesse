#ifndef AppVersion
  #define AppVersion "2.4.0"
#endif
#ifndef PublishDir
  #define PublishDir "artifacts\server"
#endif
#ifndef InstallerOutput
  #define InstallerOutput "artifacts\installer"
#endif
[Setup]
; Identità distinta dal client, stabile negli aggiornamenti futuri.
AppId={{07E5CE80-53A7-4B01-A6C7-E2898FBDF247}
AppName=TA-Gestione Cartelle Commesse - Server
AppVersion={#AppVersion}
AppPublisher=Studio Tassinari
DefaultDirName={autopf}\Tassinari\Commesse Server
DefaultGroupName=TA Commesse Server
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.20348
OutputDir={#InstallerOutput}
OutputBaseFilename=TA-Gestione-Cartelle-Commesse-Server-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
SetupMutex=TACommesseServerSetup
UsePreviousAppDir=yes
DisableDirPage=auto
RestartApplications=no
UninstallDisplayIcon={app}\TA.Server.exe
[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "deployment\Service-Manage.ps1"; DestDir: "{app}"
Source: "deployment\Configure-Server.ps1"; DestDir: "{app}"
Source: "deployment\SERVER-CLIENT.md"; DestDir: "{app}"
Source: "artifacts\manager\*"; DestDir: "{app}\Manager"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "deployment\Update-Application.ps1"; DestDir: "{app}"
[Icons]
Name: "{group}\Gestione servizio e diagnostica"; Filename: "{app}\Manager\TA.ServiceManager.exe"
Name: "{group}\Guida configurazione"; Filename: "{app}\SERVER-CLIENT.md"
[Code]
function ManageService(Action: String): String;
var ResultCode: Integer; ScriptPath: String;
begin
  Result := '';
  ScriptPath := ExpandConstant('{app}\Service-Manage.ps1');
  if not FileExists(ScriptPath) then exit;
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '" -Action ' + Action,
    ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := 'Impossibile gestire il servizio Windows.'
  else if ResultCode <> 0 then Result := 'Gestione del servizio non riuscita. Codice: ' + IntToStr(ResultCode);
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { Arresta il servizio prima di sostituire i file; non termina processi estranei. }
  Result := ManageService('Stop');
end;
procedure CurStepChanged(CurStep: TSetupStep);
var Error: String;
begin
  if CurStep = ssPostInstall then begin
    Error := ManageService('Install');
    if Error <> '' then RaiseException(Error);
  end;
end;
function InitializeUninstall(): Boolean;
var Error: String;
begin
  Error := ManageService('Remove');
  Result := Error = '';
  if not Result then MsgBox(Error, mbError, MB_OK);
end;
