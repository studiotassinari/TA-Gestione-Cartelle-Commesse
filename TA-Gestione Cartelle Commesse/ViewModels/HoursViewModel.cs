using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Ore lavorate di GeCo (pagina Ore lavorate di GeCo2027, RilevazioneStatini.aspx di GeCo2017) con l'impostazione della
// pianificazione: ogni elemento è una giornata di una persona su una commessa · fase, con le ore effettive e i km.
// Le modifiche restano qui finché non si salva; tutte le regole (tariffe, tetti, fasi obbligatorie, classificazione CDC,
// conferme, totale della giornata entro 24 ore) le applica il servizio con la libreria GeCo.Dati, che indica le caselle
// non salvate con il motivo. Chiavi: riga = commessa|voce|fase; casella = persona|commessa|voce|fase|data.
public sealed partial class HoursViewModel(IGeCoGateway gateway) : IDisposable
{
    public const string Page = "ore";
    private readonly CancellationTokenSource lifetime = new();
    public GeCoAccess? Access { get; private set; }
    public GeCoHoursStart? Start { get; private set; }
    public string Error { get; private set; } = "";
    public string Dal { get; private set; } = "";
    public int Weeks { get; private set; } = 1;
    public int[]? Chosen { get; private set; } = GeCoChoices.Load(Page);
    public sealed record Person(int Id, string Nome, bool Io, bool Esterno, GeCoPeriod? Period, GeCoProposals Proposals, string? Error);
    // Riga del consuntivo della persona; New: aggiunta in questa sessione; Synthetic: riga di ore senza ore, che porta i km registrati da soli.
    public sealed record Row(GeCoRow Item, bool New = false, bool Synthetic = false);
    public List<Person> People { get; } = [];
    public Dictionary<int, List<Row>> Rows { get; } = [];
    public Dictionary<string, Dictionary<string, string?>> Edits { get; } = [];
    public Dictionary<string, string> Errors { get; } = [];
    public HashSet<string> Open { get; } = [];
    public string? Selected { get; set; }
    public int? PersonId { get; set; }
    public int? Day { get; set; }
    public int? NewItem { get; set; }
    public Dictionary<int, List<PanelProject>> Searched { get; } = [];
    public GeCoRecents Recents { get; private set; } = new([], []);
    public (string Kind, string Text)? Note { get; set; }
    public bool Saving { get; private set; }
    public event Action? Changed;
    // Domanda di conferma all'utente (finestra di dialogo nella vista; nelle verifiche risponde sempre sì).
    public Func<string, bool> Ask { get; set; } = _ => true;
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
    public void Refresh() => Changed?.Invoke();

    public static string RowKey(GeCoRow r) => $"{r.IdCommessa}|{r.IdVdc}|{r.IdFase ?? 0}";
    public static string CellKey(int person, GeCoRow r, string date) => $"{person}|{RowKey(r)}|{date}";
    public static string CellKey(int person, int project, int item, int? phase, string date) => $"{person}|{project}|{item}|{phase ?? 0}|{date}";
    public int Changes => Edits.Count;
    public GeCoPagePermissions Permissions => Start?.Permessi ?? new("", "", false, false, false);

