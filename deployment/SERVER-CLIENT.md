## Versione 2.9.0: apertura su Z e archivi sotto lavori

Il doppio clic e i comandi Apri del client traducono la condivisione aziendale nel percorso Z del PC. La conversione supporta sia il nome DNS completo sia quello breve, senza cambiare condivisioni diverse. Il server mantiene i percorsi UNC originali. Gli archivi vengono selezionati in Esplora file senza estrazione.

La scheda Archivio include adesso ZIP e 7z presenti ricorsivamente nella radice lavori, oltre alla radice archivio. Ogni file e censito una sola volta anche con radici sovrapposte. Le sottocartelle non diventano nuove commesse. L'indice viene aggiornato dal monitor e dal controllo ogni minuto; le letture dei contenuti, le dimensioni e le gerarchie restano limitate al report delle 23 italiane. Tutte le sorgenti restano in sola lettura. Una radice non accessibile viene segnalata e non cancella le righe precedenti non verificabili.

Aggiornare prima Server e poi Client tramite l'aggiornatore diretto.

## Versione 2.8.0: elenco continuo, report alle 23

Il monitoraggio continuo riguarda la creazione, eliminazione e rinomina delle cartelle censite (e dei file ZIP/7z per l'Archivio). Il controllo di recupero ogni minuto aggiorna soltanto l'elenco. Le modifiche ai file non avviano piu analisi ricorsive; anche il comando Aggiorna aggiorna soltanto l'elenco. Nessuna commessa viene identificata nelle sottocartelle di una commessa.

Dimensioni, date dei file, preventivi e gerarchie vengono elaborati una volta al giorno a partire dalle 23:00, ora italiana, con gestione dell'ora legale indipendente dal fuso della VM. Il calendario e interno al servizio: nessuna attivita pianificata Windows e nessuna dipendenza dall'apertura del client. Le letture possono durare oltre la mezzanotte e non si sovrappongono. Se il servizio riparte tra le 23 e mezzanotte e manca il report del giorno, ritenta; un avvio prima delle 23 attende le 23, senza recuperare i giorni in cui era spento.

Il server conserva i report HTML datati nella sottocartella report-giornalieri dell'archivio JSON configurato. Export > Scarica ultimo report giornaliero (23:00) scarica dal server l'ultimo report completato. I report precedenti restano nella cartella condivisa; non vengono cancellati automaticamente. La consultazione della gerarchia usa esclusivamente l'ultima lettura salvata, anche dopo un riavvio. Il report indica avvio, fine, ultima verifica e dati precedenti/non verificati in caso di errori o accessi parziali. Non e una fotografia atomica del filesystem.

Fino alla prima elaborazione, una nuova cartella mostra In attesa del report delle 23:00. Per le altre restano visibili i dati dell'ultima elaborazione, con la data di verifica. La sorgente viene sempre letta senza modifiche. Questa modalita sostituisce la verifica continua e la coda prioritaria delle versioni precedenti.

Aggiornare prima Server e poi Client tramite il programma di aggiornamento diretto. Restano invariati autenticazione Windows, TCP dedicato, backup e aggiornamenti all'apertura senza attivita pianificate.

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

## Versione 2.4.0 - creazione da template

Installare prima il server e poi il client. Con la 2.3.0 il controllo automatico viene eseguito all'apertura delle rispettive interfacce; in alternativa usare il setup manuale.

Dal client scegliere Nuovo > Commessa da modello, inserire codice CXXYYY, cliente, luogo facoltativo e argomento, quindi Anteprima. La destinazione e JobsRoot\20XX\CXXYYY - Cliente - Luogo - Argomento (il segmento luogo viene omesso se vuoto). Crea commessa esegue l'operazione sul server. Il codice e scelto dall'utente: non viene assegnato automaticamente. La cartella annuale deve gia esistere.

JobTemplate e preimpostato su `\\ingtassinari.it\data\lavori\2026\C26198 - CLIENTE - LUOGO - COSA`; puo essere cambiato nella configurazione server. Il modello contiene attualmente 14 sottocartelle, comprese quelle annidate. Vengono copiate solo le directory e non i file (neppure il foglio Excel). I nomi delle sottocartelle restano quelli del modello, senza sostituire segnaposto. Per le gare manca un modello: la funzione e disponibile soltanto per le commesse.

Permessi aggiuntivi per svc_tacommesse: lettura/elenco del modello, creazione di cartelle nella cartella annuale di destinazione e modifica/rinomina/eliminazione delle cartelle nuove create dal servizio. Non serve concedere modifica indiscriminata alle commesse preesistenti. In NTFS si puo predisporre Creazione cartelle sulla sola cartella annuale e una regola CREATOR OWNER di Modifica ereditata sulle nuove sottocartelle; l'amministratore deve verificare anche i permessi della condivisione. La funzione usa l'account del servizio, non impersona l'utente del client. Tutti i membri del gruppo applicativo autorizzato possono richiedere la creazione. Nessuna ACL viene cambiata dal programma.

L'anteprima non scrive. La creazione ricontrolla il modello e i duplicati, costruisce una struttura temporanea .TA-new-... e la rinomina alla destinazione. Non sovrascrive cartelle esistenti; un lock nell'archivio condiviso serializza le richieste delle istanze applicative. La creazione manuale esterna simultanea di un codice con nome diverso non puo essere impedita dal programma. In caso di errore vengono rimosse solo cartelle temporanee vuote; eventuali residui sono segnalati e non vengono eliminati file altrui. In caso di perdita della risposta, controllare la destinazione prima di riprovare. La nuova commessa viene segnalata ai client e compare al termine della scansione.

Test eseguiti su cartelle sintetiche: anteprima senza scrittura, struttura annidata senza file, modello preservato, duplicati, richieste concorrenti, input non validi, anno inesistente e modello cambiato tra anteprima e creazione. Nessuna commessa reale e stata creata durante le verifiche. Restano da verificare i permessi effettivi dell'account del servizio sulla VM.

## Versione 2.5.0 - dettaglio delle modifiche

Il server include nel messaggio push un riepilogo delle differenze rilevate. Alla ricezione il client recupera lo stato completo, aggiorna l'elenco mantenendo filtro e selezione quando possibile, quindi mostra una notifica con cartella e tipo di cambiamento. Le ultime 200 differenze per categoria sono consultabili da Visualizza > Modifiche ricevute, nella sessione corrente. L'elenco rimane aggiornato anche se Windows sopprime gli avvisi.

Sono distinti aggiunte, rimozioni dall'elenco, variazioni di numero file, dimensione, ultima modifica, preventivi, conformita e anomalie. Una firma deterministica dei nomi delle sottocartelle e dei metadati dei file rileva anche cartelle vuote e rinomine a parita di dimensione. Non vengono letti i contenuti dei documenti; modifiche che preservano tutti i metadati possono non essere riconosciute. Un cambio percorso compare come rimozione e aggiunta, non come rinomina certa.

La prima fotografia inizializza la vista senza generare avvisi per tutte le cartelle. Le scansioni identiche non notificano. Dopo una disconnessione vengono confrontati lo stato conservato e quello recuperato; eventi transitori interamente compresi tra due scansioni non costituiscono uno storico recuperabile. I dettagli indicano la commessa/gara interessata e i campi modificati, non un elenco completo di ogni file modificato. Aggiornare prima server e poi client alla 2.5.0.

## Versione 2.6.0: elenchi, comunicazioni e archivi annuali

Il server verifica l'elenco di tutte le commesse e gare ogni minuto, indipendentemente dalla lettura ricorsiva dei file. Nessuna differenza di frequenza fra gli anni. Le scansioni dei metadati non si sovrappongono: se una lettura dura oltre un minuto, prosegue fino al termine. I risultati vengono pubblicati progressivamente; una cartella nuova compare prima della verifica delle sue dimensioni.

Il JSON generale diventa un indice (schema 2) e i dati si trovano nella directory omonima con suffisso .years, in file con anno e impronta del contenuto. Gli anni invariati riusano il file. La prima migrazione conserva il JSON precedente come .legacy-v1.bak. Non ripristinare un server precedente dopo la migrazione senza recuperare il backup. Indice e file annuali vanno conservati insieme; un file mancante o alterato viene segnalato senza sovrascrivere l'archivio. Per le commesse l'anno e quello del contenitore; per le gare e il codice a due cifre interpretato come 20XX. I dati privi di anno vengono salvati in senza-anno. Il salvataggio usa una coda seriale; gli errori di rete sono mostrati nello stato e nei log.

Ogni nuova notifica push richiede un riscontro sullo stesso canale Windows autenticato. Il client lo invia dopo aver applicato entrambe le viste. Un heartbeat verifica il collegamento durante l'inattivita; un riscontro mancante entro 90 secondi chiude la sessione e viene registrato. I client precedenti restano compatibili, ma non producono questi riscontri. Aiuto > Verifica comunicazione controlla comando e risposta con un identificativo univoco. Gli errori di rete temporanei attivano la riconnessione; gli errori di autenticazione richiedono intervento.

Il gestore mostra due controlli WPF separati: debug del servizio e comunicazioni dei client. I registri sono service.log e communications.jsonl nella directory Logs di ProgramData/Tassinari/CommesseServer. Le comunicazioni identificano PC dichiarato, indirizzo remoto, utente Windows autenticato, azione, esito e identificativo della prova. Il nome PC e un dato dichiarato dal client, non una credenziale. I registri ruotano e la tabella mostra al massimo 500 righe recenti.

Il client scrive un log operativo nella destinazione di rete aziendale configurata nell'applicazione; se la rete fallisce, usa LocalAppData/Tassinari/CommesseClient/Logs e ritenta la rete dopo un minuto. Nessuna password viene registrata. I log sono separati per PC/processo/giorno e ruotano a 10 MiB; la scrittura non blocca l'interfaccia e la coda conserva fino a 1000 messaggi.

Server > Gestione servizio permette stato, avvio, arresto, riavvio e consultazione/esportazione dei registri solo con il token Windows di un amministratore di dominio. Se il token e filtrato da UAC, avviare il client come amministratore. I registri amministrativi sono autorizzati nuovamente dal server contro il gruppo Domain Admins (SID del dominio dell'account di servizio, suffisso 512). La gestione usa l'account Windows corrente, non credenziali alternative inserite nel client.

Avvio/arresto/riavvio usano il gestore servizi remoto di Windows (RPC/SCM), necessario anche quando il servizio applicativo e fermo. La VM deve permettere la gestione remota dei servizi e i relativi permessi Windows. Nessuna regola firewall aggiuntiva viene aperta automaticamente. I log e la verifica applicativa usano invece la porta TCP dedicata. Non sono disponibili modifica remota di account/configurazione o elevazione automatica dei privilegi. Non vengono create attivita pianificate.

Verifiche locali: cartella C26198 con metadati bloccati, aggiornamento griglie WPF prima dell'avviso, salvataggio durante scansione, migrazione annuale, integrita dei JSON, riscontri corretti/errati e diniego dei comandi amministrativi agli utenti ordinari. La gestione SCM con account amministrativo richiede collaudo sulla VM.

## Versione 2.7.0: aggiornamento diretto e archivio compresso

### Aggiornatore

TA-Commesse-Aggiornatore-2.7.0.exe e un programma autonomo: scarica da GitHub il pacchetto ZIP del componente, verifica l'impronta pubblicata da GitHub e le impronte di ogni file, chiude l'interfaccia e sostituisce soltanto i file diversi. Non esegue un installatore. Configurazioni, dati, registrazione e account del servizio restano invariati. L'aggiornamento server richiede privilegi amministrativi e arresta/riavvia il servizio solo se prima era attivo; un servizio gia fermo resta fermo.

Per passare dalla 2.6.0 senza usare un installatore, scaricare una volta l'aggiornatore autonomo, eseguirlo sulla VM scegliendo Server e poi sui PC scegliendo Client. Dalla nuova versione il controllo automatico resta limitato all'apertura del client o del gestore, oltre al comando manuale. Nessuna attivita pianificata. I file Installazione-Iniziale servono solo per nuove installazioni; non sono il canale di aggiornamento.

Il programma conserva un backup prima della sostituzione e un giornale di ripristino. Se la verifica fallisce ripristina i file precedenti; se il processo si interrompe, riaprire l'aggiornatore per il recupero. Una nuova versione del server deve rimanere in stato Running per cinque secondi; questo non certifica l'accesso a tutte le condivisioni. In caso di file occupati non vengono terminati forzatamente processi. Backup, download e log rimangono in UpdateCache sul server e in LocalAppData/Tassinari/CommesseClient/Updates sul client: conservarli finche non e stata verificata la versione nuova.

I pacchetti sono pubblicati dal repository aziendale tramite HTTPS e non firmati digitalmente. Non vengono applicati cambi di versione principale in automatico. L'aggiornatore non modifica i file di configurazione e non rimuove file sconosciuti o obsoleti lasciati da versioni precedenti.

### Archivio e dimensioni

La terza scheda Archivio censisce ricorsivamente i file ZIP e 7z della radice ArchivedJobsRoot. Il servizio apre i file esclusivamente in lettura e legge intestazioni, nomi, date, dimensioni e candidati preventivi. Non estrae o ripara alcun contenuto e non crea file nella radice sorgente. Archivi danneggiati o protetti vengono segnalati come letture parziali; questo controllo dei metadati non verifica il CRC di tutto il contenuto. I file con nome non conforme rimangono visibili. Nessuna commessa viene censita dentro un file compresso.

Il servizio richiede soltanto Lettura/Elenco sulla radice degli archivi. I risultati vengono salvati nell'archivio JSON applicativo separato, per categoria A e anno. La verifica avviene ogni minuto, senza sovrapposizioni delle scansioni lunghe. Se alcuni contenitori non sono accessibili, vengono mostrati i risultati disponibili e conservate le precedenti righe non verificabili, con segnalazioni. Nella scheda Archivio la dimensione mostra lo spazio del file compresso, esplicitamente etichettato. Il contenuto delle cartelle interne e consultabile in sola lettura dalla gerarchia; Apri seleziona il file in Esplora file senza estrarlo.

Le colonne iniziano con Codice, Anno, Numero, Committente, Argomento, Dimensione, seguite dagli esiti. Le nuove commesse hanno una coda prioritaria per dimensioni e metadati: la riga compare prima e riceve un secondo aggiornamento quando il calcolo termina. Il valore iniziale e In verifica; le dimensioni piccole sono espresse in byte/KiB e non arrotondate a zero MiB.
