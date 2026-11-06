# Gestionale clienti, attività e ore

La finestra **Gestionale** del client è a istanza singola. Usa la connessione TCP/Kerberos già configurata. Le anagrafiche, le autorizzazioni, le attività e le ore vengono salvate esclusivamente dal server in `gestionale.sqlite`, nella `DatabaseFolder` configurata (sulla VM: `E:\TA-Commesse\Data`). È un archivio distinto dall'indice ricostruibile dei file.

## Accesso

La gestione del servizio dal client e le modifiche a clienti, pianificazione e autorizzazioni ore sono riservate ai SID Windows risolti dai due account:

- `antonio.tassinari@ingtassinari.it`
- `giuseppe.tassinari@ingtassinari.it`

Gli altri collaboratori autorizzati al servizio consultano le attività e registrano le proprie ore. Il server usa l'identità autenticata, ignorando l'eventuale nome del titolare inviato dal client. Gli amministratori Windows mantengono naturalmente i poteri previsti dal sistema operativo al di fuori dell'applicazione.

## Clienti da portali cloud

Questa versione non richiede credenziali/API di Magoot o Fatture in Cloud. Importa **l'Excel XLSX esportato da Fatture in Cloud** oppure un CSV selezionato dal client. Non naviga automaticamente nei portali e non dichiara una sincronizzazione continua.

### Excel Fatture in Cloud

Il formato è stato verificato sull'esportazione del 6 novembre 2026: foglio `Export`, intestazioni nella prima riga. Il client accetta direttamente `.xlsx` nel comando **Importa Fatture in Cloud**, senza conversione manuale o installazione di Excel.

Vengono acquisiti Denominazione, Codice interno, P.IVA/TAX ID, Codice Fiscale, Indirizzo e-mail e l'indirizzo composto da Indirizzo, CAP, Comune, Provincia, Paese e Note indirizzo. Le altre colonne (contatti aggiuntivi, PEC, SDI, IBAN, condizioni commerciali e note generali) non sono ancora acquisite. Il file Excel completo non viene conservato nel database.

Gli identificativi e i CAP testuali mantengono gli zeri iniziali. I valori numerici in tali colonne vengono segnalati per verifica, senza ricostruire zeri arbitrariamente. Le righe senza codice interno né partita IVA né codice fiscale, e quelle con identificativi sorgente duplicati, sono escluse con indicazione delle righe. Gli altri conflitti vengono valutati dalla riconciliazione comune e sono indicati nel riepilogo.

Limiti: 8 MiB compressi, 32 MiB complessivi non compressi, 5.000 clienti. Le formule non sono eseguite e vengono rifiutate; non vengono seguiti collegamenti esterni. L'anteprima usa lo stesso lettore dell'importazione e non salva anagrafiche.

### CSV

Il formato effettivo di Magoot resta da verificare su un'esportazione reale. Il CSV accettato è UTF-8, con separatore `;` oppure `,` e campi tra virgolette. Sono supportate queste intestazioni (ignorando maiuscole, spazi e punteggiatura):

| Campo | Intestazioni riconosciute |
|---|---|
| Nome obbligatorio | Ragione sociale, Denominazione, Nome, Name, Cliente |
| Identificativo sorgente | Id, Codice, Codice cliente |
| Partita IVA | Partita IVA, P.IVA, Vat number, Vat |
| Codice fiscale | Codice fiscale, CF, Tax code |
| Indirizzo | Indirizzo, Address |
| Email | Email, Posta |

Se manca il codice sorgente viene usata la partita IVA oppure il codice fiscale. È necessario almeno uno di questi identificativi. Limiti: 2 MB e 5.000 righe per importazione. Le intestazioni o i codici sorgente duplicati vengono rifiutati prima di scrivere.

L'anteprima non scrive dati. Alla conferma, i clienti sono riconciliati per identificativo sorgente o identificativo fiscale. I conflitti sono segnalati e saltati; una denominazione locale già presente non viene sovrascritta. Viene conservata la provenienza da ciascun portale. L'importazione non elimina clienti assenti da un'esportazione successiva. Ogni riga viene salvata con una transazione; in caso d'interruzione controllare il riepilogo e reimportare il file, senza creare duplicati per lo stesso identificativo.

## Pianificazione e ore

La scheda Commesse associa il cliente e stabilisce l'abilitazione delle ore: bloccate, preventivo accettato, contratto oppure deroga motivata. Il riferimento al documento è dichiarato dal responsabile: questa versione **non verifica automaticamente l'accettazione** del preventivo o la firma del contratto.

Le attività hanno commessa, fase, collaboratore Windows, date, ore previste, stato e avanzamento. Il Gantt WPF gratuito le raggruppa per commessa o collaboratore; selezionare una barra apre l'editor per i responsabili. Le date si modificano nell'editor, senza trascinamento. I totali delle ore previste riguardano le attività mostrate, non una ripartizione automatica del carico sui giorni.

Le ore si registrano solo su attività assegnate al titolare, con controllo server delle autorizzazioni, scadenza, data non futura e massimo giornaliero di 24 ore. I collaboratori vedono solo i propri consuntivi; i responsabili possono consultare e correggere quelli esistenti. L'editor usa le attività comprese nel periodo selezionato: ampliare il periodo se un'attività non compare.

Le modifiche contemporanee alla stessa scheda sono rilevate tramite revisione: aggiornare prima di riprovare. Le registrazioni gestionali hanno una traccia delle modifiche nel database del server; non è un log di connessione del client.

## Backup e documenti

Ogni cinque minuti il servizio controlla se il gestionale è cambiato e produce una copia SQLite coerente, verificata con controllo d'integrità e confronto SHA-256, nella cartella condivisa `backup-gestionale`. Mantiene una copia per giorno e il numero di giorni configurato in `BackupRetention` (predefinito 7). Nessuna attività pianificata Windows. Un errore lascia la copia precedente e viene mostrato nello stato del gestionale.

Il lettore documenti usa temporaneamente il disco locale configurato come TEMP dal servizio, anche per un documento dentro ZIP/7z: massimo 50 MiB e cancellazione alla chiusura della copia. Non copia interi archivi e non avvia analisi esterne autonomamente. Gli originali restano sempre in sola lettura.

## Validazione

`tests/BusinessChecks` verifica permessi, anteprima senza scritture, importazioni ripetute e conflitti, revisioni concorrenti, titolarità e limiti delle ore, persistenza e backup. Comprende le prove XLSX su stringhe condivise, celle sparse, zeri iniziali, formule, duplicati e identificativi mancanti. L'opzione `--preview-excel <percorso>` verifica un'esportazione reale senza salvare anagrafiche e senza stampare dati personali. `tests/BusinessUiChecks` carica e renderizza le cinque schede con soli dati sintetici. Restano necessarie la verifica dell'esportazione Magoot e la prova di accettazione sul dominio prima della distribuzione.
