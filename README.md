# TA-Gestione Cartelle Commesse — 2.33.0

## Novità 2.33.0 — curriculum dello studio

Nuovo modulo **Gestione > Curriculum dello studio…**: i lavori svolti si inseriscono a mano con committente, luogo, periodo, prestazioni, importi, macro categorie di prestazioni, categorie delle opere, fotografie e parole chiave, e si collegano per codice a commesse, gare e voci d'archivio (dal tasto destro su una riga si aprono i lavori collegati). Tutti gli utenti autorizzati possono aggiornarlo; i dati stanno nell'archivio del gestionale e rientrano nel backup.

**Esporta in Word…** produce schede complete, schede sintetiche oppure l'elenco per gare con i totali per ID opere, con copertina, simbolo a quadranti delle macro categorie e grafica dello studio. Dettagli in [deployment/CURRICULUM.md](deployment/CURRICULUM.md).

[Scarica aggiornatore 2.33.0](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.33.0/TA-Commesse-Aggiornatore-2.33.0.exe) · [Installatori, pacchetto MCP e impronte SHA256](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/tag/v2.33.0).

### Correzioni e ricerca del committente (previste come 2.32.1, pubblicate con la 2.33.0)

Corretta la creazione di una nuova commessa: dopo il salvataggio della configurazione con la 2.32.0 la cartella modello risultava vuota e l'anteprima segnalava un percorso vuoto. Un campo lasciato vuoto in **Configura** ora mantiene il modello predefinito del servizio.

Il pulsante **+ Nuova commessa** è ora nella scheda Commesse e la scheda Gare ha **+ Nuova gara**, con la stessa procedura (anteprima, poi conferma) e il codice successivo già proposto. Il modello delle gare si indica nel Gestore server > Configura; se manca, viene creata solo la cartella principale.

Nella finestra **Nuova commessa** il committente si cerca scrivendo: nome, partita IVA o codice fiscale, senza distinzione di maiuscole e accenti. L'[archiviazione ZIP](deployment/ARCHIVIAZIONE-ZIP.md) non viene più bloccata da `Thumbs.db` e dalla marcatura dei file scaricati. Dopo **Verifica e salva** il Gestore server propone il riavvio del servizio.

