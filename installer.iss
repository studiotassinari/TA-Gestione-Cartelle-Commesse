#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "artifacts\publish"
#endif
#ifndef InstallerOutput
  #define InstallerOutput "artifacts\installer"
#endif
[Setup]
AppId={{D0B63D8F-FF51-4B68-9553-4B49A78C7B3A}
AppName=TA-Gestione Cartelle Commesse
AppVersion={#AppVersion}
AppPublisher=Studio Tassinari
AppPublisherURL=https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse
DefaultDirName={localappdata}\Programs\TA-Gestione Cartelle Commesse
DefaultGroupName=TA-Gestione Cartelle Commesse
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#InstallerOutput}
OutputBaseFilename=TA-Gestione-Cartelle-Commesse-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\TA-Gestione Cartelle Commesse.exe
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
DisableDirPage=auto
SetupMutex=TA-Gestione-Cartelle-Commesse-Setup
SetupLogging=yes
[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
[Tasks]
Name: "desktopicon"; Description: "Crea un collegamento sul desktop"; Flags: unchecked
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\TA-Gestione Cartelle Commesse"; Filename: "{app}\TA-Gestione Cartelle Commesse.exe"
Name: "{autodesktop}\TA-Gestione Cartelle Commesse"; Filename: "{app}\TA-Gestione Cartelle Commesse.exe"; Tasks: desktopicon
; Fine chiude esclusivamente il setup. Avviare il programma dal menu Start.
