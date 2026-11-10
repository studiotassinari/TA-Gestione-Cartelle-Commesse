using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// La selezione è fissata all'apertura: un aggiornamento dell'elenco non cambia il bersaglio.
// Nessun accesso al filesystem: creazione, verifica e rimozione spettano al server.
public sealed class FolderArchiveViewModel(string category, string folderId, string source,
    Func<Request, CancellationToken, Task<FolderArchiveStatus>> send) : Observable
{
    public string Source { get; } = source;
    public string Zip => Source + ".zip";
    public FolderArchiveStatus? Result { get; private set; }
    public string Message { get; private set; } = "Prepara lo ZIP sul server. La cartella originale viene conservata fino alla tua conferma separata.";
    public bool Busy { get; private set; }
    public bool CanPrepare => !Busy && Result == null && !attempted;
    public bool CanRefresh => !Busy && Result != null;
    public bool CanDelete => !Busy && Result is { State: "ZIP verificato", Confirmation: not null, FinishedUtc: not null }
        && Result.ConfirmationExpiresUtc > DateTime.UtcNow;
    private bool attempted;
    public Task PrepareAsync() => CanPrepare ? Run("PrepareFolderZip", new(FolderId: folderId)) : Task.CompletedTask;
    public Task RefreshAsync() => CanRefresh ? Run("FolderZipStatus", new(JobId: Result!.JobId)) : Task.CompletedTask;
    // La vista deve chiedere conferma mostrando esattamente Source e Zip; mai confermare dal polling.
    public Task DeleteAsync(bool confirmed) => confirmed && CanDelete
        ? Run("DeleteZippedFolder", new(JobId: Result!.JobId, Confirmation: Result.Confirmation!)) : Task.CompletedTask;
    private async Task Run(string operation, FolderArchiveQuery query)
    {
        Busy = true; Changed();
        if (operation == "PrepareFolderZip") attempted = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Result = await send(new Request(operation, category, FolderArchive: query), timeout.Token);
            Message = Result.State + " · " + Result.Message + $"\n{Result.Files} file · {Result.Bytes:N0} byte";
        }
        catch (Exception ex)
        {
            // Una risposta persa non prova che il server non abbia iniziato: niente retry della mutazione.
            Message = ex.Message + "\nNon ripetere l'operazione senza verificarne lo stato sul server.";
        }
        finally { Busy = false; Changed(); }
    }
    private void Changed()
    {
        Notify(nameof(Result)); Notify(nameof(Message)); Notify(nameof(Busy));
        Notify(nameof(CanPrepare)); Notify(nameof(CanRefresh)); Notify(nameof(CanDelete));
    }
}
