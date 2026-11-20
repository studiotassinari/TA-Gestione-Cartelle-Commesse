using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
// Pianificazione delle attività e ore lavorate di GeCo, sul database comune (dalla 2.37.0): finestre a istanza singola, come le altre.
public sealed partial class MainViewModel
{
    public Command GeCoPlanningCommand { get; private set; } = null!;
    public Command GeCoHoursCommand { get; private set; } = null!;
    private Views.GeCoPlanningWindow? planningWindow;
    private Views.GeCoHoursWindow? hoursWindow;
    private PlanningViewModel? planningModel;
    private HoursViewModel? hoursModel;
    private long viewRevision;
    private void ShowGeCoPlanning()
    {
        if (connection == null) return;
        if (planningWindow != null) { planningWindow.Activate(); return; }
        planningModel = new PlanningViewModel(new ServerGeCoGateway(connection));
        planningWindow = new(planningModel) { Owner = System.Windows.Application.Current.MainWindow };
        planningWindow.Closed += (_, _) => { planningWindow = null; planningModel = null; };
        planningWindow.Show();
    }
    private void ShowGeCoHours()
    {
        if (connection == null) return;
        if (hoursWindow != null) { hoursWindow.Activate(); return; }
        hoursModel = new HoursViewModel(new ServerGeCoGateway(connection));
        hoursWindow = new(hoursModel) { Owner = System.Windows.Application.Current.MainWindow };
        hoursWindow.Closed += (_, _) => { hoursWindow = null; hoursModel = null; };
        hoursWindow.Show();
    }
    // Team della commessa selezionata nell'elenco delle commesse (2.41.0): la commessa di GeCo con il codice della cartella.
    public Command GeCoTeamCommand { get; private set; } = null!;
    private void ShowGeCoTeam()
    {
        if (connection == null || ActiveCategory.SelectedProject is not { Model.Category: "C" } row) return;
        var model = new ProjectTeamViewModel(new ServerGeCoGateway(connection), code: row.Code);
        new Views.GeCoTeamWindow(model) { Owner = System.Windows.Application.Current.MainWindow }.Show();
    }
    // Dal Collegamento cartelle: la commessa è già nota per Id.
    public void ShowGeCoTeam(int projectId)
    {
        if (connection == null || projectId <= 0) return;
        new Views.GeCoTeamWindow(new ProjectTeamViewModel(new ServerGeCoGateway(connection), projectId)) { Owner = System.Windows.Application.Current.MainWindow }.Show();
    }

    // Al cambio di connessione l'utente può essere un altro: le finestre si chiudono (le ore non salvate vengono chieste prima).
    private void CloseGeCoWindows() { planningWindow?.Close(); hoursWindow?.Close(); folderLinksWindow?.Close(); alignmentWindow?.Close(); geCoRevision = 0; viewRevision = 0; }

    // Notifica push «GeCoViste» (2.39.0): pianificazione o ore salvate in GeCo2027 o in un altro client di GeCa. Come per le
    // notifiche, torna subito (il riscontro al server parte dopo) e il lavoro si fa sul thread dell'interfaccia. La prima
    // notifica di ogni collegamento è solo il punto di partenza; senza finestre aperte non si chiede nulla al servizio, dopo
    // una riconnessione si chiedono gli eventi persi. Gli eventi arrivano già filtrati per ciò che l'utente può vedere.
    private Task GeCoViewsArrived(PushNotice notice)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (notice.Revision <= viewRevision) return;
            var from = viewRevision; viewRevision = notice.Revision;
            if (from == 0 || connection == null || (planningModel == null && hoursModel == null && !ProjectTeamViewModel.AnyOpen)) return;
            try
            {
                var updates = await new ServerGeCoGateway(connection).Send<GeCoViewUpdates>(new("ViewEvents", DopoId: from));
                viewRevision = Math.Max(viewRevision, updates.UltimoId);
                planningModel?.ViewsChanged(updates); hoursModel?.ViewsChanged(updates);
                // Team della commessa (2.41.0): sezioni e finestre aperte si rileggono se è cambiato il team della loro commessa.
                ProjectTeamViewModel.Dispatch(updates);
            }
            catch (Exception ex)
            {
                // Alla notifica successiva si richiedono anche questi eventi.
                if (viewRevision == notice.Revision) viewRevision = from;
                ClientLog.Write("Aggiornamento di pianificazione e ore non riuscito: " + ex.Message);
            }
        });
        return Task.CompletedTask;
    }
}
