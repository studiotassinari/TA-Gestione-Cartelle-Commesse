using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed partial class CustomerManagementViewModel : Observable,IDisposable
{
    private readonly BusinessViewModel business;
    private readonly string initialId;
    private Customer[] all=[];
    public CustomerSelectionViewModel CustomerSelection {get;}=new();
    public CustomerManagementViewModel(BusinessViewModel business,string id="")
    {
        this.business=business;initialId=id;
        InitializeProjects();
        CustomerSelection.SelectionChanged+=customer=>Selected=customer;
        RefreshCommand=new(async()=>await Refresh(),()=>!Busy);
        NewCommand=new(()=>{Selected=null;SetEditor(new Customer("",""));},()=>CanEdit);
        SaveCommand=new(async()=>await Save(),()=>CanEdit&&Editor!=null);
    }
    public Command RefreshCommand {get;}
    public Command NewCommand {get;}
    public Command SaveCommand {get;}
    public bool Busy {get;private set;}
    private bool manager;
    public bool CanEdit=>manager&&!Busy;
    public string Status {get;private set;}="Caricamento clienti…";
    public CustomerViewModel? Editor {get;private set;}
    public Customer? Result=>Editor is {} e&&e.Model.Id.Length>0&&e.ToModel()==e.Model?e.Model:null;
    public bool CanSelect=>!Busy&&Result!=null;
    public string Filter {get=>CustomerSelection.Filter;set{CustomerSelection.Filter=value;Notify();Notify(nameof(Rows));}}
    public IEnumerable<Customer> Rows=>all.Where(c=>c.Matches(Filter)).OrderBy(c=>c.Name);
    private Customer? selected;
    public Customer? Selected {get=>selected;set{selected=value;CustomerSelection.SelectId(value?.Id);Notify();SetEditor(value);UpdateAssociatedProjects();}}
    public void SelectCustomer(string id)
    {
        if(Editor!=null&&Editor.ToModel()!=Editor.Model)
        {Status="Salva le modifiche al cliente prima di aprire un'altra scheda.";Notify(nameof(Status));return;}
        if(all.FirstOrDefault(c=>c.Id==id) is {} found){Filter="";Selected=found;}
    }
    private void SetEditor(Customer? customer)
    {
        if(Editor!=null)Editor.PropertyChanged-=EditorChanged;
        Editor=customer==null?null:new(customer);
        if(Editor!=null)Editor.PropertyChanged+=EditorChanged;
        Notify(nameof(Editor));Changed();
    }
    private void EditorChanged(object? s,System.ComponentModel.PropertyChangedEventArgs e)=>Changed();
    private void Changed(){Notify(nameof(CanEdit));Notify(nameof(CanSelect));RefreshCommand.Refresh();NewCommand.Refresh();SaveCommand.Refresh();OpenAssociatedProjectCommand.Refresh();}
    public async Task Refresh()
    {
        if(Busy)return;Busy=true;Changed();
        try
        {
            var catalog=await business.Send<ProjectCatalog>(new("ProjectCatalog"));
            projects=catalog.Projects;
            all=catalog.Customers;manager=catalog.CanManage;Notify(nameof(Rows));
            CustomerSelection.SetCustomers(all,Selected?.Id??initialId);
            Selected=all.FirstOrDefault(c=>c.Id==(Selected?.Id??initialId))??all.FirstOrDefault();
            Status=$"{all.Length} clienti · "+(manager?"Modifiche salvate dal server":"Sola consultazione");
        }
        catch(Exception ex){Status=ex.Message;}
        finally{Busy=false;Notify(nameof(Status));Changed();}
    }
    private async Task Save()
    {
        if(!CanEdit||Editor==null)return;Busy=true;Changed();
        try
        {
            var saved=await business.Send<Customer>(new("SaveCustomer",Customer:Editor.ToModel()));
            all=all.Where(c=>c.Id!=saved.Id).Append(saved).ToArray();Notify(nameof(Rows));
            CustomerSelection.SetCustomers(all,saved.Id);Selected=saved;Status="Cliente salvato.";
        }
        catch(Exception ex){Status=ex.Message;}
        finally{Busy=false;Notify(nameof(Status));Changed();}
    }
}

