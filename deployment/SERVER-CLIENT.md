# Server e client 2.1 — account Active Directory, senza certificati

## Account e gruppo da creare

Creare l'utente di dominio **svc_tacommesse**, con UPN suggerito **svc_tacommesse@ingtassinari.it**. Il prefisso NetBIOS usato negli esempi è `INGTASSINARI`: verificarlo in Active Directory prima di copiare i comandi. L'account serve esclusivamente a eseguire il servizio Windows **TACommesseServer**.

Creare il gruppo di sicurezza AD **GG_TA_Commesse_Users** e inserirvi gli utenti abilitati a usare il client. L'account di servizio non deve necessariamente appartenere a questo gruppo. Gli utenti del gruppo possono consultare i metadati di entrambe le radici, creare report e richiedere o annullare scansioni condivise.

| Destinazione | Permesso dell'account svc_tacommesse |
|---|---|
| VM Windows Server 2022 | Diritto **Accedi come servizio** (Log on as a service) |
| Cartella di installazione server | Lettura ed esecuzione; nessuna scrittura sui binari |
| `%ProgramData%\Tassinari\CommesseServer` | Lettura delle impostazioni |
| `\\ingtassinari.it\data\lavori` e relativi figli | Lettura, elenco, lettura attributi e attraversamento cartelle |
| `\\ingtassinari.it\data\gare` e relativi figli | Lettura, elenco, lettura attributi e attraversamento cartelle |
| `\\ingtassinari.it\data\amministrazione\_GESTIONE COMMESSE` | **Modifica**, inclusi creazione, rinomina e cancellazione dei file JSON temporanei |

Applicare correttamente sia i permessi della condivisione SMB sia quelli NTFS. La scrittura deve essere limitata tramite NTFS alla sola cartella `_GESTIONE COMMESSE`, senza concedere modifica a tutta la condivisione `data`, a lavori, gare o agli altri documenti amministrativi. Concedere l'attraversamento dei percorsi superiori quando necessario.

L'account non deve essere amministratore locale o di dominio. Non occorre attivare delega Kerberos: il server legge i file con la propria identità e non impersona i client. È possibile negare l'accesso interattivo locale e RDP a questo account; non negare «Accedi come servizio» né l'accesso di rete richiesto dalle condivisioni.

Impostare una password robusta senza «Cambiamento obbligatorio al prossimo accesso». Gestire la rotazione secondo la politica aziendale e aggiornare anche le credenziali del servizio. Non inserire la password nei file JSON, negli script, in riga di comando o in questa chat. Per evitare la gestione manuale della password, un amministratore AD può in alternativa predisporre un gMSA: non è indispensabile per questa versione.

## Configurazione del server

1. Collegare la VM al dominio e assegnarle un nome DNS completo stabile, per esempio `commesse-srv.ingtassinari.it` (esempio, non un nome già configurato).
2. Installare `TA-Gestione-Cartelle-Commesse-Server-Setup.exe` **2.1.0** come amministratore. Non richiede IIS o certificati. Il runtime .NET è incluso. Alla prima installazione registra un servizio fermo con identità iniziale NetworkService.
3. Aprire `services.msc`, proprietà di **TA Gestione Commesse Server**, scheda **Connessione**. Selezionare «Account» e inserire `INGTASSINARI\svc_tacommesse` con la sua password. Windows conserva le credenziali del servizio tramite i propri meccanismi protetti, non nell'applicazione. Verificare che il diritto «Accedi come servizio» sia concesso anche dalle GPO applicate alla VM.
4. Un amministratore AD deve registrare l'SPN sullo stesso account che esegue il servizio. Sostituire il nome DNS nell'esempio:

```powershell
setspn -S TACommesse/commesse-srv.ingtassinari.it INGTASSINARI\svc_tacommesse
setspn -Q TACommesse/commesse-srv.ingtassinari.it
```

L'SPN deve essere univoco. Se era stato registrato sull'account macchina per una versione precedente, l'amministratore deve trasferirlo all'account di servizio, evitando duplicati. Non aggiungere alias DNS senza registrare il corrispondente SPN. Usare nel client lo stesso nome DNS completo, non un indirizzo IP.

