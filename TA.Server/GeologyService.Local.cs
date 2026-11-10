using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;

// Analisi automatica locale dei candidati geologici: legge solo la parte iniziale del documento.
public sealed partial class GeologyService
{
    private static bool LocalFormat(string name) => Path.GetExtension(name).ToLowerInvariant() is ".pdf" or ".docx" or ".txt";
    // Prossimo candidato senza analisi attuale; le analisi salvate su richiesta non vengono sostituite.
    internal GeologicalReport? NextLocal(Func<string, string, bool> skipped, CancellationToken token)
    {
        var catalog = ReadCatalog(token).Catalog; var saved = Analyses.Read().ToDictionary(r => r.Analysis.ReportId, StringComparer.Ordinal);
        return catalog.Reports
            .Where(r => r.OriginalAvailable && !r.AnalysisCurrent && r.SourceFingerprint.Length == 64 && !string.IsNullOrWhiteSpace(r.PhysicalPath) && r.Kind != "Elenco elaborati" && LocalFormat(r.FileName))
            .Where(r => !saved.TryGetValue(r.Id, out var record) || record.SavedBy == LocalAnalysis.Author)
            .Where(r => !skipped(r.Id, r.SourceFingerprint))
            .OrderByDescending(r => AnalysisOrder.Year(r.Year, r.ProjectCode)).ThenByDescending(r => AnalysisOrder.Number(r.ProjectCode)).ThenBy(r => r.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
    // null = testo insufficiente (per esempio scansione): nessuna analisi salvata.
    internal async Task<GeologicalAnalysisRecord?> AnalyzeLocalAsync(GeologicalReport candidate, CancellationToken token)
    {
        var page = (await documents.ExecuteAutomaticAsync(ResolveDocument(settings, candidate, new GeologicalReadQuery(candidate.Id, 0, 24000)), token)).Document
            ?? throw new IOException("Testo del documento non disponibile.");
        var previous = Analyses.Read().SingleOrDefault(r => r.Analysis.ReportId == candidate.Id);
        // Un salvataggio su richiesta arrivato nel frattempo ha la precedenza.
        if (previous != null && previous.SavedBy != LocalAnalysis.Author) return previous;
        var draft = GeologyLocalAnalyzer.Analyze(candidate, page.Version, page.Text, page.Warnings, previous);
        if (draft == null) return null;
        ValidateAnalysis(draft);
        if (!GeologyLocalAnalyzer.QuotesMatch(draft, page.Text)) throw new InvalidDataException("Citazione automatica non riscontrata nel testo letto.");
        var latest = ReadCatalog(token).Catalog.Reports.SingleOrDefault(r => r.Id == candidate.Id);
        if (latest?.SourceFingerprint != draft.SourceFingerprint) throw new IOException("Il censimento è cambiato durante l'analisi automatica.");
        return Analyses.Save(candidate, draft, LocalAnalysis.Author, token);
    }
}
