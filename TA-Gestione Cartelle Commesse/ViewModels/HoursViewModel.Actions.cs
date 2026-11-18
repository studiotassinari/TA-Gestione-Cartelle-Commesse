using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Aggiunta di giornate, salvataggio e conferma: le stesse azioni della pagina Ore lavorate di GeCo2027.
public sealed partial class HoursViewModel
{
    private readonly Dictionary<string, Task<Row>> pendingRows = [];

    // Commesse del pannello per la persona scelta: del team e pianificate, quelle con ore nel periodo e quelle cercate.
    public PanelProject[] Panel()
    {
        if (ChosenPerson is not { } p) return [];
        var all = new Dictionary<int, PanelProject>();
        foreach (var c in p.Proposals.Team) all[c.IdCommessa] = PanelProject.From(c, p.Proposals.Piano.Any(x => x.IdCommessa == c.IdCommessa));
        var km = KmOf(p);
        foreach (var r in Rows.GetValueOrDefault(p.Id) ?? [])
            if ((km == null || r.Item.IdVdc != km.Id) && !all.ContainsKey(r.Item.IdCommessa) && !r.Item.Celle.All(c => c.Bloccata != null)) all[r.Item.IdCommessa] = PanelProject.From(r.Item);
        foreach (var c in Searched.GetValueOrDefault(p.Id) ?? []) all.TryAdd(c.IdCommessa, c);
        return all.Values.OrderBy(c => c.Codice, StringComparer.Ordinal).ToArray();
    }
    // Colore legato alla commessa (non alla posizione): uguale per tutte le persone e stabile quando se ne aggiungono altre.
    public (string Back, string Fore) Color(int project) => GeCoCalendar.ProjectColor(project);
    // Voci di ore tra cui scegliere quella delle nuove giornate (solo se più di una).
    public GeCoCostItem[] NewItems() => People.Where(p => !ReadOnly(p)).SelectMany(p => p.Period!.Voci.Where(v => p.Period.VociSelezionate.Contains(v.Id) && v.Id != KmOf(p)?.Id)).DistinctBy(v => v.Id).ToArray();

