using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Server.Archiving;

// Un solo lavoro di archiviazione alla volta. Lo stato è volutamente volatile:
// un riavvio annulla ogni autorizzazione alla cancellazione, non cancella lo ZIP.
public sealed class FolderArchiveService(ServerSettings settings, ScanCoordinator scans) : IDisposable
{
    private sealed class Job(string category, string source, string user)
    {
        public readonly string Id = Guid.NewGuid().ToString("N"), Category = category, Source = source, User = user;
        public readonly DateTime Started = DateTime.UtcNow;
        public string State = "Preparazione", Message = "Creazione e verifica dello ZIP; originali conservati.";
        public VerifiedZip? Receipt;
        public DateTime? Finished, Expires;
        public bool Running = true;
        public string Confirmation => "ELIMINA " + Id + " " + Source;
    }
    private readonly object gate = new();
    private readonly List<Job> jobs = [];
    private readonly CancellationTokenSource shutdown = new();
    public static bool Handles(string operation) => operation is "PrepareFolderZip" or "FolderZipStatus" or "DeleteZippedFolder";
    private string Root(string category) => category switch { "C" => settings.JobsRoot, "G" => settings.BidsRoot, _ => throw new UnauthorizedAccessException("ZIP disponibile solo per lavori e gare.") };

    public FolderArchiveStatus Execute(Request request, string user, bool authorized)
    {
        // authorized proviene esclusivamente dal SID del token Windows autenticato.
        // Il tunnel non dispone di operazioni ZIP, neppure con un account amministratore.
        if (!string.IsNullOrEmpty(request.McpTool)) throw new UnauthorizedAccessException("Operazioni ZIP disponibili esclusivamente dall'interfaccia client, non dal tunnel MCP.");
        if (!authorized) throw new UnauthorizedAccessException("Archiviazione riservata ad Antonio e Giuseppe nell'interfaccia client.");
        if (request.Version != 2 || !Handles(request.Operation)) throw new ArgumentException("Comando ZIP non valido.");
        _ = Root(request.Category);
        var q = request.FolderArchive ?? throw new ArgumentException("Richiesta ZIP mancante.");
        if (q.FolderId == null || q.FolderId.Length > 128 || q.JobId == null || q.JobId.Length > 64 || q.Confirmation == null || q.Confirmation.Length > 4000)
            throw new ArgumentException("Parametri ZIP non validi.");
        lock (gate)
        {
            shutdown.Token.ThrowIfCancellationRequested();
            Job job;
            if (request.Operation == "PrepareFolderZip")
            {
                if (jobs.Any(j => j.Running)) throw new IOException("Un'archiviazione è già in corso.");
                var record = scans.Get(request.Category).State.Snapshot?.Records.SingleOrDefault(r => r.Id == q.FolderId)
                    ?? throw new ArgumentException("Selezionare l'ID di una cartella censita in lavori o gare.");
                if (!System.Text.RegularExpressions.Regex.IsMatch(record.Code, "^" + request.Category + "[0-9]{5}$") ||
                    !Path.GetFileName(record.Path).StartsWith(record.Code, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(record.Path))
                    throw new IOException("Selezionare una singola cartella commessa/gara con codice valido, non un archivio o un anno.");
                VerifiedFolderZip.ValidatePath(Root(request.Category), record.Path);
                while (jobs.Count >= 8) jobs.RemoveAt(0);
                job = new(request.Category, Path.GetFullPath(record.Path), user); jobs.Add(job);
                _ = Task.Run(() => Prepare(job));
            }
            else
            {
                job = jobs.SingleOrDefault(j => j.Id == q.JobId && j.Category == request.Category && j.User == user)
                    ?? throw new ArgumentException("Lavoro ZIP non disponibile per questo utente, oppure servizio riavviato.");
                if (request.Operation == "DeleteZippedFolder")
                {
                    if (job.State != "ZIP verificato" || job.Expires <= DateTime.UtcNow || q.Confirmation != job.Confirmation)
                        throw new UnauthorizedAccessException("Serve una nuova conferma esplicita dopo la verifica dello ZIP. La conferma dura un'ora ed è utilizzabile una sola volta.");
                    if (jobs.Any(j => j.Running)) throw new IOException("Un'archiviazione è già in corso.");
                    job.Running = true; job.State = "Verifica prima dell'eliminazione"; job.Finished = null; job.Expires = null;
                    _ = Task.Run(() => Delete(job));
                }
            }
            return Status(job);
        }
    }
    private static FolderArchiveStatus Status(Job j) => new(j.Id, j.Category, j.Source, j.Source + ".zip", j.State, j.Message,
        j.Receipt?.Items.Count(i => i.Hash.Length > 0) ?? 0, j.Receipt?.Items.Sum(i => i.Bytes) ?? 0, j.Started, j.Finished,
        j.State == "ZIP verificato" && j.Expires > DateTime.UtcNow ? j.Confirmation : null, j.Expires);

    private void Prepare(Job job)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token); timeout.CancelAfter(TimeSpan.FromHours(1));
            var receipt = VerifiedFolderZip.Prepare(Root(job.Category), job.Source, timeout.Token);
            lock (gate)
            {
                job.Receipt = receipt; job.State = "ZIP verificato"; job.Expires = DateTime.UtcNow.AddHours(1);
                job.Message = "Tutti i file e le sottocartelle verificati con SHA-256. Originali ancora presenti. Richiedere conferma per eliminarli. Lo ZIP non conserva autorizzazioni NTFS e tutti gli attributi del filesystem.";
            }
        }
        catch (Exception ex) { lock (gate) { job.State = "Errore"; job.Message = ex.Message + " Nessun originale eliminato."; } }
        finally { lock (gate) { job.Running = false; job.Finished = DateTime.UtcNow; } }
    }
    private void Delete(Job job)
    {
        bool started = false;
        try
        {
            VerifiedFolderZip.DeleteOriginal(Root(job.Category), job.Receipt!, shutdown.Token,
                () => { started = true; lock (gate) { job.State = "Eliminazione"; job.Message = "ZIP e originali ricontrollati; rimozione dei soli elementi verificati."; } });
            lock (gate) { job.State = "Completato"; job.Message = "ZIP verificato conservato; cartella originale eliminata."; }
            scans.Get(job.Category).NotifyChange();
            scans.Refresh(job.Category);
        }
        catch (Exception ex)
        {
            lock (gate) { job.State = started ? "Rimozione parziale" : "Eliminazione bloccata";
                job.Message = ex.Message + (started ? " ZIP verificato conservato. Non eliminare manualmente i residui senza verificarli." : " Originali conservati."); }
        }
        finally { lock (gate) { job.Running = false; job.Finished = DateTime.UtcNow; } }
    }
    public void Dispose() => shutdown.Cancel();
}
