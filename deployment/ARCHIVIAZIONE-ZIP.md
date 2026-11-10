# Archiviazione esplicita di lavori e gare

Tre comandi MCP, disponibili dopo l'aggiornamento del servizio e del pacchetto MCP e il riavvio manuale del tunnel:

1. `prepara_zip_cartella(category, folderId)` crea accanto alla cartella lo ZIP con lo stesso nome. Non sovrascrive archivi esistenti. Include tutti i file, anche quelli esclusi dalle scansioni (es. Sismicad), e le cartelle vuote. Non elimina originali.
2. `stato_zip_cartella(category, jobId)` restituisce esito, percorsi, conteggi e la conferma monouso. Mostrare questi dati all'utente e ottenere una nuova conferma esplicita prima della cancellazione.
3. `elimina_cartella_zippata(category, jobId, confirmation)` ricontrolla integralmente ZIP e originali e rimuove esclusivamente gli elementi verificati. La conferma scade dopo un'ora; lo stato del lavoro e le conferme scadono al riavvio del servizio. Consultare lo stato fino all'esito dell'operazione richiesta.

Sono consentite solo cartelle censite con codice C/G nelle radici lavori/gare. Archivio, radici e annualità non sono bersagli validi. Il server verifica il SID Windows: solo Antonio e Giuseppe, con lo stesso account che ha preparato lo ZIP. Un tunnel eseguito come Administrator non è autorizzato. Nessuna modifica di Active Directory o dei permessi delle condivisioni viene eseguita: l'account del servizio deve già poter creare lo ZIP e rimuovere la cartella autorizzata.

Lo ZIP viene riletto e confrontato con SHA-256 per ogni file. Prima della rimozione si confrontano nuovamente nomi, dimensioni, date dei file e contenuti. I file verificati vengono bloccati contro scritture, sostituzioni e rinomine durante l'operazione. La rimozione usa gli handle Windows già verificati, mai una cancellazione ricorsiva per percorso. Un file aggiunto durante la rimozione non viene cancellato; resta nella cartella residua e il lavoro segnala rimozione parziale. Anche in questo caso ogni file già rimosso è presente nello ZIP verificato.

Per questa prima versione sono rifiutati collegamenti, hard link, file cifrati, flussi alternativi e filesystem per i quali non sia possibile verificarli. Massimo 20.000 elementi e 128 livelli per lavoro, un solo lavoro alla volta, preparazione limitata a un'ora. Nessuna cancellazione automatica o recupero automatico dopo un errore. Lo ZIP conserva i contenuti e la struttura, non i permessi NTFS né tutti gli attributi originali.

Riferimento tecnico: [SetFileInformationByHandle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle). La cancellazione per handle richiede l'accesso DELETE; non concede permessi aggiuntivi.

Le scansioni, le letture documentali e gli approfondimenti restano in sola lettura. Questa è un'operazione amministrativa separata, marcata distruttiva nel catalogo MCP e mai eseguita durante un'analisi.
