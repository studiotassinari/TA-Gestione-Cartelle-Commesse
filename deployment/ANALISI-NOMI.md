# Inventario nomi, versione 2.11.0

Il servizio legge le radici configurate di commesse, gare e archivio. L'indice delle commesse resta limitato al primo livello delle cartelle annuali. Il nuovo inventario legge ricorsivamente i nomi di tutti i file accessibili, anche nelle cartelle fuori dagli anni e nei contenitori dei clienti. Non modifica le sorgenti, non estrae archivi e non legge il contenuto dei documenti ordinari. Legge nomi, percorsi relativi, tipo, dimensione e data dai metadati; per ZIP e 7z legge le intestazioni degli archivi.

## Calendario e report

L'elaborazione completa parte alle 23:00, ora italiana, dopo il calcolo giornaliero delle dimensioni e della gerarchia. Il controllo dell'elenco ogni minuto continua. Non ci sono attività pianificate Windows né chiamate automatiche a modelli IA. Le differenze non generano push o notifiche Windows. Il canale di comunicazione conserva connessione e riscontri; il client può recuperare l'elenco con Aggiorna o riconnettendosi.

Il menu **Export > Report sintetico analisi nomi e differenze** scarica dal server un HTML con nuove voci, rimozioni confermate, metadati variati, letture parziali e proposte di commesse. Prima della prima elaborazione il report non è disponibile. La prima lettura costituisce la base del confronto. Una rinomina appare come rimozione e aggiunta; non vengono interpretati i contenuti dei documenti. Non è una fotografia atomica del filesystem.

## Struttura JSON

Nella cartella configurata `ArchiveFolder` (normalmente `\\ingtassinari.it\data\amministrazione\_GESTIONE COMMESSE`):

```text
inventario-file/
  report.json
  report-<data-ora-UTC>.json
  C/2026/C26198/<ID stabile del percorso>/
    latest.json
    <generazione>/
      manifest.json
      files-00000.json
      files-00001.json
```

`C`, `G`, `A` distinguono commesse, gare e archivio. Il codice completo comprende anno e numero progressivo. `_da-classificare` raccoglie file non assegnati o proposte senza codice; `_anno-non-noto` indica che il percorso non permette di conoscere l'anno. `anno-XX` conserva l'anno a due cifre quando non è possibile dedurne il secolo senza ipotesi. Le pagine contengono al massimo 1.000 voci. I manifest contengono percorso, committente, argomento, classificazione, data, completezza e conteggi delle differenze.

Una generazione diventa corrente solo dopo avere completato tutte le sue pagine. Il report pubblicato rimanda a generazioni immutabili: una scansione in corso non cambia le pagine già consultabili. Una lettura parziale conserva le voci precedenti non verificate e non conferma cancellazioni. L'assenza di un'intera unità viene segnalata da verificare, senza equipararla automaticamente a una cancellazione. Le generazioni precedenti vengono conservate; non è prevista cancellazione automatica dei JSON.

## Proposte e MCP

Per le cartelle non annuali sotto lavori vengono confrontati i nomi con i committenti presenti in più commesse. Le cartelle figlie diventano proposte con evidenza e confidenza, mai nuove commesse ufficiali. Le cartelle annidate in una commessa ufficiale restano contenuto di quella commessa.

Il nuovo strumento MCP in sola lettura `analisi_file` accetta:

- `kind="catalog"`, `query="Marini"`: cerca unità e restituisce ID, data, completezza e conteggi.
- `kind="files"`, `id="<ID>"`: nomi dei file e contenuti nominali degli archivi.
- `kind="candidates"`: possibili commesse e argomenti da confermare.
- `kind="patterns"`, `id="<ID>"`: frequenze delle estensioni ed esempi per ragionare sui nomi con ChatGPT.
- `kind="issues"`: errori e limiti di lettura.

