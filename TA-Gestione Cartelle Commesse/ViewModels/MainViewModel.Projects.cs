using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Views;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed partial class MainViewModel
{
    public Command CustomersCommand { get; private set; } = null!;
    private CustomerWindow? customerWindow;
    private bool closingProjectWindows;
    private readonly Dictionary<string, ProjectDetailWindow> projectWindows = [];
    private void CloseProjectWindows()
    {
        closingProjectWindows = true;
        customerWindow?.Close();
        foreach (var window in projectWindows.Values.ToArray()) window.Close();
        closingProjectWindows = false;
    }
    private void ShowCustomers(string id)
    {
        if (connection == null) return;
        if (customerWindow != null)
        { if(customerWindow.DataContext is CustomerManagementViewModel vm) vm.SelectCustomer(id); customerWindow.Activate(); return; }
        var business = new BusinessViewModel(connection);
        customerWindow = new(new CustomerManagementViewModel(business, id)) { Owner = System.Windows.Application.Current.MainWindow };
        customerWindow.Closed += async (_, _) => { customerWindow = null; business.Dispose(); if (!closingProjectWindows) await Commesse.PollAsync(); };
        customerWindow.Show();
    }
    private void ShowProject(WorkProjectViewModel row)
    {
        if (connection == null) return;
        if (projectWindows.TryGetValue(row.Id, out var open)) { open.Activate(); return; }
        var business = new BusinessViewModel(connection);
        var window = new ProjectDetailWindow(new(business, row.Id)) { Owner = System.Windows.Application.Current.MainWindow };
        projectWindows.Add(row.Id, window);
        window.Closed += async (_, _) => { projectWindows.Remove(row.Id); business.Dispose(); if (!closingProjectWindows) await Commesse.PollAsync(); };
        window.Show();
    }
}
