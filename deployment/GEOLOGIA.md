# Catalogo geologico, fascicoli e analisi ChatGPT — 2.17.0

Il database è composto da JSON condivisi. Il servizio riconosce i candidati dai nomi già censiti; ChatGPT interpreta il testo e propone classificazione, fascicolo e localizzazione **solo quando l'utente avvia l'analisi nella chat**. Nessuna chiamata del server alle API dei modelli, nessuna attività pianificata e nessuna modifica dei documenti originali.

## Archivio JSON

La cartella è `<ArchiveFolder>\geologia`, normalmente `\\ingtassinari.it\data\amministrazione\_GESTIONE COMMESSE\geologia`:

```text
catalogo.json
unita/<categoria>/<anno>/<commessa>/<id-unita>.json
posizioni.json
analisi/indice.json
analisi/<anno>/<commessa>/<id-documento>/revisioni/<revisione>.json
fascicoli/<id-fascicolo>/revisioni/<revisione>.json
```

L'indice delle analisi è il punto di commit atomico: collega le revisioni immutabili di documenti e fascicoli. Ogni risultato conserva identità del documento, firma dei metadati, checksum del contenuto letto, classificazione, sintesi, autore se rilevato, sito, coordinate quando documentabili, motivazione del raggruppamento, citazioni con offset, limiti, data e account Windows del salvataggio. Le posizioni indicate manualmente restano prioritarie rispetto alle proposte IA. Le analisi sono conservate anche quando una fonte scompare dall'inventario.

Le revisioni non contengono copie integrali dei documenti. Contengono soltanto metadati, risultati e brevi citazioni necessarie a ricostruire la provenienza. Il parser tiene temporaneamente in memoria le porzioni lette per riscontrare le citazioni; dopo un riavvio occorre rileggere il documento prima di salvare un nuovo risultato.

## Aggiornamento incrementale

Il servizio continua il censimento orario delle sorgenti. Il catalogo geologico lavora all'avvio e ogni ora **sui JSON già prodotti**, senza aggiungere una scansione dei contenuti o degli archivi. Riutilizza le unità senza differenze e riclassifica quelle nuove o modificate; considera anche modifiche, aggiunte e rimozioni nei livelli inferiori. La sola data della directory non è usata come prova di assenza di modifiche.

Per ogni documento si conserva una firma di identità, dimensione e ultima modifica; per ZIP/7z entra anche la versione dei metadati del contenitore. Se i metadati sono incompleti, il documento non viene considerato invariato con certezza. La coda per ChatGPT presenta prima i documenti con modifica più recente e omette quelli già analizzati sulla stessa firma. Un aggiornamento del file rende l'analisi precedente «da aggiornare», mantenendone lo storico. Prima di salvare, il server ricontrolla anche il checksum effettivo del documento.

La prima raccolta può essere parziale, soprattutto durante la scansione iniziale di grandi archivi. Sono esplicitati dati mancanti e limiti. Nomi generici fuori dalle cartelle tematiche possono sfuggire alla selezione dei candidati: il riconoscimento dai nomi non garantisce un censimento esaustivo. Non vengono emesse notifiche delle differenze.

## Analisi avviata da ChatGPT

Esempio di richiesta: «Usa TA – Gestione Commesse per analizzare fino a cinque relazioni geologiche nuove o modificate della commessa C26001, raggrupparle in fascicoli omogenei e salvare il catalogo JSON. Individua il sito dalle fonti e segnala le posizioni non certe».

1. `inizia_consultazione(contenuti=true, analisiGeologiche=true)`: gruppo di lavoro esplicito, massimo 15 minuti/60 operazioni. Nessun rinnovo automatico.
2. `prossime_relazioni_da_analizzare`: massimo dieci candidati, normalmente cinque, filtrabili per commessa e nome. Non legge il testo.
3. `leggi_relazione_geologica`: leggere soltanto i documenti scelti e le porzioni pertinenti, mantenendo la versione e gli offset restituiti. PDF testuali, DOCX, XLSX e testi; anche ZIP e 7z. Limiti 50 MiB, 20 secondi e 200.000 caratteri. OCR, DOC/XLS legacy e file cifrati non inclusi.
4. `elenca_fascicoli_geologici`: verificare se esiste già un fascicolo relativo allo stesso intervento e sito. Stesso cliente, stesso comune o nomi simili da soli non bastano.
5. `salva_analisi_geologica`: salvare il risultato e brevi citazioni esatte. `SourceFingerprint` e `AnalysisRevision` provengono dal catalogo; `DocumentVersion` dalla lettura. `ExpectedRevision` deve coincidere con `AnalysisRevision` (vuoto solo alla prima registrazione). `DossierId` vuoto crea un fascicolo, un ID noto vi associa il documento. Il server rifiuta versioni superate, citazioni non riscontrate e aggiornamenti concorrenti.

Il nuovo strumento di salvataggio è dichiarato **write**: ChatGPT può richiedere la conferma secondo le impostazioni dell'utente/workspace. Scrive esclusivamente il catalogo derivato. Non offre scritture generiche, spostamenti o modifiche dei file sorgente. La richiesta di analisi abilita il gruppo di lavoro, non un servizio autonomo: a fine richiesta, scadenza o limite, l'elaborazione si ferma.

