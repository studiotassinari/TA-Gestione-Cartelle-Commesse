# TA-Gestione Cartelle Commesse — 2.15.1

Client WPF/MVVM .NET 10 e servizio Windows per la gestione delle commesse dello Studio Tassinari. Le sorgenti vengono analizzate **sempre e solo in lettura**: nomi, percorsi e metadati nelle scansioni automatiche; contenuti dei singoli documenti solo su richiesta esplicita tramite MCP, senza modificare gli originali o estrarre archivi su disco.

## Scarica

- [Aggiornatore diretto 2.15.1](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.15.1/TA-Commesse-Aggiornatore-2.15.1.exe) — prima Server sulla VM, poi Client sui PC.
- [Installazione iniziale Client](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.15.1/TA-Gestione-Cartelle-Commesse-Installazione-Iniziale-2.15.1.exe)
- [Installazione iniziale Server](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.15.1/TA-Gestione-Cartelle-Commesse-Server-Installazione-Iniziale-2.15.1.exe)
- [Componente MCP per la VM](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.15.1/TA-Commesse-MCP-2.15.1.zip)
- [Tutti i pacchetti e impronte SHA256](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/tag/v2.15.1)

Il runtime è incluso. L'aggiornatore verifica le impronte, conserva configurazioni e backup e sostituisce i file senza usare un installatore. I controlli degli aggiornamenti partono all'apertura del client o del gestore server, oppure su richiesta: nessuna attività pianificata. L’aggiornatore del server attende anche la chiusura effettiva del processo prima di sostituire i file. I pacchetti non sono firmati digitalmente.

## Correzioni 2.15.1: scansione e salvataggi JSON

Una data Win32 non rappresentabile non interrompe più l'intera scansione degli archivi: nomi e dimensioni leggibili vengono conservati, la data resta non disponibile e il report indica il percorso da verificare. Gli errori di lettura della singola voce sono isolati; le sorgenti non vengono corrette o modificate.

I salvataggi JSON consentono letture condivise e sostituzione atomica dell'indice. I blocchi temporanei di altri processi vengono attesi con tentativi per un massimo di dieci secondi tra le chiamate di accesso, interrompibili durante l'arresto. I blocchi persistenti restano errori espliciti; nessun processo viene forzato a liberare il file. Restano attivi lock applicativi, verifica delle generazioni annuali e protezione dei dati precedenti.

**Il componente MCP 2.15.0 è compatibile con il server 2.15.1: se già avviato, il tunnel non richiede un riavvio per questa correzione.**

## Novità 2.15.0: consultazioni MCP e documenti

Il componente MCP non interroga il servizio all'avvio o quando è inattivo. Ogni richiesta dati richiede una consultazione temporanea aperta dal plugin, limitata a due minuti e venti operazioni, senza rinnovo automatico. Le vecchie chiamate prive di consultazione vengono rifiutate. Il tunnel mantiene il solo collegamento tecnico necessario a ricevere le richieste; le scansioni orarie del server rimangono indipendenti.

**elenca_documenti** mostra un livello della cartella o dello ZIP/7z scelto. **leggi_documento** legge tramite il server un PDF testuale, DOCX, XLSX o testo, anche dentro ZIP/7z, solo per una richiesta esplicita del documento. Il testo è paginato, con versione e avvisi sui limiti, e non viene salvato nei JSON o nei log. Non sono inclusi OCR, DOC/XLS legacy, macro e documenti cifrati. La lettura viene interrotta dopo 20 secondi; massimo 50 MiB e 200.000 caratteri estratti. Vedere la [guida MCP](deployment/MCP.md).

**Per il passaggio da versioni precedenti alla 2.15.0, aggiornare sia il servizio sia il pacchetto MCP sulla VM**, riavviare il tunnel e ricaricare gli strumenti del plugin: devono risultare dieci. Il nuovo canale locale rifiuta i componenti MCP precedenti; il collegamento Kerberos dei client resta disponibile.

## Scansione ogni ora e interfaccia

La scansione oraria include elenchi, dimensioni, gerarchie e inventario di tutti i nomi accessibili sotto lavori, gare e archivio. La prima scansione parte all'avvio se manca il checkpoint; le successive alla prima ora intera dopo la conclusione, con tolleranza di un minuto. Nessuna sovrapposizione o recupero in coda delle ore occupate. Il checkpoint evita ripetizioni dopo un riavvio. Questa frequenza sostituisce il precedente controllo ogni minuto e la scansione delle 23; il monitor degli eventi non avvia elaborazioni aggiuntive.

