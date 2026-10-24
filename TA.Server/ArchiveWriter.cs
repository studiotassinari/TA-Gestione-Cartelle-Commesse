using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;
// Una sola fotografia pendente, scritta fuori dal thread di enumerazione.
public sealed class ArchiveWriter(string folder, char prefix, ILogger logger)
{
    private readonly object gate = new();
    private (ArchiveSnapshot Snapshot, CancellationToken Token)? pending;
    private Task task = Task.CompletedTask;
    private bool running;
    private DateTime latestRevision;
    public event Action<string>? StatusChanged;
    public Task Completion { get { lock (gate) return task; } }
    public void Enqueue(ArchiveSnapshot snapshot, CancellationToken token)
    {
        lock (gate)
        {
            if (snapshot.SavedUtc < latestRevision) return;
            latestRevision = snapshot.SavedUtc; pending = (snapshot, token);
            if (!running) { running = true; task = Task.Run(Drain); }
        }
    }
    private void Drain()
    {
        while (true)
        {
            (ArchiveSnapshot Snapshot, CancellationToken Token) item;
            lock (gate)
            {
                if (pending == null) { running = false; return; }
                item = pending.Value; pending = null;
            }
            string status;
            try
            {
                var saved = SharedArchiveService.Save(folder, item.Snapshot, item.Token);
                status = saved ? "JSON aggiornato: " + DateTime.Now.ToString("dd/MM HH:mm:ss") + ". Metadati secondo la verifica di ciascuna cartella."
                    : "JSON contiene dati piu recenti; scrittura precedente ignorata.";
                logger.LogInformation("Archivio {Category}: {Status}", prefix, status);
            }
            catch (OperationCanceledException) { status = "Salvataggio interrotto; JSON precedente conservato."; }
            catch (Exception ex)
            {
                status = "Salvataggio JSON fallito; dati aggiornati disponibili in memoria: " + ex.Message;
                logger.LogWarning(ex, "Archivio {Category}: scrittura fallita", prefix);
            }
            StatusChanged?.Invoke(status);
        }
    }
}
