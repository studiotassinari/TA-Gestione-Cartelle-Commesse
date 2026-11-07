using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed partial class CustomerManagementViewModel
{
    private WorkProject[] projects=[];
    private WorkProject? selectedProject;
    public BusinessViewModel Business=>business;
    public Command OpenAssociatedProjectCommand {get;private set;}=null!;
    public event Action<WorkProject>? ProjectRequested;
    public WorkProject[] AssociatedProjects=>Selected is {} client
        ?projects.Where(p=>p.Category=="C"&&p.CustomerId.Length>0&&p.CustomerId==client.Id)
            .OrderByDescending(p=>p.Year).ThenByDescending(p=>p.Number).ThenBy(p=>p.Id).ToArray():[];
    public string AssociatedSummary=>Selected==null?"Seleziona un cliente per vedere le commesse associate."
        :AssociatedProjects.Length==0?"Nessuna commessa associata a questo cliente."
        :$"{AssociatedProjects.Length} commesse associate · tutte, senza limite di 50";
    public WorkProject? SelectedProject
    {
        get=>selectedProject;
        set{selectedProject=value;Notify();OpenAssociatedProjectCommand.Refresh();}
    }
    private void InitializeProjects()
    {
        OpenAssociatedProjectCommand=new(()=>{if(SelectedProject is {} project)ProjectRequested?.Invoke(project);},()=>!Busy&&SelectedProject!=null);
        business.ProjectChanged+=ProjectUpdated;
    }
    private void ProjectUpdated(WorkProject project,Customer[]? clients)
    {
        if(projects.FirstOrDefault(p=>p.Id==project.Id) is {} previous&&previous.Revision>project.Revision)return;
        projects=projects.Where(p=>p.Id!=project.Id).Append(project).ToArray();
        UpdateAssociatedProjects();
    }
    private void UpdateAssociatedProjects()
    {
        var id=SelectedProject?.Id;
        Notify(nameof(AssociatedProjects));Notify(nameof(AssociatedSummary));
        SelectedProject=AssociatedProjects.FirstOrDefault(p=>p.Id==id);
    }
    public void Dispose(){business.ProjectChanged-=ProjectUpdated;ProjectRequested=null;}
}
