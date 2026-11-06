using System.Security.Cryptography;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;

namespace TA.Server;

// Un indice atomico è il punto di commit. Le revisioni per anno/commessa restano immutabili.
public sealed class GeologyAnalysisStore(ServerSettings settings)
{
    private string Root => Path.Combine(GeologyStorage.Root(settings), "analisi");
    private string Index => Path.Combine(Root, "indice.json");
    public List<GeologicalAnalysisRecord> Read()
    {
        if (!CatalogStorage.Exists(settings,Index)) return [];
        if (CatalogStorage.Bytes(settings,Index).Length > 32 * 1024 * 1024) throw new IOException("Indice analisi oltre 32 MiB.");
        var index = CatalogStorage.Read<GeologicalAnalysisIndex>(settings,Index);
        if (index.SchemaVersion != 1 || index.Records == null || index.Records.Count > 50000 ||
            index.Records.Any(r => r == null || r.Analysis == null) ||
            index.Records.Select(r => r.Analysis.ReportId).Distinct(StringComparer.Ordinal).Count() != index.Records.Count)
            throw new InvalidDataException("Indice delle analisi non valido; dati precedenti conservati.");
        return index.Records;
    }
    public GeologicalAnalysisRecord Save(GeologicalReport report, GeologicalAnalysisDraft draft, string user, CancellationToken token)
    {

        using var gate = CatalogStorage.Lock(settings,Index + ".lock");
        var records = Read(); var previous = records.SingleOrDefault(r => r.Analysis.ReportId == report.Id);
        if ((previous?.Revision ?? "") != draft.ExpectedRevision) throw new IOException("Analisi aggiornata da un altro utente: rileggere il catalogo prima di salvare.");
        // Il client sceglie solo un fascicolo noto oppure ne richiede uno nuovo, mai un percorso su disco.
        var dossier = string.IsNullOrWhiteSpace(draft.DossierId) ? Guid.NewGuid().ToString("N") : draft.DossierId;
        if (!Guid.TryParseExact(dossier, "N", out _) || (!string.IsNullOrWhiteSpace(draft.DossierId) && !records.Any(r => r.Analysis.DossierId == dossier)))
            throw new ArgumentException("Fascicolo sconosciuto: usare un ID restituito da elenca_fascicoli_geologici oppure lasciare vuoto.");
        var revision = Guid.NewGuid().ToString("N");
        var row = new GeologicalAnalysisRecord(revision, DateTime.UtcNow, user, report.Year, report.ProjectCode, draft with { DossierId = dossier }, report);
        records.RemoveAll(r => r.Analysis.ReportId == report.Id); records.Add(row);
        var index = new GeologicalAnalysisIndex(1, records);
        if (JsonSerializer.SerializeToUtf8Bytes(index).Length > 32 * 1024 * 1024) throw new IOException("Indice analisi oltre il limite; nessun dato sostituito.");
        static string Segment(string value, string fallback) => !string.IsNullOrWhiteSpace(value) && value.Length <= 40 && value.All(char.IsAsciiLetterOrDigit) ? value : fallback;
        var id = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(report.Id)));
        var directory = Path.Combine(Root, Segment(report.Year, "senza-anno"), Segment(report.ProjectCode, "senza-commessa"), id);
        token.ThrowIfCancellationRequested();
        CatalogStorage.Write(settings,Path.Combine(directory, "revisioni", "current.json"), row);
        // Il file della revisione è scritto prima dell'indice: un'interruzione non espone una revisione incompleta.
        var members = records.Where(r => r.Analysis.DossierId == dossier).ToList();
        CatalogStorage.Write(settings,Path.Combine(GeologyStorage.Root(settings), "fascicoli", dossier, "revisioni", "current.json"),
            new { SchemaVersion = 1, Id = dossier, Title = draft.DossierTitle, SavedUtc = row.SavedUtc, SavedBy = user,
                Documents = members.Select(r => new { r.Analysis.ReportId, r.Revision, r.Analysis.DocumentVersion, r.Analysis.SourceFingerprint, r.Analysis.DossierReason }).ToList() });
        CatalogStorage.Write(settings,Index, index);
        return row;
    }
}
