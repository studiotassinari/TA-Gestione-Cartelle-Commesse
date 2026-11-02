# Relazioni geologiche e geotecniche — 2.16.0

Il catalogo è generato autonomamente dal server a partire dai nomi dei file già censiti. Non richiede un catalogo esterno, chiavi API o servizi di IA a pagamento. Gli originali restano sempre in sola lettura.

## Catalogazione autonoma

Un componente del servizio lavora all'avvio e poi ogni ora esclusivamente sui JSON dell'inventario. Consulta anche le unità già completate durante il primo censimento, senza aspettare il report globale. Non apre documenti e non riesamina i contenuti degli archivi.

Riconosce termini geologici/geotecnici nei nomi di PDF, Word, testi e altri documenti; include come candidati anche i documenti nelle cartelle tematiche. Le voci ZIP/7z sono trattate allo stesso modo, mantenendo contenitore e percorso interno distinti. Sono escluse le cartelle, i file temporanei Office e i formati non documentali come DWG. I risultati sono **da verificare**: il nome non dimostra che si tratti effettivamente di una relazione. Le unità non classificate non diventano commesse ufficiali.

La posizione non viene dedotta dal solo committente o dal codice numerico. Solo coordinate esplicitamente etichettate lat/lon nei nomi vengono proposte, con indicazione che non sono verificate. Negli altri casi la posizione resta sconosciuta finché viene indicata da un utente. Nessuna geocodifica esterna automatica.

I risultati vengono conservati in:

```text
<ArchiveFolder>\geologia\catalogo.json
<ArchiveFolder>\geologia\unita\<categoria>\<anno>\<codice>\<id-unita>.json
<ArchiveFolder>\geologia\posizioni.json
```

Nel sistema aziendale `ArchiveFolder` è normalmente `\\ingtassinari.it\data\amministrazione\_GESTIONE COMMESSE`. Il primo file è il catalogo aggregato; i file delle unità vengono rigenerati solo se cambia la generazione dell'inventario. Le posizioni manuali sono separate e non vengono sovrascritte dal censimento. Le scritture usano sostituzione atomica; le modifiche alle posizioni usano anche un lock tra processi e il controllo della versione.

Il catalogo conserva gli ultimi dati in caso di errore e segnala letture parziali o unità non disponibili. Una rimozione viene applicata quando l'inventario dell'unità la conferma. I risultati possono comprendere dati della verifica precedente; ogni relazione espone la data e la completezza della sua unità. L'assenza di una voce in un catalogo parziale non prova l'assenza del documento. Nessuna notifica delle differenze viene inviata.

Limiti: 50.000 candidati e 20 MiB per catalogo aggregato; eventuali limiti raggiunti sono espliciti. `GeologyCatalogPath`, se valorizzato in `server-settings.json`, seleziona un catalogo esterno compatibile e disattiva la generazione automatica: non è necessario per l'uso ordinario. Tutti i salvataggi geologici devono essere fuori dalle radici sorgenti.

## Finestra del client

**Analisi → Relazioni geologiche e geotecniche** apre una finestra indipendente, non modale e a istanza unica. Il comando riporta in primo piano la finestra già aperta; la chiusura o riconnessione del client la chiude.

La ricerca dinamica filtra titolo, nome file, località, indirizzo, commessa, committente, autore e note nella fotografia caricata. Filtri aggiuntivi: tipo di relazione e presenza della posizione. L'elenco e la mappa si selezionano reciprocamente. Relazioni nella stessa posizione sono raggruppate in un simbolo numerato con scelta individuale. Le relazioni senza coordinate restano nell'elenco.

Per localizzare una relazione: selezionarla e usare **Imposta posizione…**, oppure fare clic destro sulla mappa. La finestra propone coordinate WGS84, località, indirizzo e fonte/precisione. Il salvataggio è esplicito e riguarda soltanto il catalogo; registra account Windows e data sul server. Gli utenti già autorizzati al client possono contribuire alle posizioni. Una modifica concorrente richiede di ricaricare prima di salvare. Il plugin MCP non dispone di questo comando di scrittura.

## Mappa senza servizi a pagamento

Leaflet 1.9.4 è incluso nei file dell'applicazione. La cartografia è OpenStreetMap, con attribuzione visibile, identificazione dell'applicazione e cache standard di WebView2. Nessuna chiave o fatturazione richiesta. Vengono richieste solo le tessere della vista interattiva: nessun download di aree, scansione delle mappe o modalità offline. Al fornitore cartografico non sono inviati nomi, percorsi o contenuti dei documenti; i simboli sono generati localmente. Occorrono Internet e Microsoft Edge WebView2 Runtime. In assenza di cartografia l'elenco resta utilizzabile; il servizio cartografico pubblico non offre disponibilità garantita.

Riferimenti ufficiali: [Leaflet](https://leafletjs.com/download.html), [condizioni di uso delle tessere OpenStreetMap](https://operations.osmfoundation.org/policies/tiles/). Licenza Leaflet inclusa in `Assets/Maps/Leaflet-LICENSE.txt`.

## MCP e contenuti

- `cerca_relazioni_geologiche`: ricerca per parole, commessa e presenza di coordinate, con totale, data, versione e paginazione. Restituisce candidati e motivazione del riconoscimento; non apre i documenti.
- `leggi_relazione_geologica`: legge il singolo documento identificato dal catalogo, esclusivamente con consultazione aperta con `contenuti=true`.

Il lettore isolato del server supporta PDF testuali, DOCX, XLSX e testi Unicode, anche in ZIP/7z. Nessun OCR, macro o contenuto cifrato. DOC/XLS/RTF/ODT possono essere individuati dal nome ma la lettura del loro contenuto non è supportata. Limiti condivisi con `leggi_documento`: 50 MiB, 20 secondi, due letture contemporanee, 200.000 caratteri e pagine di massimo 24.000 caratteri. Nessuna estrazione su disco, memorizzazione del testo o lettura massiva in background.

`cerca_file` può ora consultare i JSON delle unità già censite prima della conclusione del primo inventario generale. I risultati sono dichiarati parziali. `elenca_documenti` e `leggi_documento` continuano a funzionare indipendentemente dalla ricerca globale, appena una cartella è presente nel censimento. Gli errori applicativi vengono comunicati esplicitamente al plugin, senza stack trace e senza presentarli come assenza di file.

## Verifiche

`GeologyChecks` verifica generazione da inventari, file ZIP, associazioni non confermate, posizioni assenti, protezione degli originali, salvataggi per anno/commessa, aggiornamenti e concorrenza delle posizioni, catalogo parziale, paginazione, finestra unica e lettura isolata dei contenuti.

`node tests/GeologyChecks/map-checks.cjs` verifica raggruppamento, filtri e selezione. `dotnet run --project tests/GeologyChecks -c Release -- --map-browser` verifica anche WebView2 e Leaflet reali con tessere simulate: nessun traffico automatico verso OpenStreetMap. I test non utilizzano documenti aziendali. Restano le verifiche MCP, canale Windows, ricerca file, lettore documenti e interfaccia client.
