using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;

// Analisi automatica locale dei candidati geologici: legge solo la parte iniziale del documento.
public sealed partial class GeologyService
{
    private static bool LocalFormat(string name) => Path.GetExtension(name).ToLowerInvariant() is ".pdf" or ".docx" or ".txt";
    public static bool Direct(GeologicalReport r) => r.DetectionEvidence == GeologyClassifier.DirectEvidence;
    // Candidato che l'analisi automatica può leggere e che non ha ancora un'analisi valida per la versione attuale del documento.
    public static bool Queued(GeologicalReport r) => r.OriginalAvailable && !r.AnalysisCurrent && r.SourceFingerprint.Length == 64 && !string.IsNullOrWhiteSpace(r.PhysicalPath) && r.Kind != "Elenco elaborati" && LocalFormat(r.FileName);
    // Riga di stato del catalogo: dice se l'analisi automatica è attiva, quanto ha fatto e quanto resta, così l'avanzamento
    // si vede dal client senza aprire il registro del servizio.
    public static string AnalysisStatus(LocalAnalysisLimits limits, IReadOnlyCollection<GeologicalReport> reports)
    {
        if (!limits.Enabled) return "Analisi automatica locale non attiva: i contenuti vengono letti solo su richiesta. Si attiva in Gestore server > Configura.";
        int analyzed = reports.Count(r => r.AnalysisCurrent), queued = reports.Count(Queued), direct = reports.Count(r => Queued(r) && Direct(r));
        int hours = limits.StartHour == limits.EndHour ? 24 : (limits.EndHour - limits.StartHour + 24) % 24;
        return $"{limits.Describe()}. Documenti con analisi: {analyzed} su {reports.Count}; in coda per l'analisi automatica: {queued}, di cui {direct} con il termine nel nome del file (analizzati per primi). " +
            $"Capacità massima: circa {limits.DocumentsPerHour * hours} documenti al giorno, condivisa con il catalogo MUR; i documenti senza testo estraibile (scansioni) vengono messi da parte.";
    }
    // Prossimo candidato senza analisi attuale; le analisi salvate su richiesta non vengono sostituite.
    internal GeologicalReport? NextLocal(Func<string, string, bool> skipped, CancellationToken token)
    {
        var catalog = ReadCatalog(token).Catalog; var saved = Analyses.Read().ToDictionary(r => r.Analysis.ReportId, StringComparer.Ordinal);
        // Prima i documenti con «geologica» o «geotecnica» nel nome: quelli trovati solo per la cartella in cui stanno
        // sono molti di più e quasi mai sono relazioni, quindi non devono tenere in coda gli altri.
        return catalog.Reports.Where(Queued)
            .Where(r => !saved.TryGetValue(r.Id, out var record) || record.SavedBy == LocalAnalysis.Author)
            .Where(r => !skipped(r.Id, r.SourceFingerprint))
            .OrderByDescending(Direct).ThenByDescending(r => AnalysisOrder.Year(r.Year, r.ProjectCode)).ThenByDescending(r => AnalysisOrder.Number(r.ProjectCode)).ThenBy(r => r.Id, StringComparer.Ordinal)
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
