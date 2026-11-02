# Catalogo geologico e geotecnico — integrazione in preparazione

Questa funzionalità è su un ramo di sviluppo. Non è ancora distribuita alla VM o ai client. La release pubblicata resta la 2.15.1.

## Stato dell'integrazione

Il lavoro/catalogo di Giuseppe non è stato individuato nei sorgenti o nella cartella condivisa dei salvataggi. Il contratto JSON descritto qui è il formato interno proposto dall'applicazione, non il formato verificato del suo lavoro. Occorre ottenere il catalogo originale, adattarlo e controllare associazioni e coordinate prima del rilascio.

I test usano solo dati sintetici creati nella cartella temporanea. Nessuna relazione o coordinata di prova viene installata nel catalogo aziendale. Non sono stati letti in massa documenti reali né acquistati o abilitati servizi Google.

## Finestra del client

Menu **Analisi → Relazioni geologiche e geotecniche**: finestra indipendente, non modale, con una sola istanza per client. Un secondo comando riporta in primo piano quella aperta; chiudendola è possibile riaprirla. La disconnessione o l'uscita dal client la chiudono e annullano il caricamento.

L'elenco contiene titolo, località, eventuale commessa e tipo di relazione. La ricerca dinamica e i filtri operano sulla fotografia già caricata. Il dettaglio indica anche il file, committente, autore, fonte e precisione della posizione. Le relazioni senza coordinate restano nell'elenco come **da localizzare**, senza inventare un punto sulla mappa. Selezionare un simbolo seleziona la relazione; selezionare la relazione evidenzia il simbolo. Relazioni con coordinate identiche sono raggruppate in un simbolo con selezione individuale.

## Catalogo sul server

Per impostazione predefinita il server legge `geologia/catalogo.json` nella cartella `ArchiveFolder`, quindi normalmente:

```text
\\ingtassinari.it\data\amministrazione\_GESTIONE COMMESSE\geologia\catalogo.json
```

È possibile indicare un file diverso con `GeologyCatalogPath` in `server-settings.json`. Il servizio necessita del solo permesso di lettura sul catalogo e sulle sorgenti. Questa componente non crea o modifica il catalogo. Il produttore del catalogo dovrà pubblicarlo tramite sostituzione atomica, per evitare letture durante una scrittura parziale.

Esempio strutturale, non dati aziendali:

```json
{
  "SchemaVersion": 1,
  "UpdatedUtc": "2026-11-02T10:00:00Z",
  "Reports": [
    {
      "Id": "identificativo-stabile-della-relazione",
      "Title": "Relazione geologica",
      "Kind": "Geologica",
      "FileName": "relazione.pdf",
      "PhysicalPath": "\\\\ingtassinari.it\\data\\lavori\\2026\\C26XXX - ESEMPIO\\relazione.pdf",
      "Municipality": "",
      "Address": "",
      "Latitude": null,
      "Longitude": null,
      "PositionSource": "",
      "PositionAccuracy": "",
      "ProjectCode": "",
      "Client": "",
      "Author": "",
      "Notes": "",
      "DocumentDate": null
    }
  ],
  "Warnings": []
}
```

Coordinate in WGS84, latitudine e longitudine entrambe note o entrambe `null`. `Id` unico e stabile. `Kind` normalmente `Geologica` o `Geotecnica`. Non associare una commessa o un punto geografico in assenza di evidenza; indicare sempre fonte/precisione quando disponibili.

Per un documento compresso, `PhysicalPath` indica lo ZIP o 7z e `ArchiveEntry` il percorso interno del documento. In alternativa a `PhysicalPath` si possono usare `Category` (`C`, `G`, `A`), `FolderId` dell'inventario, `RelativePath` e l'eventuale `ArchiveEntry`. Un documento fisico deve comunque appartenere a una delle radici lavori, gare o archivio configurate; collegamenti e attraversamenti di percorso restano soggetti ai controlli del lettore documenti esistente.

