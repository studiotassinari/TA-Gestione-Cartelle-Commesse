using TA.Server.Documents;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

public sealed record LocalAnalysisSkip(string Fingerprint, DateTime UntilUtc, string Reason);
public sealed class LocalAnalysisState
{
    // Documenti da non ritentare: illeggibili finché non cambiano, falliti per un periodo limitato.
    public Dictionary<string, LocalAnalysisSkip> Skipped { get; set; } = new();
    public DateTime? LastStartedUtc { get; set; }
    public int Analyzed { get; set; }
    public int Unreadable { get; set; }
    public int Failed { get; set; }
}

// Analizza da solo i candidati già censiti, un documento alla volta, entro i limiti di LocalAnalysisLimits.
// Legge gli originali in sola lettura tramite lo stesso processo isolato delle letture su richiesta;
// scrive soltanto nei cataloghi del servizio. Nessun servizio esterno.
public sealed class LocalAnalysisService(ServerSettings settings, ScanCoordinator scans, DocumentService documents, IpripiService mur, GeologyService geology, ILogger<LocalAnalysisService> logger) : BackgroundService
{
    private const string StateKey = "ANALISI/LOCALE/STATO";
    private volatile string statusText = "";
    public string StatusText => statusText;
    private InventoryStore Store => new(settings.InventoryFolder);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var limits = LocalAnalysisLimits.From(settings);
        if (!limits.Enabled) { logger.LogInformation("Analisi automatica locale disattivata: i contenuti sono letti solo su richiesta"); return; }
        logger.LogInformation("{Mode}", limits.Describe());
        try
        {
            while (!scans.InventoryReady) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            LocalAnalysisState? state = null; bool murTurn = true;
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMinutes(5);
                // Nessun errore deve fermare il servizio: l'analisi automatica è accessoria.
                try
                {
                    state ??= Load();
                    var wait = limits.Wait(DateTime.UtcNow, state.LastStartedUtc, scans.Progress.IsRunning, documents.OnDemandReading);
                    if (wait.Delay > TimeSpan.Zero) { statusText = wait.Reason; delay = wait.Delay; }
                    else
                    {
                        // Alterna i due cataloghi; se uno non ha documenti in attesa passa all'altro.
                        bool worked = murTurn ? await Mur(state, stoppingToken) || await Geology(state, stoppingToken) : await Geology(state, stoppingToken) || await Mur(state, stoppingToken);
                        murTurn = !murTurn;
                        if (worked) { state.LastStartedUtc = DateTime.UtcNow; Save(state); delay = limits.Pause; }
                        else { statusText = "Nessun documento in attesa di analisi automatica"; delay = TimeSpan.FromMinutes(15); }
                    }
                }
                catch (AnalysisDeferredException) { statusText = "In pausa: lettura su richiesta in corso"; delay = TimeSpan.FromSeconds(30); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { logger.LogWarning(ex, "Analisi automatica locale: ciclo non completato; nuovo tentativo tra cinque minuti"); statusText = "Errore temporaneo: " + ex.Message; }
                await Task.Delay(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    private async Task<bool> Mur(LocalAnalysisState state, CancellationToken token)
    {
        var candidate = mur.NextLocal((id, fingerprint) => Skip(state, "M:" + id, fingerprint));
        if (candidate == null) return false;
        await Run(state, "M:" + candidate.Id, candidate.SourceFingerprint, "MUR " + candidate.ProjectCode, async () =>
        {
            var record = await mur.AnalyzeLocalAsync(candidate, token);
            if (record.Analysis.Outcome == "Non leggibile") state.Unreadable++; else state.Analyzed++;
            return record.Analysis.Outcome;
        });
        return true;
    }
    private async Task<bool> Geology(LocalAnalysisState state, CancellationToken token)
    {
        var candidate = geology.NextLocal((id, fingerprint) => Skip(state, "G:" + id, fingerprint), token);
        if (candidate == null) return false;
        await Run(state, "G:" + candidate.Id, candidate.SourceFingerprint, "Geologia " + candidate.ProjectCode, async () =>
        {
            var record = await geology.AnalyzeLocalAsync(candidate, token);
            if (record != null) { state.Analyzed++; return record.Analysis.Kind; }
            // Nessun testo utile: non si ritenta finché il documento non cambia.
            state.Unreadable++; state.Skipped["G:" + candidate.Id] = new(candidate.SourceFingerprint, DateTime.MaxValue, "Testo non estraibile");
            return "testo non estraibile";
        });
        return true;
    }
    // Un documento che fallisce (troppo grande, protetto, oltre il tempo o la memoria ammessi) viene messo da parte per sette giorni.
    private async Task Run(LocalAnalysisState state, string key, string fingerprint, string label, Func<Task<string>> analyze)
    {
        statusText = "In analisi: " + label;
        try
        {
            var outcome = await analyze();
            statusText = $"Ultimo documento: {label} — {outcome} ({LocalAnalysisLimits.ItalianTime(DateTime.UtcNow):dd/MM HH:mm}); analizzati {state.Analyzed}, senza testo {state.Unreadable}, messi da parte {state.Failed}";
            logger.LogInformation("Analisi automatica locale: {Label} — {Outcome}", label, outcome);
        }
        catch (AnalysisDeferredException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state.Failed++; state.Skipped[key] = new(fingerprint, DateTime.UtcNow.AddDays(7), ex.Message.Length > 300 ? ex.Message[..300] : ex.Message);
            statusText = $"Documento messo da parte: {label} — {ex.Message}";
            logger.LogWarning("Analisi automatica locale: {Label} messo da parte per sette giorni — {Reason}", label, ex.Message);
        }
    }
    private static bool Skip(LocalAnalysisState state, string key, string fingerprint) =>
        state.Skipped.TryGetValue(key, out var skip) && skip.Fingerprint == fingerprint && skip.UntilUtc > DateTime.UtcNow;
    private LocalAnalysisState Load() =>
        Store.Document(StateKey) is { } json ? System.Text.Json.JsonSerializer.Deserialize<LocalAnalysisState>(json) ?? new() : new();
    private void Save(LocalAnalysisState state)
    {
        // Elenco limitato: le voci scadute si eliminano, poi le più vicine alla scadenza.
        foreach (var key in state.Skipped.Where(p => p.Value.UntilUtc <= DateTime.UtcNow).Select(p => p.Key).ToArray()) state.Skipped.Remove(key);
        foreach (var key in state.Skipped.OrderBy(p => p.Value.UntilUtc).Take(Math.Max(0, state.Skipped.Count - 20000)).Select(p => p.Key).ToArray()) state.Skipped.Remove(key);
        Store.SaveDocument(StateKey, state);
    }
}
