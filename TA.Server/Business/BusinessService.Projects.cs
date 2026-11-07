using System.Security.Cryptography;
using System.Text;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.Business;

public sealed partial class BusinessService
{
    private WorkPhase[] Phases()=>Store.All<WorkPhase>("phase").OrderBy(p=>p.Order).ThenBy(p=>p.Name).ToArray();
    private WorkActivity[] Activities()
    {
        var phases=Phases().ToDictionary(p=>p.Id);
        return Store.All<WorkActivity>("activity").Select(a=>phases.TryGetValue(a.PhaseId,out var p)?a with{Phase=p.Name}:a).ToArray();
    }
    // Identificativi deterministici rendono ripetibile il recupero delle vecchie fasi testuali.
    private WorkPhase ResolvePhase(string projectId,string phaseId,string name,string actor)
    {
        if(!string.IsNullOrEmpty(phaseId))
        {
            var phase=Store.Find<WorkPhase>("phase",phaseId)??throw new ArgumentException("Fase non presente.");
            if(phase.ProjectId!=projectId)throw new ArgumentException("La fase appartiene a un'altra commessa.");
            return phase;
        }
        Required(name,"Fase",100);
        var existing=Phases().FirstOrDefault(p=>p.ProjectId==projectId&&p.Name.Equals(name.Trim(),StringComparison.OrdinalIgnoreCase));
        if(existing!=null)return existing;
        var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(projectId+"\n"+name.Trim().ToUpperInvariant())).AsSpan(0,16)).ToString("N");
        return SavePhase(new(id,projectId,name.Trim()),actor);
    }
    private void EnsurePhases()
    {
        foreach(var old in Store.All<WorkActivity>("activity").Where(a=>string.IsNullOrEmpty(a.PhaseId)))
        {
            var phase=ResolvePhase(old.ProjectId,"",old.Phase,"Migrazione fasi");
            Store.Save("activity",old.Id,old.Revision,old with{PhaseId=phase.Id,Revision=old.Revision+1},"Migrazione fasi","PhaseMigration");
        }
    }
    private WorkPhase SavePhase(WorkPhase phase,string actor)
    {
        GetProject(phase.ProjectId);Required(phase.Name,"Nome fase",100);
        if(phase.Description==null||phase.Description.Length>2000||phase.Order<0)throw new ArgumentException("Descrizione o ordine della fase non validi.");
        var old=Store.Find<WorkPhase>("phase",phase.Id);
        if(old!=null&&old.ProjectId!=phase.ProjectId)throw new ArgumentException("Non è possibile spostare una fase in un'altra commessa.");
        if(Phases().Any(p=>p.Id!=phase.Id&&p.ProjectId==phase.ProjectId&&p.Name.Equals(phase.Name.Trim(),StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Nome fase già presente nella commessa.");
        var saved=phase with{Id=Id(phase.Id),Name=phase.Name.Trim(),Revision=phase.Revision+1};
        Store.Save("phase",saved.Id,phase.Revision,saved,actor,"Phase");return saved;
    }
    private WorkProject SaveProjectDetails(WorkProject project,string actor)
    {
        var known=GetProject(project.Id);
        Required(project.CustomerId,"Cliente associato",200);Required(project.Subject,"Argomento",1000);
        var customer=Store.Find<Customer>("customer",project.CustomerId)??throw new ArgumentException("Cliente non presente.");
        if(project.Location==null||project.Location.Length>1000||project.Status is not ("" or "Open" or "Closed" or "Invoiced"))throw new ArgumentException("Luogo o stato non validi.");
        // L'aggiornamento descrittivo non modifica le autorizzazioni per l'inserimento ore.
        var saved=known with{CustomerId=customer.Id,Subject=project.Subject.Trim(),Title=customer.Name+" — "+project.Subject.Trim(),Location=project.Location.Trim(),Status=project.Status,Revision=project.Revision+1};
        Store.Save("project",saved.Id,project.Revision,saved,actor,"ProjectDetails");return saved;
    }
}