5. Dalla cartella del server, in PowerShell amministrativa:

```powershell
.\Configure-Server.ps1 -AllowedGroup 'INGTASSINARI\GG_TA_Commesse_Users' -Port 5443 -OpenDomainFirewall
```

Lo script salva le impostazioni in `%ProgramData%\Tassinari\CommesseServer\server-settings.json`. Il parametro firewall apre soltanto TCP 5443 per l'eseguibile del server, profilo Dominio e sottorete locale. Per PC su altre sottoreti, far adattare l'ambito dal responsabile della rete. Lo script non crea utenti, non registra SPN e non modifica i permessi sulle condivisioni.

6. Avviare il servizio con `Start-Service TACommesseServer`. Controllare il registro Applicazione in Visualizzatore eventi se non parte. Il servizio carica l'archivio e verifica i dati all'avvio, ogni 60 minuti e su richiesta del client. `RefreshMinutes=0` disabilita solo la verifica periodica. Riavviare il servizio dopo ogni modifica delle impostazioni.

Le radici sono `\\ingtassinari.it\data\lavori` e `\\ingtassinari.it\data\gare`; i JSON sono in `\\ingtassinari.it\data\amministrazione\_GESTIONE COMMESSE`. Le scansioni non modificano i documenti. Gli aggiornamenti conservano account del servizio, impostazioni e dati. La disinstallazione conserva impostazioni e JSON; la regola firewall eventualmente configurata può essere rimossa dall'amministratore.

## Client

Installare anche il client **2.1.0**: la versione 2.0 con TLS non è compatibile con questo trasporto. Inserire nome DNS completo e porta 5443, senza prefissi HTTP/HTTPS.

- **Usa il mio account Windows**: utilizza l'identità della sessione corrente.
- Deselezionando l'opzione, **Connetti** apre una finestra con utente `DOMINIO\utente` oppure UPN `utente@ingtassinari.it` e password. Le credenziali restano solo nella memoria della sessione e non sono scritte su disco o nei log. La modalità esplicita non viene ripristinata automaticamente al successivo avvio.

L'utente deve appartenere a `GG_TA_Commesse_Users`. Dopo aver modificato i gruppi potrebbe essere necessario rinnovare la sessione Windows. I PC devono poter raggiungere DNS e controller di dominio ed avere l'orario sincronizzato. Dopo un errore il polling viene fermato: correggere il problema e premere **Connetti**, evitando tentativi ripetuti con password errate.

L'apertura in Esplora file usa sempre la sessione Windows del PC, anche se il client applicativo è collegato con credenziali diverse. I permessi personali sui documenti non vengono modificati dal login nell'applicazione.

## Trasporto e collaudo

Il protocollo dedicato passa direttamente su TCP protetto da `NegotiateStream` con **EncryptAndSign**. Client e server verificano cifratura, firma e autenticazione reciproca prima di scambiare dati applicativi. Non c'è un canale in chiaro di ripiego; un'autenticazione non reciproca viene rifiutata. Non vengono usati TLS, HTTP, IIS, pagine web o certificati. Le password non sono inviate come campi JSON del protocollo.

Prima dell'uso aziendale provare sulla VM: accesso con account corrente, accesso con credenziali esplicite valide, rifiuto di un account fuori gruppo, riavvio del servizio, accesso alle condivisioni, report e aggiornamento di entrambi i pacchetti. I test locali non possono certificare Kerberos senza gli account e gli SPN effettivi.

Riferimenti Microsoft: [NegotiateStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.negotiatestream?view=net-10.0) e [setspn](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/setspn).

## Utilita di gestione e diagnostica (server 2.1.1)

Nel menu Start aprire **TA Commesse Server > Gestione servizio e diagnostica**. Richiede privilegi amministrativi locali per gestire il servizio. Il client 2.1.0 resta compatibile.

