using System.Windows;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Un solo punto di navigazione per tutte le aperture da elenchi, anagrafiche e pianificazione.
public sealed class EntityWorkspace(BusinessViewModel business,Window owner) : IDisposable
{
    public ProjectDetailWindow? ProjectWindow {get;private set;}
    public CustomerWindow? CustomerWindow {get;private set;}
    public void ShowProject(string id)
    {
        if(ProjectWindow is {} existing)
        {
            if(existing.Navigate(new(business,id)))CustomerWindow?.RequestSelection(null);
            TA.Desktop.SessionInstance.Restore(existing);return;
        }
        var window=new ProjectDetailWindow(new(business,id)){Owner=owner};ProjectWindow=window;
        window.CustomerRequested+=ChooseCustomer;
        window.Closed+=(_,_)=>{window.CustomerRequested-=ChooseCustomer;ProjectWindow=null;CustomerWindow?.RequestSelection(null);};
        window.Show();
    }
    public void ShowCustomers(string id="",Action<Customer>? choose=null,string context="",bool startNew=false)
    {
        if(CustomerWindow is {} existing)
        {
            var model=(CustomerManagementViewModel)existing.DataContext;
            if(model.CanLeave())
            {
                model.SelectCustomer(id);
                if(startNew&&model.NewCommand.CanExecute(null))model.NewCommand.Execute(null);
                existing.RequestSelection(choose,context);
            }
            TA.Desktop.SessionInstance.Restore(existing);return;
        }
        var window=new CustomerWindow(new(business,id),startNew:startNew){Owner=owner};CustomerWindow=window;
        window.RequestSelection(choose,context);
        void Open(WorkProject project)=>ShowProject(project.Id);
        window.ProjectRequested+=Open;
        window.Closed+=(_,_)=>{window.ProjectRequested-=Open;CustomerWindow=null;};
        window.Show();
    }
    private void ChooseCustomer(ProjectDetailViewModel detail)
    {
        ShowCustomers(detail.Project?.CustomerId??"",async customer=>
        {
            // Non applicare una scelta destinata ad una commessa a una scheda aperta successivamente.
            if(ProjectWindow?.DataContext!=detail)return;
            await detail.ReloadCustomers(customer);
            if(ProjectWindow is {} window)TA.Desktop.SessionInstance.Restore(window);
        },detail.Project?.Code??"");
    }
    public void Dispose(){CustomerWindow?.Close();ProjectWindow?.Close();business.Dispose();}
}
