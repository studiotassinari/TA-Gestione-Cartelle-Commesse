namespace TA_Gestione_Cartelle_Commesse.Contracts;

public sealed record CustomerSource(string Provider,string ExternalId,string Name,string Vat,string TaxCode,DateTime ReadUtc);
