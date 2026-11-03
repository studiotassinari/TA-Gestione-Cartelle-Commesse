using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TA.Server.Documents;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;

namespace TA.Server;

// Solo inventario JSON durante la ricerca. I contenuti sono aperti soltanto dalle richieste di lettura/salvataggio.
public sealed class IpripiService(ServerSettings settings, DocumentService documents)
{
    private readonly IpripiEvidenceStore evidence = new();
    public string Root
    {
        get
        {
            var root = Path.GetFullPath(Path.Combine(settings.ArchiveFolder, "ipripi"));
            foreach (var source in new[] { settings.JobsRoot, settings.BidsRoot, settings.ArchivedJobsRoot }.Where(s => s.Length > 0))
            {
                var full = Path.GetFullPath(source).TrimEnd('\\', '/');
                if (root.Equals(full, StringComparison.OrdinalIgnoreCase) || root.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("L'archivio IPRiPI deve essere esterno alle sorgenti.");
            }
            return root;
        }
    }
    private string Index => Path.Combine(Root, "indice.json");
    public static bool IsCandidate(string name) => Regex.IsMatch(name.Replace('_', ' '), @"\b(?:MUR|IPRIPI)\b|\bA[ ._-]*1[ ._/-]*D[ ._-]*1\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private (List<IpripiDocument> Rows, List<string> Warnings) Sources(string code, bool allPdfs, CancellationToken token, HashSet<string>? savedIds = null, string? documentId = null)
    {
        if (code == null || code.Length > 40) throw new ArgumentException("Codice commessa non valido.");
        var store = new InventoryStore(settings.ArchiveFolder);
        var run = InventoryAvailability.Load(store, code: code, token: token)
            ?? throw new IOException("Inventario non disponibile: non equivale ad assenza di MUR.");
        var watch = Stopwatch.StartNew(); var rows = new List<IpripiDocument>();
        var warnings = run.Issues.Take(10).ToList();
        foreach (var manifest in run.Inventories.Where(m => m.Unit.Category is "C" or "A")
            .Where(m => code.Length == 0 || m.Unit.Code.StartsWith(code, StringComparison.OrdinalIgnoreCase)))
        {
            if (!manifest.Complete && warnings.Count < 20) warnings.Add("Inventario parziale: " + manifest.Unit.Code);
            foreach (var entry in store.Entries(manifest))
            {
                token.ThrowIfCancellationRequested();
                if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new IOException("Ricerca troppo ampia: specificare la commessa.");
                if (entry.Kind == "directory") continue;
                var location = entry.Location ?? FileLocator.Locate(manifest.Unit, entry);
                if (!Supported(location.Extension) ||
                    documentId != null && location.Id != documentId ||
                    !allPdfs && !IsCandidate(location.Name) && savedIds?.Contains(location.Id) != true) continue;
                // Gli archivi e le date mancanti richiedono una nuova analisi dopo il ricensimento (scelta conservativa).
                var stamp = string.Join('|', location.Id, entry.Bytes, entry.ModifiedUtc?.ToString("O"),
                    entry.Container != null || entry.Bytes == null || entry.ModifiedUtc == null ? manifest.Generation : "");
                rows.Add(new(location.Id, manifest.Unit.Category, manifest.Unit.Id, manifest.Unit.Classification == "censita" ? manifest.Unit.Code : "",
                    manifest.Unit.Year, location.Name, location.PhysicalPath, location.InternalPath, Hash(stamp), manifest.Complete));
                if (rows.Count > 50000) throw new IOException("Troppi PDF: restringere la commessa.");
            }
        }
        warnings.Add("Candidati dai nomi: usare Tutti i PDF per i moduli rinominati. Nessuna assenza di interventi è deducibile dall'elenco.");
        return (rows.DistinctBy(r => r.Id).ToList(), warnings);
    }

    private List<IpripiAnalysisRecord> ReadIndex()
    {
        if (!File.Exists(Index)) return [];
        if (new FileInfo(Index).Length > 32 * 1024 * 1024) throw new IOException("Indice IPRiPI oltre 32 MiB.");
        var saved = InventoryStore.Read<IpripiAnalysisIndex>(Index);
        if (saved.SchemaVersion != 1 || saved.Records == null || saved.Records.Any(r => r == null || r.Source == null || r.Analysis == null) ||
            saved.Records.Select(r => r.Source.Id).Distinct().Count() != saved.Records.Count)
            throw new InvalidDataException("Indice IPRiPI non valido; dati precedenti conservati.");
        return saved.Records;
    }
    public static bool Supported(string extension) => extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);
    public HashSet<string> SavedIds() => ReadIndex().Select(r => r.Source.Id).ToHashSet();
    public static IpripiDocument Describe(InventoryManifest manifest, InventoryEntry entry)
    {
        var location = entry.Location ?? FileLocator.Locate(manifest.Unit, entry);
        var stamp = string.Join('|', location.Id, entry.Bytes, entry.ModifiedUtc?.ToString("O"),
            entry.Container != null || entry.Bytes == null || entry.ModifiedUtc == null ? manifest.Generation : "");
        return new(location.Id, manifest.Unit.Category, manifest.Unit.Id, manifest.Unit.Classification == "censita" ? manifest.Unit.Code : "",
            manifest.Unit.Year, location.Name, location.PhysicalPath, location.InternalPath, Hash(stamp), manifest.Complete, InventoryCode: manifest.Unit.Code);
    }
    public IpripiPage Query(IpripiQuery query, CancellationToken token)
    {
        if (query.Code == null || query.Code.Length > 40 || query.Search == null || query.Search.Length > 300 || query.Offset < 0 || query.Offset > 50000 || query.Limit is < 1 or > 100 || query.Offset > 0 && query.Version == null)
            throw new ArgumentException("Pagina IPRiPI non valida.");
        // Include anche analisi di PDF rinominati e documenti non più censiti, senza perderne lo storico.
        var saved = ReadIndex().Where(r => r.Source.Category is "C" or "A")
            .Where(r => query.Code.Length == 0 || r.Source.ProjectCode.StartsWith(query.Code, StringComparison.OrdinalIgnoreCase)).ToDictionary(r => r.Source.Id);
        var catalogPath = Path.Combine(Root, "candidati.json");
        List<IpripiDocument> sources; List<string> warnings;
        if (!query.AllPdfs && File.Exists(catalogPath))
        {
            var catalog = InventoryStore.Read<IpripiCandidateCatalog>(catalogPath);
            sources = catalog.Documents.Where(r => r.Category is "C" or "A").Where(r => query.Code.Length == 0 || r.ProjectCode.StartsWith(query.Code, StringComparison.OrdinalIgnoreCase)).ToList();
            warnings = catalog.Warnings.ToList();
            warnings.Insert(0, "Scansione MUR: " + catalog.CheckedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") + (catalog.Running ? " — in corso" : " — completata"));
        }
        else (sources, warnings) = Sources(query.Code, query.AllPdfs, token, saved.Keys.ToHashSet());
        var ids = sources.Select(r => r.Id).ToHashSet();
        var rows = sources.Where(r => query.AllPdfs || IsCandidate(r.FileName) || saved.ContainsKey(r.Id))
            .Select(r => r with { Saved = saved.GetValueOrDefault(r.Id) })
            .Concat(saved.Values.Where(s => !ids.Contains(s.Source.Id)).Select(s => s.Source with { Saved = s, OriginalAvailable = false }))
            .Where(r => !query.RecognizedOnly || r.FormType == "MUR A.1/D.1")
            .Where(r => string.Join(' ', r.ProjectCode, r.Year, r.FileName, r.Codes, r.Status, r.Designers, r.Typologies, r.Address, r.Municipality, r.Location).Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.ProjectCode, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        var version = Hash(JsonSerializer.Serialize(new { rows, warnings }));
        if (query.Version != null && query.Version != version) throw new IOException("Catalogo cambiato: ripartire dalla prima pagina.");
        return new(version, rows.Count, query.Offset, query.Offset + query.Limit < rows.Count ? query.Offset + query.Limit : null,
            rows.Skip(query.Offset).Take(query.Limit).ToList(), warnings);
    }
    private IpripiDocument Find(string id, string code, CancellationToken token)
    {
        var catalogPath = Path.Combine(Root, "candidati.json");
        if (File.Exists(catalogPath) && InventoryStore.Read<IpripiCandidateCatalog>(catalogPath).Documents.FirstOrDefault(r => r.Id == id) is { } cached)
        {
            if (cached.Category is not ("C" or "A")) throw new ArgumentException("Categoria non prevista per il catalogo MUR.");
            if (code.Length > 0 && !cached.ProjectCode.StartsWith(code, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Commessa diversa dal documento scelto.");
            var inventoryCode = cached.InventoryCode.Length > 0 ? cached.InventoryCode : cached.ProjectCode;
            if (inventoryCode.Length > 0)
            {
                var store = new InventoryStore(settings.ArchiveFolder);
                var unit = new InventoryUnit(cached.FolderId,cached.Category,cached.Year,inventoryCode,"","","","");
                var manifest = InventoryStore.Read<InventoryManifest>(Path.Combine(store.UnitDirectory(unit),"latest.json"));
                if (!store.UnitDirectory(unit).Equals(store.UnitDirectory(manifest.Unit),StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unità inventario MUR non coerente.");
                foreach (var entry in store.Entries(manifest))
                {
                    token.ThrowIfCancellationRequested();
                    var location = entry.Location ?? FileLocator.Locate(manifest.Unit,entry);
                    if (location.Id == id && Supported(location.Extension)) return Describe(manifest,entry);
                }
                throw new KeyNotFoundException("Documento non più censito nella sua unità.");
            }
        }
        return Sources(code, true, token, documentId: id).Rows.SingleOrDefault(r => r.Id == id)
            ?? throw new KeyNotFoundException("PDF/DOCX non presente nell'inventario: aggiornare la ricerca.");
    }
    public async Task<DocumentPage> ReadAsync(IpripiReadQuery query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query.DocumentId) || query.DocumentId.Length > 128) throw new ArgumentException("ID documento non valido.");
        var source = Find(query.DocumentId, query.Code, token);
        var page = await ReadSource(source, query, token);
        evidence.Remember(source.Id, page);
        return page;
    }
    private async Task<DocumentPage> ReadSource(IpripiDocument source, IpripiReadQuery query, CancellationToken token)
    {
        // Riutilizza il percorso confinato e il worker in sola lettura già collaudato, inclusi ZIP/7z.
        var report = new GeologicalReport { Id = source.Id, PhysicalPath = source.PhysicalPath, ArchiveEntry = source.ArchiveEntry };
        var work = GeologyService.ResolveDocument(settings, report, new(source.Id, query.Offset, query.Length, query.Version));
        return (await documents.ExecuteWorkAsync(work, token)).Document ?? throw new IOException("Testo PDF non disponibile.");
    }
    public async Task<IpripiAnalysisRecord> SaveAsync(IpripiAnalysisDraft draft, string user, CancellationToken token)
    {
        if (!settings.EnableMcpIpripiWrites) throw new UnauthorizedAccessException("Abilitare EnableMcpIpripiWrites sul server per salvare analisi IPRiPI.");
        Validate(draft);
        var source = Find(draft.DocumentId, draft.Code, token);
        if (source.SourceFingerprint != draft.SourceFingerprint) throw new IOException("Inventario cambiato: rileggere il PDF.");
        var warnings = evidence.Validate(draft);
        await ReadSource(source, new(source.Id, draft.Code, 0, 1, draft.DocumentVersion), token);
        if (Find(source.Id, draft.Code, token).SourceFingerprint != draft.SourceFingerprint) throw new IOException("Inventario cambiato durante il salvataggio.");
        Directory.CreateDirectory(Root);
        using var gate = new FileStream(Index + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var records = ReadIndex(); var previous = records.SingleOrDefault(r => r.Source.Id == source.Id);
        if ((previous?.Revision ?? "") != draft.ExpectedRevision) throw new IOException("Analisi già aggiornata: rileggere il catalogo.");
        var record = new IpripiAnalysisRecord(Guid.NewGuid().ToString("N"), DateTime.UtcNow, user, source,
            draft with { Warnings = draft.Warnings.Concat(warnings).Distinct().ToList() });
        records.RemoveAll(r => r.Source.Id == source.Id); records.Add(record);
        var index = new IpripiAnalysisIndex(1, records);
        if (JsonSerializer.SerializeToUtf8Bytes(index).Length > 32 * 1024 * 1024) throw new IOException("Indice IPRiPI troppo grande: nessun dato sostituito.");
        token.ThrowIfCancellationRequested();
        GeologyStorage.Atomic(Path.Combine(Root, "revisioni", Hash(source.Id), record.Revision + ".json"), record);
        GeologyStorage.Atomic(Index, index);
        return record;
    }
    public static void Validate(IpripiAnalysisDraft d)
    {
        static bool Text(string? s, int max) => s != null && s.Length <= max;
        static bool HashValue(string? s) => s?.Length == 64 && s.All(Uri.IsHexDigit);
        static bool Quotes(List<IpripiEvidence>? values) => values != null && values.Count <= 20 && values.All(e => e != null && e.Offset is >= 0 and <= 200000 && e.Quote != null && e.Quote.Length is >= 3 and <= 600);
        if (d == null || !Text(d.DocumentId,128) || !Text(d.Code,40) || !HashValue(d.SourceFingerprint) || !HashValue(d.DocumentVersion) ||
            !Text(d.ExpectedRevision,32) || !Text(d.Summary,3000) || string.IsNullOrWhiteSpace(d.Summary) ||
            d.FormType is not ("MUR A.1/D.1" or "Altro" or "Non leggibile") ||
            d.Outcome is not ("IPRiPI individuati" or "Da verificare" or "Nessun IPRiPI dichiarato" or "Non pertinente" or "Non leggibile") ||
            d.Interventions == null || d.Interventions.Count > 50 || !Quotes(d.FormEvidence) ||
            d.Warnings == null || d.Warnings.Count > 20 || d.Warnings.Any(s => !Text(s,500))) throw new ArgumentException("Analisi IPRiPI non valida.");
        foreach (var designer in d.Designers ?? [])
        {
            if (designer == null || !Text(designer.Name,200) || string.IsNullOrWhiteSpace(designer.Name) ||
                !Text(designer.Role,200) || string.IsNullOrWhiteSpace(designer.Role) || !Quotes(designer.Evidence) || designer.Evidence.Count == 0 ||
                !designer.Evidence.Any(e => e.Quote.Contains(designer.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Progettista privo di nome, ruolo o citazione del nominativo.");
        }
        if (d.Designers?.Count > 20) throw new ArgumentException("Troppi progettisti nel modulo.");
        if (d.Position is { } position && (!Text(position.Address,500) || !Text(position.Municipality,200) ||
            !Text(position.Source,1000) || string.IsNullOrWhiteSpace(position.Source) || !Quotes(position.Evidence) || position.Evidence.Count == 0 ||
            position.Latitude.HasValue != position.Longitude.HasValue ||
            position.Latitude is { } lat && (!double.IsFinite(lat) || lat < -90 || lat > 90) ||
            position.Longitude is { } lon && (!double.IsFinite(lon) || lon < -180 || lon > 180)))
            throw new ArgumentException("Posizione non valida: indicare fonte, citazioni e coordinate WGS84 valide oppure entrambe assenti.");
        if (d.Outcome == "Non leggibile")
        {
            if (d.FormType != "Non leggibile" || d.Interventions.Count != 0) throw new ArgumentException("Un PDF non leggibile non consente classificazioni.");
        }
        else if (d.FormEvidence.Count == 0) throw new ArgumentException("Citare il modulo e la dichiarazione pertinente.");
        if (d.Outcome == "IPRiPI individuati" && (d.FormType != "MUR A.1/D.1" || d.Interventions.Count == 0)) throw new ArgumentException("Indicare gli interventi e riconoscere il modulo.");
        if ((d.Interventions.Count > 0 || d.Outcome == "Nessun IPRiPI dichiarato") && d.FormType != "MUR A.1/D.1")
            throw new ArgumentException("Prima di classificare gli interventi riconoscere il modulo MUR A.1/D.1.");
        if (d.Outcome is "Nessun IPRiPI dichiarato" or "Non pertinente" && d.Interventions.Count != 0) throw new ArgumentException("Esito incompatibile con gli interventi indicati.");
        foreach (var item in d.Interventions)
        {
            if (item == null || !Text(item.Description,1000) || string.IsNullOrWhiteSpace(item.Description) || !Text(item.Reason,3000) || string.IsNullOrWhiteSpace(item.Reason) ||
                !Text(item.RuleCode,30) || !Text(item.Level,2) || item.Basis is not ("Dichiarato" or "Dedotto" or "Da verificare") ||
                item.MissingData == null || item.MissingData.Count > 30 || item.MissingData.Any(s => !Text(s,500)) || !Quotes(item.Evidence) || item.Evidence.Count == 0)
                throw new ArgumentException("Intervento privo di motivazione o citazioni valide.");
            IpripiRegulation.Validate(item);
            var codePattern = @"\b" + string.Join(@"\s*\.?\s*", item.RuleCode.Split('.').Select(Regex.Escape)) + @"\b";
            if (item.Basis == "Dichiarato" && (item.RuleCode.Length == 0 || !item.Evidence.Any(e => Regex.IsMatch(e.Quote, codePattern, RegexOptions.CultureInvariant))))
                throw new ArgumentException("Un codice dichiarato deve essere citato dal modulo; se assente usare Dedotto o Da verificare.");
        }
    }
}
