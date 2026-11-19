namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Pianificazione delle attività e ore lavorate di GeCo, sul database comune. Le regole sono quelle della libreria
// GeCo.Dati (GeCo.Dati.Regole), applicate dal servizio: qui ci sono soltanto i dati scambiati con il client.
// I nomi delle proprietà sono quelli dei record della libreria e del JSON di GeCo2027 (Codice, Fasi, IdCdc, ...):
// il servizio li copia così come sono, e un nome diverso perderebbe il dato senza errori.

// Comando verso il servizio. Persone null = solo la persona dell'utente; Settimane null = quelle predefinite.
// Collegamento cartelle (2.38.0): Tipo "commessa" o "gara", Id dell'elemento, IdCartella, Valore = sostituire il collegamento
// esistente, Collegamenti = anteprima delle proposte sicure, DopoId = ultimo evento già ricevuto (null = gli ultimi).
// Scheda (2.39.0, facoltativa): finestra del client che chiede, scritta negli eventi tecnici dei salvataggi; la finestra che ha
// salvato non si ricarica per il proprio salvataggio (lettere, cifre, - e _, al massimo 40 caratteri, altrimenti ignorata).
public sealed record GeCoCommand(string Action, string? Dal = null, int? Settimane = null, int[]? Persone = null, int? IdCdc = null, int[]? Voci = null,
    string? Testo = null, int Id = 0, int IdCommessa = 0, int IdVdc = 0, int? IdFase = null, bool Valore = false,
    GeCoActivityRequest? Attivita = null, GeCoSaveRequest? Salvataggio = null, GeCoConfirmRequest? Conferma = null,
    string? Tipo = null, int IdCartella = 0, GeCoLinkItem[]? Collegamenti = null, long? DopoId = null, string? Scheda = null,
    GeCoAlignmentRequest? Allineamento = null);

// Chi è l'utente per GeCo, deciso dal servizio dal token Windows. Recognized falso: le finestre mostrano Message e non permettono nulla.
// Match: come è stato trovato l'utente (abbinamento della pagina Utenti di GeCo, nome uguale...). Hours: funzione delle ore assegnata.
// Administrator: amministratore di GeCo (tipo A o tutte le funzioni); Links: database con il collegamento delle cartelle (migrazione 12).
public sealed record GeCoAccess(bool Recognized, string Message, string Account, string User = "", string Name = "", string Match = "", string Company = "",
    int DatabaseVersion = 0, bool Planning = false, bool Hours = false, bool Administrator = false, bool Links = false);

// ---------------------------------------------------------------- pianificazione (ServizioPianificazione)

public sealed record GeCoPhase(int Id, string Descrizione);
// Mia: l'utente vi può pianificare (amministratore o capocommessa).
public sealed record GeCoPlanProject(int Id, string Codice, string Descrizione, string Cliente, bool Mia, GeCoPhase[] Fasi);
public sealed record GeCoPerson(int Id, string Nome, bool Io);
// Gestita: pianificabile su qualunque commessa (solo per gli amministratori).
public sealed record GeCoShownPerson(int Id, string Nome, bool Io, bool Gestita);
// Dal e Al in formato aaaa-mm-gg; Modificabile: l'utente la può spostare o togliere.
public sealed record GeCoActivity(int Id, int IdCdc, int IdCommessa, int? IdFase, string Fase, string Dal, string Al, bool Modificabile);
// Ruolo: "amministratore", "teammanager" o "collaboratore", come nella pagina di GeCo2027.
public sealed record GeCoPlan(string Dal, int Settimane, string Oggi, int OreGiorno, string Ruolo, GeCoPerson[] Elenco, GeCoShownPerson[] Persone,
    bool PuoPianificare, bool PuoOrganizzare, GeCoPlanProject[] Commesse, GeCoActivity[] Attivita);
public sealed record GeCoActivityRequest(int IdCdc, int IdCommessa, int? IdFase, string? Dal, string? Al);
public sealed record GeCoAdministrator(int Id, string Nome, string Utente, string Come);
public sealed record GeCoUserRole(int Id, string Nome, bool TeamManager);
public sealed record GeCoRoles(GeCoAdministrator[] Amministratori, GeCoUserRole[] Utenti);

// ---------------------------------------------------------------- ore lavorate (ServizioConsuntivo)

