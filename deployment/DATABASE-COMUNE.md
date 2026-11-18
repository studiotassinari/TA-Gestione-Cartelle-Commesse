# Database comune con GeCo2027

TA-Gestione Cartelle Commesse e GeCo2027 condividono un database SQL Server. Lo schema, gli aggiornamenti e le regole comuni stanno nella libreria **GeCo.Dati** (repository GeCo2027, cartella `shared/GeCo.Dati`), che questo progetto usa come pacchetto NuGet: qui non si scrivono script per quel database.

## Cosa fa il servizio (dalla versione 2.34.0; pianificazione e ore dalla 2.37.0)

Se è configurata la stringa di connessione, il servizio:

1. porta il database alla versione prevista dalla libreria, se non l'ha già fatto GeCo2027;
2. scrive nella tabella `tblCartelle` l'elenco delle cartelle di commesse (C), gare (G) e archivio (A): codice, anno, committente e oggetto letti dal nome, percorso, stato, dimensione, ultima modifica, esito del controllo sul preventivo;
3. lo ripete all'avvio del servizio e alla fine di ogni elaborazione giornaliera. Le cartelle non più trovate restano in tabella segnate come non presenti.

Vengono inviati solo dati già presenti negli elenchi del servizio: nessuna lettura aggiuntiva sulle cartelle e nessuna scrittura sugli originali. Un errore di collegamento non ferma l'elaborazione: viene scritto nel registro del servizio e segnalato nell'esito.

GeCo2027 mostra questi dati nella pagina **Cartelle**.

Dalla 2.37.0 lo stesso collegamento serve anche alla **pianificazione delle attività** e alle **ore lavorate** del client (vedi sotto).

## Configurazione

Il servizio si collega al database **sempre con un account SQL** (utente e password), mai con l'account Windows con cui gira. Gli utenti, invece, si riconoscono con il proprio account Windows sia nel programma sia nel browser.

In **Gestore server > Configura**, sezione «Database comune con GeCo», compilare i quattro campi, poi salvare e riavviare il servizio:

| Campo | Esempio |
| --- | --- |
| Server SQL | `.\SQLEXPRESS` |
| Nome del database | quello usato da GeCo2027 |
| Utente SQL | lo stesso account di GeCo2027 oppure uno dedicato |
| Password | la password di quell'utente |

- L'utente SQL deve avere sul database i ruoli `db_datareader`, `db_datawriter` e `db_ddladmin` (l'ultimo serve per applicare gli aggiornamenti dello schema).
- Utente e password restano nel file `server-settings.json` del server, leggibile solo dagli amministratori: non vanno copiati altrove.
- Quattro campi vuoti = nessun collegamento, comportamento identico alle versioni precedenti.

