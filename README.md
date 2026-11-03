# TA-Gestione Cartelle Commesse — 2.21.1

Client WPF/MVVM .NET 10 e servizio Windows per la gestione delle commesse dello Studio Tassinari. Le sorgenti vengono analizzate **sempre e solo in lettura**: nomi, percorsi e metadati nelle scansioni automatiche; contenuti dei singoli documenti solo su richiesta esplicita tramite MCP, senza modificare gli originali. L’apertura esplicita dal client prepara solo la copia locale del documento selezionato, anche se interno a ZIP/7z.

## Novità 2.21.1: scansione completa a mezzanotte e campi PDF

Il catalogo MUR dispone di un indice JSON dedicato, generato a ogni avvio del servizio (anche dopo gli aggiornamenti) e ogni giorno alle **00:00, ora italiana**. Ricerca e apertura non ripetono più la scansione globale a ogni richiesta. PDF e DOCX sono censiti in Commesse e Archivio, incluse sottocartelle e contenitori ZIP/7z, con risultati progressivi e segnalazione delle fonti parziali. Le analisi salvate restano separate dal censimento dei nomi. Nessuna API OpenAI automatica: riconoscimento e compilazione avvengono tramite ChatGPT su richiesta. Corretto anche l'avvio manuale del tunnel con Windows PowerShell 5.1.

## Catalogo MUR e mappa

Catalogo MUR A.1/D.1 con progettisti, ruoli, tipologie, indirizzo, comune e coordinate. Ricerca esclusivamente in Commesse e Archivio, anche nei PDF di ZIP/7z; Gare escluse. Mappa OpenStreetMap con selezione collegata all'elenco. Le analisi e le fonti sono conservate nei JSON del server; gli originali restano in sola lettura. I dati mancanti richiedono analisi su richiesta tramite ChatGPT. Aggiornare client, server e MCP alla 2.21.1; il tunnel va avviato manualmente sulla VM con la chiave runtime.

## Politica delle scritture client

Il client non salva log di connessione, heartbeat o notifiche su disco o sulle condivisioni. Anche l'aggiornatore client mostra i messaggi soltanto nella propria finestra. Restano consentiti aggiornamenti, impostazioni, copie locali per aprire i documenti ed esportazioni richieste dall'utente. Download, file scompattati e backup dell'aggiornatore client utilizzano `%LOCALAPPDATA%\Tassinari\CommesseClient\Updates`, non la cartella da cui è stato scaricato o avviato l'installatore. I registri del servizio sulla VM restano disponibili per la diagnostica. Le vecchie copie del client devono essere aggiornate e riavviate per interrompere le loro scritture.

## Novità 2.19.0: analisi IPRiPI

La nuova finestra **Analisi > IPRiPI — analisi MUR A.1/D.1** consulta PDF e risultati salvati da ChatGPT tramite il plugin, con citazioni verificate e classificazione secondo la DGR Emilia-Romagna **2272/2016**. Nessuna API OpenAI dal server. Aggiornare Server, Client e MCP alla 2.19.0 e riavviare il tunnel. Abilitare `Server.EnableMcpIpripiWrites` per salvare le analisi; [guida, configurazione e limiti](deployment/IPRIPI.md).

## Scarica la versione di produzione

- [Aggiornatore diretto 2.21.1](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.21.1/TA-Commesse-Aggiornatore-2.21.1.exe) — prima Server sulla VM, poi Client sui PC.
- [Installazione iniziale Client](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.21.1/TA-Gestione-Cartelle-Commesse-Installazione-Iniziale-2.21.1.exe)
- [Installazione iniziale Server](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.21.1/TA-Gestione-Cartelle-Commesse-Server-Installazione-Iniziale-2.21.1.exe)
- [Componente MCP per la VM](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.21.1/TA-Commesse-MCP-2.21.1.zip)
- [Tutti i pacchetti e impronte SHA256](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/tag/v2.21.1)

Il runtime è incluso. L'aggiornatore verifica le impronte, conserva configurazioni e backup e sostituisce i file senza usare un installatore. I controlli degli aggiornamenti partono all'apertura del client o del gestore server, oppure su richiesta: nessuna attività pianificata. L’aggiornatore del server attende anche la chiusura effettiva del processo prima di sostituire i file. I pacchetti non sono firmati digitalmente.

## Novità 2.18.0: localizzazione e percorsi delle relazioni

Ricerca Nominatim su richiesta dal client, tramite il server autenticato: indirizzo visibile e modificabile prima dell'invio, scelta esplicita del risultato, coordinate salvate nei JSON con fonte e precisione come posizione indicativa da verificare. Cache condivisa persistente, limite complessivo di una richiesta al secondo, nessuna ricerca automatica durante digitazione o scansioni. Non inserire indirizzi personali o riservati nel servizio pubblico. Nessuna chiave o fatturazione configurata.

Griglia con indirizzo e percorso sul server. Menu contestuale per aprire una copia del documento in sola lettura o selezionare il file in Esplora file; per ZIP/7z viene selezionato il contenitore. I percorsi aziendali vengono tradotti in Z: sul client. Comune e indirizzo restano visibili anche senza coordinate.

