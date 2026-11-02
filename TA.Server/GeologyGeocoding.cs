using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;
public sealed partial class GeologyService
{
    public Task<GeocodingResult> GeocodeAsync(GeologicalGeocodeQuery query, CancellationToken token)
    {
        var (catalog, version) = ReadCatalog(token);
        if (query.Version != version) throw new IOException("Catalogo cambiato: aggiornare l'elenco prima di localizzare.");
        if (!catalog.Reports.Any(r => r.Id == query.ReportId)) throw new KeyNotFoundException("Relazione non presente nel catalogo.");
        return new NominatimGeocoder(GeologyStorage.Root(settings), settings.NominatimSearchUrl).SearchAsync(query.Address, token);
    }
}
