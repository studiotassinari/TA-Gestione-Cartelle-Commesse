using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Pianificazione delle attività di GeCo (stessi contenuti della pagina Pianificazione di GeCo2027). Il servizio decide chi è
// l'utente, chi vede e chi può pianificare: qui si tengono solo la vista scelta e la barra in movimento.
public sealed class PlanningViewModel(IGeCoGateway gateway) : IDisposable
{
    public const string Page = "pianificazione";
    private readonly CancellationTokenSource lifetime = new();
    public GeCoAccess? Access { get; private set; }
    public GeCoPlan? Plan { get; private set; }
    public string Error { get; private set; } = "";
    public bool Busy { get; private set; }
    public int Weeks { get; private set; } = 4;
    private string from = "";
    public int[]? Chosen { get; private set; } = GeCoChoices.Load(Page);
    public int? Selected { get; set; }
    public int? Person { get; set; }
    public int? Day { get; set; }
    // Barra in movimento: posizione provvisoria finché il servizio non conferma.
    public (int Id, int Start, int Length)? Draft { get; set; }
    public List<GeCoPlanProject> Added { get; } = [];
    public event Action? Changed;
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }

    public async Task StartAsync()
    {
        try { Access = await gateway.Send<GeCoAccess>(new("Access"), lifetime.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Access = new(false, Message(ex), ""); }
        if (Access.Recognized && Access.Planning) await LoadAsync(); else Changed?.Invoke();
    }
    // Un servizio precedente alla 2.37.0 non conosce il comando: si chiede di aggiornarlo invece di mostrare un errore generico.
    public static string Message(Exception ex) => ex.Message == "Comando non supportato." ? "Il server non gestisce ancora pianificazione e ore di GeCo: aggiornare il servizio alla versione 2.37.0 o successiva." : ex.Message;

    public async Task LoadAsync()
    {
        try
        {
            Busy = true;
            Plan = await gateway.Send<GeCoPlan>(new("Plan", Dal: from.Length == 0 ? null : from, Settimane: Weeks, Persone: Chosen), lifetime.Token);
            Error = "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = Message(ex); }
        finally { Busy = false; Changed?.Invoke(); }
    }
    public Task Go(int days) { Selected = null; from = Plan == null ? "" : GeCoCalendar.AddDays(Plan.Dal, days); return LoadAsync(); }
    public Task Today() { Selected = null; from = ""; return LoadAsync(); }
    public Task SetWeeks(int weeks) { Selected = null; Weeks = weeks; from = Plan?.Dal ?? from; return LoadAsync(); }
    public Task Choose(int[]? ids) { Selected = null; Chosen = ids; GeCoChoices.Save(Page, ids); return LoadAsync(); }
    public int[] Shown => Plan?.Persone.Select(p => p.Id).ToArray() ?? [];

    public int Days => (Plan?.Settimane ?? 0) * 5;
    public int Index(string iso, bool end) => GeCoCalendar.WorkIndex(Plan!.Dal, iso, end);
    public string DateOf(int index) => GeCoCalendar.WorkDate(Plan!.Dal, index);
    public int TodayIndex => Plan == null ? -1 : Index(Plan.Oggi, false);

    // Attività con gli indici dei giorni lavorativi; la barra in movimento usa la posizione provvisoria.
    public sealed record Bar(GeCoActivity Item, int Start, int Length);
    public IEnumerable<Bar> Bars()
    {
        if (Plan == null) yield break;
        foreach (var a in Plan.Attivita)
        {
            int start = Index(a.Dal, false), end = Math.Max(start, Index(a.Al, true));
            yield return Draft is { } d && d.Id == a.Id ? new(a, d.Start, d.Length) : new(a, start, end - start + 1);
        }
    }
    public GeCoPlanProject[] Projects()
    {
        var all = new Dictionary<int, GeCoPlanProject>();
        foreach (var c in Plan?.Commesse ?? []) all[c.Id] = c;
        foreach (var c in Added) all.TryAdd(c.Id, c);
        return all.Values.OrderBy(c => c.Codice, StringComparer.Ordinal).ToArray();
    }
    public (string Back, string Fore) Color(int project) => GeCoCalendar.ProjectColor(project);
    public string Code(int project) => Projects().FirstOrDefault(c => c.Id == project)?.Codice ?? "";

    // Piano di una persona: le sue barre, le righe parallele, le attività in corso per giorno e la stima delle ore.
    public sealed record PersonPlan(Bar[] Bars, Dictionary<int, int> Row, int Rows, Func<int, int> Running, Func<Bar, decimal> Estimate);
    public PersonPlan PlanOf(int person)
    {
        var bars = Bars().Where(b => b.Item.IdCdc == person).ToArray();
        int Running(int day) => bars.Count(b => day >= b.Start && day < b.Start + b.Length);
        int hours = Plan?.OreGiorno ?? 8;
        decimal Estimate(Bar b) { decimal total = 0; for (int g = b.Start; g < b.Start + b.Length; g++) total += (decimal)hours / Math.Max(1, Running(g)); return total; }
        var rows = GeCoCalendar.Rows(bars.OrderBy(b => b.Start).ThenBy(b => b.Item.Id).Select(b => (b.Item.Id, b.Start, b.Length)), out var count);
        return new(bars, rows, Math.Max((Plan?.Persone.Length ?? 0) > 1 ? 2 : 5, count + 1), Running, Estimate);
    }
    public GeCoShownPerson? ChosenPerson => Plan?.Persone.FirstOrDefault(p => p.Id == Person) ?? Plan?.Persone.FirstOrDefault(p => p.Io) ?? Plan?.Persone.FirstOrDefault();
    public static bool CanPlanOn(GeCoShownPerson? person, GeCoPlanProject? project) => person != null && project != null && (person.Gestita || (person.Io && project.Mia));

    public async Task<GeCoPlanProject[]> SearchAsync(string text)
    {
        if (text.Trim().Length < 2) return [];
        try { return await gateway.Send<GeCoPlanProject[]>(new("PlanProjects", Testo: text.Trim()), lifetime.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return []; }
    }
    public void Keep(GeCoPlanProject project) { if (!Added.Any(c => c.Id == project.Id)) Added.Add(project); Changed?.Invoke(); }

    // Esegue un comando e ricarica il piano; l'errore del servizio è il messaggio da mostrare (null se riuscito).
    private async Task<string?> Run(GeCoCommand command, Action<System.Text.Json.JsonElement>? done = null)
    {
        string? failure = null;
        try { var reply = await gateway.Send<System.Text.Json.JsonElement>(command, lifetime.Token); done?.Invoke(reply); }
        catch (Exception ex) when (ex is not OperationCanceledException) { failure = Message(ex); }
        Draft = null; await LoadAsync(); return failure;
    }
    public Task<string?> AddAsync(GeCoShownPerson? person, GeCoPlanProject project, GeCoPhase? phase, int day)
    {
        if (person == null) return Task.FromResult<string?>("Nessuna persona su cui pianificare");
        if (!CanPlanOn(person, project)) return Task.FromResult<string?>(person.Io ? "Si può pianificare sé stessi solo sulle commesse di cui si è capocommessa." : $"Non si può pianificare {person.Nome}.");
        var date = DateOf(Math.Clamp(day, 0, Days - 1));
        Keep(project);
        return Run(new("AddActivity", Attivita: new(person.Id, project.Id, phase?.Id, date, date)), reply => Selected = reply.GetInt32());
    }
    public Task<string?> MoveAsync(GeCoActivity activity, int start, int length) =>
        Run(new("MoveActivity", Id: activity.Id, Attivita: new(activity.IdCdc, activity.IdCommessa, activity.IdFase, DateOf(start), DateOf(start + Math.Max(1, length) - 1))));
    public async Task<string?> RemoveAsync(GeCoActivity activity) { var failure = await Run(new("RemoveActivity", Id: activity.Id)); if (failure == null) Selected = null; return failure; }

    // Ruoli e team (riservati agli amministratori nel servizio).
    public Task<GeCoRoles> RolesAsync() => gateway.Send<GeCoRoles>(new("Roles"), lifetime.Token);
    public Task SetTeamManagerAsync(int user, bool value) => gateway.Send<bool>(new("SetTeamManager", Id: user, Valore: value), lifetime.Token);
}
