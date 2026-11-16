# Analisi automatica locale

Il server può analizzare da solo i documenti candidati dei cataloghi **MUR A.1/D.1 — IPRiPI** e **Relazioni geologiche**, senza ChatGPT e senza alcun servizio esterno. È disattivata finché non viene scelta nel **Gestore server > Configura**; le modifiche valgono dal successivo avvio del servizio.

## Che cosa fa

- Prende i candidati già individuati dal nome nel censimento, dalle commesse più recenti alle più vecchie, alternando i due cataloghi.
- Legge il testo con lo stesso processo isolato e in sola lettura delle letture su richiesta. Gli originali non vengono mai modificati.
- **MUR**: riconosce il modulo dal testo e propone solo i codici dell'Allegato 1 della DGR 2272/2016 citati esplicitamente, con il livello L0/L1/L2 se leggibile. Molti codici diversi (elenco prestampato), codici assenti o livello non leggibile restano «Da verificare». Non deduce mai un esito negativo.
- **Geologia**: dalle prime pagine conferma o smentisce il candidato (geologica, geotecnica, entrambe, non pertinente) e propone il Comune se citato. Autore, coordinate e fascicoli comuni non vengono dedotti.
- Ogni risultato è salvato come «Analisi automatica locale», con citazioni esatte del testo, ed è una **proposta da verificare**. Un'analisi salvata su richiesta tramite ChatGPT non viene mai sostituita; può invece sostituire quella automatica.

## Limiti all'uso di risorse

| Limite | Valore |
|---|---|
| Fascia oraria (ora italiana) | Predefinita 20:00–07:00; configurabile. Inizio uguale a fine = tutto il giorno |
| Documenti l'ora | Predefinito 30 (uno ogni 2 minuti); configurabile da 1 a 120 |
| Letture contemporanee | Una sola, a priorità di processo ridotta |
| Tempo per documento | Al massimo 20 secondi per lettura, poi il processo viene terminato |
| Memoria per documento | Al massimo 512 MB, poi il processo viene terminato |
| Dimensione del documento | Al massimo 50 MB |
| Testo letto | MUR: prime due porzioni (48.000 caratteri); geologia: prima porzione (24.000 caratteri) |
| Precedenze | In pausa durante la scansione dell'inventario e durante le letture su richiesta |
| Documenti problematici | Messi da parte per sette giorni; quelli senza testo finché non cambiano |

Proprietà nel file `server-settings.json`, sezione `Server`: `EnableLocalAnalysis`, `LocalAnalysisStartHour`, `LocalAnalysisEndHour`, `LocalAnalysisDocumentsPerHour`. Valori fuori intervallo sono ricondotti ai limiti ammessi.

Lo stato compare nel registro del servizio e nella voce «modalità di analisi» dello stato analisi.

## Che cosa non fa ancora

Non include OCR: i documenti acquisiti come immagine sono registrati come «Non leggibile» (MUR) o saltati (geologia) e restano da leggere con ChatGPT o a vista. Le caselle barrate, le firme e le annotazioni grafiche non sono valutate.

## Ordine di analisi e avanzamento (dalla 2.34.1)

Per le relazioni geologiche l'analisi automatica parte dai documenti che hanno «geologica» o «geotecnica» nel nome del file e solo dopo passa a quelli individuati per la cartella in cui si trovano, che sono molti di più e raramente sono relazioni. Gli elenchi elaborati e i formati diversi da PDF, DOCX e TXT non vengono analizzati in automatico.

La riga di stato del catalogo delle relazioni geologiche riporta: se l'analisi è attiva, la fascia oraria e il limite orario, quanti documenti hanno un'analisi valida, quanti sono in coda e quanti se ne possono analizzare al massimo in un giorno (la capacità è condivisa con il catalogo MUR). Se la coda è lunga rispetto alla capacità, si possono allargare la fascia oraria o il numero di documenti l'ora in **Gestore server > Configura** (fino a 120 l'ora, anche tutto il giorno).

Le posizioni sulla mappa non vengono cercate in automatico: la ricerca di un indirizzo usa un servizio esterno e parte solo su richiesta dal client.
