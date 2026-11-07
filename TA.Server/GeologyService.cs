using System.Security.Cryptography;
using System.Text.Json;
using TA.Server.Documents;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Server;

// Catalogo curato separato dall'inventario: nessuna lettura automatica dei documenti o geocodifica.
public sealed partial class GeologyService(ServerSettings settings, DocumentService documents)
{
    public static void ValidateQuery(GeologyQuery q)
    {
        if (q.Search == null || q.Code == null || q.Search.Length > 300 || q.Code.Length > 40 || q.Offset < 0 || q.Limit is < 1 or > 500 || q.Offset > int.MaxValue - q.Limit || (q.Offset > 0 && string.IsNullOrWhiteSpace(q.Version)))
            throw new ArgumentException("Ricerca geologica non valida: massimo 500 voci per pagina, versione necessaria nelle pagine successive.");
    }
    public static void ValidateRead(GeologicalReadQuery q)
    {
        if (string.IsNullOrWhiteSpace(q.ReportId) || q.ReportId.Length > 128 || q.Offset < 0 || q.Offset > 200000 || q.Length is < 1 or > 24000 || (q.Offset > 0 && q.Version == null))
            throw new ArgumentException("Relazione o pagina non valida.");
    }
    public GeologyPage Query(GeologyQuery q, CancellationToken token)
    {
        ValidateQuery(q); var (catalog, version) = ReadCatalog(token);
        if (q.Version != null && q.Version != version) throw new IOException("Catalogo aggiornato: ripartire dalla prima pagina.");
        var terms = q.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = catalog.Reports.Where(r => (!q.OnlyLocated || r.HasPosition) && r.ProjectCode.Contains(q.Code, StringComparison.OrdinalIgnoreCase) &&
            terms.All(t => string.Join(' ', r.Title, r.FileName, r.Municipality, r.Address, r.ProjectCode, r.Client, r.Author, r.Kind, r.Notes, r.DossierTitle, r.AnalysisStatus).Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => r.Municipality, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.ProjectCode, StringComparer.Ordinal).ThenBy(r => r.Title, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
        return new(version, catalog.UpdatedUtc, rows.Length, q.Offset, q.Offset + q.Limit < rows.Length ? q.Offset + q.Limit : null, rows.Skip(q.Offset).Take(q.Limit).ToList(), catalog.Warnings);
    }
    private (GeologyCatalog Catalog, string Version) ReadCatalog(CancellationToken token)
    {
        var path = GeologyStorage.CatalogPath(settings);
        if (!CatalogStorage.Exists(settings,path)) throw new IOException("Catalogo delle relazioni geologiche non disponibile: attendere la generazione dal censimento dei nomi. L'assenza del catalogo non equivale a zero relazioni.");
        var bytes = CatalogStorage.Bytes(settings,path);
        if(bytes.Length>20*1024*1024)throw new IOException("Catalogo geologico oltre 20 MiB");
        token.ThrowIfCancellationRequested();
        GeologyCatalog catalog;
        try { catalog = JsonSerializer.Deserialize<GeologyCatalog>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Catalogo vuoto."); }
        catch (JsonException ex) { throw new InvalidDataException("Catalogo geologico non valido: controllare il formato JSON.", ex); }
        if (catalog.SchemaVersion != 1 || catalog.UpdatedUtc == default || catalog.Reports == null || catalog.Warnings == null || catalog.Reports.Count > 50000) throw new InvalidDataException("Struttura del catalogo geologico non valida.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in catalog.Reports)
        {
            token.ThrowIfCancellationRequested();
            if (r == null || string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 128 || !ids.Add(r.Id) || string.IsNullOrWhiteSpace(r.Title) ||
                new[] { r.Title,r.Kind,r.FileName,r.PhysicalPath,r.Municipality,r.Address,r.PositionSource,r.PositionAccuracy,r.ProjectCode,r.Client,r.Author,r.Notes,r.Category,r.FolderId,r.RelativePath }.Any(s => s == null || s.Length > 4096) ||
                (r.Latitude.HasValue != r.Longitude.HasValue) || (r.Latitude.HasValue && !r.HasPosition))
                throw new InvalidDataException("Relazione con identità, campi o coordinate non validi nel catalogo.");
        }
        catalog=GeologyListReferences.Merge(settings,catalog);
        var analyzed = MergeAnalyses(catalog, JsonSerializer.SerializeToUtf8Bytes(catalog));
        return MergePositions(analyzed.Catalog, analyzed.Bytes);
    }
    public async Task<DocumentPage> ReadAsync(GeologicalReadQuery q, CancellationToken token, bool scheduled=false)
    {
        ValidateRead(q);
        var report = ReadCatalog(token).Catalog.Reports.SingleOrDefault(r => r.Id == q.ReportId) ?? throw new KeyNotFoundException("Relazione non presente nel catalogo.");
        if (!report.CanRead) throw new IOException("Il catalogo non contiene ancora il collegamento al documento originale.");
        using var schedule=scheduled?documents.AnalysisSchedule.Enter(report.Id):null;
        DocumentPage page;
        if (string.IsNullOrWhiteSpace(report.PhysicalPath))
            page = (await documents.ExecuteAsync(report.Category, new(report.FolderId, report.RelativePath, report.ArchiveEntry, q.Offset, q.Length, q.Version), null, token)).Document!;
        else page = (await documents.ExecuteWorkAsync(ResolveDocument(settings, report, q), token)).Document!;
        evidence.Remember(report.Id, page);
        GeologyListReferences.Discover(settings,report,page,token);
        return page;
    }
    public static DocumentWork ResolveDocument(ServerSettings settings, GeologicalReport report, GeologicalReadQuery q)
    {
        ValidateRead(q);
        var full = Path.GetFullPath(report.PhysicalPath);
        var root = new[] { settings.JobsRoot, settings.BidsRoot, settings.ArchivedJobsRoot }.Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(Path.GetFullPath).FirstOrDefault(r => full.StartsWith(r.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            ?? throw new UnauthorizedAccessException("Il documento geologico è fuori dalle sorgenti autorizzate.");
        var id = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(report.Id)));
        var read = new DocumentQuery(id, Path.GetRelativePath(root, full), report.ArchiveEntry, q.Offset, q.Length, q.Version);
        DocumentValidation.Validate(read);
        return new(root, root, read);
    }
}
