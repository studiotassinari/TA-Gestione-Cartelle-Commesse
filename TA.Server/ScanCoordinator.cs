using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;

// Una sola elaborazione giornaliera comprende elenchi, metadati e inventario dei nomi.
public sealed class ScanCoordinator(ServerSettings settings, ILogger<ScanCoordinator> logger, ChangeHub changes, AnalysisService analysis,
    IpripiIndexer? mur = null, GeologyIndexer? geology = null) : BackgroundService
{
    private readonly Dictionary<char, CategoryWorker> workers = new()
    {
        ['C'] = new('C', settings.JobsRoot, settings.ArchiveFolder, logger, changes),
        ['G'] = new('G', settings.BidsRoot, settings.ArchiveFolder, logger, changes),
        ['A'] = new('A', settings.ArchivedJobsRoot, settings.ArchiveFolder, logger, changes, additionalArchiveRoot: settings.JobsRoot)
    };
    private readonly object gate = new();
    private ScanProgress progress = new(false, "Avvio", 0, null, "Caricamento del servizio");
    private CancellationToken shutdown;
    private Task current = Task.CompletedTask;
    private DateTime retryAfter = DateTime.MinValue;
    private DateTime? lastCompleted;
    private string ReportsDirectory => Path.Combine(settings.ArchiveFolder, "report-giornalieri");
    public string DailyReport()
    {
        try { return DailyReports.Latest(ReportsDirectory); }
        catch (IOException) { return DailyReports.Latest(Path.Combine(settings.ArchiveFolder, "report-orari")); }
    }
    public ScanProgress Progress
    {
        get
        {
            ScanProgress state; lock (gate) state = progress;
            if (state.IsRunning && state.Phase == "Dimensioni e gerarchie")
            {
                var items = workers.Values.Select(w => w.Progress).ToArray();
                state = state with { Completed = items.Sum(p => p.Completed), Total = items.Any(p => p.Total == null) ? null : items.Sum(p => p.Total!.Value),
                    Message = string.Join(" • ", workers.Select(p => p.Key + ": " + p.Value.Progress.Message)), HasErrors = items.Any(p => p.HasErrors), Locations=items.Where(p=>p.IsRunning).SelectMany(p=>p.Locations ?? []).ToArray() };
            }
            return state;
        }
    }
    private void SetProgress(ScanProgress value) { lock (gate) progress = value with { FinishedUtc=value.FinishedUtc ?? lastCompleted }; }
    public CategoryWorker Get(string category) => category.ToUpperInvariant() switch
    { "C" => workers['C'], "G" => workers['G'], "A" => workers['A'], _ => throw new ArgumentException("Categoria non valida") };
    public bool Refresh(string category) { _ = Get(category).RefreshIndexAsync(shutdown); return true; }
    public Task<bool> RefreshAsync(string category) => Get(category).RefreshIndexAsync(shutdown);
    private async Task RunScan(CancellationToken token)
    {
        var started = DateTime.UtcNow;
        try
        {
            SetProgress(new(true, "Preparazione", 0, null, "Preparazione delle categorie", started));
            logger.LogInformation("Elaborazione giornaliera avviata: {Started}", started);
            foreach(var worker in workers.Values) worker.Start(token);
            SetProgress(new(true, "Dimensioni e gerarchie", 0, null, "Verifica cartelle", started));
            await Task.WhenAll(workers.Values.Select(w => w.Completion));
            token.ThrowIfCancellationRequested();
            bool warnings = workers.Values.Any(w => w.Progress.HasErrors);
            await analysis.RunAsync(workers.Values.Select(w => w.State), token,
                p => { warnings |= p.HasErrors; SetProgress(p with { StartedUtc = started, HasErrors = warnings }); });
            SetProgress(new(true, "Salvataggio report", 0, null, "Pubblicazione dei risultati", started));
            // I cataloghi devono vedere gli inventari appena completati, non quelli del giorno precedente.
            if (mur != null) { SetProgress(new(true, "Catalogo MUR", 0, null, "Aggiornamento JSON MUR", started)); await Task.Run(() => mur.Refresh(token), token); }
            if (geology != null) { SetProgress(new(true, "Catalogo geologico", 0, null, "Aggiornamento JSON geologia", started)); await Task.Run(() => geology.Refresh(token), token); }
            DailyReports.Save(ReportsDirectory, DailyReports.ItalianTime(new DateTimeOffset(started)), workers.Values, new DateTimeOffset(started));
            var finished = DateTime.UtcNow;
            HourlySchedule.Save(ReportsDirectory, finished);
            lastCompleted = finished;
            SetProgress(new(false, warnings ? "Completata con segnalazioni" : "Completata", 1, 1,
                warnings ? "Alcune letture sono parziali: consultare il report" : "Risultati disponibili; nessuna notifica delle differenze", started,
                lastCompleted, DailyScanSchedule.Next(lastCompleted.Value), warnings));
            logger.LogInformation("Elaborazione giornaliera completata: {Finished}", lastCompleted);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { SetProgress(Progress with { IsRunning = false, Phase = "Interrotta", Message = "Scansione interrotta; dati completati conservati" }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Elaborazione giornaliera non completata"); retryAfter = DateTime.UtcNow.AddMinutes(5);
            SetProgress(Progress with { IsRunning = false, Phase = "Errore", Message = ex.Message, HasErrors = true, NextRunUtc = retryAfter });
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        shutdown = stoppingToken;
        await Task.WhenAll(workers.Values.Select(w => w.InitializeAsync()));
        bool scheduleLoaded = false;
        try
        {
            do
            {
                if (current.IsCompleted && DateTime.UtcNow >= retryAfter)
                {
                    try
                    {
                        if (!scheduleLoaded)
                        {
                            // Il checkpoint precedente resta su disco, ma ogni avvio deve ripetere la scansione completa.
                            lastCompleted = null; scheduleLoaded = true;
                            SetProgress(new(false, "In attesa", 0, null, "Scansione completa all'avvio e alle 00:00 italiane", NextRunUtc: DateTime.UtcNow));
                        }
                        if (DailyScanSchedule.IsDue(DateTime.UtcNow, lastCompleted)) current = RunScan(stoppingToken);
                    }
                    catch(Exception ex)
                    {
                        retryAfter = DateTime.UtcNow.AddMinutes(5); logger.LogWarning(ex, "Calendario scansioni non disponibile");
                        SetProgress(new(false, "Errore calendario", 0, null, ex.Message, NextRunUtc: retryAfter, HasErrors: true));
                    }
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            } while(!stoppingToken.IsCancellationRequested);
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach(var worker in workers.Values) worker.Cancel();
            await current;
            await Task.WhenAll(workers.Values.Select(w => w.Completion));
        }
    }
}
