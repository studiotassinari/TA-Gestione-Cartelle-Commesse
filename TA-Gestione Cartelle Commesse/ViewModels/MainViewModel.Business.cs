using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed partial class MainViewModel
{
    public Command BusinessCommand { get; private set; } = null!;
    private Views.BusinessWindow? businessWindow;
    private BusinessViewModel CreateBusiness()
    {
        var business=new BusinessViewModel(connection!);
        foreach(var category in new[]{Commesse,Gare,Archivio})
        {
            business.ProjectChanged+=category.ApplyProjectChange;
            business.CustomerChanged+=category.ApplyCustomerChange;
            business.CatalogChanged+=category.ApplyProjectCatalog;
        }
        return business;
    }
    private void ShowBusiness()
    {
        if (connection == null) return;
        if (businessWindow != null) { businessWindow.Activate(); return; }
        businessWindow = new(CreateBusiness()) { Owner = System.Windows.Application.Current.MainWindow };
        businessWindow.Closed += (_, _) => businessWindow = null;
        businessWindow.Show();
    }
}
