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

In **Gestore server > Configura**, campo «Database comune con GeCo», indicare la stringa di connessione, poi salvare e riavviare il servizio. Esempi:

```
Server=.\SQLEXPRESS;Database=NomeDatabase;Integrated Security=True;TrustServerCertificate=True
Server=.\SQLEXPRESS;Database=NomeDatabase;User ID=utente;Password=...;TrustServerCertificate=True
```

- Con `Integrated Security=True` il collegamento avviene con l'account con cui gira il servizio: quell'account deve avere un accesso in SQL Server e, sul database, i ruoli `db_datareader`, `db_datawriter` e `db_ddladmin` (l'ultimo serve per applicare gli aggiornamenti dello schema).
- Con utente e password la stringa resta nel file `server-settings.json` del server: non va copiata altrove.
- Campo vuoto = nessun collegamento, comportamento identico alle versioni precedenti.

Ordine degli aggiornamenti quando cambia il database: prima GeCo2027, poi il server di TA-Gestione, poi i client.

## Compilazione

Il pacchetto `GeCo.Dati` è su GitHub Packages di `studiotassinari`, che richiede sempre l'autenticazione.

- **Su GitHub**: i flussi `verify.yml` e `installer.yml` usano il segreto `GECO_PACKAGES_TOKEN` se esiste, altrimenti il token del flusso. Perché basti il token del flusso, nelle impostazioni del pacchetto GeCo.Dati (*Package settings > Manage Actions access*) va aggiunto questo repository con accesso in lettura.
- **Sul proprio PC**: impostare la variabile d'ambiente `GECO_PACKAGES_TOKEN` con un token personale che abbia il permesso `read:packages` (e `GITHUB_ACTOR` con il proprio nome utente GitHub), poi compilare normalmente.

La verifica `SharedDatabaseChecks` (dentro `BusinessChecks`) prova la scrittura su un SQL Server vero solo se è impostata la variabile `TA_TEST_SQL` con una stringa di connessione a un database di prova vuoto; su GitHub è LocalDB.
