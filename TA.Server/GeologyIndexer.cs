using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;

namespace TA.Server;

// Lavora esclusivamente sui JSON dell'inventario, anche prima che termini il primo censimento completo.
// Nessun documento, archivio ZIP/7z o percorso sorgente viene enumerato o aperto da questo componente.
public sealed class GeologyIndexer(ServerSettings settings, ILogger<GeologyIndexer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Run(() => Refresh(stoppingToken), stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Catalogo geologico non aggiornato; dati precedenti conservati"); }
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public GeologyCatalog Refresh(CancellationToken token)
    {
        var store = new InventoryStore(settings.ArchiveFolder);
        var root = GeologyStorage.Root(settings); var target = GeologyStorage.CatalogPath(settings);
        // Un percorso esplicito permette ancora di usare un catalogo esterno curato; non lo sovrascrive.
        if (!string.IsNullOrWhiteSpace(settings.GeologyCatalogPath)) throw new IOException("Catalogo esterno configurato: generazione automatica disabilitata.");
        var groups = new Dictionary<string, List<GeologicalReport>>(StringComparer.Ordinal);
        if (File.Exists(target))
        {
            var old = InventoryStore.Read<GeologyCatalog>(target);
            foreach (var group in old.Reports.GroupBy(r => r.InventoryUnitId)) groups[group.Key] = group.ToList();
        }
        var issues = new List<string>(); int processed = 0; bool partial = false;
        var seen = new HashSet<string>(StringComparer.Ordinal); var watch = Stopwatch.StartNew();
        GeologyCatalog Publish(bool running)
        {
            var warnings = issues.Take(12).ToList();
            warnings.Insert(0, "Candidati individuati dai nomi, non relazioni confermate. Contenuto leggibile solo su richiesta.");
            if (running) warnings.Add("Catalogo in aggiornamento: risultati provvisori dalle unità già censite.");
            if (partial || !File.Exists(Path.Combine(store.Root, "report.json"))) warnings.Add("Inventario parziale o prima scansione ancora in corso: l'elenco non dimostra l'assenza di altri documenti.");
            var rows = groups.Values.SelectMany(r => r).OrderByDescending(r => !string.IsNullOrWhiteSpace(r.ProjectCode))
                .ThenBy(r => r.Category == "C" ? 0 : r.Category == "G" ? 1 : 2).ThenBy(r => r.InventoryUnitId, StringComparer.Ordinal)
                .DistinctBy(r => r.Id).ToList();
            if (rows.Count > 50000) { rows = rows.Take(50000).ToList(); warnings.Add("Limite di 50.000 candidati raggiunto: catalogo parziale."); }
            var catalog = new GeologyCatalog(1, DateTime.UtcNow, rows, warnings);
            if (JsonSerializer.SerializeToUtf8Bytes(catalog).Length > 20 * 1024 * 1024) throw new IOException("Catalogo geologico oltre 20 MiB: dati precedenti conservati.");
            GeologyStorage.Atomic(target, catalog); watch.Restart(); return catalog;
        }
        // Profondità fissa: non attraversa le generazioni storiche e non segue collegamenti di directory.
        foreach (var category in Children(store.Root).Where(d => Path.GetFileName(d) is "C" or "G" or "A"))
        foreach (var year in Children(category))
        foreach (var code in Children(year))
        foreach (var unit in Children(code))
        {
            token.ThrowIfCancellationRequested();
            if (!Regex.IsMatch(Path.GetFileName(unit), "^[A-Fa-f0-9]{64}$")) continue;
            var latest = Path.Combine(unit, "latest.json"); if (!File.Exists(latest)) continue;
            try
            {
                var manifest = InventoryStore.Read<InventoryManifest>(latest);
                if (!Path.GetFullPath(store.UnitDirectory(manifest.Unit)).Equals(Path.GetFullPath(unit), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Manifest in una posizione inattesa.");
                seen.Add(manifest.Unit.Id); partial |= !manifest.Complete;
                var relative = Path.GetRelativePath(store.Root, unit);
                var cache = Path.Combine(root, "unita", relative + ".json");
                var saved = File.Exists(cache) ? InventoryStore.Read<GeologicalUnitIndex>(cache) : null;
                if (saved?.Generation != manifest.Generation)
                {
                    var reports = new List<GeologicalReport>();
                    foreach (var entry in store.Entries(manifest))
                    {
                        token.ThrowIfCancellationRequested();
                        if (GeologyClassifier.Classify(manifest, entry) is { } report) reports.Add(report);
                        if (reports.Count > 50000) throw new IOException("Troppi candidati nella singola unità.");
                    }
                    saved = new(manifest.Generation, reports); GeologyStorage.Atomic(cache, saved);
                }
                groups[manifest.Unit.Id] = saved.Reports;
                if (++processed % 100 == 0) logger.LogInformation("Catalogo geologico: {Count} unità JSON, ultima {Path}", processed, manifest.Unit.Path);
                if (processed == 1 || watch.Elapsed > TimeSpan.FromSeconds(30)) Publish(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { partial = true; if (issues.Count < 12) issues.Add("Unità non aggiornata: " + Path.GetFileName(unit) + " — " + ex.Message); }
        }
        if (processed == 0) throw new IOException("Nessuna unità di inventario leggibile: attendere il censimento dei nomi. I dati precedenti sono conservati.");
        if (groups.Keys.Any(id => !seen.Contains(id))) { partial = true; issues.Add("Conservate relazioni di unità temporaneamente non disponibili; assenze non confermate."); }
        var result = Publish(false);
        logger.LogInformation("Catalogo geologico aggiornato: {Count} candidati da {Units} unità JSON; nessuna lettura contenuti e nessuna notifica", result.Reports.Count, processed);
        return result;
    }
    private static IEnumerable<string> Children(string directory)
    {
        if (!Directory.Exists(directory)) yield break;
        foreach (var path in Directory.EnumerateDirectories(directory))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) yield return path;
    }
}
