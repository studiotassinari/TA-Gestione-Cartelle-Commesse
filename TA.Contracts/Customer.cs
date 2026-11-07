namespace TA_Gestione_Cartelle_Commesse.Contracts;

public sealed record Customer(string Id,string Name,string Vat="",string TaxCode="",string Address="",string Email="",int Revision=0,CustomerSource[]? Sources=null)
{
    public bool Matches(string query)=>query.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
        .All(term=>string.Join(' ',Name,Vat,TaxCode,Address,Email).Contains(term,StringComparison.OrdinalIgnoreCase));
}
