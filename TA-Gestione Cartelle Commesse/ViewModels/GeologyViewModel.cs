using System.Collections.ObjectModel;
using System.IO;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;

namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Il filtro opera sulla fotografia caricata: digitare non genera richieste o letture di documenti.
public sealed class GeologyViewModel : Observable, IDisposable
{
    private readonly Func<CancellationToken, Task<GeologyCatalog>>? load;
    private readonly CancellationTokenSource lifetime = new();
    private List<GeologicalReport> all = [];
    private string search = "", status = "Apri il catalogo delle relazioni dal server.", kind = "Tutte", location = "Tutte";
    private GeologicalReport? selected;
    private bool busy;
    public GeologyViewModel(Func<CancellationToken, Task<GeologyCatalog>>? loader = null)
    {
        load = loader;
        if (loader == null) status = "Collegare il client al server per caricare il catalogo delle relazioni.";
        RefreshCommand = new(() => _ = LoadAsync(), () => !busy && load != null);
    }
    public ObservableCollection<GeologicalReport> Rows { get; } = [];
    public string[] Kinds { get; } = ["Tutte", "Geologica", "Geotecnica"];
    public string[] Locations { get; } = ["Tutte", "Localizzate", "Da localizzare"];
    public string Search { get => search; set { search = value; Notify(); ApplyFilter(); } }
    public string Kind { get => kind; set { kind = value; Notify(); ApplyFilter(); } }
    public string Location { get => location; set { location = value; Notify(); ApplyFilter(); } }
    public GeologicalReport? Selected { get => selected; set { if (selected == value) return; selected = value; Notify(); SelectionChanged?.Invoke(); } }
    public string Status { get => status; private set { status = value; Notify(); } }
    public string CountText => $"{Rows.Count} relazioni su {all.Count} · {Rows.Count(r => r.HasPosition)} sulla mappa · {Rows.Count(r => !r.HasPosition)} da localizzare";
    public DateTime? UpdatedUtc { get; private set; }
    public Command RefreshCommand { get; }
    public event Action? RowsChanged;
    public event Action? SelectionChanged;

    public async Task LoadAsync()
    {
        if (load == null || busy || lifetime.IsCancellationRequested) return;
        busy = true; RefreshCommand.Refresh(); Status = "Caricamento del catalogo dal server...";
        try
        {
            var catalog = await load(lifetime.Token);
            if (!lifetime.IsCancellationRequested) Apply(catalog);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Status = "Catalogo non aggiornato: " + ex.Message + (all.Count > 0 ? " Dati precedenti conservati." : ""); }
        finally { busy = false; RefreshCommand.Refresh(); }
    }
    public void Apply(GeologyCatalog catalog)
    {
        if (catalog.SchemaVersion != 1) throw new InvalidDataException("Versione del catalogo geologico non supportata.");
        all = catalog.Reports.OrderBy(r => r.Municipality, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.ProjectCode).ThenBy(r => r.Title).ToList();
        UpdatedUtc = catalog.UpdatedUtc; Notify(nameof(UpdatedUtc));
        Status = "Catalogo aggiornato il " + catalog.UpdatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") + ". " + string.Join(" · ", catalog.Warnings.Take(3));
        ApplyFilter();
    }
    private void ApplyFilter()
    {
        var id = Selected?.Id;
        var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filtered = all.Where(r => (kind == "Tutte" || r.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)) &&
            (location == "Tutte" || (location == "Localizzate" ? r.HasPosition : !r.HasPosition)) &&
            terms.All(term => string.Join(' ', r.Title, r.FileName, r.Municipality, r.Address, r.ProjectCode, r.Client, r.Author, r.Kind, r.Notes)
                .Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        Rows.Clear(); foreach (var row in filtered) Rows.Add(row);
        Selected = Rows.FirstOrDefault(r => r.Id == id);
        Notify(nameof(CountText)); RowsChanged?.Invoke();
    }
    public void Select(string id) => Selected = Rows.FirstOrDefault(r => r.Id == id);
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
}
