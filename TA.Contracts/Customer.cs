namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Dalla 2.40.0 il catalogo dei clienti è quello di GeCo (Origin «GeCo», Id «geco:<Cli_Id>», ShortName = nome breve usato nei
// nomi delle cartelle); i clienti dell'archivio locale non ritrovati in GeCo restano consultabili come storico (Origin vuota).
public sealed record Customer(string Id,string Name,string Vat="",string TaxCode="",string Address="",string Email="",int Revision=0,CustomerSource[]? Sources=null,
    string? ShortName=null,string Origin="")
{
    public const string GeCoOrigin="GeCo";
    public bool FromGeCo=>Origin==GeCoOrigin;
    public bool Matches(string query)=>query.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
        .All(term=>string.Join(' ',Name,Vat,TaxCode,Address,Email).Contains(term,StringComparison.OrdinalIgnoreCase));
}
