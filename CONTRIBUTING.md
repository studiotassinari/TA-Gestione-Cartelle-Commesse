# Collaborazione al progetto

Il repository contiene codice, istruzioni e test. Documenti delle commesse, cataloghi reali, coordinate riservate, chiavi del tunnel e credenziali restano nei sistemi aziendali e non vanno caricati nel repository pubblico.

## Lavorare insieme

Ogni intervento parte da un ramo distinto aggiornato da `master`, con una descrizione del problema e del risultato atteso. Per lavorare contemporaneamente sullo stesso PC usare checkout o worktree diversi; non modificare gli stessi file nella stessa cartella da due sessioni.

Per l'area geologica, Giuseppe può definire criteri di riconoscimento, informazioni da estrarre e casi da verificare; Antonio coordina interfaccia, collegamenti alle commesse e distribuzione. Le decisioni devono essere riportate nelle issue o nella documentazione, così restano disponibili a entrambi indipendentemente dalla chat utilizzata. I casi di collaudo pubblicati devono essere sintetici o anonimizzati.

Prima di unire un ramo, aprire una pull request con comportamento atteso, cambiamenti, test e limiti. Una sola persona coordina gli aggiornamenti della VM e del tunnel. Unire su `master` avvia il workflow di installatori e release: verificare prima il numero di versione e le note di rilascio. Non sovrascrivere una release esistente.

## Verifiche per l'area geologica

Compilare la soluzione in Release e avviare `GeologyChecks`, `McpChecks`, `LocalMcpChecks`, `FileSearchChecks` e `DocumentChecks`. Il test della mappa nel browser incorporato è disponibile con `GeologyChecks -- --map-browser`; richiede WebView2 e usa cartografia simulata. Verificare che gli originali restino immutati e che candidati, dati parziali e posizioni non accertate siano indicati come tali.

## Accessi distinti

L'accesso al repository GitHub consente di lavorare sul codice secondo il ruolo assegnato. L'accesso al workspace ChatGPT, al plugin e alle cartelle aziendali va configurato separatamente. Condividere una conversazione non concede automaticamente accesso alla cartella di sviluppo locale o alla VM.

La VM usa l'account di servizio e il gruppo Windows configurati. Il tunnel MCP usa l'account Windows del processo; il plugin deve essere accessibile dal workspace aziendale previsto. Le chiavi runtime vengono inserite solo nella finestra del client ufficiale, mai nei file di progetto o nelle chat.
