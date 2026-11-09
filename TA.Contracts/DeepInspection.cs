namespace TA_Gestione_Cartelle_Commesse.Contracts;
public sealed record DeepInspectionQuery(string Action="avvia",string FolderId="",string JobId="",string Terms="",int Offset=0,int Limit=50);
