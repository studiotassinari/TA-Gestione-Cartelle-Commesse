using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed partial class MainViewModel
{
    public Command ArchiveFolderCommand { get; private set; } = null!;
    private Views.FolderArchiveWindow? archiveWindow;
    // Mantiene l'ID del lavoro in memoria anche chiudendo la finestra; nessun log o file client.
    private readonly Dictionary<string, FolderArchiveViewModel> archiveSessions = [];
    private void ShowFolderArchive(WorkProjectViewModel? row)
    {
        if (connection == null || !CanManageServer || row?.Folder == null || row.Model.Category is not ("C" or "G")) return;
        if (archiveWindow != null) { archiveWindow.Activate(); return; }
        var key = ServerHost + ":" + ServerPort + "/" + row.Model.Category + "/" + row.Id;
        if (!archiveSessions.TryGetValue(key, out var vm))
        {
            var captured = connection;
            vm = new(row.Model.Category, row.Id, row.Folder.Path,
                (request, token) => captured.SendAsync<FolderArchiveStatus>(request, token));
            archiveSessions.Add(key, vm);
        }
        archiveWindow = new(vm) { Owner = System.Windows.Application.Current.MainWindow };
        archiveWindow.Closed += (_, _) => archiveWindow = null;
        archiveWindow.Show();
        if (vm.CanRefresh) _ = vm.RefreshAsync();
    }
}
