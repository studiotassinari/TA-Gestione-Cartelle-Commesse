namespace TA_Gestione_Cartelle_Commesse.Contracts;
// Utente autenticato e IP provengono dal server; il nome PC e dichiarato dal client.
public sealed record CommunicationRecord(DateTimeOffset Time, string Computer, string Address, string User,
    string Action, string Outcome, string Detail, string Correlation);
public sealed record ServerPermissions(bool DomainAdministrator);
public sealed record ServerLogs(string Service, string Communications);
