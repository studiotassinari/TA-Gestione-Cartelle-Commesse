using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// La vista non accede ai file: legge e salva esclusivamente attraverso il servizio autenticato.
public sealed class ProjectDetailViewModel : Observable
{
    private readonly BusinessViewModel business;
    private readonly string id;
    private readonly CustomerDirectory customers = new();
    public BusinessViewModel Business => business;
    public event Action? CustomerManagementRequested;
    public Command ManageCustomerCommand {get;}
    public ProjectDetailViewModel(BusinessViewModel business,string id)
    {
        this.business=business;this.id=id;
        ManageCustomerCommand=new(()=>CustomerManagementRequested?.Invoke(),()=>CanEdit);
        RefreshCommand=new(async()=>await Refresh(),()=>!Busy);
        SaveProjectCommand=new(async()=>await Execute(()=>new("SaveProjectDetails",Project:Project!.ToModel())),()=>CanEdit&&Project!=null);
        SavePhaseCommand=new(async()=>await Execute(()=>new("SavePhase",Phase:Phase!.ToModel())),()=>CanEdit&&Phase!=null);
        SaveActivityCommand=new(async()=>await Execute(()=>new("SaveActivity",Activity:Activity!.ToModel())),()=>CanEdit&&Activity!=null);
        NewPhaseCommand=new(()=>{SelectedPhase=null;Phase=new(new("",id,""));Notify(nameof(Phase));},()=>CanEdit);
        NewActivityCommand=new(()=>{
            var day=DateOnly.FromDateTime(DateTime.Today);
            Activity=new(new("",id,SelectedPhase!.Name,"","",day,day,0,PhaseId:SelectedPhase.Id));Notify(nameof(Activity));
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
    public bool Busy {get;private set;}
    public bool CanEdit=>!Busy&&Details?.CanManage==true;
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
            Project.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(Project.Customer)){Customer=Project.Customer;Notify(nameof(Customer));}};
            Notify(nameof(Details));Notify(nameof(Project));Notify(nameof(Customer));
            SelectedPhase=Details.Phases.FirstOrDefault(p=>p.Id==phaseId)??Details.Phases.FirstOrDefault();
            Status=$"{Details.Phases.Length} fasi · {Details.Activities.Length} attività complessive. Le modifiche riguardano solo il gestionale.";
        }
        catch(Exception ex){Status=ex.Message;}
        finally{Busy=false;Notify(nameof(Status));Notify(nameof(CanEdit));Commands();}
    }
    public async Task ReloadCustomers(Customer? selected)
    {
        try
        {
            var catalog=await business.Send<ProjectCatalog>(new("ProjectCatalog"));
            customers.Replace(catalog.Customers);
            if(Details!=null) Details=Details with{Customers=catalog.Customers};
            if(Project!=null) Project.CustomerId=selected?.Id??Project.CustomerId;
            Notify(nameof(Details)); Status="Anagrafica aggiornata. Salva la commessa per confermare l'associazione.";
        }
        catch(Exception ex){Status=ex.Message;}
        Notify(nameof(Status));
    }
}

