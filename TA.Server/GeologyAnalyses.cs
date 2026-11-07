using System.Security.Cryptography;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Server;

// ChatGPT interpreta il documento; il server controlla versione, citazioni e destinazione dei risultati.
public sealed partial class GeologyService
{
    private readonly GeologyReadEvidence evidence = new();
    private GeologyAnalysisStore Analyses => new(settings);

    public GeologicalAnalysisQueue Pending(GeologicalAnalysisQueueQuery query, CancellationToken token)
    {
        if (query.Code == null || query.Search == null || query.Code.Length > 40 || query.Search.Length > 300 || query.Limit is < 1 or > 10)
            throw new ArgumentException("Filtri non validi: massimo dieci documenti per gruppo di analisi.");
        var (catalog, _) = ReadCatalog(token);
        var pending = catalog.Reports.Where(r => !r.AnalysisCurrent && r.CanRead &&
            r.ProjectCode.Contains(query.Code, StringComparison.OrdinalIgnoreCase) &&
            string.Join(' ', r.Title, r.FileName, r.RelativePath).Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r=>AnalysisOrder.Year(r.Year,r.ProjectCode)).ThenByDescending(r=>r.ProjectCode,StringComparer.Ordinal)
            .ThenBy(r=>r.Kind=="Elenco elaborati"?0:1).ThenByDescending(r => r.SourceModifiedUtc).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        return new(catalog.UpdatedUtc, pending.Count, pending.Take(1).ToList(), catalog.Warnings.Concat(new[]{"Un documento per volta, dalle commesse più recenti. Completare e salvare l'analisi prima del prossimo file; almeno 60 secondi tra nuovi documenti."}).ToList());
    }

    public async Task<GeologicalAnalysisRecord> SaveAnalysisAsync(GeologicalAnalysisDraft draft, string user, CancellationToken token)
    {
        if (!settings.EnableMcpGeologyWrites) throw new UnauthorizedAccessException("Salvataggio analisi non abilitato sul server (EnableMcpGeologyWrites).");
        ValidateAnalysis(draft);
        var report = ReadCatalog(token).Catalog.Reports.SingleOrDefault(r => r.Id == draft.ReportId) ?? throw new KeyNotFoundException("Relazione assente dal catalogo.");
        if (report.SourceFingerprint.Length != 64 || draft.SourceFingerprint != report.SourceFingerprint)
            throw new IOException("Documento ricensito o metadati precedenti non disponibili: rileggere la voce aggiornata del catalogo.");
        var warnings = evidence.Validate(report.Id, draft);
        // La verifica usa il checksum del contenuto, oltre ai metadati censiti: non accetta analisi di file già cambiati.
        await ReadAsync(new(report.Id, 0, 1, draft.DocumentVersion), token);
        var latest = ReadCatalog(token).Catalog.Reports.SingleOrDefault(r => r.Id == draft.ReportId);
        if (latest?.SourceFingerprint != draft.SourceFingerprint) throw new IOException("Il censimento è cambiato durante il salvataggio.");
        return Analyses.Save(report, draft with { Warnings = draft.Warnings.Concat(warnings).Distinct().Take(30).ToList() }, user, token);
    }

