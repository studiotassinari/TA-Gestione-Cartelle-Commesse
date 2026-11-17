# Database comune con GeCo2027

TA-Gestione Cartelle Commesse e GeCo2027 condividono un database SQL Server. Lo schema, gli aggiornamenti e le regole comuni stanno nella libreria **GeCo.Dati** (repository GeCo2027, cartella `shared/GeCo.Dati`), che questo progetto usa come pacchetto NuGet: qui non si scrivono script per quel database.

## Cosa fa il servizio (versione 2.34.0)

Se è configurata la stringa di connessione, il servizio:

1. porta il database alla versione prevista dalla libreria, se non l'ha già fatto GeCo2027;
2. scrive nella tabella `tblCartelle` l'elenco delle cartelle di commesse (C), gare (G) e archivio (A): codice, anno, committente e oggetto letti dal nome, percorso, stato, dimensione, ultima modifica, esito del controllo sul preventivo;
3. lo ripete all'avvio del servizio e alla fine di ogni elaborazione giornaliera. Le cartelle non più trovate restano in tabella segnate come non presenti.

Vengono inviati solo dati già presenti negli elenchi del servizio: nessuna lettura aggiuntiva sulle cartelle e nessuna scrittura sugli originali. Un errore di collegamento non ferma l'elaborazione: viene scritto nel registro del servizio e segnalato nell'esito.

GeCo2027 mostra questi dati nella pagina **Cartelle**.

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

## Pianificazione delle attività

La pianificazione delle attività sta in GeCo2027 (pagina **Pianificazione**). Il client la apre nel browser con il comando **Gestione > Pianificazione delle attività in GeCo…**: non ne esiste una copia nel client.

Il comando è attivo quando in **Gestore server > Configura** è indicato l'**indirizzo di GeCo** (per esempio `http://server:8027`); dopo averlo salvato va riavviato il servizio. Sono accettati solo indirizzi `http` o `https`.
