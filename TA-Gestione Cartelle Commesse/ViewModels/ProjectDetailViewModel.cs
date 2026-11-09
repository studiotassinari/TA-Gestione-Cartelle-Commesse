using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// La vista non accede ai file: legge e salva esclusivamente attraverso il servizio autenticato.
public sealed class ProjectDetailViewModel : Observable
{
    private readonly BusinessViewModel business;
    private readonly string id;
    public string Id=>id;
    public bool CanLeave()
    {
        if(Busy){Status="Attendi il completamento dell'operazione prima di cambiare commessa.";Notify(nameof(Status));return false;}
        bool dirty;
        try { dirty=(Project!=null&&Project.ToModel()!=Project.Model)||(Phase!=null&&Phase.ToModel()!=Phase.Model)||(Activity!=null&&Activity.ToModel()!=Activity.Model); }
        catch(ArgumentException){dirty=true;}
        if(dirty){Status="Salva le modifiche prima di cambiare commessa, oppure premi Annulla per ricaricare i dati salvati.";Notify(nameof(Status));return false;}
        return true;
    }
    private readonly CustomerDirectory customers = new();
    public BusinessViewModel Business => business;
    public CustomerSelectionViewModel CustomerSelection {get;}=new();
    public event Action? CustomerManagementRequested;
    public Command ManageCustomerCommand {get;}
    public ProjectDetailViewModel(BusinessViewModel business,string id)
    {
        this.business=business;this.id=id;
        CustomerSelection.SelectionChanged+=customer=>{if(Project!=null)Project.CustomerId=customer.Id;};
        ManageCustomerCommand=new(()=>CustomerManagementRequested?.Invoke(),()=>CanEdit);
        RefreshCommand=new(async()=>await Refresh(),()=>!Busy);
        SaveProjectCommand=new(async()=>await Execute(()=>new("SaveProjectDetails",Project:Project!.ToModel())),()=>CanEdit&&Project!=null);
        SavePhaseCommand=new(async()=>await Execute(()=>new("SavePhase",Phase:Phase!.ToModel())),()=>CanEdit&&Phase!=null);
        SaveActivityCommand=new(async()=>await Execute(()=>new("SaveActivity",Activity:Activity!.ToModel())),()=>CanEdit&&Activity!=null);
        NewPhaseCommand=new(()=>{SelectedPhase=null;Phase=new(new("",id,""));Notify(nameof(Phase));Commands();},()=>CanEdit);
        NewActivityCommand=new(()=>{
            SelectedActivity=null;
            var day=DateOnly.FromDateTime(DateTime.Today);
            Activity=new(new("",id,SelectedPhase!.Name,"","",day,day,0,PhaseId:SelectedPhase.Id));Notify(nameof(Activity));Commands();
        },()=>CanEdit&&SelectedPhase!=null);
    }
    public Command RefreshCommand {get;}
    public Command SaveProjectCommand {get;}
    public Command SavePhaseCommand {get;}
    public Command SaveActivityCommand {get;}
    public Command NewPhaseCommand {get;}
    public Command NewActivityCommand {get;}
    public WorkProjectViewModel? Project {get;private set;}
    public CustomerViewModel? Customer {get;private set;}
    public WorkPhaseViewModel? Phase {get;private set;}
    public WorkActivityViewModel? Activity {get;private set;}
    public ProjectDetails? Details {get;private set;}
    public string DetailTitle=>(Details?.Project.Category switch{"G"=>"Dettaglio gara","A"=>"Dettaglio archivio",_=>"Dettaglio commessa"})+" — TA";
    public string OverviewTitle=>Details?.Project.Category switch{"G"=>"Gara e cliente","A"=>"Archivio e cliente",_=>"Commessa e cliente"};
    public string SaveLabel=>Details?.Project.Category switch{"G"=>"Salva gara","A"=>"Salva dati archivio",_=>"Salva commessa"};
    public bool Busy {get;private set;}
    public bool CanEdit=>!Busy&&Details?.CanManage==true;
    public bool CanEditPhase=>CanEdit&&Phase!=null;
    public bool CanEditActivity=>CanEdit&&Activity!=null;
    public string Status {get;private set;}="Caricamento…";
    private WorkPhase? selectedPhase;
    public WorkPhase? SelectedPhase
    {
        get=>selectedPhase;
        set {selectedPhase=value;Phase=value==null?null:new(value);Activity=null;Notify();Notify(nameof(Phase));Notify(nameof(Activities));SelectedActivity=Activities.FirstOrDefault();Commands();}
    }
    public WorkActivity[] Activities=>Details?.Activities.Where(a=>a.PhaseId==SelectedPhase?.Id).ToArray()??[];
    public WorkActivity? SelectedActivity
    {
        get=>Activity?.Model;
        set{Activity=value==null?null:new(value);Notify();Notify(nameof(Activity));Commands();}
    }
    public async Task Refresh()=>await Execute(null);
    private void Commands()
    {
        Notify(nameof(CanEditPhase));Notify(nameof(CanEditActivity));
        foreach(var command in new[]{RefreshCommand,SaveProjectCommand,SavePhaseCommand,SaveActivityCommand,NewPhaseCommand,NewActivityCommand,ManageCustomerCommand})command.Refresh();
    }
    private async Task Execute(Func<BusinessCommand>? makeCommand)
    {
        if(Busy)return;Busy=true;Notify(nameof(CanEdit));Commands();
        try
        {
            var command=makeCommand?.Invoke();
            var phaseId=SelectedPhase?.Id;
            if(command!=null)
            {
                if(command.Action=="SavePhase")phaseId=(await business.Send<WorkPhase>(command)).Id;
                else await business.Send<System.Text.Json.JsonElement>(command);
            }
            Details=await business.Send<ProjectDetails>(new("ProjectDetail",Id:id));
            Project?.Dispose(); customers.Replace(Details.Customers);
            Project=new(Details.Project,customers); Customer=Project.Customer;
            Project.PropertyChanged+=(_,e)=>{
                if(e.PropertyName==nameof(Project.Customer)){Customer=Project.Customer;Notify(nameof(Customer));}
                if(e.PropertyName==nameof(Project.CustomerId))CustomerSelection.SelectId(Project.CustomerId);
            };
            CustomerSelection.SetCustomers(Details.Customers,Project.CustomerId);
            Notify(nameof(Details));Notify(nameof(Project));Notify(nameof(Customer));
            Notify(nameof(DetailTitle));Notify(nameof(OverviewTitle));Notify(nameof(SaveLabel));
            SelectedPhase=Details.Phases.FirstOrDefault(p=>p.Id==phaseId)??Details.Phases.FirstOrDefault();
            Status=$"{Details.Phases.Length} fasi · {Details.Activities.Length} attività complessive. Le modifiche riguardano solo il gestionale.";
        }
        catch(Exception ex){Status=ex.Message;}
        finally{Busy=false;Notify(nameof(Status));Notify(nameof(CanEdit));Commands();}
    }
    public async Task ReloadCustomers(Customer? selected)
    {
        var selectedId=selected?.Id??Project?.CustomerId??"";
        try
        {
            var catalog=await business.Send<ProjectCatalog>(new("ProjectCatalog"));
            customers.Replace(catalog.Customers);
            if(Details!=null) Details=Details with{Customers=catalog.Customers};
            Notify(nameof(Details));
            if(selected!=null)CustomerSelection.Filter="";
            CustomerSelection.SetCustomers(catalog.Customers,selectedId);
            if(Project!=null) Project.CustomerId=selectedId;
            Status="Anagrafica aggiornata. Salva per confermare l'associazione.";
        }
        catch(Exception ex){Status=ex.Message;}
        Notify(nameof(Status));
    }
}

