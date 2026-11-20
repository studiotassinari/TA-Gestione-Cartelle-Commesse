namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Team della commessa di GeCo (2.41.0), lo stesso della sezione «Team» della scheda della commessa di GeCo2027: le regole
// (chi lo modifica, chi si può aggiungere, eventi e notifiche) sono di ServizioTeamCommessa nella libreria GeCo.Dati, applicate
// dal servizio. I nomi delle proprietà sono quelli dei record della libreria (PersonaTeam, ComponenteTeam, TeamDellaCommessa).
// Comandi: «ProjectTeam» con IdCommessa (o Testo = codice della cartella, per la commessa principale con quel codice);
// «SetTeamMember» con IdCommessa, IdCdc e Valore (aggiungere o togliere); «SetPermanentTeam» con IdCommessa, Id del team e Valore.

public sealed record GeCoTeamPerson(int Id, string Nome, bool Attiva = true);
public sealed record GeCoPermanentTeam(int Id, string Nome, bool Attivo, GeCoTeamPerson[] Membri);
// Persona del team con il ruolo: capocommessa, membro indicato una per una (Diretto), membro dei team permanenti (Team).
public sealed record GeCoTeamMember(int Id, string Nome, bool Attiva, bool Capocommessa, bool Diretto, string[] Team, string Ruolo, string Iniziali);
// Team null: database prima della migrazione 9. TeamDisponibili e Candidati solo per chi può modificare (PuoModificare).
public sealed record GeCoProjectTeam(int IdCommessa, string Codice, string Descrizione, int? IdResponsabile, GeCoTeamMember[] Componenti, GeCoTeamPerson[] Membri,
    GeCoPermanentTeam[]? Team, GeCoTeamPerson[]? TeamDisponibili, GeCoTeamPerson[]? Candidati, bool PuoModificare);
// Risposta di «ProjectTeam»: Trovata falso quando il codice non corrisponde a nessuna commessa di GeCo (Message spiega).
public sealed record GeCoProjectTeamResult(bool Trovata, string Message, GeCoProjectTeam? Team);