**Controllo**: riaprendo Configura, sotto i quattro campi compare l'esito dell'ultimo invio («Ultimo invio riuscito: C: … cartelle…» oppure «Ultimo invio NON riuscito:» con l'errore di SQL Server). Lo stesso esito è nel file `database-comune-stato.txt` della cartella dati del servizio.

Ordine degli aggiornamenti quando cambia il database: prima GeCo2027, poi il server di TA-Gestione, poi i client.

## Compilazione

Il pacchetto `GeCo.Dati` è su GitHub Packages di `studiotassinari`, che richiede sempre l'autenticazione.

- **Su GitHub**: i flussi `verify.yml` e `installer.yml` usano il segreto `GECO_PACKAGES_TOKEN` se esiste, altrimenti il token del flusso. Perché basti il token del flusso, nelle impostazioni del pacchetto GeCo.Dati (*Package settings > Manage Actions access*) va aggiunto questo repository con accesso in lettura.
- **Sul proprio PC**: impostare la variabile d'ambiente `GECO_PACKAGES_TOKEN` con un token personale che abbia il permesso `read:packages` (e `GITHUB_ACTOR` con il proprio nome utente GitHub), poi compilare normalmente.

La verifica `SharedDatabaseChecks` (dentro `BusinessChecks`) prova la scrittura su un SQL Server vero solo se è impostata la variabile `TA_TEST_SQL` con una stringa di connessione a un database di prova vuoto; su GitHub è LocalDB.

## Pianificazione delle attività e ore lavorate (2.37.0)

La pianificazione delle attività e le ore lavorate (statini) sono quelle di GeCo: il client le mostra in due finestre proprie (**Gestione > Pianificazione delle attività…** e **Gestione > Ore lavorate…**), con gli stessi contenuti delle pagine Pianificazione e Ore lavorate di GeCo2027, e il servizio le legge e le scrive nel database comune (`tblPianificazioneAttivita`, `tblPianificazioneTeamManager`, `tblRilevazioni` e collegate). Le regole non sono scritte qui: il servizio chiama i servizi della libreria GeCo.Dati (`ServizioPianificazione`, `ServizioConsuntivo`), gli stessi usati da GeCo2027, quindi tariffe, periodo di apertura delle commesse, fasi obbligatorie, tetti di valore e di ore, classificazione CDC, rilevazione speculare sulla commessa collegata, conferme e totale della giornata entro 24 ore valgono allo stesso modo nel browser e nel programma.

**Chi è l'utente.** Il servizio prende l'account Windows autenticato sul canale Kerberos (mai un nome inviato dal client) e cerca l'utente di GeCo con la funzione comune `AccountWindows.TrovaUtenteAsync`, la stessa dell'accesso con l'account Windows di GeCo2027: prima l'abbinamento indicato nella pagina **Utenti** di GeCo, poi il nome utente uguale al nome dell'account (o all'indirizzo completo; per gli account fatti del solo cognome, l'unico «nome.cognome» con quel cognome). Le corrispondenze scritte nella configurazione di GeCo2027 non valgono per il programma: se un account non viene riconosciuto, abbinarlo nella pagina Utenti. L'utente deve essere attivo e abilitato alla società di lavoro; da quel momento valgono i suoi permessi di GeCo: la funzione «Ore lavorate» (RilevazioneStatini.aspx), i ruoli della pianificazione (amministratori, team manager, collaboratori) e chi può modificare le ore di chi. Un account senza utente di GeCo vede nelle finestre il motivo e non può fare nulla.

**Società.** Il servizio lavora sulla società indicata dalla proprietà `GeCoCompanyId` di `server-settings.json` (sezione `Server`, il `Soc_Id` della società su cui lavora GeCo2027); senza la proprietà usa la prima società del database, che basta quando ce n'è una sola. La finestra Configura conserva la proprietà quando salva.

**Ruoli e team.** Dalla pianificazione gli amministratori danno o tolgono il ruolo di team manager. I team permanenti e i team per persona (usati dalle ore per proporre le commesse) si gestiscono per ora in GeCo2027, pagina Pianificazione > Ruoli e team: le loro regole non sono ancora nella libreria comune.

**Requisiti.** Il database comune configurato (i quattro campi qui sopra) e aggiornato da GeCo.Dati: pianificazione e team manager richiedono la migrazione 7 e gli abbinamenti della pagina Utenti la migrazione 8 (le ore lavorate usano le tabelle di GeCo2017); con il database non aggiornato le finestre lo dicono. I ruoli SQL sono quelli già indicati (`db_datareader`, `db_datawriter`). Un servizio precedente alla 2.37.0 risponde che il comando non è supportato e il client chiede di aggiornarlo.

L'indirizzo di GeCo in Configura non serve più per la pianificazione: il comando che apriva la pagina nel browser (2.36.0) è stato sostituito dalle finestre del programma.

Le attività, il Gantt e le ore registrate nel vecchio archivio del gestionale (`gestionale.sqlite`) non si mostrano più nel client: erano dati di prova, restano nel file e nei suoi backup e non vengono cancellati.

Verifiche: `tests/GeCoChecks` (identità, permessi e comandi del protocollo su dati sintetici), `tests/TransportChecks` (trasporto del comando), `tests/ClientChecks` e `tests/BusinessUiChecks` (calcoli e finestre del client); le regole stesse sono provate in GeCo2027 (`tests/GeCo.Dati.Checks`).
