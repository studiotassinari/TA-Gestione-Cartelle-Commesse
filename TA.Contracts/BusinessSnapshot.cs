namespace TA_Gestione_Cartelle_Commesse.Contracts;

public sealed record BusinessSnapshot(string User,bool CanManage,Customer[] Customers,WorkProject[] Projects,WorkActivity[] Activities,WorkTime[] Times,string[] Collaborators,string BackupStatus,WorkPhase[]? Phases=null);