    public async Task<GeCoFoundProject[]> SearchAsync(string text)
    {
        if (text.Trim().Length < 2) return [];
        try { return await gateway.Send<GeCoFoundProject[]>(new("HoursProjects", Dal: Dal, Settimane: Weeks, Testo: text.Trim()), lifetime.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return []; }
    }
    private Task<GeCoRow> NewRow(int person, int project, int item, int? phase) =>
        gateway.Send<GeCoRow>(new("HoursRow", Dal: Dal, Settimane: Weeks, IdCdc: person, IdCommessa: project, IdVdc: item, IdFase: phase), lifetime.Token);

    // Riga commessa · voce · fase della persona: quella caricata, oppure i dati di una riga nuova (fasi e giorni bloccati).
    private Task<Row> EnsureRow(int person, int project, int item, int? phase)
    {
        var rows = Rows.GetValueOrDefault(person) ?? [];
        var found = rows.FirstOrDefault(r => !r.Synthetic && r.Item.IdCommessa == project && r.Item.IdVdc == item && (r.Item.IdFase ?? 0) == (phase ?? 0));
        if (found != null) return Task.FromResult(found);
        var key = $"{person}|{project}|{item}|{phase ?? 0}";
        if (!pendingRows.TryGetValue(key, out var pending))
            pendingRows[key] = pending = Load();
        return pending;
        async Task<Row> Load()
        {
            try
            {
                var row = new Row(await NewRow(person, project, item, phase), New: true);
                var list = (Rows.GetValueOrDefault(person) ?? []).Where(x => !(x.Synthetic && RowKey(x.Item) == RowKey(row.Item))).ToList();
                if (!list.Any(x => RowKey(x.Item) == RowKey(row.Item))) list.Add(row);
                Rows[person] = list; return list.First(x => RowKey(x.Item) == RowKey(row.Item));
            }
            finally { pendingRows.Remove(key); }
        }
    }

    // Nuova giornata della persona su commessa · fase nel giorno, con le ore proposte dalla pianificazione già scritte.
    public async Task<bool> AddAsync(int person, PanelProject project, GeCoOption? phase, int day, decimal? hours = null, bool quiet = false)
    {
        var p = PersonOf(person);
        if (p?.Period == null) return false;
        if (ReadOnly(p)) { Note = ("warning", $"Le ore di {p.Nome} sono in sola lettura."); Changed?.Invoke(); return false; }
        if (!Permissions.Ins) { Note = ("warning", "Operazione non consentita per l'utente"); Changed?.Invoke(); return false; }
        if (HoursItem(p.Period, NewItem) is not int item) { Note = ("warning", $"Nessuna voce di ore con tariffa per {p.Nome} nel periodo."); Changed?.Invoke(); return false; }
        try
        {
            var r = await EnsureRow(person, project.IdCommessa, item, phase?.Value);
            var c = r.Item.Celle[Math.Clamp(day, 0, r.Item.Celle.Length - 1)];
            var key = CellKey(person, r.Item, c.Data);
            if (c.Bloccata != null && c.Qta == null) { Note = ("warning", $"{r.Item.Codice}, {GeCoCalendar.Long(c.Data)}: {c.Bloccata}"); Changed?.Invoke(); return false; }
            Open.Add(key);
            if (hours is decimal h && Value(key, c).Length == 0) Modify(key, "Qta", GeCoCalendar.Format(h));
            if (!quiet) { PersonId = person; Selected = key; Changed?.Invoke(); }
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Note = ("warning", $"{project.Codice}: {PlanningViewModel.Message(ex)}"); Changed?.Invoke(); return false; }
    }
    // Commessa cercata o scelta tra recenti e preferite: entra nel pannello con le fasi utilizzabili dalla persona.
    public async Task AddToPanelAsync(int project, string code)
    {
        if (ChosenPerson is not { } p || Panel().Any(c => c.IdCommessa == project) || HoursItem(p.Period, NewItem) is not int item) return;
        try
        {
            var item0 = PanelProject.From(await NewRow(p.Id, project, item, null));
            if (!Searched.TryGetValue(p.Id, out var list)) Searched[p.Id] = list = [];
            list.Add(item0);
            // Commessa senza fasi: non c'è nulla da scegliere, la giornata si aggiunge subito nel giorno scelto.
            if (item0.Fasi.Length == 0) await AddAsync(p.Id, item0, null, Day ?? Math.Max(0, TodayIndex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Note = ("warning", $"{code}: {PlanningViewModel.Message(ex)}"); }
        Changed?.Invoke();
    }
    // Le ore stimate di tutte le sagome delle persone modificabili diventano giornate da controllare e salvare.
    public async Task UsePlanAsync()
    {
        int n = 0;
        foreach (var p in People.Where(p => !ReadOnly(p)).ToList())
            foreach (var g in Ghosts(p, Elements(p)))
                if (await AddAsync(p.Id, g.Project, g.Phase, g.Index, g.Hours, true)) n++;
        Note = n > 0 ? ("success", $"Inserite {n} giornate dalla pianificazione: controlla e salva") : ("info", "Nessuna attività pianificata da consuntivare");
        Changed?.Invoke();
    }
    // Copia ore e km della settimana precedente nella prima settimana mostrata della persona scelta, da controllare e salvare.
    public async Task CopyPreviousWeekAsync()
    {
        if (ChosenPerson is not { } p || ReadOnly(p)) return;
        try
        {
            var previous = await gateway.Send<GeCoPeriod>(new("HoursPeriod", Dal: GeCoCalendar.AddDays(Dal, -7), Settimane: 1, IdCdc: p.Id), lifetime.Token);
            int n = 0;
            foreach (var pr in previous.Righe.Where(r => r.Celle.Any(c => c.Qta is decimal q && q != 0)))
            {
                Row r;
                try { r = await EnsureRow(p.Id, pr.IdCommessa, pr.IdVdc, pr.IdFase); }
                catch (Exception ex) when (ex is not OperationCanceledException) { continue; } // commessa non più aperta o voce senza tariffa
                for (int i = 0; i < pr.Celle.Length && i < r.Item.Celle.Length; i++)
                {
                    var (c, t) = (pr.Celle[i], r.Item.Celle[i]);
                    var key = CellKey(p.Id, r.Item, t.Data);
                    if (c.Qta is not decimal q || q == 0 || t.Bloccata != null || t.Qta != null || Edits.ContainsKey(key)) continue;
                    Modify(key, "Qta", GeCoCalendar.Format(q)); Modify(key, "Note", c.Note ?? "");
                    if (KmOf(p) is not { } km || pr.IdVdc != km.Id) Open.Add(key);
                    n++;
                }
            }
            Note = n > 0 ? ("success", $"Copiate {n} celle dalla settimana precedente: controlla e salva") : ("info", "Nulla da copiare dalla settimana precedente");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Note = ("danger", PlanningViewModel.Message(ex)); }
        Changed?.Invoke();
    }
    // Toglie la giornata: le ore e i km salvati si cancellano al salvataggio (casella a zero).
    public void Remove(Element e)
    {
        var p = PersonOf(e.P);
        if (ReadOnly(p)) return;
        if (Confirmed(e.Cell) || Confirmed(e.KmCell)) { Note = ("warning", "La giornata contiene rilevazioni confermate: non può essere tolta"); Changed?.Invoke(); return; }
        bool saved = e.Cell.Qta != null || e.KmCell?.Qta != null;
        if (saved && !Ask("Togliere la giornata? Le ore e i km verranno cancellati al salvataggio.")) return;
        Open.Remove(e.Key);
        if (e.Cell.Qta != null) Modify(e.Key, "Qta", ""); else Forget(e.Key);
        if (e.KmKey != null) { if (e.KmCell?.Qta != null) Modify(e.KmKey, "Qta", ""); else Forget(e.KmKey); }
        Selected = null; Changed?.Invoke();
    }

    // Salvataggio per persona: le caselle valide si salvano, quelle rifiutate restano con il motivo sulla giornata.
    public async Task SaveAsync()
    {
        if (Changes == 0 || Saving) return;
        Saving = true; Changed?.Invoke();
        var errors = new Dictionary<string, string>(); var keepEdits = new Dictionary<string, Dictionary<string, string?>>(); var keepOpen = new HashSet<string>(); var warnings = new List<string>();
        int saved = 0; string? failure = null;
        try
        {
            foreach (var group in Edits.ToList().GroupBy(e => int.Parse(e.Key.Split('|')[0])))
            {
                var p = PersonOf(group.Key);
                if (ReadOnly(p)) continue;
                var km = KmOf(p); var rows = Rows.GetValueOrDefault(group.Key) ?? [];
                GeCoCell? CellOf(int idc, int idv, int? idf, string date) => rows.FirstOrDefault(x => !x.Synthetic && x.Item.IdCommessa == idc && x.Item.IdVdc == idv && (x.Item.IdFase ?? 0) == (idf ?? 0))?.Item.Celle.ElementAtOrDefault(IndexOf(date));
                var cells = group.Select(e =>
                {
                    var parts = e.Key.Split('|'); int idc = int.Parse(parts[1]), idv = int.Parse(parts[2]); int? idf = int.Parse(parts[3]) is var f && f != 0 ? f : null; var date = parts[4];
                    var c = CellOf(idc, idv, idf, date);
                    string Get(string name) => Field(e.Key, c, name) ?? "";
                    var cls = Get("IdClass"); var sal = Get("DataRifSal");
                    // I km seguono la loro giornata: stessa classificazione CDC e, per i centri esterni, stessa data di riferimento SAL.
                    if (km != null && idv == km.Id && rows.FirstOrDefault(x => x.Item.IdVdc != km.Id && x.Item.IdCommessa == idc && (x.Item.IdFase ?? 0) == (idf ?? 0)) is { } hoursRow)
                    {
                        var key2 = CellKey(group.Key, hoursRow.Item, date); var hc = hoursRow.Item.Celle.ElementAtOrDefault(IndexOf(date));
                        if (cls.Length == 0) cls = Field(key2, hc, "IdClass") ?? ""; if (sal.Length == 0) sal = Field(key2, hc, "DataRifSal") ?? "";
                    }
                    var q = Value(e.Key, c);
                    return new GeCoCellSave(idc, idv, date, q.Trim().Length == 0 ? "0" : q, idf, Get("Note"), Get("NumDoc"), Get("DataDoc"), Get("Fornitore"), sal, Get("LinkFattura"), int.TryParse(cls, out var ic) && ic != 0 ? ic : null);
                }).ToArray();
                try
                {
                    var result = await gateway.Send<GeCoSaveResult>(new("HoursSave", Salvataggio: new(group.Key, Dal, Weeks, cells)), lifetime.Token);
                    saved += result.Salvate;
                    foreach (var a in result.Avvisi ?? []) if (!warnings.Contains(a)) warnings.Add(a);
                    foreach (var err in result.Errori)
                    {
                        var key = CellKey(group.Key, err.IdCommessa, err.IdVdc, err.IdFase, err.Data);
                        errors[key] = err.Messaggio;
                        if (Edits.TryGetValue(key, out var edit)) keepEdits[key] = edit;
                        if (km == null || err.IdVdc != km.Id) keepOpen.Add(key);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failure = $"{p!.Nome}: {PlanningViewModel.Message(ex)}";
                    foreach (var e in group) keepEdits[e.Key] = e.Value;
                }
            }
            // Le righe nuove restano, così le giornate non salvate restano visibili con il loro errore.
            var keep = Rows.ToDictionary(r => r.Key, r => r.Value.Where(x => x.New).ToList());
            await LoadAsync(keep, keepEdits, errors, keepOpen);
            Note = failure != null ? ("danger", failure)
                : errors.Count > 0 ? ("danger", (errors.Count == 1 ? "1 cella non salvata" : errors.Count + " celle non salvate") + ": il motivo è sulla giornata in rosso")
                : warnings.Count > 0 ? ("warning", $"Salvate {saved} celle. " + string.Join(" · ", warnings))
                : ("success", saved > 0 ? $"Salvate {saved} celle" : "Nessuna modifica da salvare");
            if (errors.Count > 0) Selected = errors.Keys.First();
        }
        finally { Saving = false; Changed?.Invoke(); }
    }

    // Conferma o sblocco di una settimana della persona (riservati a chi ha la conferma degli statini).
    public async Task ConfirmAsync(Person p, int week, bool confirm)
    {
        if (Edits.Keys.Any(k => k.StartsWith(p.Id + "|", StringComparison.Ordinal))) { Note = ("warning", confirm ? "Salvare le modifiche prima di confermare" : "Salvare le modifiche prima di sbloccare"); Changed?.Invoke(); return; }
        var dal = GeCoCalendar.AddDays(Dal, week * 7);
        if (!Ask($"{(confirm ? "Confermare" : "Sbloccare")} le ore di {p.Nome} della settimana {GeCoCalendar.IsoWeek(dal)} (dal {GeCoCalendar.Short(dal)})?")) return;
        try
        {
            var r = await gateway.Send<GeCoConfirmResult>(new("HoursConfirm", Conferma: new(p.Id, dal, p.Period!.VociSelezionate, confirm)), lifetime.Token);
            await LoadAsync(); Note = ("success", r.Messaggio);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Note = ("danger", PlanningViewModel.Message(ex)); }
        Changed?.Invoke();
    }
}
