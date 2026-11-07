# Archiviazione locale e ricostruzione — 2.23

Il database operativo deve risiedere su disco locale della VM. In modalità `LocalStorageOnly` anche report e file di lavoro sono sotto `ArchiveFolder` locale. `BackupFolder` è l'unica destinazione condivisa delle nuove scritture; gli originali Lavori/Gare/Archivio rimangono in sola lettura.

La transizione richiede una nuova cartella database vuota, separata dalla precedente. `Enable-LocalStorage.ps1` configura i percorsi con servizio fermo dopo l'aggiornamento. Il servizio deve poter leggere i vecchi cataloghi e modificare la nuova cartella locale e la destinazione backup UNC. Lo script non crea gruppi AD e non modifica gli originali.

All'avvio vengono prodotti una copia SQLite coerente del vecchio database e un recupero dei soli cataloghi geologia/IPRiPI, con precedenza alle versioni già in SQL. Entrambi vengono verificati e copiati nella sottocartella backup `prima-ricostruzione`. Errori di accesso o verifica bloccano la ricostruzione; non vengono ignorati. Completato il recupero, un marcatore locale impedisce di ripetere l'importazione. Il vecchio inventario dei file non viene importato.

La prima scansione ricostruisce l'inventario. Le successive verifiche avvengono all'avvio e alle 00:00 italiane, con approfondimento delle unità modificate. Un servizio interno controlla ogni cinque minuti la necessità di una copia giornaliera del database, indipendentemente dal completamento della scansione. Nessuna attività pianificata Windows. Conservazione predefinita: sette copie, oltre alle copie iniziali di recupero. SQLite Backup API, controllo d'integrità e confronto SHA256 evitano la copia incoerente di un database aperto.

Il gestionale attività/clienti/ore usa `gestionale.sqlite` nella stessa radice locale, con backup nella sottocartella condivisa `gestionale`. La traccia delle modifiche gestionali risiede nel database del server. Nessun log di connessione sul client.

Non eliminare la vecchia cartella condivisa finché non sono verificati recupero delle analisi, completamento della ricostruzione e un backup successivo alla scansione. La transizione non elimina automaticamente nessun file precedente. Conservare il vecchio database per il ripristino; non sostituire un database mentre il servizio è attivo e non copiare a mano un file SQLite aperto senza i dati WAL.
