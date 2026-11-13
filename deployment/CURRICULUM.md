# Curriculum dello studio

Modulo per raccogliere i lavori svolti, da usare come referenze e nelle gare. Si apre dal menu **Gestione > Curriculum dello studio…** oppure, da una riga di commesse, gare o archivio, con il tasto destro e **Curriculum: lavori collegati…**.

## Scheda del lavoro

Titolo, committente, luogo, date di inizio e fine, prestazioni svolte, ruolo dello studio, importo dei lavori, importo del servizio, categorie delle opere (ID opere, importo e descrizione, una per riga), riferimenti del committente, descrizione, parole chiave e fotografie con didascalia.

I lavori si inseriscono a mano. Ogni lavoro può essere collegato a una o più commesse o gare tramite il **codice** (per esempio `C24031`, `G23012`): il codice resta lo stesso quando la cartella passa dalle commesse attive all'archivio, quindi il collegamento non si perde. La scheda indica dove si trova oggi ogni cartella collegata e permette di mostrarla nell'elenco; al contrario, da una commessa si aprono i lavori del curriculum che la citano e se ne può creare uno nuovo già precompilato.

La ricerca considera titolo, committente, luogo, descrizione, prestazioni, parole chiave, categorie, didascalie, anni e codici collegati, senza distinzione di maiuscole e accenti.

## Chi può modificare

Tutti gli utenti autorizzati al programma possono consultare e aggiornare il curriculum. Ogni salvataggio registra autore e data; il registro delle modifiche conserva la versione precedente. Se due persone modificano la stessa scheda, il secondo salvataggio viene rifiutato con l'invito ad aggiornare: nessuna modifica viene sovrascritta in silenzio.

## Dove sono i dati

I lavori sono nello stesso archivio SQLite del gestionale (`gestionale.sqlite`, sul disco locale del server), quindi rientrano nel backup verificato già esistente. Le fotografie sono file nella cartella `curriculum-foto` accanto al database e vengono aggiunte al backup. Il client le riduce a 1800 punti sul lato maggiore prima dell'invio; il server accetta solo JPEG e PNG, fino a 8 MB e 30 fotografie per lavoro. Le cartelle delle commesse non vengono mai lette né modificate da questo modulo.

## Esportazione in Word

**Esporta in Word…** produce un documento con i lavori mostrati in quel momento dalla ricerca: un lavoro per pagina, con dati, tabella delle categorie, descrizione e fotografie.

Per avere l'aspetto dello studio indicare nel **Gestore server > Configura** un modello Word (`.docx` o `.dotx`). Dal modello vengono conservati intestazione, piè di pagina, margini e stili; il testo di esempio viene sostituito. Il documento usa gli stili predefiniti di Word presenti nel modello: *Titolo* per l'intestazione del documento, *Titolo 1* per ogni lavoro, *Titolo 2* per le sezioni, *Didascalia* per le fotografie e *Normale* per il testo. Senza modello l'impaginazione è neutra.
