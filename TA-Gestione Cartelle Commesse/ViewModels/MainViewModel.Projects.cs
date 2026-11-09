using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Views;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed partial class MainViewModel
{
    public Command CustomersCommand { get; private set; } = null!;
    private EntityWorkspace? entityWorkspace;
    public EntityWorkspace? Workspace=>connection==null?null:entityWorkspace??=new(CreateBusiness(),System.Windows.Application.Current.MainWindow);
    private void CloseProjectWindows(){entityWorkspace?.Dispose();entityWorkspace=null;}
    private void ShowCustomers(string id)=>Workspace?.ShowCustomers(id);
    private void ShowProject(WorkProjectViewModel row)=>Workspace?.ShowProject(row.Id);
}
