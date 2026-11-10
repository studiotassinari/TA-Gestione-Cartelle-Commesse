using System.Diagnostics;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

// Censisce nomi e metadati dai JSON server: nessuna lettura automatica dei contenuti.
public sealed class IpripiIndexer(ServerSettings settings, IpripiService service, ILogger<IpripiIndexer> logger) : BackgroundService
{
    public static DateTime NextRun(DateTime utc) => DailyScanSchedule.Next(utc);
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
    private readonly object refreshGate=new();
    private ScanProgress indexProgress=new(false,"In attesa",0,null,"Attende il completamento dell'inventario file");
    public ScanProgress Progress=>Volatile.Read(ref indexProgress);
    public IpripiCandidateCatalog Refresh(CancellationToken token){lock(refreshGate)return RefreshTracked(token);}
    private IpripiCandidateCatalog RefreshTracked(CancellationToken token)
    {
        indexProgress=new(true,"Censimento candidati",0,null,"Preparazione",DateTime.UtcNow);
        try { var result=RefreshCore(token);indexProgress=Progress with{IsRunning=false,Phase="Completato",FinishedUtc=DateTime.UtcNow,Message="Catalogo aggiornato; contenuti analizzati solo su richiesta"};return result; }
        catch(Exception ex){indexProgress=Progress with{IsRunning=false,Phase="Interrotto",HasErrors=true,Message=ex.Message,FinishedUtc=DateTime.UtcNow};throw;}
    }
    private IpripiCandidateCatalog RefreshCore(CancellationToken token)
    {
        var store = new InventoryStore(settings.InventoryFolder);
        var target = Path.Combine(service.Root, "candidati.json");

        using var lease = CatalogStorage.Lock(settings,Path.Combine(service.Root, "censimento.lock"));
        var groups = new Dictionary<string, List<IpripiDocument>>();
        if (CatalogStorage.Exists(settings,target))
            foreach (var group in CatalogStorage.Read<IpripiCandidateCatalog>(settings,target).Documents.Where(r=>r.Category!="A"||ArchiveScope.Contains(settings.ArchivedJobsRoot,r.PhysicalPath)).GroupBy(r => r.FolderId)) groups[group.Key] = group.ToList();
        var savedIds = service.SavedIds(); var warnings = new List<string>(); var seen = new HashSet<string>();
        int processed = 0; var watch = Stopwatch.StartNew();
        IpripiCandidateCatalog Publish(bool running)
        {
            var notes = warnings.Take(20).ToList();
            notes.Add("PDF e DOCX candidati dai nomi in Commesse e Archivio. " + (settings.EnableLocalAnalysis ? "Analisi dei contenuti automatica in locale e su richiesta tramite ChatGPT" : "Analisi dei contenuti tramite ChatGPT su richiesta") + "; originali in sola lettura.");
            var catalog = new IpripiCandidateCatalog(1, DateTime.UtcNow, running, groups.Values.SelectMany(x => x).DistinctBy(r => r.Id).ToList(), notes);
            if (JsonSerializer.SerializeToUtf8Bytes(catalog).Length > 64 * 1024 * 1024) throw new IOException("Catalogo candidati MUR oltre 64 MiB.");
            CatalogStorage.Write(settings,target, catalog); watch.Restart(); return catalog;
        }
        var ordered=store.Manifests().Where(m=>m.Unit.Category!="A"||ArchiveScope.Contains(settings.ArchivedJobsRoot,m.Unit.Path)).Where(m => m.Unit.Category is "C" or "A").OrderByDescending(m=>AnalysisOrder.Year(m.Unit.Year,m.Unit.Code)).ThenByDescending(m=>AnalysisOrder.Number(m.Unit.Code)).ThenBy(m=>m.Unit.Id,StringComparer.Ordinal).ToArray();
        indexProgress=Progress with{Total=ordered.Length};
        foreach (var manifest in ordered)
        {
            token.ThrowIfCancellationRequested();
            indexProgress=Progress with{Completed=processed,Message=manifest.Unit.Code+" — "+manifest.Unit.Path};
            var unit = store.UnitDirectory(manifest.Unit);
            try
            {
                var cache = Path.Combine(service.Root, "unita", Path.GetRelativePath(store.Root, unit) + ".json");
                var old = CatalogStorage.Exists(settings,cache) ? CatalogStorage.Read<IpripiUnitIndex>(settings,cache) : null;
                if (old?.Generation != manifest.Generation || old.SchemaVersion < 3)
                {
                    var rows = new List<IpripiDocument>();
                    foreach (var entry in store.Entries(manifest))
                    {
                        token.ThrowIfCancellationRequested();
                        if (entry.Kind == "directory") continue;
                        var location = entry.Location ?? FileLocator.Locate(manifest.Unit, entry);
                        if (IpripiService.Supported(location.Extension) && (IpripiService.IsCandidate(location.Name) || savedIds.Contains(location.Id))) rows.Add(IpripiService.Describe(manifest, entry));
                    }
                    old = new(manifest.Generation, rows, 3); CatalogStorage.Write(settings,cache, old);
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
        indexProgress=Progress with{Completed=processed};
        var result = Publish(false);
        logger.LogInformation("Censimento MUR completato: {Count} candidati da {Units} unità; successiva scansione alle 00:00", result.Documents.Count, processed);
        return result;
    }
    private static IEnumerable<string> Children(string path) => Directory.Exists(path)
        ? Directory.EnumerateDirectories(path).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0) : [];
}
