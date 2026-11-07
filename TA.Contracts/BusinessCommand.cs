namespace TA_Gestione_Cartelle_Commesse.Contracts;

public sealed record BusinessCommand(string Action,BusinessQuery? Query=null,Customer? Customer=null,WorkProject? Project=null,WorkActivity? Activity=null,WorkTime? Time=null,string? Id=null,int Revision=0,string? Csv=null,byte[]? Excel=null,WorkPhase? Phase=null,string? Category=null);