    public async Task StartAsync()
    {
        try
        {
            Access = await gateway.Send<GeCoAccess>(new("Access"), lifetime.Token);
            if (Access.Recognized && Access.Hours)
            {
                Start = await gateway.Send<GeCoHoursStart>(new("HoursStart"), lifetime.Token);
                Dal = Start.SettimanaCorrente;
                await LoadAsync();
                return;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Access ??= new(false, PlanningViewModel.Message(ex), ""); Error = PlanningViewModel.Message(ex); }
        Changed?.Invoke();
    }
    public string AccessMessage => Access is not { } a ? "" : !a.Recognized ? a.Message : !a.Hours ? $"La funzione «Ore lavorate» non è assegnata all'utente {a.User} di GeCo." : Error;

    // Persone nel diagramma: chi non vede gli altri ha solo sé stesso, qualunque cosa sia ricordata sul PC.
    public int[] Ids()
    {
        if (Start == null) return [];
        int? mine = Start.IdCdc is int me && Start.Cdc.Any(c => c.Value == me) ? me : Start.Cdc.FirstOrDefault()?.Value;
        if (!Start.PuoCambiareCdc || Chosen == null) return mine is int m ? [m] : [];
        return Start.Cdc.Where(c => Chosen.Contains(c.Value)).Select(c => c.Value).ToArray();
    }

    // Ricarica il periodo di tutte le persone. keep: righe nuove da tenere; edits, errors, open: modifiche non salvate con il loro esito.
    public async Task LoadAsync(Dictionary<int, List<Row>>? keep = null, Dictionary<string, Dictionary<string, string?>>? edits = null, Dictionary<string, string>? errors = null, HashSet<string>? open = null)
    {
        if (Start == null) return;
        var ids = Ids();
        try
        {
            var loaded = await Task.WhenAll(ids.Select(async id =>
            {
                var center = Start.Cdc.FirstOrDefault(c => c.Value == id);
                GeCoPeriod? period = null; string? failure = null; GeCoProposals proposals = new([], []);
                try { period = await gateway.Send<GeCoPeriod>(new("HoursPeriod", Dal: Dal, Settimane: Weeks, IdCdc: id), lifetime.Token); }
                catch (Exception ex) when (ex is not OperationCanceledException) { failure = PlanningViewModel.Message(ex); }
                try { proposals = await gateway.Send<GeCoProposals>(new("HoursProposals", Dal: Dal, Settimane: Weeks, IdCdc: id), lifetime.Token); }
                catch (Exception ex) when (ex is not OperationCanceledException) { }
                return new Person(id, center?.Text ?? "", id == Start.IdCdc, center?.Cdc_IntExt == "E", period, proposals, failure);
            }));
            People.Clear(); People.AddRange(loaded); Rows.Clear();
            foreach (var p in People.Where(p => p.Period != null))
            {
                var rows = p.Period!.Righe.Select(r => new Row(r)).ToList();
                foreach (var extra in keep?.GetValueOrDefault(p.Id) ?? []) if (!rows.Any(r => RowKey(r.Item) == RowKey(extra.Item))) rows.Add(extra);
                Rows[p.Id] = WithSynthetic(rows, p.Period, HoursItem(p.Period, null));
            }
            Edits.Clear(); foreach (var e in edits ?? []) Edits[e.Key] = e.Value;
            Errors.Clear(); foreach (var e in errors ?? []) Errors[e.Key] = e.Value;
            Open.Clear(); foreach (var e in open ?? []) Open.Add(e);
            Error = "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = PlanningViewModel.Message(ex); }
        await LoadRecentAsync();
        Changed?.Invoke();
    }
    public async Task LoadRecentAsync()
    {
        if (ChosenPerson is not { } p) { Recents = new([], []); return; }
        try { Recents = await gateway.Send<GeCoRecents>(new("HoursRecent", Dal: Dal, Settimane: Weeks, IdCdc: p.Id), lifetime.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Recents = new([], []); }
    }

    // Righe di ore «sintetiche» per i km registrati senza ore sulla stessa commessa · fase: la giornata deve esistere per mostrarli.
    private static List<Row> WithSynthetic(List<Row> rows, GeCoPeriod period, int? hoursItem)
    {
        if (period.VoceKm is not { } km || hoursItem is not int item) return rows;
        var result = rows.ToList();
        static bool Closed(string? reason) => reason == "La commessa è chiusa" || (reason ?? "").StartsWith("Commessa non aperta", StringComparison.Ordinal);
        foreach (var r in rows.Where(r => r.Item.IdVdc == km.Id))
        {
            if (result.Any(x => x.Item.IdVdc != km.Id && x.Item.IdCommessa == r.Item.IdCommessa && (x.Item.IdFase ?? 0) == (r.Item.IdFase ?? 0))) continue;
            result.Add(new(r.Item with { IdVdc = item, Voce = "", Celle = r.Item.Celle.Select(c => new GeCoCell(c.Data, null, false, null, null, null, null, null, null, null, Closed(c.Bloccata) ? c.Bloccata : null)).ToArray() }, Synthetic: true));
        }
        return result;
    }
    // Voce delle ore delle nuove giornate: quella scelta, se la persona la usa, altrimenti la prima voce di ore mostrata.
    public static int? HoursItem(GeCoPeriod? period, int? choice)
    {
        if (period == null) return null;
        var km = period.VoceKm?.Id;
        var shown = period.VociSelezionate.Where(id => id != km).ToArray();
        if (choice is int c && shown.Contains(c)) return c;
        var v = period.Voci.FirstOrDefault(x => shown.Contains(x.Id) && x.Ore) ?? period.Voci.FirstOrDefault(x => shown.Contains(x.Id));
        return v?.Id;
    }

    // ---------------------------------------------------------------- navigazione e persone

    public bool CanLeave() => Changes == 0 || Ask("Ci sono ore non salvate. Vuoi abbandonare le modifiche?");
    private Task Reload() { Selected = null; Searched.Clear(); Note = null; return LoadAsync(); }
    public Task Go(int days) { if (!CanLeave()) return Task.CompletedTask; Dal = GeCoCalendar.AddDays(Dal, days); return Reload(); }
    public Task Today() { if (!CanLeave() || Start == null) return Task.CompletedTask; Dal = Start.SettimanaCorrente; return Reload(); }
    public Task SetWeeks(int weeks) { if (!CanLeave()) return Task.CompletedTask; Weeks = Math.Clamp(weeks, 1, 8); return Reload(); }
    public Task Choose(int[]? ids) { if (!CanLeave()) return Task.CompletedTask; Selected = null; Chosen = ids; GeCoChoices.Save(Page, ids); return LoadAsync(); }
    public Person? PersonOf(int id) => People.FirstOrDefault(p => p.Id == id);
    public Person? ChosenPerson => People.FirstOrDefault(p => p.Id == PersonId && p.Period != null) ?? People.FirstOrDefault(p => p.Io && p.Period != null) ?? People.FirstOrDefault(p => p.Period != null);
    public async Task ChoosePerson(int id) { if (PersonId == id) return; PersonId = id; await LoadRecentAsync(); Changed?.Invoke(); }

    // ---------------------------------------------------------------- giorni, elementi e sagome

    public int DayCount => Weeks * 7;
    public string DateOf(int index) => GeCoCalendar.AddDays(Dal, index);
    public int IndexOf(string date) { int i = (int)(GeCoCalendar.Date(date) - GeCoCalendar.Date(Dal)).TotalDays; return i >= 0 && i < DayCount ? i : -1; }
    public int TodayIndex => Start == null ? -1 : IndexOf(Start.Oggi);
    public static GeCoKmItem? KmOf(Person? p) => p?.Period?.VoceKm;
    public static bool ReadOnly(Person? p) => p?.Period == null || p.Period.SolaLettura;
    public static bool CountsHours(Person p, int item)
    {
        if (KmOf(p) is { } km && km.Id == item) return false;
        var v = p.Period?.Voci.FirstOrDefault(x => x.Id == item);
        return v?.Ore ?? true;
    }
    public string Value(string key, GeCoCell? cell) => Edits.TryGetValue(key, out var e) && e.TryGetValue("Qta", out var q) ? q ?? "" : GeCoCalendar.Format(cell?.Qta);
    public string? Field(string key, GeCoCell? cell, string name) => Edits.TryGetValue(key, out var e) && e.TryGetValue(name, out var v) ? v : name switch
    {
        "Note" => cell?.Note, "NumDoc" => cell?.NumDoc, "DataDoc" => cell?.DataDoc, "Fornitore" => cell?.Fornitore, "DataRifSal" => cell?.DataRifSal,
        "LinkFattura" => cell?.LinkFattura, "IdClass" => cell?.IdClass?.ToString(), _ => null,
    };
    public void Modify(string key, string name, string? value)
    {
        if (!Edits.TryGetValue(key, out var e)) Edits[key] = e = [];
        e[name] = value; Errors.Remove(key);
    }
    private void Forget(string key) => Edits.Remove(key);
    private static bool Confirmed(GeCoCell? c) => c is { Conf: true, Qta: decimal q } && q != 0;

    // Giornata: una per commessa · voce · fase e giorno con ore, km, modifiche o appena aggiunta; i km stanno sulla prima riga di ore della stessa commessa · fase.
    public sealed record Element(string Key, string? KmKey, int P, GeCoRow Row, int Index, GeCoCell Cell, GeCoCell? KmCell);
    public List<Element> Elements(Person p)
    {
        var result = new List<Element>();
        if (p.Period == null) return result;
        var rows = Rows.GetValueOrDefault(p.Id) ?? [];
        var km = KmOf(p);
        var first = new Dictionary<string, Row>();
        foreach (var r in rows.Where(r => km == null || r.Item.IdVdc != km.Id)) first.TryAdd($"{r.Item.IdCommessa}|{r.Item.IdFase ?? 0}", r);
        foreach (var r in rows.Where(r => km == null || r.Item.IdVdc != km.Id))
        {
            bool withKm = km != null && ReferenceEquals(first[$"{r.Item.IdCommessa}|{r.Item.IdFase ?? 0}"], r);
            var kmRow = withKm ? rows.FirstOrDefault(x => x.Item.IdVdc == km!.Id && x.Item.IdCommessa == r.Item.IdCommessa && (x.Item.IdFase ?? 0) == (r.Item.IdFase ?? 0)) : null;
            for (int i = 0; i < r.Item.Celle.Length && i < DayCount; i++)
            {
                var c = r.Item.Celle[i];
                var key = CellKey(p.Id, r.Item, c.Data);
                var kmKey = withKm ? CellKey(p.Id, r.Item.IdCommessa, km!.Id, r.Item.IdFase, c.Data) : null;
                var kmCell = kmRow?.Item.Celle.ElementAtOrDefault(i);
                bool exists = c.Qta != null || Edits.ContainsKey(key) || Open.Contains(key) || (kmKey != null && (kmCell?.Qta != null || Edits.ContainsKey(kmKey)));
                if (exists) result.Add(new(key, kmKey, p.Id, r.Item, i, c, kmCell));
            }
        }
        return result;
    }
    // Sagoma: attività pianificata in un giorno in cui sulla stessa commessa · fase non c'è ancora una giornata (solo un suggerimento).
    public sealed record Ghost(string Key, int P, PanelProject Project, GeCoOption? Phase, int Index, decimal Hours);
    public List<Ghost> Ghosts(Person p, List<Element> elements)
    {
        if (p.Period == null || ReadOnly(p)) return [];
        var present = elements.Select(e => $"{e.Row.IdCommessa}|{e.Row.IdFase ?? 0}|{e.Cell.Data}").ToHashSet();
        var result = new List<Ghost>();
        foreach (var x in p.Proposals.Piano)
        {
            int index = IndexOf(x.Data);
            if (index < 0 || present.Contains($"{x.IdCommessa}|{x.IdFase ?? 0}|{x.Data}")) continue;
            // Commessa non più aperta nel periodo: nessuna sagoma.
            if (p.Proposals.Team.FirstOrDefault(c => c.IdCommessa == x.IdCommessa) is not { } team) continue;
            var phase = x.IdFase is int f ? team.Fasi.FirstOrDefault(o => o.Value == f) : null;
            result.Add(new($"s|{p.Id}|{x.IdCommessa}|{x.IdFase ?? 0}|{x.Data}", p.Id, PanelProject.From(team), phase, index, x.Ore));
        }
        return result;
    }
    // Righe parallele: ogni giornata nella prima riga libera del suo giorno, preferendo quella già usata dalla stessa commessa · fase.
    public static (Dictionary<string, int> Row, int Count) Lanes(IEnumerable<(string Key, string Group, int Index, bool Ghost)> items, int days)
    {
        var all = items.ToList(); var row = new Dictionary<string, int>(); var preferred = new Dictionary<string, int>(); int max = 0;
        for (int g = 0; g < days; g++)
        {
            var today = all.Where(v => v.Index == g).OrderBy(v => v.Ghost ? 1 : 0).ToList(); var used = new HashSet<int>();
            foreach (var v in today) if (preferred.TryGetValue(v.Group, out var r) && used.Add(r)) row[v.Key] = r;
            foreach (var v in today.Where(v => !row.ContainsKey(v.Key)))
            {
                int r = 0; while (used.Contains(r)) r++;
                row[v.Key] = r; used.Add(r); preferred.TryAdd(v.Group, r);
            }
            foreach (var v in today) max = Math.Max(max, row[v.Key] + 1);
        }
        return (row, max);
    }
    // Giorni mostrati: i feriali, più sabato e domenica solo se contengono ore, km o giornate aperte di qualcuno.
    public int[] VisibleDays()
    {
        var weekend = People.SelectMany(Elements).Select(e => e.Index).Where(i => i % 7 >= 5).ToHashSet();
        return Enumerable.Range(0, DayCount).Where(i => i % 7 < 5 || weekend.Contains(i)).ToArray();
    }
    public decimal HoursOf(Person p, List<Element> elements, int day) => elements.Where(e => e.Index == day && CountsHours(p, e.Row.IdVdc)).Sum(e => GeCoCalendar.Hours(Value(e.Key, e.Cell)));
    public decimal KmTotal(List<Element> elements, int day) => elements.Where(e => e.Index == day && e.KmKey != null).Sum(e => GeCoCalendar.Hours(Value(e.KmKey!, e.KmCell)));
    public bool Modifiable(Person? p, GeCoCell? c) => !ReadOnly(p) && c != null && c.Bloccata == null && !Confirmed(c) && (c.Qta != null ? Permissions.Mod : Permissions.Ins);
    public bool KmModifiable(Person? p, Element e) => e.KmKey != null && KmOf(p) is { Disponibile: true } && !ReadOnly(p) && !(e.KmCell != null && (e.KmCell.Bloccata != null || Confirmed(e.KmCell)))
        && !Confirmed(e.Cell) && (e.KmCell?.Qta != null ? Permissions.Mod : Permissions.Ins);
    // Avviso prima del salvataggio, come la pagina web: il servizio decide comunque.
    public string Missing(Person p, Element e)
    {
        var classes = p.Period?.Classificazioni.ElementAtOrDefault(e.Index);
        bool hours = GeCoCalendar.Hours(Value(e.Key, e.Cell)) > 0;
        if (e.Row.FaseRichiesta && e.Row.IdFase == null && hours) return "Fase obbligatoria: aggiungi la giornata dalla fase giusta";
        if (e.Row.ClassRichiesta && classes is { Associate: > 1 } && hours && string.IsNullOrEmpty(Field(e.Key, e.Cell, "IdClass"))) return "Classificazione CDC da scegliere nei dettagli (altrimenti si usa la prima associata)";
        return "";
    }
    public IEnumerable<Element> AllElements() => People.SelectMany(Elements);
    public Element? SelectedElement => AllElements().FirstOrDefault(e => e.Key == Selected || e.KmKey == Selected);
}

// Commessa del pannello «Commesse e fasi»: del team, pianificata, con ore nel periodo o cercata.
public sealed record PanelProject(int IdCommessa, string Codice, string Descrizione, string Cliente, GeCoOption[] Fasi, bool FaseRichiesta, bool Team = false, bool Pianificata = false)
{
    public static PanelProject From(GeCoTeamProposal c, bool planned = false) => new(c.IdCommessa, c.Codice, c.Descrizione, c.Cliente, c.Fasi, c.FaseRichiesta, c.Team, planned);
    public static PanelProject From(GeCoRow r) => new(r.IdCommessa, r.Codice, r.Descrizione, r.Cliente, r.Fasi, r.FaseRichiesta);
}
