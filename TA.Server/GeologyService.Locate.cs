using System.Security.Cryptography;
using System.Text;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;

public sealed record GeologyLocateRequest(string ReportId, string Query, string Municipality, string Address, string Accuracy);

// Ricerca automatica della posizione delle relazioni, se abilitata in Configura. È l'unico punto in cui il servizio
// contatta da solo un servizio esterno (OpenStreetMap Nominatim): vengono inviati soltanto località e indirizzo, mai
// documenti, nomi di file o di committenti. Le posizioni indicate a mano non vengono mai sostituite e ogni posizione
// trovata resta una proposta da verificare.
public sealed partial class GeologyService
{
    public const string AutomaticLocator = "Ricerca automatica della posizione";
    private string AttemptsPath => Path.Combine(GeologyStorage.Root(settings), "ricerche-automatiche-posizione.json");

    private static string Clean(string? text, int max = 200)
    {
        var clean = string.Join(' ', new string((text ?? "").Where(c => !char.IsControl(c) || c == ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length > max ? clean[..max] : clean;
    }
    // Località della commessa dal nome della cartella «CODICE - COMMITTENTE - LUOGO - OGGETTO».
    public static string FolderPlace(GeologicalReport report)
    {
        if (report.ProjectCode.Length == 0) return "";
        foreach (var segment in report.PhysicalPath.Split('\\', '/'))
        {
            if (!segment.StartsWith(report.ProjectCode + " ", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = segment.Split(" - ", StringSplitOptions.TrimEntries);
            return parts.Length >= 4 && parts[2].Length is >= 2 and <= 80 && parts[2].Any(char.IsLetter) ? parts[2] : "";
        }
        return "";
    }
    // Che cosa cercare per una relazione: l'indirizzo o il comune letti dal documento, se c'è un'analisi valida;
    // altrimenti la località della commessa dal nome della cartella. null = nessun dato da cercare.
    public static GeologyLocateRequest? LocateRequest(GeologicalReport report)
    {
        string municipality = Clean(report.Municipality), address = Clean(report.Address);
        if (report.AnalysisCurrent && (municipality.Length > 0 || address.Length > 0))
            return new(report.Id, string.Join(", ", new[] { address, municipality, "Italia" }.Where(s => s.Length > 0)), municipality, address,
                address.Length > 0 ? "Indirizzo letto dal documento: posizione proposta dalla ricerca automatica, da verificare" : "Comune letto dal documento: posizione approssimata dalla ricerca automatica, da verificare");
        var place = Clean(FolderPlace(report));
        return place.Length == 0 ? null : new(report.Id, place + ", Italia", place, "", "Località dal nome della cartella della commessa: posizione approssimata dalla ricerca automatica, da verificare");
    }
    private static string QueryKey(string query) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query.ToUpperInvariant())))[..24];
    private Dictionary<string, string> ReadAttempts() => CatalogStorage.Exists(settings, AttemptsPath) ? CatalogStorage.Read<Dictionary<string, string>>(settings, AttemptsPath) : new(StringComparer.Ordinal);

    // Relazioni ancora da cercare: senza posizione, oppure con una posizione automatica nata da un dato meno preciso di
    // quello disponibile ora. Prima quelle con indirizzo o comune letti dal documento, poi le più recenti.
    internal List<GeologyLocateRequest> NextToLocate(int max, CancellationToken token)
    {
        var catalog = ReadCatalog(token).Catalog; var attempts = ReadAttempts();
        var automatic = ReadPositions().Where(p => p.UpdatedBy == AutomaticLocator).Select(p => p.ReportId).ToHashSet(StringComparer.Ordinal);
        return catalog.Reports.Where(r => !r.HasPosition || automatic.Contains(r.Id))
            .OrderByDescending(r => r.AnalysisCurrent).ThenByDescending(r => AnalysisOrder.Year(r.Year, r.ProjectCode)).ThenByDescending(r => AnalysisOrder.Number(r.ProjectCode))
            .Select(LocateRequest).Where(q => q != null && (!attempts.TryGetValue(q.ReportId, out var done) || done != QueryKey(q.Query))).Select(q => q!)
            .Take(max).ToList();
    }
    // Esegue una ricerca e, se trova un risultato, salva la posizione proposta. Restituisce se la risposta veniva dalla
    // memoria locale (nessuna richiesta in rete) e se una posizione è stata salvata.
    internal async Task<(bool Saved, bool FromCache)> LocateAsync(GeologyLocateRequest request, CancellationToken token)
    {
        var result = await new NominatimGeocoder(GeologyStorage.Root(settings), settings.NominatimSearchUrl).SearchAsync(request.Query, token);
        var best = result.Candidates.FirstOrDefault(); bool saved = false;
        using var writer = CatalogStorage.Lock(settings, PositionsPath + ".lock");
        var positions = ReadPositions(); var existing = positions.FirstOrDefault(p => p.ReportId == request.ReportId);
        // Una posizione indicata da una persona ha sempre la precedenza.
        if (best != null && (existing == null || existing.UpdatedBy == AutomaticLocator))
        {
            positions.RemoveAll(p => p.ReportId == request.ReportId);
            positions.Add(new(request.ReportId, best.Latitude, best.Longitude, request.Municipality.Length > 0 ? request.Municipality : Clean(best.Municipality), request.Address,
                "OpenStreetMap Nominatim: " + Clean(best.Label, 300), DateTime.UtcNow, AutomaticLocator, request.Accuracy));
            CatalogStorage.Write(settings, PositionsPath, new GeologicalPositions(positions)); saved = true;
        }
        // Anche una ricerca senza risultati viene ricordata: non si ripete finché il dato da cercare non cambia.
        var attempts = ReadAttempts(); attempts[request.ReportId] = QueryKey(request.Query);
        CatalogStorage.Write(settings, AttemptsPath, attempts);
        return (saved, result.FromCache);
    }
    // Dato che il servizio non accetta: ricordato come tentato, così non viene riproposto a ogni giro.
    internal void SkipLocate(GeologyLocateRequest request)
    {
        using var writer = CatalogStorage.Lock(settings, PositionsPath + ".lock");
        var attempts = ReadAttempts(); attempts[request.ReportId] = QueryKey(request.Query);
        CatalogStorage.Write(settings, AttemptsPath, attempts);
    }
    public static string LocateStatus(bool enabled, IReadOnlyCollection<GeologicalReport> reports) => !enabled ? ""
        : $" Ricerca automatica della posizione attiva (OpenStreetMap Nominatim, solo località e indirizzo): {reports.Count(r => r.HasPosition && r.PositionAccuracy.Contains(AutomaticLocator, StringComparison.Ordinal))} posizioni proposte, da verificare.";
}

// Una ricerca alla volta, con una pausa ampia rispetto al limite del servizio pubblico (una richiesta al secondo).
public sealed class GeologyLocatorService(ServerSettings settings, GeologyService geology, ILogger<GeologyLocatorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.EnableAutomaticGeocoding) { logger.LogInformation("Ricerca automatica della posizione disattivata: le posizioni si cercano solo su richiesta"); return; }
        logger.LogInformation("Ricerca automatica della posizione attiva: a OpenStreetMap Nominatim vengono inviati solo località e indirizzo");
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var pause = TimeSpan.FromMinutes(15);
                try
                {
                    var batch = await Task.Run(() => geology.NextToLocate(60, stoppingToken), stoppingToken);
                    int saved = 0;
                    foreach (var request in batch)
                    {
                        try
                        {
                            var outcome = await geology.LocateAsync(request, stoppingToken);
                            if (outcome.Saved) saved++;
                            await Task.Delay(outcome.FromCache ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(5), stoppingToken);
                        }
                        // Dato non cercabile (per esempio troppo lungo): la relazione resta da localizzare a mano.
                        catch (ArgumentException ex) { geology.SkipLocate(request); logger.LogInformation("Ricerca automatica della posizione saltata per {Report}: {Reason}", request.ReportId, ex.Message); }
                    }
                    if (batch.Count > 0) { logger.LogInformation("Ricerca automatica della posizione: {Saved} posizioni proposte su {Count} ricerche", saved, batch.Count); pause = TimeSpan.FromSeconds(5); }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Ricerca automatica della posizione sospesa; nuovo tentativo tra quindici minuti"); }
                await Task.Delay(pause, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
