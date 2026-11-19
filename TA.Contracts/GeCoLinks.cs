namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Collegamento delle cartelle a commesse e gare di GeCo (dalla 2.38.0, database comune alla migrazione 12) e notifiche
// arrivate da GeCo2027. Proposte, livelli, segnalazioni e scritture sono quelli di ServizioCollegamentiCartelle della
// libreria GeCo.Dati, gli stessi della pagina Collegamento cartelle di GeCo2027: qui ci sono soltanto i dati scambiati.
// Tipo: "commessa" o "gara". Livello: "sicura", "daverificare" o "" (nessuna proposta).

// Cartella rilevata (tblCartelle). Categoria: C commesse, G gare, A archivio. Presente falso: non trovata dall'ultima scansione.
public sealed record GeCoFolder(int Id, string Percorso, string Nome, string Categoria, string Codice, int? Anno, string? Committente, string? Oggetto,
    bool Presente, string? UltimaRilevazione);
// Cartella candidata con punteggio (0–100) e motivo leggibile: un candidato non è mai un collegamento accertato.
public sealed record GeCoFolderCandidate(int IdCartella, int Punteggio, string Motivo, GeCoFolder? Cartella);
// Collegamento confermato: dove (GeCo2027 o GeCa), quando (gg/mm/aaaa hh:mm) e da chi.
public sealed record GeCoLinkInfo(int IdRiga, int IdCartella, string Data, string Origine, string? Utente);
public sealed record GeCoLinkConflict(string Tipo, string? TipoElemento, int? IdElemento, int? IdCartella, int? IdCartellaNuova, string Testo);
public sealed record GeCoLinkRow(string Tipo, int Id, string Codice, int? Anno, string? Descrizione, string? Cliente, bool Sottocommessa,
    GeCoLinkInfo? Collegamento, GeCoFolder? Cartella, GeCoFolderCandidate? Proposta, GeCoFolderCandidate[] Candidati, string Livello, GeCoLinkConflict[] Conflitti);
public sealed record GeCoLinkElement(string Tipo, int Id, string Codice);
// Cartella presente non collegata, con gli elementi per cui è candidata (nessuno: cartella senza commessa né gara).
public sealed record GeCoFreeFolder(GeCoFolder Cartella, GeCoLinkElement[] CandidataPer);
// Collegamento da scrivere: anteprima di «Conferma tutte le proposte sicure», che il servizio ricalcola prima di scrivere.
public sealed record GeCoLinkItem(string Tipo, int Id, int IdCartella);
public sealed record GeCoFolderSync(string Categoria, string Data, string Origine, int Cartelle);
public sealed record GeCoLinks(GeCoLinkRow[] Elementi, GeCoFreeFolder[] CartelleLibere, GeCoLinkConflict[] Conflitti, GeCoLinkItem[] Sicure, GeCoFolderSync[] Sincronizzazioni);
// Cartella per «Scegli cartella…», con l'elemento a cui è già collegata.
public sealed record GeCoFolderChoice(GeCoFolder Cartella, GeCoLinkElement? CollegataA);

// Evento del registro comune (tblSincronizzazioneEventi) scritto da GeCo2027: commessa creata o modificata, collegamento...
// Data gg/mm/aaaa hh:mm. Tipo: quelli di EventiSincronizzazione.Tipi; il client accetta anche tipi che non conosce.
public sealed record GeCoSyncEvent(long Id, string Data, string Origine, string Tipo, int? IdCommessa, int? IdGara, int? IdCartella, string? Codice,
    string? Percorso, string Testo, string? Utente);
// Disponibile falso: database senza la migrazione 12 (Message spiega). UltimoId: ultimo evento di GeCo2027 nel registro.
public sealed record GeCoSyncEvents(GeCoSyncEvent[] Eventi, long UltimoId, bool Disponibile, string Message = "");

// Pianificazione o ore salvate (2.39.0, eventi tecnici del registro comune, scritti da GeCo2027 o da GeCa): non sono notifiche,
// servono a ricaricare le finestre aperte. Tipo: PianificazioneModificata o OreModificate. IdCdc: persona toccata; Dal e Al
// (aaaa-mm-gg): primo e ultimo giorno; Commesse: commesse coinvolte (AltreCommesse: elenco accorciato); Scheda: finestra o
// scheda del browser che ha salvato; Utente: chi ha salvato.
public sealed record GeCoViewUpdate(long Id, string Origine, string Tipo, int IdCdc, string Dal, string Al, int[] Commesse, bool AltreCommesse, string? Scheda, string? Utente);
// Eventi tecnici dopo DopoId che l'utente può vedere (persona propria o, secondo il ruolo, di tutti). UltimoId: ultimo evento
// tecnico del registro. Parziale: ne sono arrivati più di quelli restituiti (client rimasto indietro): ricaricare comunque.
public sealed record GeCoViewUpdates(GeCoViewUpdate[] Eventi, long UltimoId, bool Disponibile, bool Parziale = false);