- **Avvia / Arresta**: esegue il comando senza bloccare la finestra; lo stato viene aggiornato ogni secondo. Arrestare interrompe scansioni e connessioni.
- **Configura**: apre la procedura guidata PowerShell; indicare il gruppo AD. Configura anche la regola firewall di dominio prevista. Non modifica l'account AD del servizio.
- **Account del servizio**: apre Servizi di Windows; selezionare TACommesseServer e impostare l'account nella scheda Accesso.
- **Diagnosi**: legge configurazione e ultimi eventi pertinenti di Windows. I controlli vengono eseguiti con l'account amministratore dell'utilita, non con le credenziali del servizio.
- **Esporta registro**: salva messaggi e diagnosi in un file di testo da condividere. Prima di esportare premere Diagnosi per includere gli eventi Windows.

Durante l'avvio si vedono lettura configurazione, configurazione valida, avvio host e **TCP pronto**. Lo stato Windows In esecuzione precede talvolta la disponibilita effettiva della porta: verificare il messaggio TCP pronto. Il registro contiene data, ora e PID per distinguere tentativi precedenti. La diagnostica non conferma da sola l'accesso dei client tramite AD.

I log sono in `%ProgramData%\Tassinari\CommesseServer\Logs\service.log`, con un file precedente e rotazione a circa 2 MB. L'installatore concede scrittura solo al SID del servizio, SYSTEM e amministratori. Il servizio registra anche gli errori precedenti all'avvio dell'host. Se Windows non riesce neppure a eseguire il programma, gli eventi Windows restano la fonte diagnostica.

Per il 1053: aggiornare il server, aprire l'utilita, premere Avvia e poi Diagnosi, infine Esporta registro. Non cambiare timeout di sistema senza aver identificato la causa. Il pacchetto non costituisce una conferma che la causa del 1053 sulla VM sia risolta.

## Server 2.1.2

La scansione continua quando un file contiene una data Win32 non rappresentabile: registra il percorso, conserva dimensione, conteggio e riconoscimento dei preventivi e segnala i metadati parziali. Non cambia le date dei file originali.

I rifiuti di connessione distinguono protezione Kerberos insufficiente, identita non Windows, gruppo non risolvibile e utente non appartenente al gruppo. Per i rifiuti di gruppo il registro indica identita, gruppo e SID; non contiene password. Il servizio registra il proprio account effettivo e la versione all'avvio. L'esportazione della diagnostica aggiorna prima i controlli, evitando di mostrare soltanto una vecchia configurazione mancante.

Aggiornare il solo server e la sua utilita con Setup-2.1.2. Il client 2.1.0 rimane compatibile. Riprovare la connessione e inviare un nuovo registro per identificare il controllo AD che rifiuta l'accesso: l'aggiornamento non modifica account, SPN o appartenenze ai gruppi del dominio.

## Notifiche push - versione 2.2.0

Aggiornare prima il server e poi i client. La sottoscrizione usa una connessione TCP persistente sulla stessa porta 5443, con autenticazione mutua Windows e controllo del gruppo. Nessuna nuova porta, HTTP o certificato TLS. I client precedenti continuano a funzionare con interrogazioni periodiche; il client 2.2 richiede il server 2.2 per le notifiche.

Il server monitora creazioni, modifiche, rinomine ed eliminazioni nelle radici lavori e gare. Per lavori considera soltanto contenitori annuali 1900-2099; le sottocartelle contribuiscono ai metadati ma non diventano nuove commesse. Gli eventi sono raggruppati dopo circa 3 secondi di quiete (massimo 30 secondi di accumulo, controllo ogni 2 secondi). Questa prima versione ricalcola la categoria interessata, non solo il singolo file: la disponibilita dei dati dipende dalla durata della scansione. Eventi durante una scansione richiedono una verifica successiva.

Il client viene notificato all'avvio e al completamento della scansione; recupera entrambe le categorie e conserva filtro e selezione quando possibile. La barra di stato mostra quando sono arrivati gli aggiornamenti. Le code conservano solo l'ultima revisione, evitando accumuli sui client lenti. Non e uno storico delle singole operazioni sui file.

Una notifica di presenza viene inviata ogni 20 secondi. In caso di interruzione dopo una sottoscrizione riuscita il client ritenta con attese da 5 a 60 secondi e recupera lo stato corrente. Rifiuti di autenticazione/autorizzazione fermano i tentativi e richiedono Connetti. La sottoscrizione viene rinnovata ogni 5 minuti per ripetere i controlli Windows. Sono ammesse fino a 64 sottoscrizioni contemporanee, separate dai 16 posti per autenticazioni e comandi brevi.

