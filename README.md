# TA-Gestione Cartelle Commesse — 2.13.0

Client WPF/MVVM .NET 10 e servizio Windows per la gestione delle commesse dello Studio Tassinari. Le sorgenti vengono analizzate **sempre e solo in lettura**: nomi, percorsi e metadati, senza leggere il contenuto dei documenti o estrarre gli archivi ZIP/7z.

## Scarica

- [Aggiornatore diretto 2.13.0](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.13.0/TA-Commesse-Aggiornatore-2.13.0.exe) — prima Server sulla VM, poi Client sui PC.
- [Installazione iniziale Client](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.13.0/TA-Gestione-Cartelle-Commesse-Installazione-Iniziale-2.13.0.exe)
- [Installazione iniziale Server](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.13.0/TA-Gestione-Cartelle-Commesse-Server-Installazione-Iniziale-2.13.0.exe)
- [Componente MCP per la VM](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.13.0/TA-Commesse-MCP-2.13.0.zip)
- [Tutti i pacchetti e impronte SHA256](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/tag/v2.13.0)

Il runtime è incluso. L'aggiornatore verifica le impronte, conserva configurazioni e backup e sostituisce i file senza usare un installatore. I controlli degli aggiornamenti partono all'apertura del client o del gestore server, oppure su richiesta: nessuna attività pianificata. I pacchetti non sono firmati digitalmente.

## Scansione ogni ora e interfaccia

La scansione oraria include elenchi, dimensioni, gerarchie e inventario di tutti i nomi accessibili sotto lavori, gare e archivio. La prima scansione parte all'avvio se manca il checkpoint; le successive alla prima ora intera dopo la conclusione, con tolleranza di un minuto. Nessuna sovrapposizione o recupero in coda delle ore occupate. Il checkpoint evita ripetizioni dopo un riavvio. Questa frequenza sostituisce il precedente controllo ogni minuto e la scansione delle 23; il monitor degli eventi non avvia elaborazioni aggiuntive.

La barra in basso mostra fase corrente, percentuale quando è noto il numero di unità e prossima esecuzione. Il valore riguarda la fase e non il tempo residuo. Durante la preparazione viene mostrata una barra indeterminata. **Visualizza > Stato e connessione**, oppure il pulsante in basso, apre il controllo con messaggi, segnalazioni di lettura, impostazioni server e verifica della comunicazione. Il pannello resta chiuso quando non serve.

Gli elenchi si aggiornano silenziosamente alla conclusione. Le differenze sono consultabili in **Export > Report sintetico analisi nomi e differenze**; le notifiche delle differenze restano sospese. **Export > Scarica ultimo report orario** recupera il report di dimensioni e gerarchie. Il comando manuale **Aggiorna elenco** verifica solo l'elenco della categoria.

Le schede Commesse, Gare e Archivio condividono le colonne Codice, Anno, Numero, Committente, Argomento e Dimensione. Una commessa ufficiale viene riconosciuta solo al primo livello delle cartelle annuali. Le altre strutture sono censite senza promuovere le sottocartelle a commesse. Il pannello della gerarchia mostra i rami salvati; il doppio clic apre Esplora file traducendo la condivisione aziendale nel percorso Z del client.

## Inventario e MCP

I JSON sono separati per categoria, anno e codice nella cartella ArchiveFolder/inventario-file. Ogni voce identifica un file con nome, estensione, percorso relativo, posizione fisica ed eventuale archivio e percorso interno. Si censiscono anche le sottocartelle e le voci ZIP/7z senza estrazione; archivi cifrati o illeggibili e altri limiti vengono segnalati. Gli archivi contenuti in altri archivi sono elencati come file, senza espansione ricorsiva.

Lo strumento **cerca_file** filtra per nome, estensione, categoria, anno e codice. Restituisce totale, data scansione, completezza e pagine vincolate alla versione dell'inventario, senza scaricare tutto il catalogo. **stato_scansione** permette a ChatGPT di consultare fase e avanzamento. **analisi_file** offre catalogo, singoli inventari, candidati e frequenze dei nomi. Nessuna chiamata automatica a modelli IA.

Il pacchetto MCP deve essere aggiornato sulla VM: fermare il vecchio tunnel, usare il nuovo Start-McpTunnel.ps1 con il tunnel-client ufficiale già configurato e riavviare il tunnel. La chiave runtime viene inserita interattivamente e non salvata dallo script. Poi ricaricare gli strumenti del plugin ChatGPT. La ricerca effettiva richiede una prima scansione completa; un inventario mancante viene segnalato, senza restituire falsamente zero risultati.

## Configurazione e verifiche

Il servizio usa TCP dedicato e autenticazione Windows Kerberos con protezione mutua; il canale MCP locale espone solo consultazioni e verifica gruppo autorizzato e servizio. Sulle sorgenti servono Lettura/Elenco; le scritture sono limitate all'archivio JSON applicativo separato. Il client non legge direttamente il catalogo di rete.

- [Installazione, account e SPN](deployment/SERVER-CLIENT.md)
- [Inventario JSON, calendario, ricerca file e limiti](deployment/ANALISI-NOMI.md)
- [Collegamento MCP e tunnel](deployment/MCP.md)

Il workflow GitHub verifica protocollo, autenticazione locale, archivio, inventario ZIP/7z, ricerca e paginazione, calendario orario e riavvio, avanzamento e controlli WPF, aggiornamento e installazione/disinstallazione prima di pubblicare i pacchetti.
