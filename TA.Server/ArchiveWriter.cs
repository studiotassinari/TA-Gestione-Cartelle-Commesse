using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;
// Una sola fotografia pendente, scritta fuori dal thread di enumerazione.
public sealed class ArchiveWriter(string folder, char prefix, ILogger logger, string? databaseFolder = null)
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
            // Accorpa aggiornamenti ravvicinati: basta salvare la fotografia più recente ancora pendente.
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
                bool saved;
                // Il database locale è usato quando configurato; il ramo condiviso è compatibilità legacy.
                if(databaseFolder==null) saved=SharedArchiveService.Save(folder,item.Snapshot,item.Token);
                else { item.Token.ThrowIfCancellationRequested();new InventoryStore(databaseFolder).SaveDocument("snapshot:"+prefix+":"+SharedArchiveService.Normalize(item.Snapshot.Root),item.Snapshot);saved=true; }
                status = saved ? "Database aggiornato: " + DateTime.Now.ToString("dd/MM HH:mm:ss") + ". Metadati secondo la verifica di ciascuna cartella."
                    : "Database contiene dati piu recenti; scrittura precedente ignorata.";
                logger.LogInformation("Archivio {Category}: {Status}", prefix, status);
            }
            catch (OperationCanceledException) { status = "Salvataggio interrotto; Database precedente conservato."; }
            catch (Exception ex)
            {
                status = "Salvataggio Database fallito; dati aggiornati disponibili in memoria: " + ex.Message;
                logger.LogWarning(ex, "Archivio {Category}: scrittura fallita", prefix);
            }
            StatusChanged?.Invoke(status);
        }
    }
}
