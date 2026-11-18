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
    private void ShowGeCoPlanning()
    {
        if (connection == null) return;
        if (planningWindow != null) { planningWindow.Activate(); return; }
        planningWindow = new(new PlanningViewModel(new ServerGeCoGateway(connection))) { Owner = System.Windows.Application.Current.MainWindow };
        planningWindow.Closed += (_, _) => planningWindow = null;
        planningWindow.Show();
    }
    private void ShowGeCoHours()
    {
        if (connection == null) return;
        if (hoursWindow != null) { hoursWindow.Activate(); return; }
        hoursWindow = new(new HoursViewModel(new ServerGeCoGateway(connection))) { Owner = System.Windows.Application.Current.MainWindow };
        hoursWindow.Closed += (_, _) => hoursWindow = null;
        hoursWindow.Show();
    }
    // Al cambio di connessione l'utente può essere un altro: le finestre si chiudono (le ore non salvate vengono chieste prima).
    private void CloseGeCoWindows() { planningWindow?.Close(); hoursWindow?.Close(); }
}
