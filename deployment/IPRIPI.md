# IPRiPI: analisi dei PDF MUR A.1/D.1 tramite ChatGPT

Funzionalità introdotta nella versione **2.19.0**. Il menu **Analisi > IPRiPI — analisi MUR A.1/D.1** apre una finestra indipendente, non modale, a istanza unica. Il servizio usa il plugin ChatGPT già previsto dal progetto: non richiede chiavi API OpenAI e non chiama modelli dal server.

## Utilizzo

1. Cercare il codice commessa nella nuova finestra. Per impostazione iniziale sono mostrati i nomi MUR/IPRiPI/A1-D1 e le analisi già salvate. Attivare **Tutti i PDF** per moduli rinominati; i nomi non provano la natura del documento. La ricerca usa l'inventario JSON e include ZIP/7z, senza lettura automatica dei contenuti.
2. Selezionare il PDF e usare **Copia richiesta per ChatGPT**. Incollare la richiesta nella conversazione con il plugin TA – Gestione Commesse aggiornato. La copia negli appunti non invia il documento né avvia un'analisi.
3. ChatGPT apre `inizia_consultazione(contenuti=true, analisiIpripi=true)`, consulta `cerca_mur_ipripi` e `regole_ipripi`, legge il PDF scelto con `leggi_mur_ipripi` e salva con `salva_analisi_ipripi`. La sessione dura 15 minuti o 60 operazioni, senza rinnovo automatico. L'abilitazione IPRiPI non abilita le scritture geologiche e viceversa.
4. Aggiornare la finestra per vedere descrizione, codice/variante, livello L0/L1/L2, motivazione, informazioni mancanti e citazioni. **Apri copia PDF** permette il controllo visivo sull'originale copiato in sola lettura. **Esporta vista JSON** salva solo la vista caricata, comprese le prove e gli stati delle versioni.

Ogni risultato rimane associato al proprio documento e alla commessa ufficiale dell'inventario. I risultati di più revisioni o moduli non vengono sommati automaticamente: possono descrivere le medesime opere o contenere dichiarazioni discordanti. Le unità non classificate restano senza commessa attribuita.

## Riferimento normativo

Il riferimento richiesto è la **DGR Emilia-Romagna 2272 del 21 dicembre 2016**, Allegato 1, non 2276/2016.

