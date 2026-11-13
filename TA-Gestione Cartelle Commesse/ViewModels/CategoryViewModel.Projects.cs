using System.Collections.ObjectModel;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

public sealed partial class CategoryViewModel
{
    private ProjectCatalog? catalog;
    private readonly CustomerDirectory customers = new();
    public bool IsProjectCategory => prefix is 'C' or 'G' or 'A';
    public ObservableCollection<WorkProjectViewModel> Projects { get; } = [];
    public string ListCaption { get; private set; } = "";
    public WorkProjectViewModel? SelectedProject
    {
        get => Projects.FirstOrDefault(p => p.Id == Selected?.Id);
        set => Selected = value?.Folder;
    }
    public event Action<WorkProjectViewModel>? ProjectRequested, CustomerRequested, ArchiveRequested, CurriculumRequested;
    public bool HasCode(string code) => all.Any(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
    // Ordinamento dell'elenco: predefinito per anno e numero, dalle più recenti. Vale su tutte le voci, prima del limite di 50.
    public string SortColumn { get; private set; } = "Code";
    public bool SortDescending { get; private set; } = true;
    public event Action? SortChanged;
    private static readonly Dictionary<string, string> SortNames = new()
    {
        ["Code"] = "anno e numero", ["Year"] = "anno", ["Number"] = "numero", ["Client"] = "cliente", ["Subject"] = "argomento",
        ["Size"] = "dimensione", ["Activity"] = "ultima attività", ["Quote"] = "preventivo", ["State"] = "stato"
    };
    // Stessa colonna: inverte il verso. Nuova colonna: testi dalla A alla Z, numeri e date dal valore più alto.
    public void SortBy(string column)
    {
        if (!SortNames.ContainsKey(column)) return;
        if (column == SortColumn) SortDescending = !SortDescending;
        else { SortColumn = column; SortDescending = column is "Code" or "Year" or "Number" or "Size" or "Activity"; }
        ApplyFilter(); SortChanged?.Invoke();
    }
    public void ApplyProjectCatalog(ProjectCatalog value)
    {
        value=value with{Projects=value.Projects.Where(p=>p.Category==prefix.ToString()).ToArray()};
        // Una risposta partita prima del salvataggio non deve ripristinare una revisione precedente.
        if(catalog is {} previous)
        {
            var projects=previous.Projects.ToDictionary(p=>p.Id);
            var clients=previous.Customers.ToDictionary(c=>c.Id);
            value=value with {
                Projects=value.Projects.Select(p=>projects.TryGetValue(p.Id,out var old)&&old.Revision>p.Revision?old:p).ToArray(),
                Customers=value.Customers.Select(c=>clients.TryGetValue(c.Id,out var old)&&old.Revision>c.Revision?old:c).ToArray()
            };
        }
        catalog = value; customers.Replace(value.Customers); ApplyFilter();
    }
    public void ApplyProjectChange(WorkProject project,Customer[]? clients)
    {
        if(project.Category!=prefix.ToString())return;
        var current=catalog??new ProjectCatalog([],[],false);
        var merged=current.Customers.ToDictionary(c=>c.Id);
        foreach(var customer in clients??[])
            if(!merged.TryGetValue(customer.Id,out var old)||customer.Revision>=old.Revision)merged[customer.Id]=customer;
        ApplyProjectCatalog(current with {
            Projects=current.Projects.Where(p=>p.Id!=project.Id).Append(project).ToArray(),
            Customers=merged.Values.ToArray()
        });
    }
    public void ApplyCustomerChange(Customer customer)
    {
        var current=catalog??new ProjectCatalog([],[],false);
        ApplyProjectCatalog(current with{Customers=current.Customers.Where(c=>c.Id!=customer.Id).Append(customer).ToArray()});
    }
    private void ApplyProjectFilter()
    {
        var selectedId = Selected?.Id;
        var byId = catalog?.Projects.ToDictionary(p => p.Id) ?? [];
        var pairs = all.Select(folder =>
        {
            if (!byId.TryGetValue(folder.Id, out var model))
            {
                int.TryParse(folder.Year, out var year); int.TryParse(folder.Number, out var number);
                if (year > 0 && year < 100) year += 2000;
                model = new(folder.Id, folder.Code, folder.Subject, Year: year, Number: number,
                    Subject: folder.Subject, SourceCustomerName: folder.Client,
                    LastActivityUtc: folder.Inspection?.LastFileWriteUtc ?? folder.ArchiveModifiedUtc,Category:prefix.ToString());
            }
            return (folder, model);
        });
        // Ogni parola scritta deve comparire in codice, anno, numero, argomento o cliente.
        var words = Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filtered = pairs.Where(p =>
        {
            var target = p.model.Code + " " + p.model.Year + " " + p.model.Number.ToString("D3") + " " + p.model.Number + " " + p.model.Subject + " " +
                p.model.SourceCustomerName + " " + customers.Find(p.model.CustomerId)?.Name;
            return words.All(word => target.Contains(word, StringComparison.OrdinalIgnoreCase));
        }).ToList();
        IOrderedEnumerable<(FolderRecord folder, WorkProject model)> Order<TKey>(Func<(FolderRecord folder, WorkProject model), TKey> key, IComparer<TKey>? comparer = null) =>
            SortDescending ? filtered.OrderByDescending(key, comparer) : filtered.OrderBy(key, comparer);
        var text = StringComparer.CurrentCultureIgnoreCase;
        var ordered = SortColumn switch
        {
            "Year" => Order(p => (long)p.model.Year),
            "Number" => Order(p => (long)p.model.Number),
            "Client" => Order(p => customers.Find(p.model.CustomerId)?.Name ?? p.model.SourceCustomerName ?? "", text),
            "Subject" => Order(p => p.model.Subject ?? "", text),
            "Size" => Order(p => p.folder.ArchiveBytes ?? p.folder.Inspection?.TotalBytes ?? -1L),
            "Activity" => Order(p => p.model.LastActivityUtc ?? DateTime.MinValue),
            "Quote" => Order(p => (long)QuoteIndicator.Level(p.folder.Inspection?.QuoteStatus)),
            "State" => Order(p => p.model.Status ?? "", text),
            _ => Order(p => p.model.Year * 100000L + p.model.Number)
        };
        // A parità di valore: anno e numero, dalle più recenti.
        var candidates = ordered.ThenByDescending(p => p.model.Year).ThenByDescending(p => p.model.Number).ThenBy(p => p.model.Id, StringComparer.Ordinal).ToList();
        Selected = null;
        foreach (var row in Projects) row.Dispose();
        Projects.Clear(); Records.Clear();
        foreach (var (folder, model) in candidates.Take(50))
        {
            var row = new WorkProjectViewModel(model, customers, folder);
            row.ArchiveRequested += p => ArchiveRequested?.Invoke(p);
            row.EditRequested += p => ProjectRequested?.Invoke(p);
            row.CustomerRequested += p => CustomerRequested?.Invoke(p);
            row.CurriculumRequested += p => CurriculumRequested?.Invoke(p);
            row.FolderRequested += p => { Selected = p.Folder; OpenFolderCommand.Execute(null); };
            Projects.Add(row); Records.Add(folder);
        }
        Selected = Records.FirstOrDefault(r => r.Id == selectedId);
        var label=prefix=='C'?"commesse":prefix=='G'?"gare":"voci in archivio";
        ListCaption = $"{Projects.Count} di {candidates.Count} {label} · ordine per {SortNames[SortColumn]}, {(SortDescending ? "decrescente" : "crescente")} · clic sull'intestazione di una colonna per cambiarlo";
        Notify(nameof(ListCaption)); Notify(nameof(SelectedProject));
    }
}