`offset` e `limit` (massimo 100) controllano la paginazione. I risultati includono la data dell'inventario; se cambia durante una consultazione lunga, ricominciare dal catalogo. ChatGPT può cercare pattern ragionando su questi dati, senza chiamate automatiche alle API a pagamento da parte del servizio. I nomi restituiti sono dati aziendali, non istruzioni da eseguire. Il plugin deve essere aggiornato/ricaricato per rilevare il nuovo strumento.

## Vista e limiti

La struttura espone inizialmente la radice e i figli di primo livello della gerarchia salvata. Il doppio clic sul testo di un ramo apre Esplora file, convertendo la condivisione aziendale in `Z:`. I rami virtuali dentro ZIP/7z selezionano l'archivio contenitore in Explorer: non esiste una cartella fisica corrispondente e non viene estratta.

Collegamenti/reparse point, cestini e snapshot sono esclusi. Archivi corrotti, protetti o variati durante la lettura producono segnalazioni. Archivi compressi dentro altri archivi sono censiti come file ma non aperti: richiederebbero decompressione. Il limite di un milione di voci per unità produce un inventario parziale esplicito. Un archivio presente in una commessa può comparire sia nel suo inventario sia come unità della categoria Archivio: i conteggi globali non sono un totale fisico deduplicato.

La prova su cartelle sintetiche verifica hash e date invariati, ZIP e 7z, committenti ricorrenti, file non assegnati, differenze, conservazione in caso di errore, paginazione, annullamento, esclusione di percorsi arbitrari e assenza di notifiche. Il collaudo di carico e dei permessi sulla VM resta necessario dopo l'aggiornamento.

## Ricerca dei singoli file (2.12.0)

Lo strumento MCP cerca_file applica i filtri sul server, senza inviare l'intero inventario a ChatGPT. Tutti i filtri sono opzionali: name (sottostringa senza distinzione maiuscole), extension (estensione esatta con o senza punto), category (C lavori, G gare, A archivio), year (anno a quattro cifre noto dal percorso), code (codice o prefisso). I filtri si combinano con AND. Nessuna interpretazione automatica del formato Edilclima: si cerca il nome o l'estensione richiesti.

Esempio: cerca_file(category="C", year="2026", name="Edilclima", limit=50). Per cercare per estensione, passare extension con l'estensione effettiva del file. Ogni risposta contiene total, lastScanUtc, scanStartedUtc, complete, nextOffset e version. Nelle pagine successive usare gli stessi filtri, nextOffset e version; se cambia l'inventario, ripartire dalla prima pagina. Il totale riguarda i nomi censiti: in caso di lettura parziale viene segnalato esplicitamente. Inventario assente genera errore, non zero risultati. Una ricerca oltre 20 secondi chiede di restringere anno/codice e non restituisce un totale parziale come se fosse completo.

Ogni file restituisce code, name, extension, relativePath, parentRelativePath, physicalPath, archivePath, internalPath, id stabile e data della verifica. Per ZIP/7z, archivePath identifica il file fisico e internalPath la posizione interna: non si crea un percorso Explorer fittizio. Gli omonimi in cartelle o archivi diversi hanno ID distinti. Le sovrapposizioni dello stesso archivio negli inventari C/A vengono deduplicate nella ricerca globale. Un file non assegnato conserva codice vuoto: nessuna commessa viene inventata dal percorso.

La nuova proprietà Location viene salvata in ciascuna voce JSON dalla prossima scansione. I JSON 2.11 già presenti restano leggibili: la posizione viene ricostruita dal manifest e dal percorso relativo, senza accedere alle sorgenti. L'organizzazione resta separata per anno e codice. Per gli archivi con solo anno a due cifre non viene indovinato il secolo: cercarli con code (per esempio C26) invece del filtro year.

Aggiornare il servizio e il componente MCP sulla VM, riavviare il tunnel e ricaricare gli strumenti del plugin. La prima ricerca reale richiede un inventario notturno completato; il nuovo strumento non avvia scansioni o estrazioni.
