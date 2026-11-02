using System.Collections.ObjectModel;
using System.IO;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;

namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Il filtro opera sulla fotografia caricata: digitare non genera richieste o letture di documenti.
public sealed partial class GeologyViewModel : Observable, IDisposable
{
    private readonly Func<CancellationToken, Task<GeologyCatalog>>? load;
    private readonly CancellationTokenSource lifetime = new();
    private List<GeologicalReport> all = [];
    private string search = "", status = "Apri il catalogo delle relazioni dal server.", kind = "Tutte", location = "Tutte";
    private GeologicalReport? selected;
    private bool busy, saving, opening;
    private readonly Func<GeologicalPositionUpdate, CancellationToken, Task>? save;
    public string Version { get; private set; } = "";
    public bool CanLocate => save != null && Selected != null && !busy && !saving;
    public GeologyViewModel(Func<CancellationToken, Task<GeologyCatalog>>? loader = null, Func<GeologicalPositionUpdate, CancellationToken, Task>? savePosition = null, Func<GeologicalGeocodeQuery, CancellationToken, Task<GeocodingResult>>? geocodeAddress = null)
    {
        load = loader; save = savePosition;
        geocode = geocodeAddress;
        OpenFolderCommand = new(OpenSelectedFolder, () => Selected?.OriginalAvailable == true && !string.IsNullOrWhiteSpace(Selected.PhysicalPath));
        if (loader == null) status = "Collegare il client al server per caricare il catalogo delle relazioni.";
        OpenDocumentCommand = new(() => _ = OpenSelectedAsync(), () => CanOpen);
        RefreshCommand = new(() => _ = LoadAsync(), () => !busy && load != null);
    }
    public ObservableCollection<GeologicalReport> Rows { get; } = [];
    public string[] Kinds { get; } = ["Tutte", "Geologica", "Geotecnica"];
    public string[] Locations { get; } = ["Tutte", "Localizzate", "Da localizzare"];
    public string Search { get => search; set { search = value; Notify(); ApplyFilter(); } }
    public string Kind { get => kind; set { kind = value; Notify(); ApplyFilter(); } }
    public string Location { get => location; set { location = value; Notify(); ApplyFilter(); } }
    public GeologicalReport? Selected { get => selected; set { if (selected == value) return; selected = value; Notify(); Notify(nameof(CanLocate)); Notify(nameof(CanGeocode)); Notify(nameof(CanOpen)); OpenFolderCommand.Refresh(); OpenDocumentCommand.Refresh(); SelectionChanged?.Invoke(); } }
    public string Status { get => status; private set { status = value; Notify(); } }
    public string CountText => $"{Rows.Count} relazioni su {all.Count} · {Rows.Count(r => r.HasPosition)} sulla mappa · {Rows.Count(r => !r.HasPosition)} da localizzare";
    public DateTime? UpdatedUtc { get; private set; }
    public Command RefreshCommand { get; }
    public Command OpenDocumentCommand { get; }
    public bool CanOpen => Selected?.OriginalAvailable == true && !string.IsNullOrWhiteSpace(Selected.PhysicalPath) && !opening;
    // Solo un gesto esplicito apre il documento nell'applicazione associata, su una copia locale in sola lettura.
    public async Task OpenSelectedAsync()
    {
        if (!CanOpen || Selected == null || lifetime.IsCancellationRequested) return;
        var report = Selected; opening = true; Notify(nameof(CanOpen)); OpenDocumentCommand.Refresh();
        Status = "Preparazione della copia in sola lettura: " + report.FileName;
        try
        {
            var path = await Services.GeologyDocumentCopy.PrepareAsync(report, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            Status = "Documento aperto in copia locale di sola lettura. Originale: " + report.PhysicalPath;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Status = "Documento non aperto: " + ex.Message; }
        finally { opening = false; Notify(nameof(CanOpen)); OpenDocumentCommand.Refresh(); }
    }
    public event Action? RowsChanged;
    public event Action? SelectionChanged;

    public async Task LoadAsync()
    {
        if (load == null || busy || lifetime.IsCancellationRequested) return;
        busy = true; Notify(nameof(CanLocate)); Notify(nameof(CanGeocode)); RefreshCommand.Refresh(); Status = "Caricamento del catalogo dal server...";
        try
        {
            var catalog = await load(lifetime.Token);
            if (!lifetime.IsCancellationRequested) Apply(catalog);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Status = "Catalogo non aggiornato: " + ex.Message + (all.Count > 0 ? " Dati precedenti conservati." : ""); }
        finally { busy = false; Notify(nameof(CanLocate)); Notify(nameof(CanGeocode)); RefreshCommand.Refresh(); }
    }
    public void Apply(GeologyCatalog catalog)
    {
        if (catalog.SchemaVersion != 1) throw new InvalidDataException("Versione del catalogo geologico non supportata.");
        Version = catalog.Version ?? "";
        all = catalog.Reports.OrderBy(r => r.Municipality, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.ProjectCode).ThenBy(r => r.Title).ToList();
        UpdatedUtc = catalog.UpdatedUtc; Notify(nameof(UpdatedUtc));
        Status = "Catalogo aggiornato il " + catalog.UpdatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") + ". " + string.Join(" · ", catalog.Warnings.Take(3));
        ApplyFilter();
    }
    private void ApplyFilter()
    {
        var id = Selected?.Id;
        var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filtered = all.Where(r => (kind == "Tutte" || r.Kind.Contains(kind, StringComparison.OrdinalIgnoreCase)) &&
            (location == "Tutte" || (location == "Localizzate" ? r.HasPosition : !r.HasPosition)) &&
            terms.All(term => string.Join(' ', r.Title, r.FileName, r.Municipality, r.Address, r.ProjectCode, r.Client, r.Author, r.Kind, r.Notes, r.DossierTitle, r.AnalysisStatus)
                .Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        Rows.Clear(); foreach (var row in filtered) Rows.Add(row);
        Selected = Rows.FirstOrDefault(r => r.Id == id);
        Notify(nameof(CountText)); RowsChanged?.Invoke();
    }
    public async Task SavePositionAsync(GeologicalPositionUpdate change)
    {
        if (!CanLocate || save == null) return;
        saving = true; Notify(nameof(CanLocate)); Notify(nameof(CanGeocode));
        try { await save(change, lifetime.Token); await LoadAsync(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Status = "Posizione non salvata: " + ex.Message; }
        finally { saving = false; Notify(nameof(CanLocate)); Notify(nameof(CanGeocode)); }
    }
    public void Select(string id) => Selected = Rows.FirstOrDefault(r => r.Id == id);
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
}
