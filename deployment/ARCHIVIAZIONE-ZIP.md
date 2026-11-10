# Archiviazione ZIP dal client — 2.31.1

Disponibile solo nell'applicazione desktop, nelle schede Commesse e Gare, per Antonio e Giuseppe autenticati. Il tunnel MCP non espone comandi ZIP; il canale locale li rifiuta e il servizio respinge anche le richieste ZIP contrassegnate MCP dei vecchi pacchetti TCP.

1. Selezionare una cartella e scegliere **Gestione → Archivia cartella in ZIP…**, oppure la stessa voce con il tasto destro sulla riga.
2. Nella finestra controllare il percorso originale e premere **1. Prepara ZIP**. Il server crea lo ZIP accanto alla cartella, senza sovrascrivere archivi esistenti, e verifica tutti i file mediante SHA-256. Gli originali rimangono presenti.
3. Solo dopo lo stato **ZIP verificato**, premere **2. Elimina originale…**. La finestra mostra i percorsi e chiede una conferma separata, con risposta predefinita No. La conferma vale un'ora ed è monouso.
4. Il server ricontrolla archivio e originali, blocca modifiche concorrenti e rimuove solo gli elementi verificati. In caso di rimozione parziale conserva ZIP e residui, con messaggio esplicito.

Nessuna compressione o eliminazione viene effettuata dal client. Un solo lavoro alla volta sul server; niente esecuzione automatica all'apertura della finestra. La selezione rimane fissata per tutta l'operazione. Chiudendo la finestra il lavoro sul server continua; riaprendola sulla stessa cartella durante la stessa connessione si ritrova lo stato. Un riavvio del servizio invalida lavori e conferme in memoria; non elimina ZIP né originali. Dopo una risposta persa non viene ripetuta automaticamente una mutazione.

Limiti: 20.000 voci, profondità 128, un'ora per preparare. Inclusi file e directory vuote, senza esclusioni dell'inventario. Rifiutati reparse point, link multipli, file cifrati e flussi alternativi; un file aperto in scrittura impedisce la preparazione. Lo ZIP non conserva tutte le informazioni del filesystem, incluse autorizzazioni NTFS. Non è un backup completo del volume. Fanno eccezione due flussi che Windows aggiunge da solo e che non contengono dati del documento: la marcatura di provenienza dei file scaricati (`Zone.Identifier`) e il segnaposto vuoto di `Thumbs.db`; non entrano nello ZIP e non bloccano l'archiviazione.

Le altre scansioni rimangono in sola lettura. Nessuna modifica ad Active Directory e nessuna scrittura di log client.
