# TA-Gestione Cartelle Commesse

Applicazione .NET 10 con client WPF/MVVM e servizio Windows per Windows Server 2022 nel dominio aziendale.

## Scarica e installa - versione 2.3.0

Aggiornare prima il server e poi i client.

- [Scarica installatore Server 2.3.0](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.3.0/TA-Gestione-Cartelle-Commesse-Server-Setup-2.3.0.exe)
- [Scarica installatore Client 2.3.0](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.3.0/TA-Gestione-Cartelle-Commesse-Setup-2.3.0.exe)

Il server verifica commesse e gare ogni minuto, senza sovrapporre scansioni della stessa categoria, e invia notifiche push ai client. Il menu Aiuto > Informazioni mostra la versione client e, quando disponibile, quella server. Resta necessario configurare account, gruppo AD e SPN seguendo la guida.

## Due installatori

- **Client**: `TA-Gestione-Cartelle-Commesse-Setup-<versione>.exe`, per i PC Windows. Aggiorna la precedente applicazione mantenendo la stessa identità di installazione.
- **Server**: `TA-Gestione-Cartelle-Commesse-Server-Setup-<versione>.exe`, da eseguire come amministratore sulla VM. Registra `TACommesseServer`; alla prima installazione resta fermo fino alla configurazione.

[Release pubblicate](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases). I setup includono il runtime e non sono firmati digitalmente; le impronte sono in `SHA256SUMS.txt`.

**[Guida di installazione e configurazione server-client](deployment/SERVER-CLIENT.md)**

## Area di notifica e aggiornamenti

Client e gestore server restano attivi nell'area di notifica quando vengono minimizzati o chiusi. Dall'icona si puo riaprire la finestra o scegliere Esci; uscire dal gestore non arresta il servizio Windows. Le notifiche Windows segnalano modifiche effettive, senza ripetersi per scansioni identiche; la loro visualizzazione dipende dalle impostazioni di notifica di Windows.

Gli aggiornamenti vengono controllati solo all'avvio del client o del gestore, oppure con Verifica aggiornamenti. Nessuna attivita pianificata, controllo periodico o controllo in chiusura. I pacchetti vengono scaricati dalla release pubblica di questo repository e verificati con dimensione e digest SHA-256 di GitHub; l'interfaccia viene chiusa senza forzarla e riaperta dopo l'installazione. Il server si aggiorna soltanto quando viene aperto il gestore con privilegi amministrativi. Nessun riavvio automatico di Windows. Non sono supportati aggiornamenti automatici tra versioni principali diverse.

## Comunicazione dedicata

Nessun HTTP, HTTPS, IIS o sito web. Il client usa un protocollo di messaggi dedicato su **TCP cifrato e firmato tramite Windows NegotiateStream**, porta predefinita **5443**, autenticazione mutua Windows/Kerberos e gruppo di dominio autorizzato. Non occorrono certificati TLS. Il nome DNS deve corrispondere allo SPN registrato per il servizio. Il client consente account Windows corrente oppure credenziali di dominio esplicite, mantenute solo in memoria. Non vengono salvate password.

Il client visualizza gli elenchi e mantiene una sottoscrizione push TCP autenticata: recupera gli stati quando il server segnala un aggiornamento, senza interrogazione ogni dieci secondi. Il server coordina le verifiche per tutti i PC: una sola scansione per categoria. Le verifiche partono all'avvio, ogni minuto e su richiesta. L'annullamento richiesto da un client riguarda la scansione condivisa della categoria. Report e gerarchie provengono dal server; solo Esplora file apre direttamente il percorso UNC con i permessi dell'utente.

## Cartelle, metadati e preventivi

- Commesse: `\\ingtassinari.it\data\lavori`, con identificazione solo nelle cartelle immediatamente sotto gli anni 1900–2099.
- Gare: `\\ingtassinari.it\data\gare`, separate dalle commesse.
- JSON condivisi: `\\ingtassinari.it\data\amministrazione\_GESTIONE COMMESSE`, gestiti esclusivamente dal server.
- Dimensione logica totale, numero file, ultima modifica dei file e istante della verifica. I livelli inferiori contribuiscono ai metadati e sono esplorabili, ma non vengono identificati come nuove commesse.
- Controllo dei nomi nelle cartelle `amministraione`, `amministrazione` e `99_AMMINISTRAZIONE`: candidati preventivo/offerta, con richieste, modelli e bozze segnalati come ambigui. Non verifica contenuti, approvazioni o firme e non usa IA esterna.

I dati salvati sono caricati prima delle verifiche. Gli accessi falliti sono segnalati come parziali e non come preventivo assente. Le scritture JSON usano file temporanei, sostituzione e lock; gli archivi corrotti non sono sovrascritti. Se il server è irraggiungibile, il client conserva i dati già caricati in memoria, con un avviso.

## Struttura della soluzione

| Progetto | Responsabilità |
|---|---|
| TA-Gestione Cartelle Commesse | Client WPF, comandi MVVM e configurazione della connessione |
| TA.Contracts | Modelli e contratti condivisi |
| TA.Core | Lettura cartelle, preventivi, JSON e report con risorse incorporate |
| TA.Transport | Protocollo, framing, connessione TCP/Kerberos e autenticazione del client |
| TA.Server | Servizio Windows, autorizzazione, pianificazione e comandi del server |

## Compilazione e verifica

Aprire `TA-Gestione Cartelle Commesse.slnx` in Visual Studio con .NET 10 e supporto WPF.

```powershell
dotnet build 'TA-Gestione Cartelle Commesse.slnx' -c Release
dotnet run --project tests/ArchiveChecks/ArchiveChecks.csproj -c Release
dotnet run --project tests/TransportChecks/TransportChecks.csproj -c Release
dotnet run --project tests/PushChecks/PushChecks.csproj -c Release
```

I test usano dati sintetici e un listener di prova su loopback. Verificano metadati, archivio, limiti delle scansioni, framing, rifiuto delle richieste HTTP, percorsi fuori radice e server non autenticati. Il collaudo reale Kerberos, avvio del servizio e permessi sulla rete deve essere eseguito sulla VM configurata.

Il workflow Installer Windows costruisce entrambi i pacchetti, verifica il client in aggiornamento dalla 1.0.2 e pubblica setup e impronte. La versione del workflow è `2.2.<numero esecuzione>`. I dati di rete e le credenziali non sono inclusi nei pacchetti.
