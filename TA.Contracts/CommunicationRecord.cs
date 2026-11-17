namespace TA_Gestione_Cartelle_Commesse.Contracts;
// Utente autenticato e IP provengono dal server; il nome PC e dichiarato dal client.
public sealed record CommunicationRecord(DateTimeOffset Time, string Computer, string Address, string User,
    string Action, string Outcome, string Detail, string Correlation);
// GeCoAddress: indirizzo del sito GeCo configurato sul server (vuoto = non configurato); il client lo usa solo per aprire le pagine di GeCo nel browser.
public sealed record ServerPermissions(bool DomainAdministrator, string GeCoAddress = "");
public sealed record ServerLogs(string Service, string Communications);
