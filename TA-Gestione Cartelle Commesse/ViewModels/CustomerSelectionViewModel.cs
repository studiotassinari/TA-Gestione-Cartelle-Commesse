using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// La selezione è identificata dall'ID, indipendente dai filtri e dalle istanze ricaricate.
public sealed class CustomerSelectionViewModel : Observable
{
    private Customer[] all=[];
    private string filter="",selectedId="";
    public string Filter {get=>filter;set{filter=value;Notify();Notify(nameof(Rows));Notify(nameof(ResultCount));NotifySelection();}}
    public Customer[] Rows=>all.Where(c=>c.Matches(Filter)).OrderBy(c=>c.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(c=>c.Id).ToArray();
    public string ResultCount=>$"{Rows.Length} di {all.Length} clienti";
    public string SelectedId=>selectedId;
    public Customer? SelectedCustomer=>all.FirstOrDefault(c=>c.Id==selectedId);
    public string SelectionText=>SelectedCustomer is {} c?"Selezionato: "+c.Name:"Nessun cliente selezionato";
    public event Action<Customer>? SelectionChanged;
    public void SetCustomers(IEnumerable<Customer> values,string id)
    {
        all=values.ToArray();selectedId=id;
        Notify(nameof(Rows));Notify(nameof(ResultCount));NotifySelection();
    }
    public void SelectId(string? id){selectedId=id??"";NotifySelection();}
    public void Select(Customer value)
    {
        var customer=all.FirstOrDefault(c=>c.Id==value.Id);
        if(customer==null)return;
        selectedId=customer.Id;NotifySelection();SelectionChanged?.Invoke(customer);
    }
    private void NotifySelection(){Notify(nameof(SelectedId));Notify(nameof(SelectedCustomer));Notify(nameof(SelectionText));}
}