    public static void ValidateAnalysis(GeologicalAnalysisDraft d)
    {
        static bool Hash(string? s) => s?.Length == 64 && s.All(Uri.IsHexDigit);
        if (d == null || string.IsNullOrWhiteSpace(d.ReportId) || d.ReportId.Length > 128 || !Hash(d.SourceFingerprint) || !Hash(d.DocumentVersion) ||
            new[] { d.Title, d.Kind, d.Summary, d.Author, d.Municipality, d.Address, d.PositionAccuracy, d.PositionSource, d.DossierId, d.DossierTitle, d.DossierReason, d.ExpectedRevision, d.ExternalPositionSource }.Any(s => s == null || s.Length > 3000) ||
            string.IsNullOrWhiteSpace(d.Title) || string.IsNullOrWhiteSpace(d.DossierTitle) || string.IsNullOrWhiteSpace(d.DossierReason) ||
            d.Kind is not ("Geologica" or "Geotecnica" or "Geologica e geotecnica" or "Allegato" or "Non pertinente") ||
            d.Evidence == null || d.Evidence.Count is < 1 or > 16 || d.Warnings == null || d.Warnings.Count > 20 || d.Warnings.Any(s => s == null || s.Length > 500) ||
            d.Evidence.Any(e => e == null || e.Field is not ("classificazione" or "localita" or "coordinate" or "autore" or "fascicolo" or "sintesi") || e.Offset < 0 || e.Offset > 200000 || e.Quote == null || e.Quote.Length is < 8 or > 600) ||
            !d.Evidence.Any(e => e.Field == "classificazione") || !d.Evidence.Any(e => e.Field == "fascicolo"))
            throw new ArgumentException("Analisi non valida: indicare documento/versione, classificazione, fascicolo e citazioni testuali con offset.");
        if ((!string.IsNullOrWhiteSpace(d.Municipality) || !string.IsNullOrWhiteSpace(d.Address)) && !d.Evidence.Any(e => e.Field == "localita"))
            throw new ArgumentException("La località richiede una citazione dal documento; non usare la sede dell'autore come sito del progetto.");
        if (d.Latitude.HasValue != d.Longitude.HasValue || (d.Latitude.HasValue &&
            (d.Latitude is not (>= -90 and <= 90) || d.Longitude is not (>= -180 and <= 180) || string.IsNullOrWhiteSpace(d.PositionSource) || string.IsNullOrWhiteSpace(d.PositionAccuracy))))
            throw new ArgumentException("Coordinate incomplete o non valide: indicare fonte e precisione oppure lasciare entrambe vuote.");
        if (d.Latitude.HasValue && !d.Evidence.Any(e => e.Field == "coordinate"))
        {
            if (!d.Evidence.Any(e => e.Field == "localita") || !Uri.TryCreate(d.ExternalPositionSource, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.IsLoopback)
                throw new ArgumentException("La posizione proposta richiede coordinate citate nel documento oppure una fonte geografica HTTPS e la località citata. Non inventare coordinate.");
        }
    }

    private (GeologyCatalog Catalog, byte[] Bytes) MergeAnalyses(GeologyCatalog catalog, byte[] bytes)
    {
        var records = Analyses.Read(); var byId = records.ToDictionary(r => r.Analysis.ReportId, StringComparer.Ordinal);
        var present = catalog.Reports.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var retained = records.Where(r => !present.Contains(r.Analysis.ReportId) && r.Source != null)
            .Select(r => r.Source! with { OriginalAvailable = false, SourceFingerprint = "" });
        var reports = catalog.Reports.Concat(retained).Select(r => {
            if (!byId.TryGetValue(r.Id, out var saved)) return r;
            var a = saved.Analysis; bool current = r.SourceFingerprint.Length == 64 && a.SourceFingerprint == r.SourceFingerprint;
            var annotated = r with { AnalysisRevision = saved.Revision, AnalysisSavedUtc = saved.SavedUtc, AnalysisCurrent = current, DossierId = a.DossierId,
                DossierTitle = a.DossierTitle, AnalysisStatus = !r.OriginalAvailable ? "Originale non presente nell’inventario — analisi conservata" : current ? "Analizzata da ChatGPT — da verificare" : "Documento modificato — analisi da aggiornare" };
            // I dati automatici superati non diventano una nuova posizione certa. Le posizioni manuali sono applicate dopo.
            if (!current) return annotated;
            return annotated with { Title = a.Title, Kind = a.Kind, Author = a.Author, Municipality = a.Municipality, Address = a.Address,
                Latitude = a.Latitude, Longitude = a.Longitude, PositionSource = a.PositionSource + (a.ExternalPositionSource.Length > 0 ? " · " + a.ExternalPositionSource : ""),
                PositionAccuracy = a.Latitude.HasValue ? "Proposta ChatGPT da verificare · " + a.PositionAccuracy : "",
                DetectionStatus = annotated.AnalysisStatus, Notes = a.Summary + "\n" + string.Join(" · ", a.Warnings) };
        }).ToList();
        var updated = records.Select(r => r.SavedUtc).Append(catalog.UpdatedUtc).Max();
        return (catalog with { Reports = reports, UpdatedUtc = updated }, bytes.Concat(JsonSerializer.SerializeToUtf8Bytes(records)).ToArray());
    }

    public GeologicalDossierPage Dossiers(GeologicalDossierQuery query, CancellationToken token)
    {
        if (query.Search == null || query.Search.Length > 300 || query.Offset < 0 || query.Limit is < 1 or > 100 || query.Offset > int.MaxValue - query.Limit || query.Offset > 0 && query.Version == null)
            throw new ArgumentException("Pagina fascicoli non valida.");
        var (catalog, version) = ReadCatalog(token);
        if (query.Version != null && query.Version != version) throw new IOException("Catalogo cambiato: ripartire dalla prima pagina.");
        var dossiers = catalog.Reports.Where(r => r.DossierId.Length > 0).GroupBy(r => r.DossierId).Select(group => {
            var rows = group.ToList(); var located = rows.Where(r => r.AnalysisCurrent && r.HasPosition).ToList();
            var points = located.Select(r => (r.Latitude, r.Longitude)).Distinct().ToList(); var one = points.Count == 1 ? located[0] : null;
            var warnings = new List<string> { "Fascicolo proposto da ChatGPT: omogeneità da verificare." };
            if (points.Count > 1) warnings.Add("Posizioni discordanti: il fascicolo non ha una coordinata unica.");
            return new GeologicalDossier(group.Key, rows[0].DossierTitle, rows.Select(r => r.Id).ToList(), rows.Select(r => r.ProjectCode).Where(s => s.Length > 0).Distinct().ToList(),
                one?.Municipality ?? "", one?.Address ?? "", one?.Latitude, one?.Longitude, one?.PositionAccuracy ?? "Posizione da verificare", rows.Count(r => !r.AnalysisCurrent), rows.Max(r => r.AnalysisSavedUtc) ?? catalog.UpdatedUtc, warnings);
        }).Where(d => string.Join(' ', d.Title, d.Municipality, d.Address, string.Join(' ', d.ProjectCodes)).Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Title, StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
        return new(version, dossiers.Count, query.Offset + query.Limit < dossiers.Count ? query.Offset + query.Limit : null, dossiers.Skip(query.Offset).Take(query.Limit).ToList());
    }
}
