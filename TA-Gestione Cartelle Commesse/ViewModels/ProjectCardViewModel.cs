using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Scheda della commessa di GeCo in GeCa (2.42.0), come il dettaglio di GeCo2027 senza costi e ricavi: dati generali, fasi, team,
// cartella, commesse collegate e stato dell'accesso. Che cosa l'utente vede e può modificare lo decide il servizio con le regole
// di GeCo2027 (ServizioCommessa, AccessoCommesse, ServizioRichiesteAccesso): qui si tengono l'ultima scheda letta, i messaggi e
// l'aggiornamento in tempo reale. Quando la commessa cambia in GeCo2027 o in un'altra finestra arriva l'evento tecnico
// «CommessaAggiornata» e la scheda si rilegge, ma non mentre l'utente sta modificando.
public sealed class ProjectCardViewModel : IDisposable
{
    public const string Changed = "CommessaAggiornata";
    private static readonly HashSet<ProjectCardViewModel> open = [];
    public static bool AnyOpen { get { lock (open) return open.Count > 0; } }
    public static void Dispatch(GeCoViewUpdates updates) { ProjectCardViewModel[] all; lock (open) all = [.. open]; foreach (var m in all) m.ViewsChanged(updates); }

    private readonly WindowGeCoGateway gateway;
    private readonly CancellationTokenSource lifetime = new();
    private int projectId;
    private string? code;
    private string? me;
    private GeCoViewRefresh? live;
    public ProjectCardViewModel(IGeCoGateway gateway, int projectId = 0, string? code = null)
    {
        this.gateway = new WindowGeCoGateway(gateway); this.projectId = projectId; this.code = code;
        Team = new ProjectTeamViewModel(gateway, projectId, code);
        lock (open) open.Add(this);
    }
    public ProjectTeamViewModel Team { get; }
    public GeCoProjectCard? Card { get; private set; }
    public GeCoAccessState? Access { get; private set; }
    public GeCoLinkRow? Folder { get; private set; }
    public string Message { get; private set; } = "";
    public string Error { get; private set; } = "";
    public string Done { get; private set; } = "";
    public bool Busy { get; private set; }
    // La vista lo imposta mentre una modifica è aperta: un aggiornamento esterno aspetta, così non si perde ciò che si scrive.
    public bool Editing { get; set; }
    public int ProjectId => projectId;
    public event Action? Updated;
    public GeCoViewRefresh Live => live ??= new(gateway.Window, () => Busy || Editing, LoadAsync, lifetime.Token);
    public void ViewsChanged(GeCoViewUpdates updates) => Live.Arrived(updates, u => u.Tipo == Changed && projectId > 0 && u.Commesse.Contains(projectId), me);

    public bool CanEdit => Card?.Permessi.Modifica == true && !Busy;
    public bool CanRequest => Access?.PuoRichiedere == true && !Busy;

    public async Task LoadAsync()
    {
        if (projectId <= 0 && string.IsNullOrWhiteSpace(code)) { Message = "Nessuna commessa selezionata."; Updated?.Invoke(); return; }
        Busy = true; Updated?.Invoke();
        try
        {
            me ??= (await gateway.Send<GeCoAccess>(new("Access"), lifetime.Token)) is { Recognized: true } a ? a.Name : null;
            Apply(await gateway.Send<GeCoProjectCardResult>(new("ProjectCard", IdCommessa: projectId, Testo: projectId > 0 ? null : code), lifetime.Token));
            Error = "";
            Live.Loaded();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Card = null; Error = Text(ex); }
        finally { Busy = false; Updated?.Invoke(); }
        if (Card != null && Team.Team == null && Team.Error.Length == 0 && !Team.Busy) await Team.ShowAsync(projectId, null);
    }

    private void Apply(GeCoProjectCardResult r)
    {
        Card = r.Scheda; Access = r.Accesso; Folder = r.Cartella; Message = r.Trovata ? "" : r.Message;
        if (Card != null) projectId = Card.Id;
    }

