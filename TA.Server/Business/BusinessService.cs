using TA_Gestione_Cartelle_Commesse.Contracts;
using System.Security.Principal;
namespace TA.Server.Business;

public sealed partial class BusinessService
{
    public BusinessStore Store {get;}
    private readonly Func<WorkProject[]> available;
    private readonly Func<string,string> resolve;
    private readonly Func<string[]>? collaborators;
    public BusinessService(ServerSettings settings,ScanCoordinator scans):this(new(settings.InventoryFolder),()=>
        (scans.Get("C").State.Snapshot?.Records??[]).Where(r=>r.Code.Length>0).Select(ProjectMetadata.FromFolder).ToArray(),CanonicalUser,new CollaboratorDirectory(settings.CollaboratorsGroup).Members){}
    public BusinessService(BusinessStore store,Func<WorkProject[]> projects,Func<string,string> resolver,Func<string[]>? members=null){Store=store;available=projects;resolve=resolver;collaborators=members;}
    private static string CanonicalUser(string input)
    {
        var sid=(SecurityIdentifier)new NTAccount(input).Translate(typeof(SecurityIdentifier));
        var name=((NTAccount)sid.Translate(typeof(NTAccount))).Value;
        if(!name.StartsWith("INGTASSINARI\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Collaboratore esterno al dominio.");
        return name;
    }
    public WorkProject[] Projects()
    {
        var saved=Store.All<WorkProject>("project").ToDictionary(p=>p.Id);
        foreach(var p in available())
            if(!saved.TryGetValue(p.Id,out var old))saved.Add(p.Id,p);
            else saved[p.Id]=old with{Year=old.Year==0?p.Year:old.Year,Number=old.Number==0?p.Number:old.Number,SourceCustomerName=string.IsNullOrEmpty(old.SourceCustomerName)?p.SourceCustomerName:old.SourceCustomerName,LastActivityUtc=p.LastActivityUtc};
        var activity=Store.ProjectActivity();
        return saved.Values.Select(ProjectMetadata.Normalize).Select(p=>activity.TryGetValue(p.Id,out var at)&&(!p.LastActivityUtc.HasValue||at>p.LastActivityUtc)?p with{LastActivityUtc=at}:p).OrderByDescending(p=>p.Code,StringComparer.Ordinal).ToArray();
    }
    public object Execute(BusinessCommand command,string actor,bool manager)
    {
        if(string.IsNullOrWhiteSpace(actor))throw new UnauthorizedAccessException("Identità Windows mancante.");
        if(!manager && collaborators!=null && !collaborators().Contains(actor,StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Gestione attività riservata agli utenti del gruppo collaboratori aziendale.");
        return Store.Locked(()=>Handle(command,actor,manager));
    }
    private object Handle(BusinessCommand c,string actor,bool manager)
    {
        if(c.Action=="ProjectCatalog")return new ProjectCatalog(Projects(),Store.All<Customer>("customer"),manager);
        EnsurePhases();
        if(c.Action=="ProjectDetail")
        {
            var project=GetProject(c.Id??"");
            return new ProjectDetails(project,Store.Find<Customer>("customer",project.CustomerId),
                Phases().Where(p=>p.ProjectId==project.Id).ToArray(),Activities().Where(a=>a.ProjectId==project.Id).ToArray(),
                Store.All<Customer>("customer"),collaborators?.Invoke()??[],manager);
        }
        if(c.Action=="Snapshot")
        {
            var q=c.Query??throw new ArgumentException("Periodo mancante.");
            if(q.To<q.From || q.To.DayNumber-q.From.DayNumber>366)throw new ArgumentException("Selezionare un periodo fino a un anno.");
            var activities=Activities();
            var times=Store.All<WorkTime>("time");
            return new BusinessSnapshot(actor,manager,Store.All<Customer>("customer"),Projects(),activities.Where(a=>a.Start<=q.To&&a.End>=q.From).ToArray(),
                times.Where(t=>t.Day>=q.From&&t.Day<=q.To&&(manager||t.User.Equals(actor,StringComparison.OrdinalIgnoreCase))).ToArray(),
                collaborators?.Invoke() ?? activities.Select(a=>a.Assignee).Concat(times.Select(t=>t.User)).Append(actor).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),Store.BackupStatus,Phases());
        }
        if(c.Action=="SaveTime")return SaveTime(c.Time??throw new ArgumentException("Ore mancanti."),actor,manager);
        if(c.Action=="DeleteTime")
        {
            var item=Store.Find<WorkTime>("time",c.Id??"")??throw new ArgumentException("Riga assente.");
            if(!manager&&!item.User.Equals(actor,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Puoi modificare solo le tue ore.");
            Store.Delete("time",item.Id,c.Revision,actor);return true;
        }
        if(!manager)throw new UnauthorizedAccessException("Operazione riservata ad Antonio e Giuseppe.");
        switch(c.Action)
        {
            case "SavePhase":return SavePhase(c.Phase??throw new ArgumentException("Fase mancante."),actor);
            case "SaveProjectDetails":return SaveProjectDetails(c.Project??throw new ArgumentException("Commessa mancante."),actor);
            case "SaveCustomer":return SaveCustomer(c.Customer??throw new ArgumentException("Cliente mancante."),actor);
            case "SaveProject":
                var p=c.Project??throw new ArgumentException("Commessa mancante.");
                var known=Projects().SingleOrDefault(x=>x.Id==p.Id)??throw new ArgumentException("Commessa non censita.");
                if(p.HoursMode is not ("Blocked" or "Quote" or "Contract" or "Override"))throw new ArgumentException("Autorizzazione non valida.");
                if(p.HoursMode is "Quote" or "Contract")Required(p.Evidence,"Riferimento al documento accettato",1000);
                if(p.HoursMode=="Override")Required(p.Reason,"Motivazione della deroga",1000);
                if(p.CustomerId==null||p.Evidence==null||p.Reason==null||p.Evidence.Length>1000||p.Reason.Length>1000)throw new ArgumentException("Dati autorizzazione non validi.");
                if(p.CustomerId.Length>0&&Store.Find<Customer>("customer",p.CustomerId)==null)throw new ArgumentException("Cliente non presente.");
                var saved=known with{CustomerId=p.CustomerId,HoursMode=p.HoursMode,Evidence=p.Evidence,Reason=p.Reason,Until=p.Until,Revision=p.Revision+1};
                Store.Save("project",p.Id,p.Revision,saved,actor,"ProjectAuthorization");return saved;
            case "SaveActivity":
                var a=c.Activity??throw new ArgumentException("Attività mancante.");GetProject(a.ProjectId);
                Required(a.Title,"Attività",200);if(string.IsNullOrEmpty(a.PhaseId))Required(a.Phase,"Fase",100);Required(a.Assignee,"Collaboratore",200);
                if(a.End<a.Start || a.End.DayNumber-a.Start.DayNumber>3660 || a.PlannedMinutes is <0 or >10000000 || a.Progress is <0 or >100 || a.State is not("Planned" or "Active" or "Done" or "Suspended"))throw new ArgumentException("Date, ore previste o stato non validi.");
                var activity=a with{Id=Id(a.Id),Assignee=resolve(a.Assignee.Trim()),Revision=a.Revision+1};
                if(collaborators!=null && !collaborators().Contains(activity.Assignee,StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException("Il collaboratore non appartiene al gruppo aziendale oppure il suo account è disabilitato.");
                var previous=Store.Find<WorkActivity>("activity",activity.Id);
                if((previous?.Revision??0)!=a.Revision)throw new IOException("Attività modificata da un altro utente: aggiornare e riprovare.");
                if(previous!=null&&(previous.ProjectId!=activity.ProjectId||previous.Assignee!=activity.Assignee)&&Store.All<WorkTime>("time").Any(t=>t.ActivityId==activity.Id))
                    throw new ArgumentException("Una attività con ore registrate non può cambiare commessa o collaboratore.");
                var phase=ResolvePhase(activity.ProjectId,activity.PhaseId,activity.Phase,actor);
                activity=activity with{PhaseId=phase.Id,Phase=phase.Name};
                Store.Save("activity",activity.Id,a.Revision,activity,actor,"Activity");return activity;
            case "ImportMagoot":return CustomerImport.ImportCsv(this,c.Csv??"",actor);
            case "PreviewMagoot":return CustomerImport.ImportCsv(this,c.Csv??"",actor,preview:true);
            case "ImportFatture":return c.Excel is {} book ? FattureExcelImport.Import(this,book,actor,false) : CustomerImport.ImportCsv(this,c.Csv??"",actor,"FattureInCloud");
            case "PreviewFatture":return c.Excel is {} previewBook ? FattureExcelImport.Import(this,previewBook,actor,true) : CustomerImport.ImportCsv(this,c.Csv??"",actor,"FattureInCloud",true);
            default:throw new ArgumentException("Comando gestionale sconosciuto.");
        }
    }
    public Customer SaveCustomer(Customer input,string actor)
    {
        Required(input.Name,"Ragione sociale",200);
        foreach(var s in new[]{input.Vat,input.TaxCode,input.Address,input.Email})if(s==null||s.Length>1000)throw new ArgumentException("Campo cliente non valido o troppo lungo.");
        var old=Store.Find<Customer>("customer",input.Id);
        var customer=input with{Id=Id(input.Id),Name=input.Name.Trim(),Vat=Tax(input.Vat),TaxCode=Tax(input.TaxCode),Revision=input.Revision+1,Sources=old?.Sources??[]};
        if(Store.All<Customer>("customer").Any(x=>x.Id!=customer.Id&&(customer.Vat.Length>0&&Tax(x.Vat)==customer.Vat||customer.TaxCode.Length>0&&Tax(x.TaxCode)==customer.TaxCode)))throw new ArgumentException("Esiste già un cliente con questa partita IVA o codice fiscale.");
        Store.Save("customer",customer.Id,input.Revision,customer,actor,"Customer");return customer;
    }
    private WorkTime SaveTime(WorkTime t,string actor,bool manager)
    {
        var old=Store.Find<WorkTime>("time",t.Id);var owner=old?.User??actor;
        if(!manager&&!owner.Equals(actor,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Puoi modificare solo le tue ore.");
        var p=GetProject(t.ProjectId);
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow,"W. Europe Standard Time"));
        if(p.HoursMode=="Blocked" || p.Until is {} end&&(end<today||t.Day>end))throw new UnauthorizedAccessException("Inserimento ore non autorizzato per questa commessa. Rivolgersi ad Antonio o Giuseppe.");
        if(t.Day>today || t.Day.Year<2000 || t.Minutes is <1 or >1440)throw new ArgumentException("Data futura o durata non valida.");
        var a=Store.Find<WorkActivity>("activity",t.ActivityId)??throw new ArgumentException("Selezionare un'attività.");
        if(a.ProjectId!=t.ProjectId || !a.Assignee.Equals(owner,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Attività non assegnata al titolare delle ore o appartenente ad altra commessa.");
        Required(t.Description,"Descrizione del lavoro",2000);
        if(Store.All<WorkTime>("time").Where(x=>x.Id!=t.Id&&x.Day==t.Day&&x.User.Equals(owner,StringComparison.OrdinalIgnoreCase)).Sum(x=>x.Minutes)+t.Minutes>1440)throw new ArgumentException("Il totale giornaliero supera 24 ore.");
        var item=t with{Id=Id(t.Id),User=owner,Revision=t.Revision+1};Store.Save("time",item.Id,t.Revision,item,actor,"TimeEntry");return item;
    }
    private WorkProject GetProject(string id)=>Projects().SingleOrDefault(p=>p.Id==id)??throw new ArgumentException("Commessa non disponibile.");
    internal static string Tax(string text)=>new(text.Where(c=>!char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());
    private static string Id(string id)=>id==null?throw new ArgumentException("Identificativo mancante."):id.Length==0?Guid.NewGuid().ToString("N"):Guid.TryParse(id,out _)?id:throw new ArgumentException("Identificativo non valido.");
    private static void Required(string? value,string label,int max){if(string.IsNullOrWhiteSpace(value)||value.Length>max)throw new ArgumentException(label+" obbligatorio (massimo "+max+" caratteri).");}
}
