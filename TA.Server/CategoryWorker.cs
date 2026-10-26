using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

// Elenco e lettura ricorsiva procedono separatamente: una commessa grande non blocca le nuove righe.
public sealed class CategoryWorker(char prefix, string root, string archive, ILogger logger, ChangeHub changes,
    Func<string, CancellationToken, FolderInspection>? inspect = null)
{
    private readonly object gate = new();
    private CategoryState state = new(prefix, root, true, "Caricamento archivio...", "", null);
    private CancellationTokenSource? cancellation;
    private bool initialized; private readonly ArchiveWriter persistence = new(archive, prefix, logger);
    private Task current = Task.CompletedTask;
    private Task<bool> indexTask = Task.FromResult(false);
    public CategoryState State { get { lock (gate) return state; } }
    public async Task InitializeAsync()
    {
        try
        {
            persistence.StatusChanged += message => { lock (gate) state = state with { ArchiveStatus = message }; changes.Publish(); };
            var snapshot = await Task.Run(() => SharedArchiveService.Load(archive, root, prefix));
            lock (gate) state = state with { Snapshot = snapshot, ArchiveStatus = snapshot == null ? "Archivio assente" : "Archivio caricato", Message = "Pronto", Busy = false };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Caricamento archivio {Category} fallito", prefix);
            lock (gate) state = state with { Busy = false, Message = "Pronto", ArchiveStatus = "Archivio non disponibile: " + ex.Message };
        }
        finally { lock (gate) initialized = true; }
    }
    // Una sola enumerazione per categoria; resta disponibile anche mentre si leggono i file.
    public Task<bool> RefreshIndexAsync(CancellationToken token)
    {
        lock (gate)
        {
            if (!initialized) return Task.FromResult(false);
            if (!indexTask.IsCompleted) return indexTask;
            return indexTask = Task.Run(() => ReadIndex(token), token);
        }
    }
    private DateTime NextRevision() => state.Snapshot is { } old && old.SavedUtc >= DateTime.UtcNow
        ? old.SavedUtc.AddTicks(1) : DateTime.UtcNow;
    private bool ReadIndex(CancellationToken token)
    {
        try
        {
            var started = DateTime.UtcNow;
            var result = (prefix == 'A' ? ArchivedJobsReader.Scan(root, token, false) : FolderReader.Scan(root, prefix, token));
            token.ThrowIfCancellationRequested();
            if (result.Cancelled) throw new OperationCanceledException(token);
            if (result.Error != null || (result.Incomplete && prefix != 'A'))
                throw new IOException(result.Error ?? "Elenco incompleto: " + string.Join("; ", result.Warnings));
            List<string> differences;
            bool publish;
            lock (gate)
            {
                var previous = state.Snapshot;
                var old = (previous?.Records ?? []).ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
                var records = result.Records.Select(r => old.TryGetValue(r.Path, out var prior) ? r with { Inspection = prior.Inspection, ArchiveBytes = prior.ArchiveBytes, ArchiveModifiedUtc = prior.ArchiveModifiedUtc } : r).ToList();
                if (prefix == 'A' && result.Incomplete)
                {
                    var found = records.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    records.AddRange(old.Values.Where(r => !found.Contains(r.Path)));
                }
                differences = previous == null ? [] : FolderChanges.Compare(previous.Records, records);
                publish = previous == null || differences.Count > 0 || !previous.Warnings.SequenceEqual(result.Warnings);
                if (publish) state = state with { Snapshot = new ArchiveSnapshot { Root = SharedArchiveService.Normalize(root), Prefix = prefix,
                    StartedUtc = started, SavedUtc = NextRevision(), Records = records, Warnings = result.Warnings } };
            }
            logger.LogInformation("Elenco {Category} verificato: {Count} cartelle, {Changes} modifiche, durata {Seconds:F1}s", prefix, result.Records.Count, differences.Count, (DateTime.UtcNow - started).TotalSeconds);
            if (publish) changes.Publish(differences.Count > 0 ? prefix + ": " + FolderChanges.Summary(differences) : null);
            persistence.Enqueue(State.Snapshot!, token);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lettura elenco {Category} fallita; dati conservati", prefix);
            lock (gate) state = state with { Message = "Elenco non verificato; dati conservati: " + ex.Message };
            changes.Publish();
            return false;
        }
    }
    public bool Start(CancellationToken shutdown)
    {
        lock (gate)
        {
            if (state.Busy) return false;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            state = state with { Busy = true, Message = "Verifica metadati server in corso..." };
            var source = cancellation;
            changes.Publish();
            current = Task.Run(() => Scan(source));
            return true;
        }
    }
    public void NotifyChange()
    {
        lock (gate) state = state with { Message = "Modifica rilevata; aggiornamento elenco in attesa." };
        changes.Publish();
    }
    public void Cancel() { lock (gate) cancellation?.Cancel(); }
    public Task Completion { get { lock (gate) return Task.WhenAll(current, indexTask, persistence.Completion); } }
    private async Task Scan(CancellationTokenSource source)
    {
        var started = DateTime.UtcNow;
        try
        {
            if (!await RefreshIndexAsync(source.Token)) return;
            List<FolderRecord> pending;
            lock (gate) pending = state.Snapshot!.Records.ToList();
            logger.LogInformation("Metadati {Category}: avvio verifica di {Count} cartelle", prefix, pending.Count);
            int completed = 0; var lastSave = DateTime.UtcNow; var lastPush = DateTime.MinValue;
            foreach (var record in pending)
            {
                source.Token.ThrowIfCancellationRequested();
                lock (gate) state = state with { Message = $"Metadati {++completed}/{pending.Count}: {record.Code}. Elenco aggiornato separatamente." };
                var inspection = (inspect ?? (prefix == 'A' ? (p, ct) => ArchivedJobsReader.Inspect(p, ct, true) : FolderInspectionService.Inspect))(record.Path, source.Token);
                List<string> differences = [];
                lock (gate)
                {
                    var snapshot = state.Snapshot!;
                    var position = snapshot.Records.FindIndex(r => string.Equals(r.Path, record.Path, StringComparison.OrdinalIgnoreCase));
                    // Non reinserisce cartelle rimosse o rinominate durante una lettura lunga.
                    if (position >= 0 && snapshot.Records[position].Inspection?.CheckedUtc > inspection.CheckedUtc != true)
                    {
                        var previous = snapshot.Records[position];
                        var updated = previous with { Inspection = inspection };
                        if (prefix == 'A') { var file = new FileInfo(record.Path); updated = updated with { ArchiveBytes = file.Length, ArchiveModifiedUtc = file.LastWriteTimeUtc }; }
                        differences = FolderChanges.Compare([previous], [updated]);
                        var records = snapshot.Records.ToList(); records[position] = updated;
                        state = state with { Snapshot = snapshot with { Records = records, SavedUtc = NextRevision() } };
                    }
                }
                if (differences.Count > 0 && DateTime.UtcNow - lastPush >= TimeSpan.FromSeconds(2))
                { changes.Publish(prefix + ": " + FolderChanges.Summary(differences)); lastPush = DateTime.UtcNow; }
                if (DateTime.UtcNow - lastSave >= TimeSpan.FromSeconds(30))
                { persistence.Enqueue(State.Snapshot!, source.Token); lastSave = DateTime.UtcNow; }
            }
            ArchiveSnapshot latest;
            lock (gate) latest = state.Snapshot!;
            persistence.Enqueue(latest, source.Token);
            await persistence.Completion;
            lock (gate) state = state with { Message = $"Metadati verificati per {completed} cartelle. Controllo elenco ogni minuto." };
            logger.LogInformation("Metadati {Category}: completati {Count}, durata {Seconds:F1}s", prefix, completed, (DateTime.UtcNow - started).TotalSeconds);
        }
        catch (OperationCanceledException) { lock (gate) state = state with { Message = "Metadati interrotti; risultati disponibili conservati" }; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Verifica {Category} fallita", prefix);
            lock (gate) state = state with { Message = "Verifica metadati fallita; risultati disponibili conservati: " + ex.Message };
        }
        finally
        {
            lock (gate) { state = state with { Busy = false }; cancellation = null; source.Dispose(); }
            changes.Publish();
        }
    }
}