Icona aziendale: cartella bianca con simbolo TA originale, scalato uniformemente. La stessa icona compare su client, gestore server, area di notifica, aggiornatore e installatori.

Aggiornare prima Server e poi Client. Il tunnel MCP 2.17.0 rimane compatibile: questa versione non aggiunge strumenti al plugin e non richiede di reinserire la chiave runtime.

## Novità 2.17.0: catalogo JSON analizzato da ChatGPT

Le relazioni possono essere analizzate su richiesta in ChatGPT e salvate nei JSON condivisi: classificazione, sintesi, sito, coordinate proposte con fonte, fascicoli omogenei e citazioni riscontrate. Nessuna API del modello chiamata dal server. La coda mostra documenti nuovi/modificati; le analisi invariate restano disponibili, quelle superate sono segnalate e lo storico non viene sovrascritto.

Il client mostra fascicolo e stato dell'analisi. Il doppio clic su una riga apre una copia locale in sola lettura del documento, anche da ZIP/7z. Gli originali restano invariati. La copia è prodotta solo su richiesta, con limiti di dimensione, tempo e memoria.

Il plugin offre **15 strumenti**, incluso il salvataggio tipizzato del catalogo. La scrittura richiede una richiesta esplicita di catalogazione e l'abilitazione server `EnableMcpGeologyWrites`; il valore predefinito è disattivato. Aggiornare anche il pacchetto MCP e ricaricare gli strumenti. [Guida completa e struttura JSON](deployment/GEOLOGIA.md).

## Catalogo geologico e mappa introdotti nella 2.16.0

Catalogo geologico e geotecnico autonomo: generato dai nomi e dalle cartelle tematiche dell'inventario, anche dentro ZIP/7z. Candidati da verificare, commesse non attribuite senza evidenza, nessuna lettura automatica dei contenuti.

Client: menu Analisi, finestra indipendente a istanza unica, ricerca dinamica, filtri e mappa OpenStreetMap con simboli selezionabili. Nessuna chiave API o fatturazione; richiede Internet e WebView2. Le posizioni possono essere indicate esplicitamente e salvate nei soli JSON condivisi con account e data.

Catalogo e cache suddivisi per categoria, anno e commessa sotto ArchiveFolder/geologia. Aggiornamento all'avvio e dopo ogni scansione completa dai JSON, conservazione dei dati precedenti in caso di errore, nessuna notifica delle differenze.

MCP: cerca_relazioni_geologiche e leggi_relazione_geologica. La ricerca dei file può consultare le unità già censite prima della fine del primo inventario generale, dichiarandole parziali. Gli errori applicativi sono espliciti. Navigazione diretta e lettura di un singolo documento restano disponibili indipendentemente dall'inventario globale.

Vedere [guida del catalogo geologico](deployment/GEOLOGIA.md) e [collaborazione al progetto](CONTRIBUTING.md). Aggiornare anche il componente MCP e ricaricare i quindici strumenti per usare la nuova area.

## Correzioni 2.15.1: scansione e salvataggi JSON

Una data Win32 non rappresentabile non interrompe più l'intera scansione degli archivi: nomi e dimensioni leggibili vengono conservati, la data resta non disponibile e il report indica il percorso da verificare. Gli errori di lettura della singola voce sono isolati; le sorgenti non vengono corrette o modificate.

I salvataggi JSON consentono letture condivise e sostituzione atomica dell'indice. I blocchi temporanei di altri processi vengono attesi con tentativi per un massimo di dieci secondi tra le chiamate di accesso, interrompibili durante l'arresto. I blocchi persistenti restano errori espliciti; nessun processo viene forzato a liberare il file. Restano attivi lock applicativi, verifica delle generazioni annuali e protezione dei dati precedenti.

**Il componente MCP 2.15.0 è compatibile con il server 2.15.1: se già avviato, il tunnel non richiede un riavvio per questa correzione.**

## Novità 2.15.0: consultazioni MCP e documenti

Il componente MCP non interroga il servizio all'avvio o quando è inattivo. Ogni richiesta dati richiede una consultazione temporanea aperta dal plugin, limitata a due minuti e venti operazioni, senza rinnovo automatico. Le vecchie chiamate prive di consultazione vengono rifiutate. Il tunnel mantiene il solo collegamento tecnico necessario a ricevere le richieste; le scansioni orarie del server rimangono indipendenti.

**elenca_documenti** mostra un livello della cartella o dello ZIP/7z scelto. **leggi_documento** legge tramite il server un PDF testuale, DOCX, XLSX o testo, anche dentro ZIP/7z, solo per una richiesta esplicita del documento. Il testo è paginato, con versione e avvisi sui limiti, e non viene salvato nei JSON o nei log. Non sono inclusi OCR, DOC/XLS legacy, macro e documenti cifrati. La lettura viene interrotta dopo 20 secondi; massimo 50 MiB e 200.000 caratteri estratti. Vedere la [guida MCP](deployment/MCP.md).

