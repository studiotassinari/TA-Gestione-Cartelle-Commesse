using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Threading;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;
public partial class CustomerSelectionView : UserControl
{
    private CustomerSelectionViewModel? observed;
    private bool pending,synchronizing;
    public CustomerSelectionView()
    {
        InitializeComponent();
        DataContextChanged+=(_,_)=>Observe();
        Loaded+=(_,_)=>Observe();
        Unloaded+=(_,_)=>{if(observed!=null)observed.PropertyChanged-=Changed;observed=null;};
    }
    private void Observe()
    {
        if(observed!=null)observed.PropertyChanged-=Changed;
        observed=DataContext as CustomerSelectionViewModel;
        if(observed!=null)observed.PropertyChanged+=Changed;
        Synchronize();
    }
    private void Changed(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(CustomerSelectionViewModel.Rows) or nameof(CustomerSelectionViewModel.SelectedId))Synchronize();
    }
    private void Synchronize()
    {
        if(pending)return;pending=true;
        // ItemsSource deve essere applicato prima della selezione: altrimenti WPF la annulla.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>{
            pending=false;synchronizing=true;
            try
            {
                CustomersList.SelectedItem=CustomersList.Items.OfType<Customer>()
                    .FirstOrDefault(c=>c.Id==observed?.SelectedId);
                if(CustomersList.SelectedItem!=null)CustomersList.ScrollIntoView(CustomersList.SelectedItem);
            }
            finally{synchronizing=false;}
        }));
    }
    private void SelectCustomer(object sender,SelectionChangedEventArgs e)
    {
        // Rimozioni dovute a filtro/ricaricamento non cancellano l'associazione.
        if(!synchronizing&&DataContext is CustomerSelectionViewModel vm&&e.AddedItems.Count>0&&e.AddedItems[0] is Customer customer
            &&vm.SelectedId!=customer.Id)vm.Select(customer);
    }
}
