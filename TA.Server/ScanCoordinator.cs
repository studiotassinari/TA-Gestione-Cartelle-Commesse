namespace TA.Server;

// L'elenco viene verificato ogni minuto; le letture ricorsive partono solo alle 23 italiane.
public sealed class ScanCoordinator(ServerSettings settings, ILogger<ScanCoordinator> logger, ChangeHub changes) : BackgroundService
{
    private readonly Dictionary<char, CategoryWorker> workers = new()
    {
        ['C'] = new('C', settings.JobsRoot, settings.ArchiveFolder, logger, changes),
        ['G'] = new('G', settings.BidsRoot, settings.ArchiveFolder, logger, changes),
        ['A'] = new('A', settings.ArchivedJobsRoot, settings.ArchiveFolder, logger, changes)
    };
    private CancellationToken shutdown;
    private Task nightly = Task.CompletedTask;
    private DateTime retryAfter = DateTime.MinValue;
    private string ReportsDirectory => Path.Combine(settings.ArchiveFolder, "report-giornalieri");
    public string DailyReport() => DailyReports.Latest(ReportsDirectory);
    public CategoryWorker Get(string category) => category.ToUpperInvariant() switch
    { "C" => workers['C'], "G" => workers['G'], "A" => workers['A'], _ => throw new ArgumentException("Categoria non valida") };
    public bool Refresh(string category) { _ = Get(category).RefreshIndexAsync(shutdown); return true; }
    private async Task RunNightly(DateTime date, CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            logger.LogInformation("Elaborazione giornaliera {Date}: dimensioni e gerarchie", date);
            foreach (var worker in workers.Values) worker.Start(token);
            await Task.WhenAll(workers.Values.Select(w => w.Completion));
            token.ThrowIfCancellationRequested();
            DailyReports.Save(ReportsDirectory, date, workers.Values, started);
            changes.Publish("Report giornaliero dimensioni e gerarchie disponibile: " + date.ToString("dd/MM/yyyy"));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Report giornaliero non salvato"); retryAfter = DateTime.UtcNow.AddMinutes(5); }
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
                foreach (var worker in workers.Values) _ = worker.RefreshIndexAsync(stoppingToken);
                if (nightly.IsCompleted && DateTime.UtcNow >= retryAfter)
                {
                    try
                    {
                        if (DailyReports.IsDue(DateTimeOffset.UtcNow, ReportsDirectory))
                            nightly = RunNightly(DailyReports.ItalianTime(DateTimeOffset.UtcNow).Date, stoppingToken);
                    }
                    catch (Exception ex) { logger.LogWarning(ex, "Calendario report non disponibile"); }
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            } while (!stoppingToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach (var worker in workers.Values) worker.Cancel();
            await nightly;
            await Task.WhenAll(workers.Values.Select(w => w.Completion));
        }
    }
}