**Per il passaggio da versioni precedenti alla 2.15.0, aggiornare sia il servizio sia il pacchetto MCP sulla VM**, riavviare il tunnel e ricaricare gli strumenti del plugin: devono risultare dieci. Il nuovo canale locale rifiuta i componenti MCP precedenti; il collegamento Kerberos dei client resta disponibile.

## Scansione all'avvio e a mezzanotte

La scansione completa di lavori, gare e archivio parte a ogni avvio del servizio e ogni giorno alle 00:00 italiane. Include elenchi, dimensioni, gerarchie e inventario dei nomi, con salvataggio JSON e successivo aggiornamento dei cataloghi MUR e geologico. Nessuna sovrapposizione: una scansione lunga termina prima della successiva. I contenuti dei documenti vengono letti soltanto su richiesta; il lettore PDF include anche i valori AcroForm e le caselle selezionate.

In basso sono sempre disponibili quattro schede: **Scansione**, **Connessione**, **Archivio JSON** e **Messaggi**. Il divisore orizzontale permette di regolarne l’altezza. La scansione mostra le ultime dodici rilevazioni delle cartelle in lettura, con categoria, fase e percorso; per gli archivi il separatore ` > ` distingue il file ZIP/7z dalla directory interna. Il client osserva il percorso corrente ogni tre secondi durante il lavoro: è un riepilogo di attività, non un elenco completo di ogni cartella attraversata. La percentuale testuale, quando stimabile, riguarda le unità della fase e non il tempo residuo. Nessuna barra animata né pannello laterale.

**Report > Non conformità** apre una finestra collegata al client con dati già censiti dal server, ricerca, filtro per categoria e salvataggio HTML della vista. Distingue nomi non conformi, problemi di lettura e preventivi da verificare; indica dati mancanti e verifiche dei file ancora da eseguire. Non avvia nuove scansioni.

Gli elenchi si aggiornano silenziosamente alla conclusione. Le differenze sono consultabili in **Export > Report sintetico analisi nomi e differenze**; le notifiche delle differenze restano sospese. **Export > Scarica ultimo report giornaliero** recupera il report di dimensioni e gerarchie. Il comando manuale **Aggiorna elenco** verifica solo l'elenco della categoria.

Le schede Commesse, Gare e Archivio condividono le colonne Codice, Anno, Numero, Committente, Argomento e Dimensione. Una commessa ufficiale viene riconosciuta solo al primo livello delle cartelle annuali. Le altre strutture sono censite senza promuovere le sottocartelle a commesse. Il pannello della gerarchia mostra i rami salvati; il doppio clic apre Esplora file traducendo la condivisione aziendale nel percorso Z del client.

## Inventario e MCP

I JSON sono separati per categoria, anno e codice nella cartella ArchiveFolder/inventario-file. Ogni voce identifica un file con nome, estensione, percorso relativo, posizione fisica ed eventuale archivio e percorso interno. Si censiscono anche le sottocartelle e le voci ZIP/7z senza estrazione; archivi cifrati o illeggibili e altri limiti vengono segnalati. Gli archivi contenuti in altri archivi sono elencati come file, senza espansione ricorsiva.

Lo strumento **cerca_file** filtra per nome, estensione, categoria, anno e codice. Restituisce totale, data scansione, completezza e pagine vincolate alla versione dell'inventario, senza scaricare tutto il catalogo. **stato_scansione** permette a ChatGPT di consultare fase e avanzamento. **analisi_file** offre catalogo, singoli inventari, candidati e frequenze dei nomi. Nessuna chiamata automatica a modelli IA.

Quando occorre aggiornare il pacchetto MCP sulla VM: fermare il vecchio tunnel, usare il nuovo Start-McpTunnel.ps1 con il tunnel-client ufficiale già configurato e riavviare il tunnel. La chiave runtime viene inserita interattivamente e non salvata dallo script. Poi ricaricare gli strumenti del plugin ChatGPT. La ricerca usa anche le unità già censite durante il primo inventario, dichiarando i risultati parziali. La navigazione diretta e la lettura dei documenti non richiedono il completamento della ricerca globale.

## Configurazione e verifiche

Il servizio usa TCP dedicato e autenticazione Windows Kerberos con protezione mutua; il canale MCP locale verifica gruppo autorizzato e servizio, espone consultazioni e una scrittura opzionale limitata al catalogo geologico derivato. Sulle sorgenti servono Lettura/Elenco; le scritture sono limitate all'archivio JSON applicativo separato. Il client non legge direttamente il catalogo di rete.

- [Installazione, account e SPN](deployment/SERVER-CLIENT.md)
- [Inventario JSON, calendario, ricerca file e limiti](deployment/ANALISI-NOMI.md)
- [Collegamento MCP e tunnel](deployment/MCP.md)

Il workflow GitHub verifica protocollo, autenticazione locale, archivio, inventario ZIP/7z, ricerca e paginazione, calendario giornaliero e riavvio, avanzamento e controlli WPF, aggiornamento e installazione/disinstallazione prima di pubblicare i pacchetti.