Il monitor sulle condivisioni SMB puo perdere eventi o interrompersi. In caso di errore il server programma una scansione e ritenta il monitor dopo 30 secondi. Resta la scansione periodica configurata, normalmente ogni 60 minuti; un valore non positivo usa comunque 60 minuti come recupero. Questi tempi non garantiscono l'immediatezza se la rete non risponde o la scansione e lunga.

Collaudo locale: eventi reali su cartelle sintetiche, notifiche su TCP locale, recupero dello stato, code limitate, raggruppamento eventi, eliminazione e identificazione al solo livello previsto. Rimangono da collaudare sulla VM autenticazione AD e notifiche della condivisione aziendale. L'installatore predispone anche la sorgente TA.Server nel registro eventi con privilegi amministrativi, senza elevare l'account del servizio.

## Versione 2.2.1 - frequenza richiesta e informazioni

Questa versione sostituisce le precedenti indicazioni di frequenza: il server richiede una verifica di entrambe le categorie ogni minuto, anche in presenza di un vecchio RefreshMinutes nel file di configurazione. Una scansione ancora attiva non viene duplicata; il nuovo tentativo avviene al minuto successivo. Non e garantito il completamento in un minuto su cartelle grandi. Le notifiche push all'avvio e al completamento aggiornano gli elenchi del client. Rimane il monitor degli eventi, con avviso anche quando una modifica deve attendere la scansione corrente.

Aiuto > Informazioni mostra versione client, versione server disponibile e connessione. Installare prima Server 2.2.1, poi Client 2.2.1. Il problema di appartenenza al gruppo AD va risolto indipendentemente dall'aggiornamento; i pacchetti non modificano il dominio.

## Versione 2.3.0 - area di notifica e aggiornamento all'apertura

Non vengono create attivita pianificate. Il controllo GitHub parte solo all'avvio di client o gestore server o dal comando manuale Aggiornamenti. Il servizio, da solo, non verifica ne installa aggiornamenti. Per ricevere questa funzionalita occorre installare manualmente la 2.3.0 una volta.

I download richiedono accesso HTTPS a GitHub e ai suoi domini di distribuzione. La connessione applicativa resta TCP/AD. Il repository e pubblico; non servono token GitHub sui PC. Sono accettati solo pacchetti del repository ufficiale con tag stabile, versione superiore della stessa versione principale, nome previsto, dimensione e digest SHA-256 corrispondenti ai metadati GitHub. Questo controllo non equivale a una firma Authenticode: chi ha permessi di pubblicazione sul repository e parte della catena di fiducia.

Quando esiste un aggiornamento, il programma lo scarica e verifica, richiede la chiusura ordinata dell'interfaccia, esegue il setup e la riapre. Non forza la terminazione di altre istanze e non riavvia Windows. Se il setup fallisce, registra l'errore e tenta di riaprire l'interfaccia esistente: non e previsto un rollback automatico dei binari. Configurazioni e archivio restano preservati dall'installatore. Una copia non ancora configurata o con problemi AD resta da configurare anche dopo l'aggiornamento.

Il client usa `%LOCALAPPDATA%\Tassinari\CommesseClient\Updates`; il server usa la cartella `UpdateCache` dell'installazione, protetta dai permessi di Program Files. I log sono update.log e installer.log. Il gestore server richiede gia elevazione: l'aggiornamento eredita quei privilegi e arresta/riavvia il servizio tramite il setup. Aprire il gestore puo quindi interrompere temporaneamente le scansioni se viene installata una nuova versione.

Minimizza e X nascondono client e gestore nell'area di notifica. Doppio clic sull'icona o Apri li ripristina; Esci termina soltanto l'interfaccia. Il client continua a ricevere il push, il gestore a leggere il registro. Le notifiche sono raggruppate per evitare raffiche; Windows puo sopprimerle con Non disturbare. Il client confronta i dati escludendo l'orario di verifica, quindi una scansione identica non genera un avviso.
