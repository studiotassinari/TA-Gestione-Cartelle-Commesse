## Versione 2.9.0: apertura su Z e archivi sotto lavori

Il doppio clic e i comandi Apri del client traducono la condivisione aziendale nel percorso Z del PC. La conversione supporta sia il nome DNS completo sia quello breve, senza cambiare condivisioni diverse. Il server mantiene i percorsi UNC originali. Gli archivi vengono selezionati in Esplora file senza estrazione.

La scheda Archivio include adesso ZIP e 7z presenti ricorsivamente nella radice lavori, oltre alla radice archivio. Ogni file e censito una sola volta anche con radici sovrapposte. Le sottocartelle non diventano nuove commesse. L'indice viene aggiornato dal monitor e dal controllo ogni minuto; le letture dei contenuti, le dimensioni e le gerarchie restano limitate al report delle 23 italiane. Tutte le sorgenti restano in sola lettura. Una radice non accessibile viene segnalata e non cancella le righe precedenti non verificabili.

Aggiornare prima Server e poi Client tramite l'aggiornatore diretto.

# TA-Gestione Cartelle Commesse

Applicazione .NET 10 con client WPF/MVVM e servizio Windows per Windows Server 2022 nel dominio aziendale.

## Versione 2.8.0: elenco continuo, report alle 23

