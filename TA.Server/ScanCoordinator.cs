using TA_Gestione_Cartelle_Commesse.Contracts;
using Microsoft.Extensions.DependencyInjection;
namespace TA.Server;

// Una sola elaborazione giornaliera comprende elenchi, metadati e inventario dei nomi.
public sealed class ScanCoordinator(ServerSettings settings, ILogger<ScanCoordinator> logger, ChangeHub changes, AnalysisService analysis,
    IServiceProvider? catalogServices = null) : BackgroundService
{
    private readonly Dictionary<char, CategoryWorker> workers = new()
    {
        ['C'] = new('C', settings.JobsRoot, settings.ArchiveFolder, logger, changes, databaseFolder: settings.InventoryFolder),
        ['G'] = new('G', settings.BidsRoot, settings.ArchiveFolder, logger, changes, databaseFolder: settings.InventoryFolder),
        ['A'] = new('A', settings.ArchivedJobsRoot, settings.ArchiveFolder, logger, changes, additionalArchiveRoot: settings.JobsRoot, databaseFolder: settings.InventoryFolder)
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
            // Risoluzione dopo la costruzione dell'host: DocumentService dipende dal coordinatore.
            if (catalogServices != null)
            {
                var mur = catalogServices.GetRequiredService<IpripiIndexer>();
                var geology = catalogServices.GetRequiredService<GeologyIndexer>();
                SetProgress(new(true, "Catalogo MUR", 0, null, "Aggiornamento database MUR", started));
                await Task.Run(() => mur.Refresh(token), token);
                SetProgress(new(true, "Catalogo geologico", 0, null, "Aggiornamento JSON geologia", started));
                await Task.Run(() => geology.Refresh(token), token);
            }
            DailyReports.Save(ReportsDirectory, DailyReports.ItalianTime(new DateTimeOffset(started)), workers.Values, new DateTimeOffset(started), currentOnly:true);
            SetProgress(new(true,"Backup database",0,null,"Verifica e copia sulla rete",started));
            var backup=settings.LocalStorageOnly ? DatabaseBackup.Status(settings) ?? new DatabaseBackupStatus(DateTime.UtcNow,null,"","Primo backup in preparazione") : await Task.Run(()=>DatabaseBackup.Save(settings,token),token);
            warnings |= backup.Error.Length>0;
            var backupMessage=backup.Error.Length>0?" Backup non riuscito: "+backup.Error:" Backup verificato: "+DailyReports.ItalianTime(backup.SuccessUtc!.Value).ToString("dd/MM/yyyy HH:mm");
            var finished = DateTime.UtcNow;
            HourlySchedule.Save(ReportsDirectory, finished);
            lastCompleted = finished;
            SetProgress(new(false, warnings ? "Completata con segnalazioni" : "Completata", 1, 1,
                (warnings ? "Consultare le segnalazioni" : "Risultati disponibili; nessuna notifica delle differenze")+backupMessage, started,
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
        if(settings.LocalStorageOnly)
        {
            while(!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Run(()=>LocalStorageTransition.Prepare(settings,stoppingToken,p=>SetProgress(new(true,"Preparazione archivio locale",0,null,p))),stoppingToken); break; }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){return;}
                catch(Exception ex){logger.LogError(ex,"Preparazione locale non completata; originali conservati");SetProgress(new(false,"Preparazione locale interrotta",0,null,ex.Message,HasErrors:true));await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);}
            }
        }

        SetProgress(new(true,"Caricamento elenchi",0,null,"Caricamento degli elenchi salvati prima della migrazione"));
        await Task.WhenAll(workers.Values.Select(w => w.InitializeAsync()));
        changes.Publish();
        while(!settings.LocalStorageOnly && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                SetProgress(new(true,"Migrazione SQLite",0,null,"Verifica degli inventari da importare; elenchi già consultabili"));
                await Task.Run(()=>DatabaseMigration.Import(settings,stoppingToken,p=>SetProgress(new(true,"Migrazione SQLite",0,null,p))),stoppingToken);
                break;
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){return;}
            catch(Exception ex)
            {
                logger.LogError(ex,"Migrazione SQLite non completata: JSON conservati");
                SetProgress(new(false,"Migrazione da completare",0,null,ex.Message,HasErrors:true));
                await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);
            }
        }
        try
        {
            if(!settings.LocalStorageOnly) await Task.Run(()=>DatabaseMigration.BackupAndCleanInventory(settings,stoppingToken,p=>SetProgress(new(true,"Pulizia inventari migrati",0,null,p))),stoppingToken);
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){return;}
        catch(Exception ex){logger.LogWarning(ex,"Pulizia sospesa: inventario SQLite disponibile, JSON residui conservati");}
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
                            SetProgress(new(false, "In attesa", 0, null, "Verifica incrementale all'avvio e alle 00:00 italiane", NextRunUtc: DateTime.UtcNow));
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