Elenco di commesse, gare e archivio: nuova colonna **Preventivo** con colori (verde presente, giallo da verificare, rosso non trovato, grigio non ancora verificato; l'esito deriva dai soli nomi dei file nella cartella amministrazione). Un clic sull'intestazione di una colonna ordina l'elenco, un secondo clic inverte l'ordine. L'elenco iniziale mostra le ultime 50 voci per anno e numero e la ricerca considera anche anno e numero, con più parole insieme (per esempio `2024 114`).

## Novità 2.32.0 — configurazione dell'installazione e analisi automatica locale

Dominio, gruppi autorizzati, gruppo collaboratori e utenti ammessi alla gestione straordinaria non sono più scritti nel programma: si impostano dal **Gestore server > Configura** e restano nel file di configurazione del server ([dettagli](deployment/SERVER-CLIENT.md)). Il client chiede al server se l'account collegato è autorizzato. **Dopo l'aggiornamento del server aprire Configura, indicare gruppo collaboratori e utenti di gestione e riavviare il servizio**: fino ad allora la gestione straordinaria è disattivata.

Nuova [analisi automatica locale](deployment/ANALISI-LOCALE.md) di MUR A.1/D.1 e relazioni geologiche, facoltativa e senza servizi esterni, con limiti all'uso di risorse: fascia oraria, documenti l'ora, una lettura alla volta a priorità ridotta, pausa durante scansioni e letture su richiesta. I risultati sono proposte da verificare e non sostituiscono le analisi salvate su richiesta. Nessun OCR: i documenti acquisiti come immagine restano da leggere a vista o con ChatGPT.

Il client ricorda l'account di dominio alternativo e, a scelta, la password in Gestione credenziali di Windows; una password non più valida viene eliminata al primo rifiuto.

## Novità 2.31.1 — catalogo IPRiPI e archiviazione confermata

Il catalogo client IPRiPI mostra solo MUR A.1/D.1 con IPRiPI identificati e analisi attuale, escludendo candidati, altri moduli, esiti negativi e classificazioni incerte. Il plugin distingue la ricerca dei candidati (`recognizedOnly=false`) dal catalogo e indica quante analisi vi sono effettivamente ammesse. Un tunnel attivo non comporta analisi automatica: i contenuti vengono elaborati da ChatGPT solo durante la richiesta esplicita in chat.

Aggiunta l'[archiviazione ZIP di lavori e gare](deployment/ARCHIVIAZIONE-ZIP.md), separata dalle analisi in sola lettura: preparazione, verifica e successiva eliminazione solo dopo nuova conferma. Solo Antonio/Giuseppe autenticati, nessuna esecuzione su cartelle reali durante i test. Aggiornare client e servizio alla 2.31.1 e avviare manualmente il nuovo pacchetto MCP; il processo espone 21 strumenti, senza comandi ZIP. ZIP e cancellazione sono disponibili esclusivamente nel client, menu Gestione o menu contestuale della commessa/gara; esecuzione sul server e conferma separata.


Client WPF/MVVM .NET 10 e servizio Windows per la gestione delle commesse dello Studio Tassinari. Le sorgenti vengono analizzate **sempre e solo in lettura**: nomi, percorsi e metadati nelle scansioni automatiche; contenuti dei singoli documenti solo su richiesta esplicita tramite MCP, senza modificare gli originali. L’apertura esplicita dal client prepara solo la copia locale del documento selezionato, anche se interno a ZIP/7z.

## Novità 2.30.0: Archivio limitato e approfondimento su richiesta

La radice archivio aziendale viene limitata a `archivio\_lavori`: ZIP/7z direttamente nella radice o nelle sottocartelle immediate, senza attraversare livelli ulteriori. Nessun archivio di lavori viene aggiunto alla scheda Archivio; gli archivi dentro le commesse restano parte dell'inventario delle rispettive commesse. L'inventario non crea unità residue ricorsive per la categoria Archivio. Al riavvio vengono rimossi dal database locale solo gli inventari fuori ambito; gli originali non vengono modificati. Le analisi storiche restano conservate ma fuori dal catalogo attivo se la fonte è esclusa.

Il nuovo strumento MCP `approfondisci_commessa` richiede una richiesta esplicita e l'ID di una commessa censita. Azioni `avvia`, `stato`, `annulla`; criteri separati da punto e virgola (es. preventivo;contratto). Un solo lavoro alla volta, tre minuti e 100.000 voci, risultati paginati fino a 1.000 candidati. Il server salva un riepilogo per commessa nel database locale, senza sostituire l'inventario ordinario. I percorsi restituiti sono pronti per `leggi_documento`: riconoscimento del committente e conferma dei documenti richiedono una lettura esplicita, non vengono dedotti dal solo nome. Il lavoro richiesto può completarsi dopo la risposta iniziale; nessun polling o riavvio automatico. Dopo riavvio del servizio gli ID dei lavori in memoria non sono più validi; il riepilogo rimane nel database. Nessuna API del modello viene chiamata autonomamente.

Il raggiungimento del limite di voci restituisce un inventario parziale con segnalazione, senza interrompere le altre commesse o interpretare le voci non lette come file eliminati.

## Correzione 2.29.1: caricamento catalogo MUR

La lettura paginata usa una fotografia in memoria del catalogo (fino a 10 minuti, massimo 16 ricerche) per evitare errori quando il censimento pubblica nuovi risultati. Data e avanzamento della scansione non invalidano le righe. Una nuova ricerca legge i dati aggiornati. Catalogo e risultati rimangono nel database SQLite del server, non in ChatGPT.

## Novità 2.29.0: interfaccia compatta e analisi parallele

Scheda commessa con Salva e Annulla in alto: Annulla ricarica i dati salvati, comprese le bozze di pianificazione. Ricerca cliente a comparsa per nome o partita IVA; selezione per ID e scheda cliente separata, senza pannello anagrafico permanente.

Interfaccia principale con quattro menu: Gestione, Analisi e report, Sistema e Aiuto. Esportazioni raggruppate sotto Analisi e report; connessione, diagnostica e aggiornamenti sotto Sistema. Numero commessa sempre a tre cifre (es. 005), senza cambiare il valore nel database.

Una finestra paritaria per il dettaglio commessa/gara/archivio e una per i clienti, riutilizzate da tutti i punti di accesso. Cambia il contenuto, non viene aperta una nuova finestra per ogni voce. Controlli MVVM riutilizzabili per dettaglio progetto, editor cliente e selezione cliente. La scelta di un cliente ritorna alla commessa di origine senza dialoghi modali; prima di cambiare scheda occorre salvare eventuali bozze oppure ricaricare i dati salvati.

Client e Gestore server ammettono ciascuno una sola istanza nella stessa sessione Windows. Un secondo avvio ripristina la finestra esistente, anche dall'area di notifica, mantenendo la massimizzazione. Le sessioni Windows diverse restano indipendenti. Nessun file di blocco o log viene scritto: il controllo usa oggetti Windows in memoria e viene rilasciato all'uscita, anche dopo una terminazione imprevista. L'aggiornatore può chiudere e riavviare il programma; il processo separato di copia documenti rimane escluso dal controllo.

L'inventario dei file e i due cataloghi si aggiornano indipendentemente: durante una scansione lunga, geologia e MUR/IPRiPI rileggono in parallelo l'inventario locale disponibile ogni minuto. Il comando MCP `stato_analisi` espone avanzamento, date, candidati, analisi salvate e ancora necessarie, canali occupati e prossimo orario di lettura. Non avvia scansioni o trasmissioni automatiche.

Le cartelle Sismicad identificate dal file `main.scj` e i loro discendenti sono esclusi, anche dentro ZIP/7z. I rami fratelli restano inclusi. Il criterio deriva dal Manuale Concrete Sismicad 12, capitolo 17; non si escludono intere commesse solo perché contengono un progetto Sismicad. Il primo ricensimento applica la nuova esclusione anche agli inventari invariati.

Il tunnel attuale trasmette il testo estratto su richiesta, non il PDF binario: i PDF costituiti da immagini richiedono ancora una componente OCR. Nessuna analisi cloud autonoma o API a pagamento viene attivata.

## Novità 2.28.0: clienti e commesse associate

Selettore clienti riutilizzabile con ricerca dinamica per nome, partita IVA, codice fiscale, indirizzo o email. La scelta dalla finestra clienti aggiorna immediatamente il dettaglio, mantenendo le altre modifiche non ancora salvate. Il filtro non cancella l'associazione selezionata.

Nell'anagrafica clienti, pannello laterale con tutte le commesse associate tramite identificativo, ordinate per anno e numero decrescenti, senza limite di 50. Doppio clic per aprire il dettaglio; l'elenco si aggiorna quando viene salvata una diversa associazione. Gare, archivio e clienti omonimi non vengono inclusi impropriamente.

Installatori e pacchetti diretti Client, Server e MCP aggiornati. Nessuna modifica alle cartelle originali, ad Active Directory o al database. Il tunnel MCP esistente resta compatibile e ad avvio manuale.
## Elenco compatto delle commesse (2.26.1)

All'avvio, elenco personalizzato WPF a una riga per commessa, con codice, anno, numero, cliente, argomento, dimensione, ultima attività e stato. Mostra le ultime 50 commesse per attività nota su file o gestionale; la ricerca include tutte le commesse censite. Le scansioni e le migrazioni automatiche non costituiscono attività dell'utente. Finestra inizialmente massimizzata, colonne cliente e argomento adattabili agli schermi ampi.

Ogni riga usa il proprio modello e risolve il cliente tramite identificativo. Se manca l'associazione, mostra il committente rilevato nella cartella con indicazione «da associare». Doppio clic per aprire Esplora file, menu contestuale e comando per il dettaglio. Anagrafica clienti dedicata dal menu Clienti e durante la modifica della commessa, senza perdere la bozza. Modifiche riservate agli utenti autorizzati dal server.

Aggiornare prima il server e poi i client. Nessuna modifica alle cartelle originali o ad Active Directory.

## Analisi documentale progressiva (2.25.0)

Le analisi tramite MCP procedono dalle commesse più recenti alle più vecchie, per anno e progressivo. La coda geologica restituisce un documento alla volta; la ricerca MUR propone una voce per pagina come impostazione iniziale. Il server ammette due letture documentali concorrenti: una geologica e una MUR/IPRiPI. Ogni canale ammette un solo documento alla volta e almeno 60 secondi tra documenti nuovi richiesti dal plugin. Le pagine successive dello stesso file non ripetono l'attesa; il limite temporale è conservato nel database locale anche dopo un riavvio. Una richiesta anticipata restituisce il tempo residuo, senza accodare lavoro nascosto.

Gli elenchi elaborati sono candidati di supporto: quando vengono letti su richiesta, i riferimenti testuali geologico/geotecnici con nome o codice file esatto vengono confrontati con l'inventario della stessa commessa. I documenti corrispondenti diventano candidati, senza aprirli automaticamente. Riferimenti ambigui, scansioni senza testo e codici non corrispondenti richiedono verifica; non costituiscono un riconoscimento certo.

ChatGPT interpreta e salva i risultati durante la consultazione richiesta dall'utente. Nessuna analisi cloud continua, rinnovo automatico della consultazione o chiamata API a pagamento dal server. Terminare e salvare un documento prima di passare al successivo. Aggiornare il pacchetto MCP e avviare manualmente il tunnel per usare le istruzioni aggiornate.

## Commesse, clienti, fasi e attività (2.24.0)

La gerarchia gestionale è **Commessa → Fase → Attività**, con un solo livello di fasi. Dal gestionale, «Apri commessa» o il doppio clic apre il dettaglio con cliente associato, anno, numero, argomento, luogo e stato descrittivo. Le fasi e tutte le attività della commessa sono consultabili anche fuori dal periodo del Gantt. Le fasi testuali precedenti vengono recuperate automaticamente; le ore registrate restano conservate. Modelli, ViewModel e viste sono separati per entità.

I dati vengono salvati dal server nel database gestionale locale. Lo stato aperta/chiusa/fatturata non modifica le autorizzazioni ore. Le cartelle originali non vengono rinominate né modificate.

## Gestione attività e archiviazione locale

Clienti importabili da Excel Fatture in Cloud o CSV con anteprima, commesse con autorizzazione ore, attività e Gantt WPF per commessa/collaboratore, consuntivi personali. Collaboratori letti dal gruppo Active Directory `GG_TA_Commesse_Users`, anche attraverso sottogruppi; account disabilitati esclusi, elenco aggiornato entro un minuto. Gestione del server e autorizzazioni riservate ad Antonio e Giuseppe. [Funzioni, formati e limiti](deployment/GESTIONALE.md).

La nuova modalità locale recupera separatamente analisi e coordinate e ricostruisce l'inventario senza importare i vecchi inventari. I backup giornalieri funzionano anche quando una scansione è incompleta. Nessuna eliminazione automatica dei dati precedenti. [Attivazione, percorsi e recupero](deployment/ARCHIVIAZIONE-LOCALE.md).

## SQLite, scansioni incrementali e backup

Il server conserva l'inventario corrente e i cataloghi in SQLite sul disco locale della VM. Non vengono memorizzati i documenti originali: solo metadati, dati delle classi e risultati delle analisi richieste. Non vengono più generate pagine JSON e generazioni per ogni scansione.

- Prima indicizzazione completa; successivamente verifica dei metadati all'avvio e alle **00:00 italiane**, approfondendo le unità modificate. ZIP/7z invariati non vengono riaperti dall'inventario.
- Due letture concorrenti per impostazione predefinita, limite configurabile da 1 a 4; scritture SQLite coordinate e transazionali.
- Ricerche dei file paginate nel database, senza caricare tutto l'inventario nel client.
- Modalità precedente compatibile; per abbandonare il vecchio inventario utilizzare la transizione locale documentata.
- Backup giornaliero verificato sulla rete, massimo cinque copie negli ultimi cinque giorni (oggi incluso), più la copia di migrazione. Errori e ultimo backup sono mostrati nello stato della scansione.

Il controllo delle dimensioni richiede comunque l'enumerazione dei metadati sulla rete. Nomi e date sono verificati insieme alle dimensioni: una modifica a dimensione invariata non viene ignorata. Contenuti letti soltanto su richiesta, nessuna API OpenAI automatica e nessuna attività pianificata Windows.

[Configurazione, migrazione, backup e ripristino](deployment/SQLITE.md).

## Catalogo MUR e mappa

Catalogo MUR A.1/D.1 con progettisti, ruoli, tipologie, indirizzo, comune e coordinate. Ricerca esclusivamente in Commesse e Archivio, anche nei PDF di ZIP/7z; Gare escluse. Mappa OpenStreetMap con selezione collegata all'elenco. Le analisi e i riferimenti alle fonti sono conservati nel database del server; gli originali restano in sola lettura. I dati mancanti richiedono analisi su richiesta tramite ChatGPT. Aggiornare client, server e MCP alla 2.28.0; il tunnel va avviato manualmente sulla VM con la chiave runtime.

## Politica delle scritture client

Il client non salva log di connessione, heartbeat o notifiche su disco o sulle condivisioni. Anche l'aggiornatore client mostra i messaggi soltanto nella propria finestra. Restano consentiti aggiornamenti, impostazioni, copie locali per aprire i documenti ed esportazioni richieste dall'utente. Download, file scompattati e backup dell'aggiornatore client utilizzano `%LOCALAPPDATA%\Tassinari\CommesseClient\Updates`, non la cartella da cui è stato scaricato o avviato l'installatore. I registri del servizio sulla VM restano disponibili per la diagnostica. Le vecchie copie del client devono essere aggiornate e riavviate per interrompere le loro scritture.

## Novità 2.19.0: analisi IPRiPI

La nuova finestra **Analisi > IPRiPI — analisi MUR A.1/D.1** consulta PDF e risultati salvati da ChatGPT tramite il plugin, con citazioni verificate e classificazione secondo la DGR Emilia-Romagna **2272/2016**. Nessuna API OpenAI dal server. Aggiornare Server, Client e MCP alla 2.19.0 e riavviare il tunnel. Abilitare `Server.EnableMcpIpripiWrites` per salvare le analisi; [guida, configurazione e limiti](deployment/IPRIPI.md).

## Scarica la versione di produzione

- [Aggiornatore diretto 2.28.0](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.28.0/TA-Commesse-Aggiornatore-2.28.0.exe) — prima Server sulla VM, poi Client sui PC.
- [Installazione iniziale Client](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.28.0/TA-Gestione-Cartelle-Commesse-Installazione-Iniziale-2.28.0.exe)
- [Installazione iniziale Server](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.28.0/TA-Gestione-Cartelle-Commesse-Server-Installazione-Iniziale-2.28.0.exe)
- [Componente MCP per la VM](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/download/v2.28.0/TA-Commesse-MCP-2.28.0.zip)
- [Tutti i pacchetti e impronte SHA256](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/tag/v2.28.0)

Il runtime è incluso. L'aggiornatore verifica le impronte, conserva configurazioni e backup e sostituisce i file senza usare un installatore. I controlli degli aggiornamenti partono all'apertura del client o del gestore server, oppure su richiesta: nessuna attività pianificata. L’aggiornatore del server attende anche la chiusura effettiva del processo prima di sostituire i file. I pacchetti non sono firmati digitalmente.

## Novità 2.18.0: localizzazione e percorsi delle relazioni

Ricerca Nominatim su richiesta dal client, tramite il server autenticato: indirizzo visibile e modificabile prima dell'invio, scelta esplicita del risultato, coordinate salvate nei JSON con fonte e precisione come posizione indicativa da verificare. Cache condivisa persistente, limite complessivo di una richiesta al secondo, nessuna ricerca automatica durante digitazione o scansioni. Non inserire indirizzi personali o riservati nel servizio pubblico. Nessuna chiave o fatturazione configurata.

Griglia con indirizzo e percorso sul server. Menu contestuale per aprire una copia del documento in sola lettura o selezionare il file in Esplora file; per ZIP/7z viene selezionato il contenitore. I percorsi aziendali vengono tradotti in Z: sul client. Comune e indirizzo restano visibili anche senza coordinate.

Icona aziendale: cartella bianca con simbolo TA originale, scalato uniformemente. La stessa icona compare su client, gestore server, area di notifica, aggiornatore e installatori.

Aggiornare prima Server e poi Client. Il tunnel MCP 2.17.0 rimane compatibile: questa versione non aggiunge strumenti al plugin e non richiede di reinserire la chiave runtime.

## Novità 2.17.0: catalogo JSON analizzato da ChatGPT

Le relazioni possono essere analizzate su richiesta in ChatGPT e salvate nei JSON condivisi: classificazione, sintesi, sito, coordinate proposte con fonte, fascicoli omogenei e citazioni riscontrate. Nessuna API del modello chiamata dal server. La coda mostra documenti nuovi/modificati; le analisi invariate restano disponibili, quelle superate sono segnalate e lo storico non viene sovrascritto.

Il client mostra fascicolo e stato dell'analisi. Il doppio clic su una riga apre una copia locale in sola lettura del documento, anche da ZIP/7z. Gli originali restano invariati. La copia è prodotta solo su richiesta, con limiti di dimensione, tempo e memoria.

Il plugin offre **15 strumenti**, incluso il salvataggio tipizzato del catalogo. La scrittura richiede una richiesta esplicita di catalogazione e l'abilitazione server `EnableMcpGeologyWrites`; il valore predefinito è disattivato. Aggiornare anche il pacchetto MCP e ricaricare gli strumenti. [Guida completa e struttura JSON](deployment/GEOLOGIA.md).

## Catalogo geologico e mappa introdotti nella 2.16.0

Catalogo geologico e geotecnico autonomo: generato dai nomi e dalle cartelle tematiche dell'inventario, anche dentro ZIP/7z. Candidati da verificare, commesse non attribuite senza evidenza, nessuna lettura automatica dei contenuti.

Client: menu Analisi, finestra indipendente a istanza unica, ricerca dinamica, filtri e mappa OpenStreetMap con simboli selezionabili. Nessuna chiave API o fatturazione; richiede Internet e WebView2. Le posizioni possono essere indicate esplicitamente e salvate nei soli JSON condivisi con account e data.

Catalogo e cache suddivisi per categoria, anno e commessa sotto ArchiveFolder/geologia. Aggiornamento all'avvio e dopo ogni scansione completa dai JSON, conservazione dei dati precedenti in caso di errore, nessuna notifica delle differenze.

MCP: cerca_relazioni_geologiche e leggi_relazione_geologica. La ricerca dei file può consultare le unità già censite prima della fine del primo inventario generale, dichiarandole parziali. Gli errori applicativi sono espliciti. Navigazione diretta e lettura di un singolo documento restano disponibili indipendentemente dall'inventario globale.

Vedere [guida del catalogo geologico](deployment/GEOLOGIA.md) e [collaborazione al progetto](CONTRIBUTING.md). Aggiornare anche il componente MCP e ricaricare i quindici strumenti per usare la nuova area.

## Correzioni 2.15.1: scansione e salvataggi JSON

Una data Win32 non rappresentabile non interrompe più l'intera scansione degli archivi: nomi e dimensioni leggibili vengono conservati, la data resta non disponibile e il report indica il percorso da verificare. Gli errori di lettura della singola voce sono isolati; le sorgenti non vengono corrette o modificate.

I salvataggi JSON consentono letture condivise e sostituzione atomica dell'indice. I blocchi temporanei di altri processi vengono attesi con tentativi per un massimo di dieci secondi tra le chiamate di accesso, interrompibili durante l'arresto. I blocchi persistenti restano errori espliciti; nessun processo viene forzato a liberare il file. Restano attivi lock applicativi, verifica delle generazioni annuali e protezione dei dati precedenti.

**Il componente MCP 2.15.0 è compatibile con il server 2.15.1: se già avviato, il tunnel non richiede un riavvio per questa correzione.**

## Novità 2.15.0: consultazioni MCP e documenti

Il componente MCP non interroga il servizio all'avvio o quando è inattivo. Ogni richiesta dati richiede una consultazione temporanea aperta dal plugin, limitata a due minuti e venti operazioni, senza rinnovo automatico. Le vecchie chiamate prive di consultazione vengono rifiutate. Il tunnel mantiene il solo collegamento tecnico necessario a ricevere le richieste; le scansioni orarie del server rimangono indipendenti.

**elenca_documenti** mostra un livello della cartella o dello ZIP/7z scelto. **leggi_documento** legge tramite il server un PDF testuale, DOCX, XLSX o testo, anche dentro ZIP/7z, solo per una richiesta esplicita del documento. Il testo è paginato, con versione e avvisi sui limiti, e non viene salvato nei JSON o nei log. Non sono inclusi OCR, DOC/XLS legacy, macro e documenti cifrati. La lettura viene interrotta dopo 20 secondi; massimo 50 MiB e 200.000 caratteri estratti. Vedere la [guida MCP](deployment/MCP.md).

**Per il passaggio da versioni precedenti alla 2.15.0, aggiornare sia il servizio sia il pacchetto MCP sulla VM**, riavviare il tunnel e ricaricare gli strumenti del plugin: devono risultare dieci. Il nuovo canale locale rifiuta i componenti MCP precedenti; il collegamento Kerberos dei client resta disponibile.

## Scansione all'avvio e a mezzanotte

La scansione completa di lavori, gare e archivio parte a ogni avvio del servizio e ogni giorno alle 00:00 italiane. Include elenchi, dimensioni, gerarchie e inventario dei nomi, con salvataggio JSON e successivo aggiornamento dei cataloghi MUR e geologico. Nessuna sovrapposizione: una scansione lunga termina prima della successiva. I contenuti dei documenti vengono letti soltanto su richiesta; il lettore PDF include anche i valori AcroForm e le caselle selezionate.

In basso sono sempre disponibili quattro schede: **Scansione**, **Connessione**, **Archivio JSON** e **Messaggi**. Il divisore orizzontale permette di regolarne l’altezza. La scansione mostra le ultime dodici rilevazioni delle cartelle in lettura, con categoria, fase e percorso; per gli archivi il separatore ` > ` distingue il file ZIP/7z dalla directory interna. Il client osserva il percorso corrente ogni tre secondi durante il lavoro: è un riepilogo di attività, non un elenco completo di ogni cartella attraversata. La percentuale testuale, quando stimabile, riguarda le unità della fase e non il tempo residuo. Nessuna barra animata né pannello laterale.

**Report > Non conformità** apre una finestra collegata al client con dati già censiti dal server, ricerca, filtro per categoria e salvataggio HTML della vista. Distingue nomi non conformi, problemi di lettura e preventivi da verificare; indica dati mancanti e verifiche dei file ancora da eseguire. Non avvia nuove scansioni.

Gli elenchi si aggiornano silenziosamente alla conclusione. Le differenze sono consultabili in **Export > Report sintetico analisi nomi e differenze**; le notifiche delle differenze restano sospese. **Export > Scarica ultimo report giornaliero** recupera il report di dimensioni e gerarchie. Il comando manuale **Aggiorna elenco** verifica solo l'elenco della categoria.

Le schede Commesse, Gare e Archivio condividono le colonne Codice, Anno, Numero, Committente, Argomento e Dimensione. Una commessa ufficiale viene riconosciuta solo al primo livello delle cartelle annuali. Le altre strutture sono censite senza promuovere le sottocartelle a commesse. Il pannello della gerarchia mostra i rami salvati; il doppio clic apre Esplora file traducendo la condivisione aziendale nel percorso Z del client.

## Inventario e MCP

I JSON sono separati per categoria, anno e codice nella cartella ArchiveFolder/inventario-file. Ogni voce identifica un file con nome, estensione, percorso relativo, posizione fisica ed eventuale archivio e percorso interno. Si censiscono anche le sottocartelle e le voci ZIP/7z senza estrazione; archivi cifrati o illeggibili e altri limiti vengono segnalati. Gli archivi contenuti in altri archivi sono elencati come file, senza espansione ricorsiva.

Lo strumento **cerca_file** filtra per nome, estensione, categoria, anno e codice. Restituisce totale, data scansione, completezza e pagine vincolate alla versione dell'inventario, senza scaricare tutto il catalogo. **stato_scansione** permette a ChatGPT di consultare fase e avanzamento. **analisi_file** offre catalogo, singoli inventari, candidati e frequenze dei nomi. Nessuna chiamata automatica a modelli IA.

Quando occorre aggiornare il pacchetto MCP sulla VM: fermare il vecchio tunnel, usare il nuovo Start-McpTunnel.ps1 con il tunnel-client ufficiale già configurato e riavviare il tunnel. La chiave runtime viene inserita interattivamente e non salvata dallo script. Poi ricaricare gli strumenti del plugin ChatGPT. La ricerca usa anche le unità già censite durante il primo inventario, dichiarando i risultati parziali. La navigazione diretta e la lettura dei documenti non richiedono il completamento della ricerca globale.

## Configurazione e verifiche

Il servizio usa TCP dedicato e autenticazione Windows Kerberos con protezione mutua; il canale MCP locale verifica gruppo autorizzato e servizio, espone consultazioni e una scrittura opzionale limitata al catalogo geologico derivato. Sulle sorgenti servono Lettura/Elenco; le scritture sono limitate all'archivio JSON applicativo separato. Il client non legge direttamente il catalogo di rete.

- [Installazione, account e SPN](deployment/SERVER-CLIENT.md)
- [Inventario JSON, calendario, ricerca file e limiti](deployment/ANALISI-NOMI.md)
- [Collegamento MCP e tunnel](deployment/MCP.md)

Il workflow GitHub verifica protocollo, autenticazione locale, archivio, inventario ZIP/7z, ricerca e paginazione, calendario giornaliero e riavvio, avanzamento e controlli WPF, aggiornamento e installazione/disinstallazione prima di pubblicare i pacchetti.
