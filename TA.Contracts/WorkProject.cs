namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Entità gestionale: i metadati si modificano nel database, mai rinominando le cartelle originali.
public sealed record WorkProject(string Id,string Code,string Title,string CustomerId="",string HoursMode="Blocked",string Evidence="",string Reason="",DateOnly? Until=null,int Revision=0,
    int Year=0,int Number=0,string Subject="",string Location="",string Status="",string SourceCustomerName="",DateTime? LastActivityUtc=null,string Category="C");