public sealed record GeCoOption(int Value, string Text);
public sealed record GeCoPagePermissions(string Pagina, string Titolo, bool Ins, bool Mod, bool Del);
// Ore: la voce conta come ore; Predefinita: mostrata di base per la persona.
public sealed record GeCoCostItem(int Id, string Codice, string Descrizione, bool Ore, bool Predefinita);
public sealed record GeCoCostCenter(int Value, string Text, string Cdc_IntExt, bool Cdc_Attivo);
public sealed record GeCoHoursStart(string Titolo, GeCoPagePermissions Permessi, GeCoCostCenter[] Cdc, int? IdCdc, bool PuoCambiareCdc, bool ModificaTutti,
    bool PuoConfermare, bool FasiObbligatorie, string SettimanaCorrente, string Oggi, GeCoCostItem? VoceKm);
// Casella di un giorno: Bloccata è il motivo per cui il giorno non si scrive (null se scrivibile); date dei documenti gg/mm/aaaa.
public sealed record GeCoCell(string Data, decimal? Qta, bool Conf, int? IdFase, string? Note, string? NumDoc, string? DataDoc, string? Fornitore,
    string? DataRifSal, string? LinkFattura, string? Bloccata, int? IdClass = null);
public sealed record GeCoClassOption(int Id, string Valore, bool Associata);
public sealed record GeCoDayClasses(string Data, int Associate, GeCoClassOption[] Opzioni);
// Riga commessa × voce × fase con una casella per giorno del periodo.
public sealed record GeCoRow(int IdCommessa, string Codice, string Descrizione, string Cliente, int IdVdc, string Voce, int? IdFase, string Fase,
    GeCoOption[] Fasi, bool FaseRichiesta, bool ClassRichiesta, GeCoCell[] Celle);
public sealed record GeCoKmItem(int Id, string Codice, string Descrizione, bool Disponibile);
public sealed record GeCoDay(string Data, string Nome, string Giorno);
public sealed record GeCoPeriod(string Dal, string Al, string Settimana, int Settimane, GeCoDay[] Giorni, int IdCdc, string Cdc, bool SolaLettura, string TipoCentro,
    GeCoCostItem[] Voci, int[] VociSelezionate, GeCoKmItem? VoceKm, GeCoRow[] Righe, bool DaSettimanaPrecedente, GeCoDayClasses[] Classificazioni, GeCoPagePermissions Permessi);
public sealed record GeCoRecent(int IdCommessa, string Codice, string Descrizione, string Cliente, bool Preferita);
public sealed record GeCoRecents(GeCoRecent[] Recenti, GeCoRecent[] Preferite);
public sealed record GeCoTeamProposal(int IdCommessa, string Codice, string Descrizione, string Cliente, bool Team, GeCoOption[] Fasi, bool FaseRichiesta);
// Ore stimate dalla pianificazione: solo un suggerimento, nulla si salva finché la persona non scrive le ore.
public sealed record GeCoPlanProposal(int IdCommessa, int? IdFase, string Data, decimal Ore);
public sealed record GeCoProposals(GeCoTeamProposal[] Team, GeCoPlanProposal[] Piano);
public sealed record GeCoFoundProject(int IdCommessa, string Codice, string Descrizione, string? Cliente);
// Casella da salvare: Qta come la scrive l'utente (7,5 - 7.5 - 7:30; vuota o 0 = rilevazione eliminata).
public sealed record GeCoCellSave(int IdCommessa, int IdVdc, string Data, string? Qta, int? IdFase = null, string? Note = null, string? NumDoc = null, string? DataDoc = null,
    string? Fornitore = null, string? DataRifSal = null, string? LinkFattura = null, int? IdClass = null);
public sealed record GeCoSaveRequest(int? IdCdc, string? Dal, int? Settimane, GeCoCellSave[] Celle);
public sealed record GeCoCellError(int IdCommessa, int IdVdc, int? IdFase, string Data, string Messaggio);
public sealed record GeCoSaveResult(int Salvate, GeCoCellError[] Errori, string[]? Avvisi);
public sealed record GeCoConfirmRequest(int? IdCdc, string? Dal, int[]? Voci, bool Conferma);
public sealed record GeCoConfirmResult(string Messaggio, int Righe);
