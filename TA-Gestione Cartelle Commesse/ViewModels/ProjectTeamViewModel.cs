using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Team della commessa di GeCo (2.41.0), lo stesso della sezione «Team» di GeCo2027: chi lavora sulla commessa con il ruolo e,
// per chi può (lo decide il servizio con le regole della libreria), aggiunta e rimozione di persone e team permanenti. Qui
// si tengono soltanto l'ultimo stato letto, i messaggi e l'aggiornamento in tempo reale: quando il team cambia in GeCo2027 o
// in un'altra finestra di GeCa arriva l'evento tecnico «TeamModificato» (corsia push delle viste) e la sezione si rilegge.
public sealed class ProjectTeamViewModel : IDisposable
{
    public const string Changed = "TeamModificato";
    // Sezioni del team aperte: il client chiede gli eventi delle viste solo se c'è qualcuno che li ascolta.
    private static readonly HashSet<ProjectTeamViewModel> open = [];
    public static bool AnyOpen { get { lock (open) return open.Count > 0; } }
    public static void Dispatch(GeCoViewUpdates updates) { ProjectTeamViewModel[] all; lock (open) all = [.. open]; foreach (var m in all) m.ViewsChanged(updates); }

    private readonly WindowGeCoGateway gateway;
    private readonly CancellationTokenSource lifetime = new();
    private int projectId;
    private string? code;
    private string? me;
    private GeCoViewRefresh? live;
    public ProjectTeamViewModel(IGeCoGateway gateway, int projectId = 0, string? code = null)
    {
        this.gateway = new WindowGeCoGateway(gateway); this.projectId = projectId; this.code = code;
        lock (open) open.Add(this);
    }
    public GeCoProjectTeam? Team { get; private set; }
    // Messaggio informativo (commessa non trovata, database non aggiornato) ed errore dell'ultima operazione.
    public string Message { get; private set; } = "";
    public string Error { get; private set; } = "";
    public bool Busy { get; private set; }
    public bool CanEdit => Team?.PuoModificare == true && !Busy;
    public event Action? Updated;
    public GeCoViewRefresh Live => live ??= new(gateway.Window, () => Busy, LoadAsync, lifetime.Token);
    public void ViewsChanged(GeCoViewUpdates updates) => Live.Arrived(updates, u => u.Tipo == Changed && Team is { } t && u.Commesse.Contains(t.IdCommessa), me);

    // Commessa da mostrare: per Id (finestre di GeCo) o per codice della cartella (scheda della commessa di GeCa).
    public async Task ShowAsync(int id, string? folderCode)
    {
        projectId = id; code = folderCode; Team = null; Message = ""; Error = "";
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (projectId <= 0 && string.IsNullOrWhiteSpace(code)) { Team = null; Message = "Nessuna commessa selezionata."; Updated?.Invoke(); return; }
        Busy = true; Updated?.Invoke();
        try
        {
            me ??= (await gateway.Send<GeCoAccess>(new("Access"), lifetime.Token)) is { Recognized: true } a ? a.Name : null;
            var result = await gateway.Send<GeCoProjectTeamResult>(new("ProjectTeam", IdCommessa: projectId, Testo: projectId > 0 ? null : code), lifetime.Token);
            Team = result.Team; Message = result.Trovata ? "" : result.Message; Error = "";
            if (Team != null) projectId = Team.IdCommessa;
            Live.Loaded();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Team = null; Error = Text(ex); }
        finally { Busy = false; Updated?.Invoke(); }
    }

    public Task SetMemberAsync(int person, bool member) => SaveAsync(new("SetTeamMember", IdCommessa: projectId, IdCdc: person, Valore: member));
    public Task SetPermanentAsync(int team, bool assigned) => SaveAsync(new("SetPermanentTeam", IdCommessa: projectId, Id: team, Valore: assigned));

    // Il servizio risponde con il team aggiornato; dopo un rifiuto (permessi, modifica concorrente) si rilegge lo stato vero.
    private async Task SaveAsync(GeCoCommand command)
    {
        if (Team == null || Busy) return;
        Busy = true; Updated?.Invoke();
        bool reload = false;
        try { Team = await gateway.Send<GeCoProjectTeam>(command, lifetime.Token); Error = ""; }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = Text(ex); reload = true; }
        finally { Busy = false; Updated?.Invoke(); }
        if (reload) { var error = Error; await LoadAsync(); Error = error; Updated?.Invoke(); }
    }

    // Un servizio precedente alla 2.41.0 non conosce i comandi del team.
    public static string Text(Exception ex) => ex.Message is "Comando non supportato." or "Comando GeCo sconosciuto."
        ? "Il server non gestisce ancora il team della commessa: aggiornare il servizio alla versione 2.41.0 o successiva." : ex.Message;

    // Persone che si possono aggiungere: candidati non ancora indicati uno per uno.
    public GeCoTeamPerson[] Addable => Team is { Candidati: { } c } t ? c.Where(p => t.Membri.All(m => m.Id != p.Id)).ToArray() : [];

    public void Dispose()
    {
        lock (open) open.Remove(this);
        lifetime.Cancel(); lifetime.Dispose();
    }
}
