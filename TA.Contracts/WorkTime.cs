namespace TA_Gestione_Cartelle_Commesse.Contracts;

public sealed record WorkTime(string Id,string ProjectId,string ActivityId,DateOnly Day,int Minutes,string Description,string User="",int Revision=0);
