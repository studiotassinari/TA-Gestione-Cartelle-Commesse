using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Mantiene i dati solo in memoria: il server decide identità, autorizzazioni e salvataggi.
public sealed class BusinessViewModel : Observable, IDisposable
{
    private readonly Func<BusinessCommand, CancellationToken, Task<System.Text.Json.JsonElement>> send;
    public BusinessViewModel(ServerConnection connection) : this((command, token) => connection.SendAsync<System.Text.Json.JsonElement>(new("Business", Business: command), token)) { }
    public BusinessViewModel(Func<BusinessCommand, CancellationToken, Task<System.Text.Json.JsonElement>> transport) { send = transport; }
    private readonly CancellationTokenSource lifetime = new();
    public BusinessSnapshot? Snapshot { get; private set; }
    private string status = "Caricamento del gestionale…";
    public string Status { get => status; private set { status = value; Notify(); } }
    private bool busy;
    public bool Busy { get => busy; private set { busy = value; Notify(); Notify(nameof(CanEdit)); } }
    public bool CanEdit => !Busy && Snapshot?.CanManage == true;
    public DateTime From { get; set; } = DateTime.Today.AddDays(-(int)(DateTime.Today.DayOfWeek + 6) % 7);
    public DateTime To { get; set; } = DateTime.Today.AddDays(30);
    public event Action? Updated;
    public event Action<WorkProject, Customer[]?>? ProjectChanged;
    public event Action<Customer>? CustomerChanged;
    public event Action<ProjectCatalog>? CatalogChanged;
    public async Task<T> Send<T>(BusinessCommand command)
    {
        var result = await send(command, lifetime.Token);
        var value = System.Text.Json.JsonSerializer.Deserialize<T>(result) ?? throw new InvalidOperationException("Risposta gestionale vuota.");
        // Pubblica solo risposte confermate dal server, mai le bozze del modulo.
        if (command.Action is "SaveProjectDetails" or "SaveProject")
            ProjectChanged?.Invoke(System.Text.Json.JsonSerializer.Deserialize<WorkProject>(result)!, null);
        else if (command.Action == "SaveCustomer")
            CustomerChanged?.Invoke(System.Text.Json.JsonSerializer.Deserialize<Customer>(result)!);
        else if (command.Action == "ProjectDetail" && value is ProjectDetails detail)
            ProjectChanged?.Invoke(detail.Project, detail.Customers);
        else if (command.Action == "Snapshot" && value is BusinessSnapshot snapshot)
            CatalogChanged?.Invoke(new(snapshot.Projects, snapshot.Customers, snapshot.CanManage));
        return value;
    }
    public async Task Refresh()
    {
        await Run(async () =>
        {
            Snapshot = await Send<BusinessSnapshot>(new("Snapshot", new(DateOnly.FromDateTime(From), DateOnly.FromDateTime(To))));
            Notify(nameof(Snapshot)); Notify(nameof(CanEdit)); Updated?.Invoke();
            Status = $"{Snapshot.User} · {Snapshot.Customers.Length} clienti · {Snapshot.Activities.Length} attività nel periodo. {Snapshot.BackupStatus}";
        });
    }
    public async Task Run(Func<Task> operation)
    {
        if (Busy) return;
        Busy = true;
        try { await operation(); }
        catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) Status = "Operazione interrotta: aggiornare prima di riprovare."; }
        catch (Exception ex) { Status = ex.Message; }
        finally { Busy = false; }
    }
    public async Task Save(BusinessCommand command)
    {
        var saved = false;
        await Run(async () => { await Send<System.Text.Json.JsonElement>(command); saved = true; });
        if (saved) await Refresh();
    }
    private bool disposed;
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); lifetime.Dispose(); ProjectChanged=null;CustomerChanged=null;CatalogChanged=null; }
}
