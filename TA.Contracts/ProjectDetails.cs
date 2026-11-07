namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Vista completa della commessa, indipendente dai filtri temporali del calendario.
public sealed record ProjectDetails(WorkProject Project,Customer? Customer,WorkPhase[] Phases,WorkActivity[] Activities,
    Customer[] Customers,string[] Collaborators,bool CanManage);