- [PDF ufficiale](https://territorio.regione.emilia-romagna.it/codice-territorio/sismica/atti-di-indirizzo-e-coordinamento/DGR2272_2016IPRIPI.pdf/@@download/file/DGR%202272_2016%20IPRIPI.pdf)
- [Atti di indirizzo regionali vigenti](https://ambiente.regione.emilia-romagna.it/it/geologia/sismica/lr-19-2008-norme-per-la-riduzione-del-rischio-sismico/l-r-19-2008-principali-provvedimenti-amministrativi/atti-di-indirizzo-aggiornati)

`TA.Server/IpripiRules.json` è una trascrizione testuale derivata dal PDF ufficiale consultato il 2 novembre 2026: contiene URL, SHA256 della fonte, premesse e documentazione dell'Allegato 1, 57 voci articolate in 83 varianti con livello e pagina PDF di inizio della voce. Per varianti su più pagine leggere anche la pagina successiva. I simboli ≤/≥ sono normalizzati dall'estrazione PDF. Il JSON è una risorsa incorporata, indipendente dalla disponibilità del sito regionale a runtime. Non è un motore automatico di asseverazione: condizioni generali, ambito temporale, cumulo degli interventi e tutti i requisiti della singola voce devono essere considerati nell'analisi. Le disposizioni successive non vengono aggiornate automaticamente; aggiornare la risorsa tramite revisione del codice quando necessario.

I codici del modulo **MUR A.1/D.1** non sono categorie IPRiPI: un intervento può avere, per esempio, codice `B.4.4.a`. Non classificare sulla sola somiglianza delle parole.

## Risultati e controlli

- `Dichiarato`: codice citato dal modulo compilato e riferito all'opera; un elenco prestampato non basta. La presenza della citazione è verificata dal server, la selezione effettiva richiede interpretazione del documento.
- `Dedotto`: proposta motivata confrontando tutti i requisiti documentati con la voce normativa; dati mancanti impediscono questo stato.
- `Da verificare`: requisiti, scelta della casella, variante o identificazione dell'opera non sufficientemente documentati. Codice/livello possono rimanere vuoti.
- `Nessun IPRiPI dichiarato` riguarda soltanto il testo del documento letto, non certifica assenza di opere nella commessa. Non è consentito con letture parziali o errori di estrazione. `Non pertinente` distingue i falsi candidati dai moduli effettivi; `Non leggibile` non consente classificazioni.

Il lettore esistente è testuale: **non include OCR né riconoscimento visivo affidabile delle caselle**. Moduli scansionati o con scelte grafiche ambigue richiedono verifica visiva/una copia testuale; non sono automaticamente negativi. Non si esegue lettura in massa, indicizzazione dei contenuti o analisi programmata. Il testo del PDF viene trattato come dato, mai come istruzione.

Le citazioni (offset assoluto e testo esatto) devono comparire nelle porzioni lette negli ultimi 20 minuti. Il salvataggio verifica anche il checksum attuale del documento, la firma dei metadati e la revisione precedente; non accetta una risposta riferita a un PDF cambiato. Le prove in memoria sono limitate a 32 documenti e 400.000 caratteri per documento. Valgono i limiti del worker esistente (50 MiB, 20 secondi, 200.000 caratteri, pagine testuali da 24.000 caratteri).

I dati derivati sono salvati in `ArchiveFolder/ipripi/indice.json`, con revisioni immutabili in `revisioni/<hash-documento>/<revisione>.json`. Nessun testo integrale è salvato: soltanto risultati e brevi citazioni. L'indice atomico è il punto di commit e non supera 32 MiB. I PDF originali non vengono scritti. Le analisi restano visibili se la fonte scompare dall'inventario; modifiche ai metadati le marcano da aggiornare. Per documenti in archivio o con metadati incompleti il ricensimento invalida conservativamente l'analisi. Lo stato aggiornato nella finestra si riferisce ai metadati dell'inventario, non a un controllo del file in tempo reale.

## Attivazione

Distribuire **server, client e componente MCP 2.19.0**. La pubblicazione su GitHub non aggiorna automaticamente la VM.

1. Sulla VM arrestare il tunnel precedente con **Ctrl+C** nella sua console.
2. Usare l'aggiornatore 2.19.0 per aggiornare prima il Server, quindi i Client. Verificare che il servizio server sia avviato e il canale TCP pronto.
3. Estrarre il pacchetto `TA-Commesse-MCP-2.19.0.zip` in una nuova cartella. Rendere disponibile il client ufficiale già utilizzato in `Tunnel/tunnel-client.exe`, oppure indicarne il percorso con `-TunnelClient`. Conservare il vecchio pacchetto per eventuale ripristino.
4. Per consentire il salvataggio, modificare soltanto la proprietà `EnableMcpIpripiWrites` nella sezione `Server` della configurazione server esistente, conservando tutte le altre impostazioni, poi riavviare il servizio dal gestore server.
5. Da PowerShell nella cartella del nuovo pacchetto eseguire `./Start-McpTunnel.ps1 -Server <server-usato-prima> -TunnelId <tunnel_id-esistente> -Transport <kerberos-o-local> -CheckConnection`, sostituendo i segnaposto con gli stessi valori usati in precedenza. Conservare anche l'eventuale porta personalizzata tramite `-Port`. Inserire la chiave runtime soltanto nel prompt locale e lasciare aperta la console.
6. Ricaricare gli strumenti del plugin ChatGPT: devono essere disponibili i quattro comandi IPRiPI elencati sotto (19 strumenti complessivi). Una semplice ripartenza del vecchio eseguibile non li aggiunge.
7. Nella finestra IPRiPI cancellare eventuali filtri (per esempio `b.4.c`), cercare la commessa e premere **Cerca / aggiorna**. Se necessario selezionare **Tutti i PDF**. La ricerca dipende dal censimento dei nomi/metadati già effettuato dal server; i contenuti e le classificazioni non sono analizzati automaticamente.

Nel file server-settings.json, sezione `Server`, impostare `EnableMcpIpripiWrites: true` solo per abilitare il salvataggio richiesto; il valore predefinito è `false`. Sono utilizzabili anche le variabili di configurazione già previste, per esempio `TA_Server__EnableMcpIpripiWrites=true`. La chiave è indipendente da `EnableMcpGeologyWrites`. L'archivio derivato deve restare fuori dalle sorgenti.

Riavviare il componente MCP aggiornato e ricaricare gli strumenti del plugin: quattro nuovi strumenti (`cerca_mur_ipripi`, `regole_ipripi`, `leggi_mur_ipripi`, `salva_analisi_ipripi`), oltre al parametro `analisiIpripi` di `inizia_consultazione`. TCP/Kerberos, ACL del canale locale e account Windows mantengono i controlli esistenti.

## Verifica

`dotnet run --project tests/IpripiChecks -c Release` verifica il flusso su PDF e ZIP sintetici, classificazioni, citazioni, revisioni, modifica delle fonti, sessioni e finestra WPF. Un percorso PNG come argomento salva un'anteprima della finestra con dati sintetici. Eseguire inoltre i controlli DocumentChecks, McpChecks, LocalMcpChecks, FileSearchChecks e GeologyChecks e compilare la soluzione in Release. I test non inviano documenti aziendali né interrogano ChatGPT.

Prima dell'uso operativo collaudare con MUR rappresentativi dello Studio, inclusi caselle selezionate, più interventi, revisioni discordanti e scansioni. Il test sintetico verifica il software, non misura l'accuratezza di ChatGPT sui moduli reali.
