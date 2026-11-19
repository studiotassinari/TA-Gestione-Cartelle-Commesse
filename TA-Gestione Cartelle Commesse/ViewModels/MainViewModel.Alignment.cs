using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
// Allineamento tra GeCo e GeCa (dalla 2.40.0): finestra a istanza singola come le altre di GeCo. Chi può vederla lo decide il
// servizio (amministratori di GeCo); la finestra spiega il motivo agli altri. Le notifiche push «GeCo» la ricaricano.
public sealed partial class MainViewModel
{
    public Command GeCoAlignmentCommand { get; private set; } = null!;
    private Views.GeCoAlignmentWindow? alignmentWindow;
    private AlignmentViewModel? alignment;
    private void ShowGeCoAlignment()
    {
        if (connection == null) return;
        if (alignmentWindow != null) { alignmentWindow.Activate(); return; }
        alignment = new AlignmentViewModel(new ServerGeCoGateway(connection));
        alignmentWindow = new(alignment) { Owner = System.Windows.Application.Current.MainWindow };
        alignmentWindow.Closed += (_, _) => { alignmentWindow = null; alignment = null; };
        alignmentWindow.Show();
    }
}
