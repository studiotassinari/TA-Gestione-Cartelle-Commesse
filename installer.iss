; Verifica se il parametro AppVersion non è stato fornito al compilatore.
#ifndef AppVersion
; Definisce il valore predefinito del parametro quando manca un valore esterno.
  #define AppVersion "2.6.0"
; Termina la definizione condizionale del parametro.
#endif
; Verifica se il parametro PublishDir non è stato fornito al compilatore.
#ifndef PublishDir
; Definisce il valore predefinito del parametro quando manca un valore esterno.
  #define PublishDir "artifacts\publish"
; Termina la definizione condizionale del parametro.
#endif
; Verifica se il parametro InstallerOutput non è stato fornito al compilatore.
#ifndef InstallerOutput
; Definisce il valore predefinito del parametro quando manca un valore esterno.
  #define InstallerOutput "artifacts\installer"
; Termina la definizione condizionale del parametro.
#endif
; Apre la sezione [Setup] della configurazione Inno Setup.
[Setup]
; Mantiene lʼidentificativo stabile tra versioni per aggiornare la stessa installazione.
AppId={{D0B63D8F-FF51-4B68-9553-4B49A78C7B3A}
; Definisce il nome mostrato dal programma di installazione e da Windows.
AppName=TA-Gestione Cartelle Commesse
; Usa la versione passata dal processo di compilazione.
AppVersion={#AppVersion}
; Indica il produttore visualizzato nelle informazioni del programma.
AppPublisher=Studio Tassinari
; Collega le informazioni del produttore al repository del progetto.
AppPublisherURL=https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse
; Propone la cartella programmi del profilo utente corrente.
DefaultDirName={localappdata}\Programs\TA-Gestione Cartelle Commesse
; Definisce la cartella dei collegamenti nel menu Start.
DefaultGroupName=TA-Gestione Cartelle Commesse
; Evita una pagina aggiuntiva per scegliere il gruppo del menu Start.
DisableProgramGroupPage=yes
; Installa per il solo utente corrente senza richiedere privilegi amministrativi.
PrivilegesRequired=lowest
; Consente lʼinstallazione su sistemi compatibili con eseguibili x64.
ArchitecturesAllowed=x64compatible
; Usa la modalità di installazione a 64 bit sui sistemi compatibili.
ArchitecturesInstallIn64BitMode=x64compatible
; Richiede Windows con versione di sistema almeno 10.0.
MinVersion=10.0
; Scrive il setup nella cartella indicata dal parametro di compilazione.
OutputDir={#InstallerOutput}
; Mantiene un nome stabile per il file scaricabile del setup.
OutputBaseFilename=TA-Gestione-Cartelle-Commesse-Setup-{#AppVersion}
; Comprime i file distribuiti usando LZMA2.
Compression=lzma2
; Comprime insieme i file per ridurre le dimensioni del pacchetto.
SolidCompression=yes
; Usa lʼaspetto moderno della procedura guidata Inno Setup.
WizardStyle=modern
; Mostra lʼicona del programma nellʼelenco delle applicazioni installate.
UninstallDisplayIcon={app}\TA-Gestione Cartelle Commesse.exe
; Consente al setup di gestire la chiusura delle applicazioni che usano i file da aggiornare.
CloseApplications=yes
; Disabilita il riavvio automatico delle applicazioni dopo lʼinstallazione.
RestartApplications=no
; Riutilizza la cartella della versione già installata per lo stesso utente.
UsePreviousAppDir=yes
; Nasconde automaticamente la scelta cartella durante gli aggiornamenti.
DisableDirPage=auto
; Impedisce lʼesecuzione contemporanea di più istanze di questo setup.
SetupMutex=TA-Gestione-Cartelle-Commesse-Setup
; Abilita il log diagnostico di installazione nella cartella temporanea.
SetupLogging=yes
; Apre la sezione [Languages] della configurazione Inno Setup.
[Languages]
; Carica i messaggi italiani della procedura guidata.
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
; Apre la sezione [Tasks] della configurazione Inno Setup.
[Tasks]
; Offre il collegamento sul desktop come opzione inizialmente non selezionata.
Name: "desktopicon"; Description: "Crea un collegamento sul desktop"; Flags: unchecked
; Apre la sezione [Files] della configurazione Inno Setup.
[Files]
; Copia ricorsivamente tutti i file pubblicati nella cartella dellʼapplicazione sostituendo quelli precedenti.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Apre la sezione Source: "deployment\Update-Application.ps1"; DestDir: "{app}"
; Collegamenti del menu Start.
Source: "deployment\Update-Application.ps1"; DestDir: "{app}"
[Icons]
; Crea il collegamento del menu Start puntando allʼeseguibile installato.
Name: "{group}\TA-Gestione Cartelle Commesse"; Filename: "{app}\TA-Gestione Cartelle Commesse.exe"
; Crea il collegamento sul desktop solo se lʼutente ha selezionato lʼopzione corrispondente.
Name: "{autodesktop}\TA-Gestione Cartelle Commesse"; Filename: "{app}\TA-Gestione Cartelle Commesse.exe"; Tasks: desktopicon
; Fine chiude esclusivamente il setup. Avviare il programma dal menu Start.
