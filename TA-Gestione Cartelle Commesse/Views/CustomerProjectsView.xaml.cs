using System.Windows.Controls;
using System.Windows.Input;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;
public partial class CustomerProjectsView : UserControl
{
    public CustomerProjectsView()=>InitializeComponent();
    private void OpenProject(object sender,MouseButtonEventArgs e)
    {
        if(ItemsControl.ContainerFromElement((ListBox)sender,e.OriginalSource as System.Windows.DependencyObject) is not ListBoxItem)return;
        if(DataContext is CustomerManagementViewModel vm&&vm.OpenAssociatedProjectCommand.CanExecute(null))
        {vm.OpenAssociatedProjectCommand.Execute(null);e.Handled=true;}
    }
}
