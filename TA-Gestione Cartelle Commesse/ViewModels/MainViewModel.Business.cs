using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed partial class MainViewModel
{
    public Command BusinessCommand { get; private set; } = null!;
    private Views.BusinessWindow? businessWindow;
    private void ShowBusiness()
    {
        if (connection == null) return;
        if (businessWindow != null) { businessWindow.Activate(); return; }
        businessWindow = new(new BusinessViewModel(connection)) { Owner = System.Windows.Application.Current.MainWindow };
        businessWindow.Closed += (_, _) => businessWindow = null;
        businessWindow.Show();
    }
}