    public Task<bool> SaveAsync(GeCoProjectEdit edit) => RunAsync(new("SaveProject", IdCommessa: projectId, Commessa: edit with { Versione = Card?.Versione }), "Dati della commessa salvati: valgono subito anche in GeCo2027.");
    public Task<bool> AddPhaseAsync(GeCoPhaseEdit phase) => RunAsync(new("AddPhase", IdCommessa: projectId, Fase: phase), "Fase aggiunta.");
    public Task<bool> SavePhaseAsync(int id, GeCoPhaseEdit phase) => RunAsync(new("SavePhase", Id: id, IdCommessa: projectId, Fase: phase), "Fase salvata.");
    public Task<bool> RemovePhaseAsync(int id) => RunAsync(new("RemovePhase", Id: id, IdCommessa: projectId), "Fase eliminata.");
    public Task<bool> RequestAccessAsync(string reason) => RunAsync(new("RequestAccess", IdCommessa: projectId, Testo: reason),
        "Richiesta di accesso inviata: chi decide (amministratori, capocommessa, team manager) riceve la notifica.");

    // Il servizio risponde con la scheda aggiornata; dopo un rifiuto (permessi, controlli, modifica concorrente) si rilegge lo stato vero.
    private async Task<bool> RunAsync(GeCoCommand command, string done)
    {
        if (Card == null || Busy) return false;
        Busy = true; Done = ""; Updated?.Invoke();
        bool ok = false;
        try { Apply(await gateway.Send<GeCoProjectCardResult>(command, lifetime.Token)); Error = ""; Done = done; ok = true; }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = Text(ex); }
        finally { Busy = false; Updated?.Invoke(); }
        if (!ok) { var error = Error; await LoadAsync(); Error = error; Updated?.Invoke(); }
        return ok;
    }

    // Un servizio precedente alla 2.42.0 non conosce i comandi delle commesse.
    public static string Text(Exception ex) => ex.Message is "Comando non supportato." or "Comando GeCo sconosciuto."
        ? "Il server non gestisce ancora le commesse di GeCo: aggiornare il servizio alla versione 2.42.0 o successiva." : ex.Message;

    // Testi della scheda (sola presentazione).
    public static string Person(GeCoCardItem? p) => p?.Nome is { Length: > 0 } n ? n : "—";
    public static string Date(string d) => d.Length == 0 ? "—" : d;
    public string FolderText => Folder switch
    {
        null => "Situazione della cartella non disponibile (database comune non aggiornato).",
        { Collegamento: not null, Cartella: { Presente: true } f } => "Cartella collegata: " + f.Percorso,
        { Collegamento: not null, Cartella: { } f } => "Cartella collegata non più presente sul disco: " + f.Percorso,
        { Collegamento: not null } => "Cartella collegata non più rilevata.",
        { Proposta: { Cartella: { } f } p } => $"Proposta (da confermare in Collegamento cartelle): {f.Percorso} — {p.Motivo}",
        _ => "Nessuna cartella rilevata per la commessa.",
    };
    public string AccessText => Access switch
    {
        null => "",
        { Attribuita: true } a => "La commessa è tra le tue: " + a.Testo.ToLowerInvariant() + ".",
        { Richiesta: { Stato: "InAttesa" } } => "Richiesta di accesso in attesa di decisione.",
        { Richiesta: { Stato: "Rifiutata" } r } => "La richiesta di accesso è stata rifiutata" + (string.IsNullOrWhiteSpace(r.NotaDecisione) ? "." : ": " + r.NotaDecisione),
        { Richiesta: { Stato: "Revocata" } r } => "L'accesso è stato revocato" + (string.IsNullOrWhiteSpace(r.NotaRevoca) ? "." : ": " + r.NotaRevoca),
        _ => "La commessa non è tra le tue: la vedi in sola lettura. Con «Richiedi accesso» la chiedi a chi decide.",
    };

    public void Dispose()
    {
        lock (open) open.Remove(this);
        Team.Dispose();
        lifetime.Cancel(); lifetime.Dispose();
    }
}
