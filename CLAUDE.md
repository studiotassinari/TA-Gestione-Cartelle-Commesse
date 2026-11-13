# Istruzioni per lavorare su TA-Gestione Cartelle Commesse

Repository **pubblico** dello Studio Tassinari (Antonio e Giuseppe). Il progetto è nato con ChatGPT/Codex: rispettare le convenzioni già presenti, non riscrivere per gusto personale.

## Architettura

Soluzione `TA-Gestione Cartelle Commesse.slnx`, .NET 10, solo Windows (`net10.0-windows`).

- `TA-Gestione Cartelle Commesse/` — client WPF/MVVM (ViewModels divisi in file parziali `MainViewModel.*.cs`, viste in `Views/`).
- `TA.Server/` — servizio Windows: inventario, scansioni, cataloghi geologia e MUR/IPRiPI, SQLite locale, lettura documenti (`Documents/`), archiviazione ZIP (`Archiving/`).
- `TA.Contracts/` — record condivisi tra client, trasporto e servizio.
- `TA.Core/` — inventario e servizi comuni. `TA.Transport/` — protocollo client/servizio (versione 2).
- `TA.Mcp/` — server MCP usato da ChatGPT tramite tunnel ad avvio manuale (21 strumenti).
- `TA.Updater/`, `TA.ServiceManager/`, `TA.Desktop/` — aggiornatore, gestore servizio, area di notifica.
- `tests/*Checks/` — programmi console di verifica (non xUnit): `Check(condizione, "descrizione")`, stampano `PASS:` e falliscono con eccezione.
- `deployment/` — script PowerShell e documentazione operativa (MCP, GEOLOGIA, IPRIPI, SQLITE, ARCHIVIAZIONE-ZIP…).

## Regole non negoziabili

- Le cartelle e i documenti originali delle commesse si leggono **soltanto**. Indici, cataloghi e cache scrivono solo nei propri database/JSON. Unica eccezione: archiviazione ZIP dal client, con conferma separata, eseguita dal server, solo per Antonio/Giuseppe.
- Scansioni automatiche solo su nomi, percorsi e metadati. Contenuti letti solo su richiesta esplicita (MCP o apertura dal client) oppure dall'analisi automatica locale, se abilitata in Configura: un documento alla volta, entro i limiti di `LocalAnalysisLimits`, senza servizi esterni e senza sostituire analisi salvate su richiesta.
- Nessuna analisi cloud autonoma, nessuna API a pagamento, nessuna modifica ad Active Directory.
- Candidati, dati parziali e posizioni non accertate vanno sempre indicati come tali, mai come risultati certi.
- Mai nel repository: documenti reali, cataloghi reali, coordinate riservate, chiavi del tunnel, credenziali. I casi di prova sono sintetici o anonimizzati. Unica eccezione, decisa dai titolari: la grafica dello studio per il curriculum in `TA.Server/Assets/CurriculumGrafica` (logo, copertina, icone, `stile.json`); i file dei caratteri non vanno mai aggiunti.
- I permessi si decidono sul server (token Windows), mai da campi della richiesta.
- Nessun dominio, gruppo, utente o nome di persona nel codice e nei messaggi: provengono da `server-settings.json` (Gestore server > Configura). Nei testi usare «responsabili autorizzati».

## Stile del codice

- Testi dell'interfaccia, messaggi di errore, commenti, note di versione e documentazione **in italiano**.
- I commenti spiegano il *perché* (vincoli, garanzie di sola lettura, concorrenza), non il cosa.
- C# compatto come nel codice esistente: record, espressioni `switch`, metodi brevi, `Nullable` abilitato.
- Le operazioni lunghe sono cancellabili (`CancellationToken`) e conservano i dati precedenti in caso di errore.

## Versioni e rilasci

Il numero di versione è ripetuto in più punti; aggiornarli tutti insieme:
`.github/workflows/installer.yml` (`$version` e note di rilascio), `installer.iss`, `server-installer.iss`, titolo e sezione «Novità» del `README.md`, eventuali documenti in `deployment/` che riportano la versione.

- Un push su `master` che tocca i sorgenti avvia `installer.yml`, che crea la release `vX.Y.Z`: non sovrascrivere mai una release esistente. `[skip ci]` nel messaggio evita la pubblicazione.
- Le pull request verso `master` avviano `verify.yml`.
- Aggiornare prima il server, poi i client; il tunnel MCP va riavviato manualmente.
- Messaggi di commit nello stile esistente: `Release X.Y.Z: descrizione breve`.

## Flusso di lavoro

- Ogni intervento su un ramo distinto da `master` aggiornato, poi pull request con comportamento atteso, modifiche, test e limiti (vedi `CONTRIBUTING.md`).
- Compilazione e test richiedono Windows con .NET 10 SDK: `dotnet build 'TA-Gestione Cartelle Commesse.slnx' -c Release`, poi `dotnet run --project tests/<Nome>Checks -c Release`. L'elenco completo dei test è in `installer.yml`.
- Quando si aggiunge una funzione, aggiungere o aggiornare il relativo `tests/*Checks` e, se cambia l'uso, il documento in `deployment/`.
