namespace TA_Gestione_Cartelle_Commesse.Contracts;
// Dati della schermata iniziale, indipendenti da attività, consuntivi e periodo Gantt.
public sealed record ProjectCatalog(WorkProject[] Projects,Customer[] Customers,bool CanManage);