Catalogo massimo 20 MiB e 50.000 relazioni; paginazione con totale, data dell'aggiornamento e versione del catalogo. Se cambia durante la lettura delle pagine occorre ricaricare dalla prima. Un catalogo mancante o non valido produce un messaggio esplicito, non un elenco presentato come completo e vuoto.

## Google Maps

La mappa usa WebView2 e Google Maps JavaScript API. Servono il runtime Microsoft Edge WebView2, una chiave del progetto Google abilitato e un Map ID JavaScript per i simboli avanzati. La configurazione di produzione comporta l'attivazione della fatturazione Google Maps Platform; impostare quote e limiti nel proprio progetto.

Nel client, **Configura mappa** salva chiave e Map ID in `%LOCALAPPDATA%\Tassinari\CommesseClient\google-maps.json`. Limitare la chiave a Maps JavaScript API e al referrer `https://maps.ta-commesse.example/*`. Il dominio è un'origine virtuale locale del controllo; non va pubblicato su Internet. Le chiavi non sono incluse nel repository, nei log o nei pacchetti. Dopo il salvataggio chiudere e riaprire la finestra.

Senza configurazione, la mappa mostra le istruzioni e l'elenco rimane utilizzabile. Google riceve le richieste necessarie alla mappa; il controllo non invia documenti o percorsi dei file e non effettua geocodifica. Al controllo vengono passati solo identificativo, titolo, commessa e coordinate dei risultati localizzati. I titoli sono inseriti come testo, mai come HTML. Non è stato ancora effettuato il collaudo con una chiave Google reale; dovranno essere verificati rendering, referrer, caricamento delle risorse, attribuzione e condizioni del servizio prima del rilascio.

Riferimenti ufficiali: [configurazione della chiave](https://developers.google.com/maps/documentation/javascript/get-api-key), [simboli avanzati e Map ID](https://developers.google.com/maps/documentation/javascript/advanced-markers/start), [CSP di Google Maps](https://developers.google.com/maps/documentation/javascript/content-security-policy), [origine locale WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content).

## MCP: consultazione esplicita

Due nuovi strumenti, da aggiungere agli strumenti esistenti dopo la distribuzione del componente MCP:

- `cerca_relazioni_geologiche`: ricerca per parole, commessa e presenza di coordinate; restituisce metadati, totale, versione, data e pagine fino a 100 risultati. Non apre i documenti.
- `leggi_relazione_geologica`: prende l'ID della relazione scelta e legge il testo attraverso il server, solo con una consultazione aperta tramite `inizia_consultazione(contenuti=true)`.

Il lettore esistente supporta PDF testuali, DOCX, XLSX e testi, anche in ZIP/7z. Nessun OCR, esecuzione di macro, scansione automatica o scrittura dei documenti. Mantiene limiti e isolamento: 50 MiB, 20 secondi, due letture contemporanee complessive, massimo 200.000 caratteri, pagine di massimo 24.000 caratteri. Testi parziali o formati non supportati sono segnalati. Una richiesta valida produce un accesso esplicito; non vengono accodate letture in background. Il testo dei documenti è trattato come dato, mai come istruzione al plugin.

Il trasporto Windows/Kerberos e il canale locale MCP con ACL restano quelli esistenti. Non vengono aggiunte porte web al server. Aggiornare il catalogo degli strumenti di ChatGPT solo dopo avere distribuito servizio e componente MCP compatibili.

## Verifiche

`GeologyChecks`: catalogo, paginazione, filtri, coordinate mancanti/non valide, limiti delle sorgenti, originali invariati, autorizzazione della lettura, finestra unica e indipendente, chiusura e riapertura. Genera un'anteprima WPF con dati sintetici.

`map-checks.cjs`: selezione bidirezionale, raggruppamento nella stessa posizione, titoli trattati come testo, rimozione dei simboli filtrati con un simulatore dell'interfaccia Google; non sostituisce il collaudo della mappa reale.

Verificate inoltre le regressioni con `McpChecks`, `LocalMcpChecks`, `DocumentChecks` e `ClientStatusChecks`. Il test di creazione dei collegamenti simbolici dipende dalle autorizzazioni del sistema e può risultare saltato.
