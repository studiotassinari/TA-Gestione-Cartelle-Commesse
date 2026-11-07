using TA.Server.Business;
using TA_Gestione_Cartelle_Commesse.Contracts;
internal static class ProjectStructureChecks
{
    public static void Run(string folder)
    {
        var store=new BusinessStore(folder);
        var project=new WorkProject("source1","C26001","Cliente — Argomento");
        var other=new WorkProject("source2","C26002","Altra commessa");
        var service=new BusinessService(store,()=>[project,other],s=>s,()=>["utente"]);
        T Do<T>(BusinessCommand c,bool admin=true)=>(T)service.Execute(c,"utente",admin);
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        void Reject(Action action){try{action();}catch(Exception ex)when(ex is ArgumentException or UnauthorizedAccessException or IOException){return;}throw new Exception("Vincolo non verificato");}
        var customer=Do<Customer>(new("SaveCustomer",Customer:new("","Cliente prova")));
        Reject(()=>Do<WorkProject>(new("SaveProjectDetails",Project:project)));
        project=Do<WorkProject>(new("SaveProjectDetails",Project:project with{CustomerId=customer.Id,Subject="Progetto",Location="Rimini",Status="Closed"}));
        Check(project.Year==2026&&project.Number==1&&project.HoursMode=="Blocked","Metadati o autorizzazioni alterati");
        Reject(()=>Do<WorkProject>(new("SaveProjectDetails",Project:project),false));
        var phase=Do<WorkPhase>(new("SavePhase",Phase:new("",project.Id,"Progettazione")));
        Reject(()=>Do<WorkPhase>(new("SavePhase",Phase:new("",project.Id,"progettazione"))));
        Reject(()=>Do<WorkPhase>(new("SavePhase",Phase:phase with{ProjectId=other.Id})));
        var day=new DateOnly(2020,1,1);
        var activity=Do<WorkActivity>(new("SaveActivity",Activity:new("",project.Id,phase.Name,"Storico","utente",day,day,60,PhaseId:phase.Id)));
        Reject(()=>Do<WorkActivity>(new("SaveActivity",Activity:activity with{ProjectId=other.Id})));
        Reject(()=>Do<WorkPhase>(new("SavePhase",Phase:phase),false));
        phase=Do<WorkPhase>(new("SavePhase",Phase:phase with{Name="Progetto definitivo"}));
        Reject(()=>Do<WorkPhase>(new("SavePhase",Phase:phase with{Revision=1})));
        var detail=Do<ProjectDetails>(new("ProjectDetail",Id:project.Id),false);
        Check(detail.Customer?.Id==customer.Id&&detail.Activities.Single().Phase==phase.Name&&!detail.CanManage,"Dettaglio o rinomina fase incoerente");
        Check(Do<BusinessSnapshot>(new("Snapshot",new(new(2026,1,1),new(2026,1,31)))).Activities.Length==0&&detail.Activities.Length==1,"Il dettaglio eredita il filtro Gantt");
        var legacy=new WorkActivity(Guid.NewGuid().ToString("N"),project.Id,"Preliminare","Vecchia attività","utente",day,day,20,Revision:1);
        store.Save("activity",legacy.Id,0,legacy,"test","legacy");
        detail=Do<ProjectDetails>(new("ProjectDetail",Id:project.Id));
        var migrated=detail.Activities.Single(a=>a.Id==legacy.Id);
        Check(migrated.PhaseId.Length>0&&detail.Phases.Length==2,"Migrazione fasi non riuscita");
        var changes=store.ChangeNumber;
        Do<ProjectDetails>(new("ProjectDetail",Id:project.Id));
        Check(store.ChangeNumber==changes,"Migrazione non idempotente");
        var reopened=new BusinessService(new(folder),()=>[project,other],s=>s);
        var persisted=(ProjectDetails)reopened.Execute(new("ProjectDetail",Id:project.Id),"utente",false);
        Check(persisted.Phases.Length==2&&persisted.Activities.Length==2,"Relazioni non persistite");
        Console.WriteLine("ProjectStructureChecks: cliente, fasi, attività, permessi, storico, migrazione e concorrenza verificati.");
    }
}