Riferimento: [MCP personalizzato in ChatGPT](https://developers.openai.com/api/docs/guides/custom-mcp-server). L'uso resta soggetto ai [limiti del piano ChatGPT](https://learn.chatgpt.com/docs/pricing); non viene configurata una fatturazione API aggiuntiva da questa applicazione.

## Localizzazione

ChatGPT deve distinguere il sito indagato dalle sedi di committente, geologo e progettista. Comune e indirizzo devono essere sostenuti da una citazione. Le coordinate devono provenire dal documento oppure da una fonte geografica verificabile, riportata come URL HTTPS e collegata alla località citata. Specificare la precisione: sito, indirizzo, località o centro del comune. Non inventare coordinate; se mancano evidenze, lasciarle vuote.

Le coordinate IA sono mostrate sulla mappa come **proposte da verificare**. Un fascicolo con documenti che indicano posizioni discordanti non riceve una posizione unica. Nessun geocoder viene chiamato automaticamente dal server; un'eventuale ricerca geografica avviene nella richiesta ChatGPT con gli strumenti disponibili e le relative autorizzazioni.

## Client e doppio clic

### Ricerca dell'indirizzo con Nominatim

Selezionare una relazione e scegliere **Localizza indirizzo con Nominatim…**. Controllare l'indirizzo proposto e premere **Cerca indirizzo**: soltanto il testo mostrato viene inviato al servizio. Non inserire dati personali o riservati. Non vengono inviati documenti o nomi dei committenti automaticamente. Scegliere il risultato pertinente e salvare: coordinate, precisione, fonte e autore sono conservati nel catalogo JSON come posizione **geocodificata, da verificare**, senza modificare gli originali. Un risultato relativo al centro abitato non identifica il sito dell'intervento.

La ricerca passa dal server autenticato, non dal tunnel MCP. Non viene avviata alla digitazione, durante le scansioni o in modo periodico. Il server condivide fra i client cache persistente e limite di una richiesta al secondo, rispetta le pause richieste dal fornitore e non ripete automaticamente le richieste fallite. Cache e stato del limite si trovano nella sottocartella `geocodifica` dell'archivio geologico. Dati © OpenStreetMap contributors, ODbL; [condizioni Nominatim](https://operations.osmfoundation.org/policies/nominatim/).

La proprietà `Server.NominatimSearchUrl` nel file delle impostazioni del server consente di sostituire l'endpoint senza ricompilare: valore predefinito `https://nominatim.openstreetmap.org/search`. Usare un endpoint HTTPS compatibile con la ricerca JSON v2 Nominatim; riavviare il servizio dopo la modifica. Nessuna chiave o fatturazione viene configurata dall'applicazione.

La griglia mostra **Indirizzo** e **Percorso sul server**, inclusa la voce interna agli archivi. Il menu contestuale della riga offre **Apri documento (copia in sola lettura)** e **Apri percorso in Esplora file**. Il secondo comando seleziona il documento, oppure lo ZIP/7z che lo contiene, traducendo `\\ingtassinari.it\data` in `Z:` per l'apertura sul client.

**Analisi → Relazioni geologiche e geotecniche** apre una finestra indipendente a istanza unica, con ricerca dinamica, fascicolo, stato dell'analisi e mappa OpenStreetMap. Le selezioni di elenco e mappa sono collegate. Gli elementi privi di coordinate restano nell'elenco.

Il doppio clic su una riga e il pulsante **Apri documento** aprono il file nell'applicazione Windows associata. Per proteggere gli originali viene preparata una copia locale con attributo di sola lettura e protezione Office, anche quando il documento è contenuto in ZIP/7z. Si legge soltanto la voce scelta; nessuna estrazione dell'intero archivio. Il processo è separato e limitato a 25 secondi/512 MiB/50 MiB di documento. Le copie si trovano in `%LOCALAPPDATA%\Tassinari\CommesseClient\DocumentiConsultazione`. Non sono un nuovo archivio condiviso e possono essere eliminate dall'utente dopo la consultazione.

La mappa usa Leaflet 1.9.4 e tasselli OpenStreetMap, senza chiave o fatturazione configurata; richiede WebView2 e Internet. Solo le tessere della vista sono richieste. Nessun geocoding, prefetch o download offline automatico. Attribuzione visibile e cache normale del browser.

## Configurazione e aggiornamento

Installare/aggiornare Server, Client e pacchetto MCP 2.17.0. Sul server impostare `EnableMcpGeologyWrites: true` in `C:\ProgramData\Tassinari\CommesseServer\server-settings.json` e riavviare il servizio: il valore predefinito è `false`. Account, gruppo Windows e isolamento delle sorgenti restano gli stessi. Il canale MCP ammette una sola scrittura tipizzata al catalogo, soggetta a questa impostazione.

Riavviare il tunnel con il pacchetto nuovo e aggiornare gli strumenti del plugin: **15 strumenti**, di cui uno di scrittura del catalogo. La chiave runtime si inserisce solo nella finestra del client ufficiale. Un `GeologyCatalogPath` esplicito continua a usare un catalogo esterno; per quello generato dal servizio lasciare il valore vuoto.

## Collaborazione con Giuseppe

Giuseppe può sviluppare il motore di riconoscimento e interpretazione come componente separato o applicazione autonoma. I contratti `GeologicalAnalysisDraft`, `GeologicalAnalysisRecord` e `GeologicalDossier` definiscono lo scambio con il catalogo centrale; la parte di salvataggio/versionamento resta sul server. Le estensioni future ai parametri geotecnici devono riportare unità, fonte e stato di verifica e mantenere la compatibilità dello schema.
