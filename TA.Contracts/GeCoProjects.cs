namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Lavoro quotidiano sulle commesse di GeCo in GeCa (2.42.0): «Le mie commesse» e «Tutte», scheda della commessa con dati
// generali, fasi, team, cartella e commesse collegate, richieste di accesso. Le regole sono della libreria GeCo.Dati, le stesse
// di GeCo2027 (ElencoCommesse, ServizioCommessa, AccessoCommesse, ServizioRichiesteAccesso), applicate dal servizio sull'utente
// del token Windows. I nomi delle proprietà sono quelli dei record della libreria: il servizio li copia così come sono.
// Costi e ricavi non arrivano mai qui: restano nelle pagine di GeCo2027 (comando «Apri in GeCo2027»).
//
// Comandi (GeCoCommand):
// «Projects»        Valore = solo le mie, Testo = ricerca, IdCdc = persona del team → GeCoProjectList;
// «ProjectCard»     IdCommessa (o Testo = codice della cartella) → GeCoProjectCardResult;
// «SaveProject»     IdCommessa, Commessa (dati generali e Versione letta) → GeCoProjectCardResult;
// «AddPhase»        IdCommessa, Fase; «SavePhase» Id = fase, Fase; «RemovePhase» Id = fase → GeCoProjectCardResult;
// «RequestAccess»   IdCommessa, Testo = motivo → GeCoProjectCardResult;
// «AccessRequests»  → GeCoAccessRequests; «CancelAccess» Id, Versione; «DecideAccess» Id, Valore = concedi, Testo = nota, Versione;
// «RevokeAccess»    Id, Testo = nota, Versione → GeCoAccessRequests.

// Riga dell'elenco: commessa (IdCommessa) con la sua cartella, o cartella senza commessa (IdCommessa null). Collegata: la cartella
// è collegata in modo definitivo; altrimenti è solo la cartella presente con lo stesso codice. Attribuita falso: commessa da
// consultare in sola lettura (se ne chiede l'accesso). UltimaModifica gg/mm/aaaa hh:mm.
public sealed record GeCoProjectRow(string Chiave, string Codice, int? Anno, string? Numero, string? Committente, string? Argomento, string? Stato,
    long? Byte, string? UltimaModifica, string? Preventivo, string? Percorso, int? IdCommessa, int? IdCliente, int? IdCartella, bool Collegata,
    string[] Motivi, string? Attribuzione, bool Attribuita, GeCoTeamMember[]? Team);
// Trovate: righe dopo la ricerca (ne arrivano al massimo 1000, le più recenti); Totale: prima della ricerca.
public sealed record GeCoProjectList(GeCoProjectRow[] Righe, int Trovate, int Totale, bool ConCartelle, bool ConTeam, bool Mie, int? Persona);

// Scheda della commessa (SchedaCommessa della libreria). Date gg/mm/aaaa; Stato A aperta, C chiusa, F fatturata, vuoto non iniziata.
public sealed record GeCoCardItem(int Id, string Nome);
public sealed record GeCoCardPhase(int Id, int? Ordinamento, int? IdTipoFase, string? TipoFase, string Descrizione, int? IdResponsabile, string? Responsabile, string? Stato);
public sealed record GeCoLinkedProject(int IdRiga, int IdCommessa, string Codice, string? Descrizione, string? Cliente, string Stato);
// Che cosa l'utente può fare, deciso dal servizio con le regole di GeCo2027.
public sealed record GeCoCardPermissions(bool Modifica, bool VedeFasi, bool InsFasi, bool ModFasi, bool DelFasi, bool VedeCollegate);
public sealed record GeCoCardAttribution(bool Attribuita, string[] Motivi, string Testo, bool SolaLettura);
public sealed record GeCoProjectCard(int Id, int Anno, string Codice, string Descrizione, string Note, int IdCliente, string Cliente, string TipoDocumento,
    GeCoCardItem? Capocommessa, GeCoCardItem? RespContr, GeCoCardItem? Manager, GeCoCardItem? Tipo, string? AreaBusiness,
    string DataIniPrev, string DataFinePrev, string DataIniEff, string DataFineEff, string Stato, string StatoTesto, string TipoProd, bool Archiviata,
    GeCoCardAttribution Attribuzione, GeCoCardPermissions Permessi, GeCoCardPhase[]? Fasi, GeCoLinkedProject[]? Collegate, string Versione,
    GeCoOption[]? Capicommessa, GeCoOption[]? Managers, GeCoOption[]? Tipologie, GeCoOption[]? TipiFase, GeCoOption[]? Responsabili, int NuovoOrdinamento);

// Richieste di accesso (ServizioRichiesteAccesso). Stato: InAttesa, Concessa, Rifiutata, Annullata, Revocata.
public sealed record GeCoAccessRequest(int Id, int IdCommessa, string Codice, string? Descrizione, string? Cliente, int IdUtente, string Utente,
    string Motivo, string Stato, string StatoTesto, DateTime DataRichiesta, DateTime? DataDecisione, string? Decisore, string? NotaDecisione,
    DateTime? DataRevoca, string? Revocatore, string? NotaRevoca, string Origine, int Versione);
// Disponibile falso: database senza la migrazione 13. Richiesta: l'ultima dell'utente per la commessa.
public sealed record GeCoAccessState(int IdCommessa, bool Disponibile, bool Attribuita, string[] Motivi, string Testo, GeCoAccessRequest? Richiesta,
    bool PuoRichiedere, bool PuoDecidere, int DaDecidere);
public sealed record GeCoAccessRequests(bool Decide, bool Amministratore, GeCoAccessRequest[] Mie, GeCoAccessRequest[] DaDecidere, GeCoAccessRequest[] Concessi,
    GeCoAccessRequest[] Decise);

// Risposta della scheda: Trovata falso se la commessa non c'è (Message spiega). Accesso: stato delle richieste (null prima della
// migrazione 13). Cartella: situazione del collegamento con la cartella di GeCa (null se non disponibile); un candidato resta
// un candidato, mai una cartella accertata.
public sealed record GeCoProjectCardResult(bool Trovata, string Message, GeCoProjectCard? Scheda, GeCoAccessState? Accesso, GeCoLinkRow? Cartella);

// Dati generali modificati dalla scheda (gli altri campi restano quelli salvati). Versione: quella letta con la scheda.
public sealed record GeCoProjectEdit(string? Versione, string? Descrizione, string? Note, int? IdResponsabile, int? IdRespContr, int? IdManager, int? IdTipo,
    string? DataIniPrev, string? DataFinePrev, string? DataIniEff, string? DataFineEff);
// Fase da salvare: ordinamento come lo scrive l'utente; gli importi non si toccano da GeCa.
public sealed record GeCoPhaseEdit(int? IdTipoFase, string? Descrizione, string? Ordinamento, int? IdResponsabile, string? Stato);
