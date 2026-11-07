namespace TA_Gestione_Cartelle_Commesse.Contracts;

public sealed record CustomerImportResult(int Read,int Added,int Linked,string[] Issues,int Skipped=0);
