# Archivio SQLite e scansioni concorrenti — 2.22.1

SQLite è incluso nel pacchetto, senza costi di licenza né un servizio SQL da installare. Client e MCP continuano a usare il servizio TA; non accedono direttamente al database.

## Posizione e impostazioni

Il database si trova per impostazione predefinita in `%PROGRAMDATA%\Tassinari\CommesseServer\Data\inventario-file\inventario.sqlite`. `Server.DatabaseFolder` permette un'altra cartella su disco locale. Condivisioni UNC e unità di rete sono rifiutate; il percorso non può stare nelle sorgenti censite. L'account del servizio deve avere Modifica sulla cartella locale, Lettura sulle sorgenti e Modifica sull'archivio derivato/backups.

`Server.ArchiveFolder` conserva il percorso dell'archivio precedente, per migrazione, report e backup sulla rete. Non spostare il file SQLite attivo in questa condivisione. I file tecnici `-wal` e `-shm` sono parte del funzionamento del database; non eliminarli mentre il servizio è attivo.

`Server.MaxConcurrentScans` vale 2 per impostazione predefinita (1–4). Le letture sono concorrenti, le scritture transazionali sono coordinate. Non vengono accodate più elaborazioni globali sovrapposte. Le commesse invariate mantengono la stessa generazione; nessuno storico integrale viene duplicato. File cancellati vengono rimossi soltanto dopo una lettura completa; errori di accesso conservano i dati precedenti con segnalazione.

La prima indicizzazione è completa. Ai controlli successivi il server confronta nomi, dimensioni, date e struttura: soltanto le unità cambiate richiedono un nuovo inventario approfondito. Il controllo dei metadati richiede comunque accesso ricorsivo alla rete; non è una lettura istantanea della dimensione di una cartella. File modificati mantenendo intenzionalmente nome, dimensione e data non sono rilevabili senza leggerne il contenuto. Gli originali non vengono modificati e non sono incorporati nel database.

## Migrazione e pulizia

Al primo avvio il servizio importa le sole generazioni puntate da `latest.json`, verifica il numero e i metadati delle voci, conserva analisi geologiche/MUR e coordinate, e controlla l'integrità SQLite. L'importazione è riprendibile dopo un'interruzione. Un errore impedisce la pulizia; lo stato riporta la cartella in elaborazione.

Prima di eliminare i vecchi inventari viene prodotto un backup coerente, verificato e copiato in `ArchiveFolder\backup-database\migrazione.sqlite`. La pulizia riguarda solo i formati e percorsi generati dal gestionale e verificati come migrati; non usa eliminazioni ricorsive indiscriminate. File estranei, cartelle collegate e dati non verificati restano intatti. La copia di migrazione è mantenuta separatamente dalle sette copie giornaliere. Configurazione, report ed eventuale cache geografica non sono documenti originali e possono restare fuori dal database.

Il servizio può richiedere tempo al primo avvio se l'archivio precedente è grande. Consultare lo stato della migrazione; non riavviare ripetutamente il servizio. Lo spazio locale deve contenere il database, i suoi file tecnici e una copia temporanea di backup. Un disco insufficiente o un backup non verificato interrompono la pulizia.

## Backup e ripristino

Al termine della prima elaborazione del giorno il servizio usa l'API di backup SQLite, verifica `integrity_check`, copia il risultato sulla rete e confronta le impronte SHA-256. `Server.BackupRetention` vale 7 (configurabile 1–30); le copie più vecchie vengono eliminate solo dopo la verifica della nuova. Un errore conserva il backup precedente ed è visibile nello stato. Una copia di rete protegge dalla perdita della VM, ma non sostituisce un backup aziendale offline/immutabile contro guasti del sistema di storage o ransomware.

Per ripristinare: arrestare `TACommesseServer`; conservare il database corrente e gli eventuali file `-wal`/`-shm` in una cartella separata; verificare il backup con SQLite; copiarlo come `inventario.sqlite` nella cartella locale vuota; ripristinare le autorizzazioni dell'account del servizio; avviare il servizio e verificare cataloghi e conteggi. Non sostituire un database aperto e non accompagnare un backup con vecchi file WAL/SHM. Il backup contiene i metadati e le analisi, non i documenti originali o la configurazione Windows del servizio, che richiedono la normale politica di backup aziendale.

Le esportazioni JSON restano disponibili dal client. Il tunnel MCP resta ad avvio manuale con inserimento della chiave runtime; non serve esporre porte SQL.
