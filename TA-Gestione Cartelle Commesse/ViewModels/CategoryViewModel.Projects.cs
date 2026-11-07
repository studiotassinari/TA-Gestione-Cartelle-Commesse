using System.Collections.ObjectModel;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

public sealed partial class CategoryViewModel
{
    private ProjectCatalog? catalog;
    private readonly CustomerDirectory customers = new();
    public bool IsProjectCategory => prefix == 'C';
    public ObservableCollection<WorkProjectViewModel> Projects { get; } = [];
    public string ListCaption { get; private set; } = "";
    public WorkProjectViewModel? SelectedProject
    {
        get => Projects.FirstOrDefault(p => p.Id == Selected?.Id);
        set => Selected = value?.Folder;
    }
    public event Action<WorkProjectViewModel>? ProjectRequested, CustomerRequested;
    public void ApplyProjectCatalog(ProjectCatalog value)
    {
        catalog = value; customers.Replace(value.Customers); ApplyFilter();
    }
    private void ApplyProjectFilter()
    {
        var selectedId = Selected?.Id;
        var byId = catalog?.Projects.ToDictionary(p => p.Id) ?? [];
        var candidates = all.Select(folder =>
        {
            if (!byId.TryGetValue(folder.Id, out var model))
            {
                int.TryParse(folder.Year, out var year); int.TryParse(folder.Number, out var number);
                if (year > 0 && year < 100) year += 2000;
                model = new(folder.Id, folder.Code, folder.Subject, Year: year, Number: number,
                    Subject: folder.Subject, SourceCustomerName: folder.Client,
                    LastActivityUtc: folder.Inspection?.LastFileWriteUtc ?? folder.ArchiveModifiedUtc);
            }
            return (folder, model);
        }).Where(p => (p.model.Code + " " + p.model.Subject + " " + p.model.SourceCustomerName + " " +
            customers.Find(p.model.CustomerId)?.Name).Contains(Filter, StringComparison.OrdinalIgnoreCase))
          .OrderByDescending(p => p.model.LastActivityUtc).ThenByDescending(p => p.model.Year)
          .ThenByDescending(p => p.model.Number).ThenBy(p => p.model.Id).ToList();
        Selected = null;
        foreach (var row in Projects) row.Dispose();
        Projects.Clear(); Records.Clear();
        foreach (var (folder, model) in candidates.Take(50))
        {
            var row = new WorkProjectViewModel(model, customers, folder);
            row.EditRequested += p => ProjectRequested?.Invoke(p);
            row.CustomerRequested += p => CustomerRequested?.Invoke(p);
            row.FolderRequested += p => { Selected = p.Folder; OpenFolderCommand.Execute(null); };
            Projects.Add(row); Records.Add(folder);
        }
        Selected = Records.FirstOrDefault(r => r.Id == selectedId);
        ListCaption = $"{Projects.Count} di {candidates.Count} commesse · ultima attività nota, più recenti prima";
        Notify(nameof(ListCaption)); Notify(nameof(SelectedProject));
    }
}
