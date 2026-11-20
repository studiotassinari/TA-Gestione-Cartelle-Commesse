using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// «Commesse di GeCo» (2.42.0), come la scheda Commesse della pagina iniziale di GeCo2027: «Le mie commesse» (attribuite
// all'utente, con il motivo) e «Tutte», con ricerca, team e cartella. Quali commesse vede l'utente, perché sono sue e quale
// cartella hanno lo decide il servizio con le regole della libreria (ElencoCommesse); qui si tengono soltanto la scelta,
// il testo cercato e l'ultimo elenco. Le commesse non attribuite si aprono in sola lettura, con «Richiedi accesso».
public sealed class GeCoProjectsViewModel : IDisposable
{
    public const string Changed = ProjectCardViewModel.Changed;
    private static readonly HashSet<GeCoProjectsViewModel> open = [];
    public static bool AnyOpen { get { lock (open) return open.Count > 0; } }
    public static void Dispatch(GeCoViewUpdates updates) { GeCoProjectsViewModel[] all; lock (open) all = [.. open]; foreach (var m in all) m.ViewsChanged(updates); }

    private readonly WindowGeCoGateway gateway;
    private readonly CancellationTokenSource lifetime = new();
    private GeCoViewRefresh? live;
    private string? me;
    public GeCoProjectsViewModel(IGeCoGateway gateway) { this.gateway = new WindowGeCoGateway(gateway); lock (open) open.Add(this); }

    public bool Mine { get; set; } = true;
    public string Search { get; set; } = "";
    public GeCoProjectList? List { get; private set; }
    public string Error { get; private set; } = "";
    public bool Busy { get; private set; }
    public event Action? Updated;
    public GeCoViewRefresh Live => live ??= new(gateway.Window, () => Busy, LoadAsync, lifetime.Token);

    // Team o dati di una commessa mostrata cambiati in GeCo2027 o in un'altra finestra: l'elenco si rilegge.
    public void ViewsChanged(GeCoViewUpdates updates) => Live.Arrived(updates,
        u => u.Tipo is Changed or ProjectTeamViewModel.Changed && List is { } l && l.Righe.Any(r => r.IdCommessa is int id && u.Commesse.Contains(id)), me);

    public string Summary => List is not { } l ? "" : l.Righe.Length == 0
        ? (Mine ? "Nessuna commessa attribuita: con «Tutte» si cercano le altre commesse e se ne chiede l'accesso." : "Nessuna commessa trovata.")
        : $"{l.Righe.Length} {(l.Righe.Length == 1 ? "riga" : "righe")}" + (l.Trovate > l.Righe.Length ? $" (le più recenti di {l.Trovate})" : "")
          + (Mine ? " · commesse attribuite a te" : " · tutte le commesse che puoi vedere");

    public async Task LoadAsync()
    {
        Busy = true; Updated?.Invoke();
        try
        {
            me ??= (await gateway.Send<GeCoAccess>(new("Access"), lifetime.Token)) is { Recognized: true } a ? a.Name : null;
            List = await gateway.Send<GeCoProjectList>(new("Projects", Valore: Mine, Testo: Search.Trim().Length == 0 ? null : Search.Trim()), lifetime.Token);
            Error = "";
            Live.Loaded();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = ProjectCardViewModel.Text(ex); }
        finally { Busy = false; Updated?.Invoke(); }
    }

    // Testi delle colonne (sola presentazione).
    public static string Team(GeCoProjectRow r) => r.Team is not { Length: > 0 } t ? "" : string.Join(", ", t.Select(p => p.Capocommessa ? p.Nome + " (capocommessa)" : p.Nome));
    public static string Folder(GeCoProjectRow r) => r.Percorso is not { Length: > 0 } p ? (r.IdCommessa is null ? "" : "nessuna cartella rilevata")
        : (r.Collegata ? "collegata: " : "stesso codice, non collegata: ") + Name(p);
    // Ultima parte del percorso di rete (\\server\…\C26012 - …), anche dove la barra rovescia non separa le cartelle.
    public static string Name(string path) { var t = path.TrimEnd('\\', '/'); return t[(t.LastIndexOfAny(['\\', '/']) + 1)..]; }

    public void Dispose()
    {
        lock (open) open.Remove(this);
        lifetime.Cancel(); lifetime.Dispose();
    }
}
