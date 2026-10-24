namespace TA.Server;

// Un solo coordinatore per processo carica le cache e pianifica le verifiche indipendentemente dai client.
public sealed class ScanCoordinator(ServerSettings settings, ILogger<ScanCoordinator> logger, ChangeHub changes) : BackgroundService
{
    private readonly Dictionary<char, CategoryWorker> workers = new()
    {
        ['C'] = new('C', settings.JobsRoot, settings.ArchiveFolder, logger, changes),
        ['G'] = new('G', settings.BidsRoot, settings.ArchiveFolder, logger, changes)
    };
    private CancellationToken shutdown;
    public CategoryWorker Get(string category) => category.ToUpperInvariant() switch
    { "C" => workers['C'], "G" => workers['G'], _ => throw new ArgumentException("Categoria non valida") };
    public bool Refresh(string category)
    {
        var worker = Get(category);
        _ = worker.RefreshIndexAsync(shutdown);
        return worker.Start(shutdown);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        shutdown = stoppingToken;
        await Task.WhenAll(workers.Values.Select(w => w.InitializeAsync()));
        changes.Publish();
        try
        {
            do
            {
                foreach (var worker in workers.Values)
                {
                    _ = worker.RefreshIndexAsync(stoppingToken);
                    worker.Start(stoppingToken);
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            } while (!stoppingToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach (var worker in workers.Values) worker.Cancel();
            await Task.WhenAll(workers.Values.Select(w => w.Completion));
        }
    }
}