Il monitoraggio continuo riguarda la creazione, eliminazione e rinomina delle cartelle censite (e dei file ZIP/7z per l'Archivio). Il controllo di recupero ogni minuto aggiorna soltanto l'elenco. Le modifiche ai file non avviano piu analisi ricorsive; anche il comando Aggiorna aggiorna soltanto l'elenco. Nessuna commessa viene identificata nelle sottocartelle di una commessa.

Dimensioni, date dei file, preventivi e gerarchie vengono elaborati una volta al giorno a partire dalle 23:00, ora italiana, con gestione dell'ora legale indipendente dal fuso della VM. Il calendario e interno al servizio: nessuna attivita pianificata Windows e nessuna dipendenza dall'apertura del client. Le letture possono durare oltre la mezzanotte e non si sovrappongono. Se il servizio riparte tra le 23 e mezzanotte e manca il report del giorno, ritenta; un avvio prima delle 23 attende le 23, senza recuperare i giorni in cui era spento.

Il server conserva i report HTML datati nella sottocartella report-giornalieri dell'archivio JSON configurato. Export > Scarica ultimo report giornaliero (23:00) scarica dal server l'ultimo report completato. I report precedenti restano nella cartella condivisa; non vengono cancellati automaticamente. La consultazione della gerarchia usa esclusivamente l'ultima lettura salvata, anche dopo un riavvio. Il report indica avvio, fine, ultima verifica e dati precedenti/non verificati in caso di errori o accessi parziali. Non e una fotografia atomica del filesystem.

Fino alla prima elaborazione, una nuova cartella mostra In attesa del report delle 23:00. Per le altre restano visibili i dati dell'ultima elaborazione, con la data di verifica. La sorgente viene sempre letta senza modifiche. Questa modalita sostituisce la verifica continua e la coda prioritaria delle versioni precedenti.

Aggiornare prima Server e poi Client tramite il programma di aggiornamento diretto. Restano invariati autenticazione Windows, TCP dedicato, backup e aggiornamenti all'apertura senza attivita pianificate.

## Scarica e installa - versione 2.9.0

**[Scarica il programma di aggiornamento diretto 2.9.0](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.9.0/TA-Commesse-Aggiornatore-2.9.0.exe)**. Eseguirlo prima sulla VM (Server), poi sui PC (Client). I setup sotto servono solo per installazioni iniziali.

- [Scarica installatore Server 2.9.0](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.9.0/TA-Gestione-Cartelle-Commesse-Server-Installazione-Iniziale-2.9.0.exe)
- [Scarica installatore Client 2.9.0](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.9.0/TA-Gestione-Cartelle-Commesse-Installazione-Iniziale-2.9.0.exe)

Il server verifica commesse, gare e archivio ogni minuto, senza sovrapporre scansioni della stessa categoria, e invia notifiche push ai client. Il menu Aiuto > Informazioni mostra la versione client e, quando disponibile, quella server. Resta necessario configurare account, gruppo AD e SPN seguendo la guida.

## Due installatori

- **Client**: `TA-Gestione-Cartelle-Commesse-Installazione-Iniziale-<versione>.exe`, per i PC Windows. Aggiorna la precedente applicazione mantenendo la stessa identità di installazione.
- **Server**: `TA-Gestione-Cartelle-Commesse-Server-Installazione-Iniziale-<versione>.exe`, da eseguire come amministratore sulla VM. Registra `TACommesseServer`; alla prima installazione resta fermo fino alla configurazione.

[Release pubblicate](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases). I setup includono il runtime e non sono firmati digitalmente; le impronte sono in `SHA256SUMS.txt`.

**[Guida di installazione e configurazione server-client](deployment/SERVER-CLIENT.md)**

## Notifiche dettagliate

Il push segnala le modifiche e il client recupera automaticamente lo stato aggiornato. Gli avvisi indicano la cartella aggiunta, rimossa o modificata e i dati cambiati. Visualizza > Modifiche ricevute conserva le ultime 200 segnalazioni per categoria nella sessione. Il confronto esclude la sola ora di scansione, ma rileva anche cartelle vuote e rinomine tramite la firma dei metadati. Un cambio di percorso viene mostrato come rimozione e aggiunta; non e uno storico completo delle operazioni sui singoli file.

## Nuova commessa da modello

Il menu **Nuovo > Commessa da modello...** richiede codice CXXYYY, cliente, luogo facoltativo e argomento. Anteprima mostra destinazione e struttura; Crea commessa crea solo cartelle vuote. Il modello predefinito e `\\ingtassinari.it\data\lavori\2026\C26198 - CLIENTE - LUOGO - COSA`, con 14 sottocartelle al momento della verifica; il file Excel non viene copiato. Il percorso si configura sul server con JobTemplate. Il modello per le gare non e ancora definito.

La cartella annuale di destinazione deve esistere. Codici duplicati, percorsi non validi e collegamenti vengono rifiutati. Il servizio necessita di permessi di creazione e rinomina sulla destinazione; non vengono concessi automaticamente. Gli utenti autorizzati dal gruppo AD possono usare il comando. Consultare la guida per i permessi minimi e i limiti.

## Area di notifica e aggiornamenti

Client e gestore server restano attivi nell'area di notifica quando vengono minimizzati o chiusi. Dall'icona si puo riaprire la finestra o scegliere Esci; uscire dal gestore non arresta il servizio Windows. Le notifiche Windows segnalano modifiche effettive, senza ripetersi per scansioni identiche; la loro visualizzazione dipende dalle impostazioni di notifica di Windows.

Gli aggiornamenti vengono controllati solo all'avvio del client o del gestore, oppure con Verifica aggiornamenti. Nessuna attivita pianificata, controllo periodico o controllo in chiusura. I pacchetti vengono scaricati dalla release pubblica di questo repository e verificati con dimensione e digest SHA-256 di GitHub; l'interfaccia viene chiusa senza forzarla e riaperta dopo l'installazione. Il server si aggiorna soltanto quando viene aperto il gestore con privilegi amministrativi. Nessun riavvio automatico di Windows. Non sono supportati aggiornamenti automatici tra versioni principali diverse.

## Comunicazione dedicata

Nessun HTTP, HTTPS, IIS o sito web. Il client usa un protocollo di messaggi dedicato su **TCP cifrato e firmato tramite Windows NegotiateStream**, porta predefinita **5443**, autenticazione mutua Windows/Kerberos e gruppo di dominio autorizzato. Non occorrono certificati TLS. Il nome DNS deve corrispondere allo SPN registrato per il servizio. Il client consente account Windows corrente oppure credenziali di dominio esplicite, mantenute solo in memoria. Non vengono salvate password.

Il client visualizza gli elenchi e mantiene una sottoscrizione push TCP autenticata: recupera gli stati quando il server segnala un aggiornamento, senza interrogazione ogni dieci secondi. Il server coordina le verifiche per tutti i PC: una sola scansione per categoria. Le verifiche partono all'avvio, ogni minuto e su richiesta. L'annullamento richiesto da un client riguarda la scansione condivisa della categoria. Report e gerarchie provengono dal server; solo Esplora file apre direttamente il percorso UNC con i permessi dell'utente.

## Cartelle, metadati e preventivi

- Commesse: `\\ingtassinari.it\data\lavori`, con identificazione solo nelle cartelle immediatamente sotto gli anni 1900–2099.
- Gare: `\\ingtassinari.it\data\gare`, separate dalle commesse.
- JSON condivisi: `\\ingtassinari.it\data\amministrazione\_GESTIONE COMMESSE`, gestiti esclusivamente dal server.
- Dimensione logica totale, numero file, ultima modifica dei file e istante della verifica. I livelli inferiori contribuiscono ai metadati e sono esplorabili, ma non vengono identificati come nuove commesse.
- Controllo dei nomi nelle cartelle `amministraione`, `amministrazione` e `99_AMMINISTRAZIONE`: candidati preventivo/offerta, con richieste, modelli e bozze segnalati come ambigui. Non verifica contenuti, approvazioni o firme e non usa IA esterna.

I dati salvati sono caricati prima delle verifiche. Gli accessi falliti sono segnalati come parziali e non come preventivo assente. Le scritture JSON usano file temporanei, sostituzione e lock; gli archivi corrotti non sono sovrascritti. Se il server è irraggiungibile, il client conserva i dati già caricati in memoria, con un avviso.

## Struttura della soluzione

| Progetto | Responsabilità |
|---|---|
| TA-Gestione Cartelle Commesse | Client WPF, comandi MVVM e configurazione della connessione |
| TA.Contracts | Modelli e contratti condivisi |
| TA.Core | Lettura cartelle, preventivi, JSON e report con risorse incorporate |
| TA.Transport | Protocollo, framing, connessione TCP/Kerberos e autenticazione del client |
| TA.Server | Servizio Windows, autorizzazione, pianificazione e comandi del server |

## Compilazione e verifica

Aprire `TA-Gestione Cartelle Commesse.slnx` in Visual Studio con .NET 10 e supporto WPF.

```powershell
dotnet build 'TA-Gestione Cartelle Commesse.slnx' -c Release
dotnet run --project tests/ArchiveChecks/ArchiveChecks.csproj -c Release
dotnet run --project tests/TransportChecks/TransportChecks.csproj -c Release
dotnet run --project tests/PushChecks/PushChecks.csproj -c Release
```

I test usano dati sintetici e un listener di prova su loopback. Verificano metadati, archivio, limiti delle scansioni, framing, rifiuto delle richieste HTTP, percorsi fuori radice e server non autenticati. Il collaudo reale Kerberos, avvio del servizio e permessi sulla rete deve essere eseguito sulla VM configurata.

Il workflow Installer Windows costruisce entrambi i pacchetti, verifica il client in aggiornamento dalla 1.0.2 e pubblica setup e impronte. La versione del workflow è `2.2.<numero esecuzione>`. I dati di rete e le credenziali non sono inclusi nei pacchetti.

## Versione 2.6.0: elenchi, comunicazioni e archivi annuali

Il server verifica l'elenco di tutte le commesse e gare ogni minuto, indipendentemente dalla lettura ricorsiva dei file. Nessuna differenza di frequenza fra gli anni. Le scansioni dei metadati non si sovrappongono: se una lettura dura oltre un minuto, prosegue fino al termine. I risultati vengono pubblicati progressivamente; una cartella nuova compare prima della verifica delle sue dimensioni.

Il JSON generale diventa un indice (schema 2) e i dati si trovano nella directory omonima con suffisso .years, in file con anno e impronta del contenuto. Gli anni invariati riusano il file. La prima migrazione conserva il JSON precedente come .legacy-v1.bak. Non ripristinare un server precedente dopo la migrazione senza recuperare il backup. Indice e file annuali vanno conservati insieme; un file mancante o alterato viene segnalato senza sovrascrivere l'archivio. Per le commesse l'anno e quello del contenitore; per le gare e il codice a due cifre interpretato come 20XX. I dati privi di anno vengono salvati in senza-anno. Il salvataggio usa una coda seriale; gli errori di rete sono mostrati nello stato e nei log.

Ogni nuova notifica push richiede un riscontro sullo stesso canale Windows autenticato. Il client lo invia dopo aver applicato entrambe le viste. Un heartbeat verifica il collegamento durante l'inattivita; un riscontro mancante entro 90 secondi chiude la sessione e viene registrato. I client precedenti restano compatibili, ma non producono questi riscontri. Aiuto > Verifica comunicazione controlla comando e risposta con un identificativo univoco. Gli errori di rete temporanei attivano la riconnessione; gli errori di autenticazione richiedono intervento.

Il gestore mostra due controlli WPF separati: debug del servizio e comunicazioni dei client. I registri sono service.log e communications.jsonl nella directory Logs di ProgramData/Tassinari/CommesseServer. Le comunicazioni identificano PC dichiarato, indirizzo remoto, utente Windows autenticato, azione, esito e identificativo della prova. Il nome PC e un dato dichiarato dal client, non una credenziale. I registri ruotano e la tabella mostra al massimo 500 righe recenti.

Il client scrive un log operativo nella destinazione di rete aziendale configurata nell'applicazione; se la rete fallisce, usa LocalAppData/Tassinari/CommesseClient/Logs e ritenta la rete dopo un minuto. Nessuna password viene registrata. I log sono separati per PC/processo/giorno e ruotano a 10 MiB; la scrittura non blocca l'interfaccia e la coda conserva fino a 1000 messaggi.

Server > Gestione servizio permette stato, avvio, arresto, riavvio e consultazione/esportazione dei registri solo con il token Windows di un amministratore di dominio. Se il token e filtrato da UAC, avviare il client come amministratore. I registri amministrativi sono autorizzati nuovamente dal server contro il gruppo Domain Admins (SID del dominio dell'account di servizio, suffisso 512). La gestione usa l'account Windows corrente, non credenziali alternative inserite nel client.

Avvio/arresto/riavvio usano il gestore servizi remoto di Windows (RPC/SCM), necessario anche quando il servizio applicativo e fermo. La VM deve permettere la gestione remota dei servizi e i relativi permessi Windows. Nessuna regola firewall aggiuntiva viene aperta automaticamente. I log e la verifica applicativa usano invece la porta TCP dedicata. Non sono disponibili modifica remota di account/configurazione o elevazione automatica dei privilegi. Non vengono create attivita pianificate.

Verifiche locali: cartella C26198 con metadati bloccati, aggiornamento griglie WPF prima dell'avviso, salvataggio durante scansione, migrazione annuale, integrita dei JSON, riscontri corretti/errati e diniego dei comandi amministrativi agli utenti ordinari. La gestione SCM con account amministrativo richiede collaudo sulla VM.

## Storico 2.7.0: aggiornamento diretto e archivio compresso

### Aggiornatore

TA-Commesse-Aggiornatore-2.9.0.exe e un programma autonomo: scarica da GitHub il pacchetto ZIP del componente, verifica l'impronta pubblicata da GitHub e le impronte di ogni file, chiude l'interfaccia e sostituisce soltanto i file diversi. Non esegue un installatore. Configurazioni, dati, registrazione e account del servizio restano invariati. L'aggiornamento server richiede privilegi amministrativi e arresta/riavvia il servizio solo se prima era attivo; un servizio gia fermo resta fermo.

Per passare dalla 2.6.0 senza usare un installatore, scaricare una volta l'aggiornatore autonomo, eseguirlo sulla VM scegliendo Server e poi sui PC scegliendo Client. Dalla nuova versione il controllo automatico resta limitato all'apertura del client o del gestore, oltre al comando manuale. Nessuna attivita pianificata. I file Installazione-Iniziale servono solo per nuove installazioni; non sono il canale di aggiornamento.

Il programma conserva un backup prima della sostituzione e un giornale di ripristino. Se la verifica fallisce ripristina i file precedenti; se il processo si interrompe, riaprire l'aggiornatore per il recupero. Una nuova versione del server deve rimanere in stato Running per cinque secondi; questo non certifica l'accesso a tutte le condivisioni. In caso di file occupati non vengono terminati forzatamente processi. Backup, download e log rimangono in UpdateCache sul server e in LocalAppData/Tassinari/CommesseClient/Updates sul client: conservarli finche non e stata verificata la versione nuova.

I pacchetti sono pubblicati dal repository aziendale tramite HTTPS e non firmati digitalmente. Non vengono applicati cambi di versione principale in automatico. L'aggiornatore non modifica i file di configurazione e non rimuove file sconosciuti o obsoleti lasciati da versioni precedenti.

### Archivio e dimensioni

La terza scheda Archivio censisce ricorsivamente i file ZIP e 7z della radice ArchivedJobsRoot. Il servizio apre i file esclusivamente in lettura e legge intestazioni, nomi, date, dimensioni e candidati preventivi. Non estrae o ripara alcun contenuto e non crea file nella radice sorgente. Archivi danneggiati o protetti vengono segnalati come letture parziali; questo controllo dei metadati non verifica il CRC di tutto il contenuto. I file con nome non conforme rimangono visibili. Nessuna commessa viene censita dentro un file compresso.

Il servizio richiede soltanto Lettura/Elenco sulla radice degli archivi. I risultati vengono salvati nell'archivio JSON applicativo separato, per categoria A e anno. La verifica avviene ogni minuto, senza sovrapposizioni delle scansioni lunghe. Se alcuni contenitori non sono accessibili, vengono mostrati i risultati disponibili e conservate le precedenti righe non verificabili, con segnalazioni. Nella scheda Archivio la dimensione mostra lo spazio del file compresso, esplicitamente etichettato. Il contenuto delle cartelle interne e consultabile in sola lettura dalla gerarchia; Apri seleziona il file in Esplora file senza estrarlo.

Le colonne iniziano con Codice, Anno, Numero, Committente, Argomento, Dimensione, seguite dagli esiti. Le nuove commesse hanno una coda prioritaria per dimensioni e metadati: la riga compare prima e riceve un secondo aggiornamento quando il calcolo termina. Il valore iniziale e In verifica; le dimensioni piccole sono espresse in byte/KiB e non arrotondate a zero MiB.
