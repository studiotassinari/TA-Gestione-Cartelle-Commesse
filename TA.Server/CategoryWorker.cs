using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

// Serializza le scansioni della categoria, condivise da tutti i client.
public sealed class CategoryWorker(char prefix, string root, string archive, ILogger logger, ChangeHub changes)
{
    private readonly object gate = new();
    private CategoryState state = new(prefix, root, true, "Caricamento archivio...", "", null);
    private CancellationTokenSource? cancellation;
    private Task current = Task.CompletedTask;
    public CategoryState State { get { lock (gate) return state; } }

    public async Task InitializeAsync()
    {
        try
        {
            var snapshot = await Task.Run(() => SharedArchiveService.Load(archive, root, prefix));
            lock (gate) state = state with { Snapshot = snapshot, ArchiveStatus = snapshot == null ? "Archivio assente" : "Archivio caricato", Message = "Pronto", Busy = false };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Caricamento archivio {Category} fallito", prefix);
            lock (gate) state = state with { Busy = false, Message = "Pronto", ArchiveStatus = "Archivio non disponibile: " + ex.Message };
        }
    }

    // Restituisce false se una scansione è già attiva, senza crearne una seconda.
    public bool Start(CancellationToken shutdown)
    {
        lock (gate)
        {
            if (state.Busy) return false;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            state = state with { Busy = true, Message = "Verifica server in corso..." };
            var source = cancellation;
            changes.Publish();
            current = Task.Run(() => Scan(source));
            return true;
        }
    }

    public void NotifyChange()
    {
        lock (gate) state = state with { Message = state.Busy
            ? "Modifica rilevata; verifica in corso, nuova verifica in attesa."
            : "Modifica rilevata; verifica in attesa." };
        changes.Publish();
    }
    public void Cancel() { lock (gate) cancellation?.Cancel(); }
    public Task Completion { get { lock (gate) return current; } }

    private void Scan(CancellationTokenSource source)
    {
        var started = DateTime.UtcNow;
        try
        {
            var result = FolderReader.Scan(root, prefix, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (result.Cancelled) throw new OperationCanceledException();
            if (result.Error != null || result.Incomplete)
                throw new IOException(result.Error ?? "Scansione dei contenitori incompleta. " + string.Join("; ", result.Warnings));
            // La lettura annidata arricchisce esclusivamente i record principali già riconosciuti.
            result = FolderInspectionService.Enrich(result, source.Token);
            var snapshot = new ArchiveSnapshot { Root = SharedArchiveService.Normalize(root), Prefix = prefix, StartedUtc = started,
                SavedUtc = DateTime.UtcNow, Records = result.Records, Warnings = result.Warnings };
            string saved;
            try
            {
                saved = SharedArchiveService.Save(archive, snapshot, source.Token) ? "Salvato: " + snapshot.SavedUtc.ToLocalTime().ToString("g")
                    : "Archivio contiene una scansione più recente";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { saved = "Dati verificati, salvataggio fallito: " + ex.Message; logger.LogWarning(ex, "Salvataggio fallito"); }
            lock (gate) state = state with { Snapshot = snapshot, ArchiveStatus = saved, Message = $"{snapshot.Records.Count} cartelle verificate" };
        }
        catch (OperationCanceledException) { lock (gate) state = state with { Message = "Verifica annullata; dati precedenti conservati" }; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Verifica {Category} fallita", prefix);
            lock (gate) state = state with { Message = "Verifica fallita; dati precedenti conservati: " + ex.Message };
        }
        finally
        {
            lock (gate) { state = state with { Busy = false }; cancellation = null; source.Dispose(); }
            changes.Publish();
        }
    }
}
