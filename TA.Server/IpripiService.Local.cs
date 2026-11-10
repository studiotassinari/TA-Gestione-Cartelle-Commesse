using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;

// Analisi automatica locale dei candidati MUR: stesse verifiche di versione e citazioni del salvataggio su richiesta.
public sealed partial class IpripiService
{
    // Prossimo candidato mai analizzato, oppure con analisi locale superata da una modifica del documento.
    // Le analisi salvate su richiesta non vengono mai sostituite dall'analisi automatica.
    internal IpripiDocument? NextLocal(Func<string, string, bool> skipped)
    {
        var path = Path.Combine(Root, "candidati.json");
        if (!CatalogStorage.Exists(settings, path)) return null;
        var saved = ReadIndex().ToDictionary(r => r.Source.Id);
        return CatalogStorage.Read<IpripiCandidateCatalog>(settings, path).Documents
            .Where(r => r.OriginalAvailable && (r.Category == "C" || r.Category == "A") && IsCandidate(r.FileName))
            .Where(r => r.Category != "A" || ArchiveScope.Contains(settings.ArchivedJobsRoot, r.PhysicalPath))
            .Where(r => !saved.TryGetValue(r.Id, out var record) || record.SavedBy == LocalAnalysis.Author && record.Analysis.SourceFingerprint != r.SourceFingerprint)
            .Where(r => !skipped(r.Id, r.SourceFingerprint))
            .OrderByDescending(r => AnalysisOrder.Year(r.Year, r.ProjectCode)).ThenByDescending(r => AnalysisOrder.Number(r.ProjectCode)).ThenBy(r => r.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
    internal async Task<IpripiAnalysisRecord> AnalyzeLocalAsync(IpripiDocument candidate, CancellationToken token)
    {
        var source = Find(candidate.Id, "", token);
        var report = new GeologicalReport { Id = source.Id, PhysicalPath = source.PhysicalPath, ArchiveEntry = source.ArchiveEntry };
        async Task<DocumentPage> Read(int offset, string? version) =>
            (await documents.ExecuteAutomaticAsync(GeologyService.ResolveDocument(settings, report, new GeologicalReadQuery(source.Id, offset, 24000, version)), token)).Document
                ?? throw new IOException("Testo del documento non disponibile.");
        // Al massimo due porzioni (48.000 caratteri): un modulo è breve e ogni porzione costa un processo di lettura.
        var first = await Read(0, null); var text = first.Text; bool partial = first.Truncated;
        if (first.NextOffset is { } next)
        {
            var second = await Read(next, first.Version);
            text += second.Text; partial |= second.NextOffset != null || second.Truncated;
        }
        var previous = ReadIndex().SingleOrDefault(r => r.Source.Id == source.Id);
        var draft = IpripiLocalAnalyzer.Analyze(source, first.Version, text, partial, first.Warnings, previous?.Revision ?? "");
        Validate(draft);
        if (!IpripiLocalAnalyzer.QuotesMatch(draft, text)) throw new InvalidDataException("Citazione automatica non riscontrata nel testo letto.");
        if (Find(source.Id, "", token).SourceFingerprint != draft.SourceFingerprint) throw new IOException("Inventario cambiato durante l'analisi automatica.");

        using var gate = CatalogStorage.Lock(settings, Index + ".lock");
        var records = ReadIndex(); var current = records.SingleOrDefault(r => r.Source.Id == source.Id);
        // Un salvataggio su richiesta arrivato nel frattempo ha la precedenza.
        if ((current?.Revision ?? "") != draft.ExpectedRevision) throw new IOException("Analisi già aggiornata da un salvataggio su richiesta.");
        var record = new IpripiAnalysisRecord(Guid.NewGuid().ToString("N"), DateTime.UtcNow, LocalAnalysis.Author, source, draft);
        records.RemoveAll(r => r.Source.Id == source.Id); records.Add(record);
        var index = new IpripiAnalysisIndex(1, records);
        if (JsonSerializer.SerializeToUtf8Bytes(index).Length > 32 * 1024 * 1024) throw new IOException("Indice IPRiPI troppo grande: nessun dato sostituito.");
        token.ThrowIfCancellationRequested();
        CatalogStorage.Write(settings, Path.Combine(Root, "revisioni", Hash(source.Id), "current.json"), record);
        CatalogStorage.Write(settings, Index, index);
        return record;
    }
}
