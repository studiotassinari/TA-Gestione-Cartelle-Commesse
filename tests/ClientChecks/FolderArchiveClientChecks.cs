using System.IO;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
internal static partial class Program
{
    static void CheckFolderArchiveClient()
    {
        var calls = new List<Request>();
        var state = new FolderArchiveStatus("job", "C", "source", "source.zip", "Preparazione", "Attendere", 0, 0, DateTime.UtcNow, null, null, null);
        var vm = new FolderArchiveViewModel("C", "immutable-id", "source", (request, _) =>
        { calls.Add(request); return Task.FromResult(state); });
        vm.DeleteAsync(true).GetAwaiter().GetResult();
        Check(calls.Count == 0 && vm.CanPrepare, "ZIP client: impossibile eliminare prima della preparazione");
        vm.PrepareAsync().GetAwaiter().GetResult();
        Check(calls.Single().FolderArchive?.FolderId == "immutable-id" && calls[0].McpTool == null, "ZIP client: solo richiesta al server sul bersaglio fissato");
        Check(!vm.CanPrepare && !vm.CanDelete && vm.CanRefresh, "ZIP client: nessun doppio avvio durante il lavoro");
        state = state with { State = "ZIP verificato", FinishedUtc = DateTime.UtcNow, Confirmation = "explicit", ConfirmationExpiresUtc = DateTime.UtcNow.AddMinutes(1) };
        vm.RefreshAsync().GetAwaiter().GetResult();
        vm.DeleteAsync(false).GetAwaiter().GetResult();
        Check(calls.Count == 2 && vm.CanDelete, "ZIP client: annullare la conferma non invia cancellazioni");
        vm.DeleteAsync(true).GetAwaiter().GetResult();
        Check(calls.Last().Operation == "DeleteZippedFolder" && calls.Last().FolderArchive?.Confirmation == "explicit", "ZIP client: token separato solo dopo conferma");
        state = state with { ConfirmationExpiresUtc = DateTime.UtcNow.AddSeconds(-1) };
        vm.RefreshAsync().GetAwaiter().GetResult();
        Check(!vm.CanDelete, "ZIP client: conferma scaduta disabilitata");
        var failed = new FolderArchiveViewModel("C", "id", "source", (_, _) => throw new IOException("Connessione interrotta"));
        failed.PrepareAsync().GetAwaiter().GetResult();
        Check(!failed.CanPrepare && !failed.CanDelete && failed.Message.Contains("Connessione interrotta"), "ZIP client: risposta persa non causa replica della mutazione");
    }
}
