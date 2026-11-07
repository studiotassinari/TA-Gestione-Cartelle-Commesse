namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Un solo livello: ogni fase appartiene direttamente a una commessa, senza sottofasi.
public sealed record WorkPhase(string Id,string ProjectId,string Name,string Description="",int Order=0,int Revision=0);
