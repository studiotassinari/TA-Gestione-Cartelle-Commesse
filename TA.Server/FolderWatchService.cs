using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace TA.Server;
// Il watcher e un acceleratore: sulle condivisioni SMB gli eventi possono perdersi, quindi ogni 5 minuti si rilegge
// comunque l'elenco di commesse e gare (solo nomi e percorsi, come la lettura manuale). La scansione completa dei
// metadati resta quella notturna. Ogni elenco cambiato va subito ai client e al database comune (ScanCoordinator).
public sealed class FolderWatchService(ServerSettings settings, ScanCoordinator scans, ILogger<FolderWatchService> logger) : BackgroundService
{
    private sealed class Watch(string category, string root)
    {
        public string Category { get; } = category;
        public string Root { get; } = root;
        public ChangeBatch Batch { get; } = new();
        public FileSystemWatcher? Watcher;
        public int Failed;
        public DateTime Retry;
    }
    private static readonly TimeSpan Periodic = TimeSpan.FromMinutes(5);
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var nextPeriodic = DateTime.UtcNow + Periodic;
        var watches = new[] { new Watch("C", settings.JobsRoot), new Watch("G", settings.BidsRoot), new Watch("A", settings.ArchivedJobsRoot) };
        try
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var watch in watches)
                {
                    if (Interlocked.Exchange(ref watch.Failed, 0) != 0)
                    { watch.Watcher?.Dispose(); watch.Watcher = null; watch.Retry = DateTime.UtcNow.AddSeconds(30); }
                    if (watch.Watcher == null && DateTime.UtcNow >= watch.Retry) Attach(watch);
                    if (watch.Batch.TryGetDue(DateTime.UtcNow, out var revision) && scans.Refresh(watch.Category))
                    {
                        watch.Batch.Acknowledge(revision);
                        logger.LogInformation("Modifiche rilevate: verifica categoria {Category} avviata", watch.Category);
                    }
                }
                if (DateTime.UtcNow >= nextPeriodic)
                {
                    // L'archivio (A) ha molte copie e cambia di rado: solo watcher e scansione notturna.
                    foreach (var category in new[] { "C", "G" }) scans.Refresh(category);
                    nextPeriodic = DateTime.UtcNow + Periodic;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { foreach (var watch in watches) watch.Watcher?.Dispose(); }
    }
    private void Attach(Watch watch)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(watch.Root)
            {
                IncludeSubdirectories = true, InternalBufferSize = 32768,
                NotifyFilter = watch.Category == "A" ? NotifyFilters.FileName | NotifyFilters.DirectoryName : NotifyFilters.DirectoryName
            };
            void Detected()
            {
                if (watch.Batch.Mark(DateTime.UtcNow))
                {
                    scans.Get(watch.Category).NotifyChange();
                    logger.LogInformation("Evento filesystem ricevuto per {Category}; verifica in attesa", watch.Category);
                }
            }
            void Changed(object sender, FileSystemEventArgs args)
            {
                if (Relevant(watch.Category, watch.Root, args.FullPath)) Detected();
            }
            watcher.Created += Changed; watcher.Deleted += Changed;
            watcher.Renamed += (sender, args) =>
            {
                if (Relevant(watch.Category, watch.Root, args.FullPath) || Relevant(watch.Category, watch.Root, args.OldFullPath)) Detected();
            };
            watcher.Error += (_, args) =>
            {
                watch.Batch.Mark(DateTime.UtcNow); Interlocked.Exchange(ref watch.Failed, 1);
                logger.LogWarning("Monitor cartelle {Category} interrotto; recupero programmato: {Reason}", watch.Category, args.GetException().Message);
            };
            watcher.EnableRaisingEvents = true;
            watch.Watcher = watcher;
            watch.Batch.Mark(DateTime.UtcNow);
            logger.LogInformation("Monitor modifiche attivo: {Category}", watch.Category);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            watcher?.Dispose(); watch.Retry = DateTime.UtcNow.AddSeconds(30);
            logger.LogWarning("Monitor {Category} non disponibile; nuovo tentativo tra 30 secondi: {Reason}", watch.Category, ex.Message);
        }
    }
    public static bool Relevant(string category, string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative)) return false;
        if(category=="A")return relative=="."||relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Length<=2;
        if (relative == "." || category == "G") return true;
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first.Length == 4 && int.TryParse(first, out var year) && year is >= 1900 and <= 2099;
    }
}
