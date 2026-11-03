using System.Diagnostics;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

// Censisce nomi e metadati dai JSON server: nessuna lettura automatica dei contenuti.
public sealed class IpripiIndexer(ServerSettings settings, IpripiService service, ILogger<IpripiIndexer> logger) : BackgroundService
{
    public static DateTime NextRun(DateTime utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        var next = local.Date.AddHours(23);
        if (next <= local) next = next.AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(next, zone);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Ogni avvio, inclusi quelli successivi agli aggiornamenti; nessuna attività pianificata Windows.
            while (!stoppingToken.IsCancellationRequested)
            {
                var success = false;
                try { await Task.Run(() => Refresh(stoppingToken), stoppingToken); success = true; }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Censimento MUR non completato; dati precedenti conservati"); }
                await Task.Delay(success ? NextRun(DateTime.UtcNow) - DateTime.UtcNow : TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public IpripiCandidateCatalog Refresh(CancellationToken token)
    {
        var store = new InventoryStore(settings.ArchiveFolder);
        var target = Path.Combine(service.Root, "candidati.json");
        Directory.CreateDirectory(service.Root);
        using var lease = new FileStream(Path.Combine(service.Root, "censimento.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var groups = new Dictionary<string, List<IpripiDocument>>();
        if (File.Exists(target))
            foreach (var group in InventoryStore.Read<IpripiCandidateCatalog>(target).Documents.GroupBy(r => r.FolderId)) groups[group.Key] = group.ToList();
        var savedIds = service.SavedIds(); var warnings = new List<string>(); var seen = new HashSet<string>();
        int processed = 0; var watch = Stopwatch.StartNew();
        IpripiCandidateCatalog Publish(bool running)
        {
            var notes = warnings.Take(20).ToList();
            notes.Add("PDF e DOCX candidati dai nomi in Commesse e Archivio. Analisi dei contenuti tramite ChatGPT su richiesta; originali in sola lettura.");
            var catalog = new IpripiCandidateCatalog(1, DateTime.UtcNow, running, groups.Values.SelectMany(x => x).DistinctBy(r => r.Id).ToList(), notes);
            if (JsonSerializer.SerializeToUtf8Bytes(catalog).Length > 64 * 1024 * 1024) throw new IOException("Catalogo candidati MUR oltre 64 MiB.");
            GeologyStorage.Atomic(target, catalog); watch.Restart(); return catalog;
        }
        foreach (var category in Children(store.Root).Where(p => Path.GetFileName(p) is "C" or "A"))
        foreach (var year in Children(category).OrderByDescending(Path.GetFileName))
        foreach (var code in Children(year))
        foreach (var unit in Children(code))
        {
            token.ThrowIfCancellationRequested();
            var latest = Path.Combine(unit, "latest.json"); if (!File.Exists(latest)) continue;
            try
            {
                var manifest = InventoryStore.Read<InventoryManifest>(latest);
                if (!Path.GetFullPath(store.UnitDirectory(manifest.Unit)).Equals(Path.GetFullPath(unit), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Manifest MUR in posizione inattesa.");
                var cache = Path.Combine(service.Root, "unita", Path.GetRelativePath(store.Root, unit) + ".json");
                var old = File.Exists(cache) ? InventoryStore.Read<IpripiUnitIndex>(cache) : null;
                if (old?.Generation != manifest.Generation || old.SchemaVersion < 2)
                {
                    var rows = new List<IpripiDocument>();
                    foreach (var entry in store.Entries(manifest))
                    {
                        token.ThrowIfCancellationRequested();
                        if (entry.Kind == "directory") continue;
                        var location = entry.Location ?? FileLocator.Locate(manifest.Unit, entry);
                        if (IpripiService.Supported(location.Extension) && (IpripiService.IsCandidate(location.Name) || savedIds.Contains(location.Id))) rows.Add(IpripiService.Describe(manifest, entry));
                    }
                    old = new(manifest.Generation, rows, 2); GeologyStorage.Atomic(cache, old);
                }
                groups[manifest.Unit.Id] = old.Documents; seen.Add(manifest.Unit.Id); processed++;
                if (!manifest.Complete && warnings.Count < 20) warnings.Add("Inventario parziale: " + manifest.Unit.Code);
                if (processed == 1 || watch.Elapsed > TimeSpan.FromSeconds(20)) Publish(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { if (warnings.Count < 20) warnings.Add("Unità non aggiornata: " + Path.GetFileName(unit) + " — " + ex.Message); }
        }
        if (processed == 0) throw new IOException("Inventario non ancora disponibile: nuova verifica tra cinque minuti.");
        foreach (var id in groups.Keys.Where(id => !seen.Contains(id)).ToList())
            groups[id] = groups[id].Select(r => r with { OriginalAvailable = false }).ToList();
        var result = Publish(false);
        logger.LogInformation("Censimento MUR completato: {Count} candidati da {Units} unità; successiva scansione alle 23", result.Documents.Count, processed);
        return result;
    }
    private static IEnumerable<string> Children(string path) => Directory.Exists(path)
        ? Directory.EnumerateDirectories(path).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0) : [];
}