In basso sono sempre disponibili quattro schede: **Scansione**, **Connessione**, **Archivio JSON** e **Messaggi**. Il divisore orizzontale permette di regolarne l’altezza. La scansione mostra le ultime dodici rilevazioni delle cartelle in lettura, con categoria, fase e percorso; per gli archivi il separatore ` > ` distingue il file ZIP/7z dalla directory interna. Il client osserva il percorso corrente ogni tre secondi durante il lavoro: è un riepilogo di attività, non un elenco completo di ogni cartella attraversata. La percentuale testuale, quando stimabile, riguarda le unità della fase e non il tempo residuo. Nessuna barra animata né pannello laterale.

**Report > Non conformità** apre una finestra collegata al client con dati già censiti dal server, ricerca, filtro per categoria e salvataggio HTML della vista. Distingue nomi non conformi, problemi di lettura e preventivi da verificare; indica dati mancanti e verifiche dei file ancora da eseguire. Non avvia nuove scansioni.

Gli elenchi si aggiornano silenziosamente alla conclusione. Le differenze sono consultabili in **Export > Report sintetico analisi nomi e differenze**; le notifiche delle differenze restano sospese. **Export > Scarica ultimo report orario** recupera il report di dimensioni e gerarchie. Il comando manuale **Aggiorna elenco** verifica solo l'elenco della categoria.

Le schede Commesse, Gare e Archivio condividono le colonne Codice, Anno, Numero, Committente, Argomento e Dimensione. Una commessa ufficiale viene riconosciuta solo al primo livello delle cartelle annuali. Le altre strutture sono censite senza promuovere le sottocartelle a commesse. Il pannello della gerarchia mostra i rami salvati; il doppio clic apre Esplora file traducendo la condivisione aziendale nel percorso Z del client.

## Inventario e MCP

I JSON sono separati per categoria, anno e codice nella cartella ArchiveFolder/inventario-file. Ogni voce identifica un file con nome, estensione, percorso relativo, posizione fisica ed eventuale archivio e percorso interno. Si censiscono anche le sottocartelle e le voci ZIP/7z senza estrazione; archivi cifrati o illeggibili e altri limiti vengono segnalati. Gli archivi contenuti in altri archivi sono elencati come file, senza espansione ricorsiva.

Lo strumento **cerca_file** filtra per nome, estensione, categoria, anno e codice. Restituisce totale, data scansione, completezza e pagine vincolate alla versione dell'inventario, senza scaricare tutto il catalogo. **stato_scansione** permette a ChatGPT di consultare fase e avanzamento. **analisi_file** offre catalogo, singoli inventari, candidati e frequenze dei nomi. Nessuna chiamata automatica a modelli IA.

Quando occorre aggiornare il pacchetto MCP sulla VM: fermare il vecchio tunnel, usare il nuovo Start-McpTunnel.ps1 con il tunnel-client ufficiale già configurato e riavviare il tunnel. La chiave runtime viene inserita interattivamente e non salvata dallo script. Poi ricaricare gli strumenti del plugin ChatGPT. La ricerca effettiva richiede una prima scansione completa; un inventario mancante viene segnalato, senza restituire falsamente zero risultati.

## Configurazione e verifiche

Il servizio usa TCP dedicato e autenticazione Windows Kerberos con protezione mutua; il canale MCP locale espone solo consultazioni e verifica gruppo autorizzato e servizio. Sulle sorgenti servono Lettura/Elenco; le scritture sono limitate all'archivio JSON applicativo separato. Il client non legge direttamente il catalogo di rete.

- [Installazione, account e SPN](deployment/SERVER-CLIENT.md)
- [Inventario JSON, calendario, ricerca file e limiti](deployment/ANALISI-NOMI.md)
- [Collegamento MCP e tunnel](deployment/MCP.md)

Il workflow GitHub verifica protocollo, autenticazione locale, archivio, inventario ZIP/7z, ricerca e paginazione, calendario orario e riavvio, avanzamento e controlli WPF, aggiornamento e installazione/disinstallazione prima di pubblicare i pacchetti.
