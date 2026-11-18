using System.IO;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.ViewModels;

// Pianificazione e ore di GeCo nel client, con un servizio simulato: calcoli di presentazione (giorni, righe parallele,
// totali, sagome) e richieste inviate. Le regole restano sul servizio e sono provate in GeCoChecks e in GeCo2027.
internal static class GeCoClientChecks
{
    private sealed class Fake(Func<GeCoCommand, object> answer) : IGeCoGateway
    {
        public List<GeCoCommand> Sent { get; } = [];
        public Task<T> Send<T>(GeCoCommand command, CancellationToken token = default)
        {
            Sent.Add(command);
            return Task.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(answer(command)))!);
        }
    }
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }

    public static void Run()
    {
        Check(GeCoCalendar.WorkIndex("2026-11-16", "2026-11-20", false) == 4 && GeCoCalendar.WorkIndex("2026-11-16", "2026-11-23", false) == 5
              && GeCoCalendar.WorkIndex("2026-11-16", "2026-11-21", true) == 4 && GeCoCalendar.WorkIndex("2026-11-16", "2026-11-22", false) == 5 && GeCoCalendar.WorkIndex("2026-11-16", "2026-11-13", false) == -1,
            "Pianificazione: giorni lavorativi dal lunedì, sabato e domenica sul venerdì o sul lunedì");
        Check(GeCoCalendar.WorkDate("2026-11-16", 5) == "2026-11-23" && GeCoCalendar.WorkDate("2026-11-16", 9) == "2026-11-27", "Pianificazione: data dell'indice lavorativo");
        var rows = GeCoCalendar.Rows(new[] { (1, 0, 3), (2, 1, 1), (3, 3, 2) }, out var count);
        Check(rows[1] == 0 && rows[2] == 1 && rows[3] == 0 && count == 2, "Pianificazione: attività sovrapposte su righe parallele");
        Check(GeCoCalendar.Hours("7:30") == 7.5m && GeCoCalendar.Hours("7,5") == 7.5m && GeCoCalendar.Hours("7.5") == 7.5m && GeCoCalendar.Hours("") == 0 && GeCoCalendar.Clean("7h,5x") == "7,5",
            "Ore: 7:30, 7,5 e 7.5 nei totali; caselle solo con cifre e separatori");
        Check(GeCoCalendar.Format(7.5m) == "7,5" && GeCoCalendar.Format(0) == "" && GeCoCalendar.IsoWeek("2026-11-16") == 47, "Ore: formato italiano e settimana ISO");
        Check(PlanningViewModel.Message(new IOException("Comando non supportato.")).Contains("2.37.0"), "Servizio precedente: si chiede di aggiornarlo");

        // Pianificazione con un servizio simulato.
        var plan = new GeCoPlan("2026-11-16", 1, "2026-11-18", 8, "collaboratore", [new(11, "Persona B", true)], [new(11, "Persona B", true, false)], true, false,
            [new(1000, "B260001", "Progetto", "Cliente", true, [new(2000, "Progetto")]), new(1001, "N260001", "Altra", "Cliente", false, [])],
            [new(1, 11, 1000, 2000, "Progetto", "2026-11-16", "2026-11-20", true), new(2, 11, 1001, null, "", "2026-11-18", "2026-11-19", false)]);
        var planning = new Fake(c => c.Action switch { "Access" => new GeCoAccess(true, "", "DOM\\b", "b", "Persona B", Planning: true, Hours: true), "Plan" => plan, "AddActivity" => 7, _ => true });
        var pvm = new PlanningViewModel(planning);
        pvm.StartAsync().GetAwaiter().GetResult();
        var person = pvm.PlanOf(11);
        var first = person.Bars.Single(b => b.Item.Id == 1);
        Check(pvm.Days == 5 && pvm.TodayIndex == 2 && first is { Start: 0, Length: 5 } && person.Row[1] != person.Row[2] && person.Running(2) == 2 && person.Estimate(first) == 32,
            "Pianificazione: barre, righe parallele, attività in corso e impegno stimato (8 h divise tra le attività)");
        Check(pvm.AddAsync(pvm.ChosenPerson, plan.Commesse[1], null, 1).GetAwaiter().GetResult() == "Si può pianificare sé stessi solo sulle commesse di cui si è capocommessa." && !planning.Sent.Any(c => c.Action == "AddActivity"),
            "Pianificazione: nessuna richiesta per le commesse di cui non si è capocommessa");
        Check(pvm.AddAsync(pvm.ChosenPerson, plan.Commesse[0], plan.Commesse[0].Fasi[0], 3).GetAwaiter().GetResult() == null && pvm.Selected == 7
              && planning.Sent.Last(c => c.Action == "AddActivity").Attivita is { IdCdc: 11, IdCommessa: 1000, IdFase: 2000, Dal: "2026-11-19", Al: "2026-11-19" },
            "Pianificazione: la fase trascinata sul giovedì diventa un'attività di un giorno");
        pvm.MoveAsync(plan.Attivita[0], 1, 3).GetAwaiter().GetResult();
        Check(planning.Sent.Last(c => c.Action == "MoveActivity") is { Id: 1, Attivita: { Dal: "2026-11-17", Al: "2026-11-19" } }, "Pianificazione: spostamento e durata in giorni lavorativi");

        // Ore lavorate con un servizio simulato.
        GeCoCell Cell(string day, decimal? q = null, bool conf = false) => new(day, q, conf, null, null, null, null, null, null, null, null);
        string[] week = ["2026-11-16", "2026-11-17", "2026-11-18", "2026-11-19", "2026-11-20", "2026-11-21", "2026-11-22"];
        GeCoCell[] Cells(params (int Day, decimal Q)[] values) => week.Select((d, i) => Cell(d, values.Where(v => v.Day == i).Select(v => (decimal?)v.Q).FirstOrDefault())).ToArray();
        var perms = new GeCoPagePermissions("RilevazioneStatini.aspx", "Ore lavorate", true, true, false);
        var start = new GeCoHoursStart("Ore lavorate", perms, [new(11, "Persona B", "I", true)], 11, false, false, true, false, "2026-11-16", "2026-11-18", new(101, "KM", "Chilometri", false, false));
        var period = new GeCoPeriod("2026-11-16", "2026-11-22", "47/2026", 1, week.Select(d => new GeCoDay(d, "", "")).ToArray(), 11, "Persona B", false, "I",
            [new(100, "ORE", "Ore", true, true), new(101, "KM", "Chilometri", false, false)], [100, 101], new(101, "KM", "Chilometri", true),
            [new(1000, "B260001", "Progetto", "Cliente", 100, "ORE", 2000, "Progetto", [new(2000, "Progetto")], false, false, Cells((1, 6), (5, 2))),
             new(1000, "B260001", "Progetto", "Cliente", 101, "KM", 2000, "Progetto", [new(2000, "Progetto")], false, false, Cells((1, 35))),
             new(1000, "B260001", "Progetto", "Cliente", 100, "ORE", null, "", [new(2000, "Progetto")], false, false, Cells((1, 5)))], false, [], perms);
        var proposals = new GeCoProposals([new(1000, "B260001", "Progetto", "Cliente", true, [new(2000, "Progetto")], false)], [new(1000, 2000, "2026-11-18", 8)]);
        var hours = new Fake(c => c.Action switch
        {
            "Access" => new GeCoAccess(true, "", "DOM\\b", "b", "Persona B", Planning: true, Hours: true), "HoursStart" => start, "HoursPeriod" => period, "HoursProposals" => proposals,
            "HoursRecent" => new GeCoRecents([], []), "HoursRow" => period.Righe[0],
            "HoursSave" => new GeCoSaveResult(1, [new(1000, 100, 2000, "2026-11-17", "Il totale delle ore della giornata supera 24")], []), _ => true,
        });
        var hvm = new HoursViewModel(hours);
        hvm.StartAsync().GetAwaiter().GetResult();
        var p = hvm.People.Single();
        var elements = hvm.Elements(p);
        var withKm = elements.Single(e => e.Row.IdFase == 2000 && e.Index == 1);
        Check(elements.Count == 3 && withKm.KmKey != null && hvm.Value(withKm.KmKey, withKm.KmCell) == "35",
            "Ore: una giornata per commessa · fase e giorno, con i km sulla stessa giornata");
        Check(hvm.HoursOf(p, elements, 1) == 11 && hvm.KmTotal(elements, 1) == 35, "Ore: totale del giorno sulle voci di ore (più fasi) e km a parte");
        Check(hvm.VisibleDays().SequenceEqual(new[] { 0, 1, 2, 3, 4, 5 }), "Ore: giorni feriali, il sabato solo perché contiene ore");
        var ghosts = hvm.Ghosts(p, elements);
        Check(ghosts.Single() is { Index: 2, Hours: 8 } && ghosts[0].Phase?.Value == 2000, "Ore: sagoma dalla pianificazione dove non c'è ancora la giornata");
        var lanes = HoursViewModel.Lanes(elements.Select(e => (e.Key, $"{e.Row.IdCommessa}|{e.Row.IdFase ?? 0}", e.Index, false)), 7);
        Check(lanes.Count == 2 && lanes.Row[elements.Single(e => e.Index == 5).Key] == lanes.Row[withKm.Key], "Ore: più fasi nello stesso giorno su righe parallele, la stessa fase resta sulla sua riga");
        hvm.Modify(withKm.Key, "Qta", "20");
        hvm.SaveAsync().GetAwaiter().GetResult();
        var sent = hours.Sent.Last(c => c.Action == "HoursSave").Salvataggio!;
        Check(sent is { IdCdc: 11, Dal: "2026-11-16", Settimane: 1 } && sent.Celle.Single() is { IdCommessa: 1000, IdVdc: 100, IdFase: 2000, Data: "2026-11-17", Qta: "20" },
            "Ore: si inviano solo le caselle modificate, con la fase nella chiave");
        Check(hvm.Errors.Values.Single() == "Il totale delle ore della giornata supera 24" && hvm.Edits.ContainsKey(withKm.Key) && hvm.Note?.Kind == "danger",
            "Ore: la casella rifiutata resta modificata con il motivo del servizio");
        hvm.Remove(hvm.Elements(p).Single(e => e.Index == 5));
        Check(hvm.Edits.Values.Any(e => e.TryGetValue("Qta", out var q) && q == ""), "Ore: togliere una giornata salvata la azzera al salvataggio");
        Check(hvm.AddAsync(11, ghosts[0].Project, ghosts[0].Phase, ghosts[0].Index, ghosts[0].Hours).GetAwaiter().GetResult() && hvm.Elements(p).Any(e => e.Index == 2 && hvm.Value(e.Key, e.Cell) == "8"),
            "Ore: la sagoma diventa una giornata con le ore stimate, da salvare");
        var denied = new HoursViewModel(new Fake(c => new GeCoAccess(false, "L'account Windows DOM\\x non corrisponde a nessun utente attivo di GeCo", "DOM\\x")));
        denied.StartAsync().GetAwaiter().GetResult();
        Check(denied.Start == null && denied.AccessMessage.Contains("non corrisponde") && denied.People.Count == 0, "Ore: senza utente di GeCo la finestra mostra il motivo e non permette nulla");
        FolderLinks();
    }

    // Collegamento cartelle (2.38.0): testi delle righe (collegamento confermato o soltanto proposta), filtri, richieste inviate,
    // notifiche di GeCo2027 senza ripetizioni. Proposte e scritture restano del servizio (GeCoChecks) e della libreria.
    private static void FolderLinks()
    {
        GeCoFolder F(int id, string name, bool present = true) => new(id, @"\\server\lavori\2026\" + name, name, "C", name[..7], 2026, null, null, present, "01/11/2026 08:00");
        var safeRow = new GeCoLinkRow("commessa", 1000, "B260001", 2026, "Progetto", "Cliente", false, null, null, new(1, 100, "stesso codice della commessa", F(1, "B260001 - Cliente - Progetto")),
            [new(1, 100, "stesso codice della commessa", F(1, "B260001 - Cliente - Progetto"))], "sicura", []);
        var movedRow = new GeCoLinkRow("gara", 3, "G26001", 2026, "Gara", "Ente", false, new(5, 2, "01/11/2026 09:00", "GeCo2027", "Amministratore"), F(2, "G26001 - Vecchia", false),
            new(4, 80, "stesso codice della gara", F(4, "G26001 - Nuova")), [new(4, 80, "stesso codice della gara", F(4, "G26001 - Nuova"))], "daverificare",
            [new("CartellaSpostata", "gara", 3, 2, 4, "La cartella collegata alla gara G26001 non è più sul disco.")]);
        var plainRow = new GeCoLinkRow("commessa", 1001, "N260001", 2026, "Altro", null, false, null, null, null, [], "", [new("SenzaCartella", "commessa", 1001, null, null, "La commessa N260001 non ha una cartella.")]);
        var links = new GeCoLinks([safeRow, movedRow, plainRow], [], [], [new("commessa", 1000, 1)], [new("C", "01/11/2026 08:00", "TA-Gestione", 3)]);
        long last = 10;
        var events = new List<GeCoSyncEvent> { new(10, "18/11/2026 09:00", "GeCo2027", "CommessaCreata", 1000, null, null, "B260001", null, "Commessa B260001 creata.", "Amministratore") };
        var gateway = new Fake(c => c.Action switch
        {
            "Access" => new GeCoAccess(true, "", "DOM\\a", "admin", "Amministratore", Administrator: true, Links: true),
            "Links" => links, "Link" => 9, "Unlink" => true, "ConfirmSafe" => 1,
            "SyncEvents" => new GeCoSyncEvents(events.Where(e => e.Id > (c.DopoId ?? 0)).ToArray(), last, true),
            _ => true,
        });
        var vm = new FolderLinksViewModel(gateway);
        vm.StartAsync().GetAwaiter().GetResult();
        Check(vm.Allowed && vm.Links != null && vm.Events.Single().Id == 10 && vm.Arrived == 0, "Collegamento cartelle: amministratore, elenco e ultime notifiche (non contate come nuove)");
        var items = vm.Items().ToList();
        Check(items[0] is { Stato: "Proposta sicura (da confermare)", Cartella: "B260001 - Cliente - Progetto", Elemento: "Commessa" } && items[0].Motivo.StartsWith("Punteggio 100")
            && items[1] is { Stato: "Collegata, cartella non più sul disco", Elemento: "Gara" } && items[1].Motivo.Contains("proposta da verificare: G26001 - Nuova") && items[2].Stato == "Senza cartella",
            "Collegamento cartelle: una proposta non è mai mostrata come collegamento; la cartella sparita è segnalata con la nuova da verificare");
        vm.View = FolderLinksViewModel.Safe; Check(vm.Items().Single().Row.Id == 1000, "Collegamento cartelle: filtro delle proposte sicure");
        vm.View = FolderLinksViewModel.Warnings; Check(vm.Items().Count() == 2, "Collegamento cartelle: filtro delle segnalazioni");
        vm.View = FolderLinksViewModel.All; vm.Filter = "ente g26"; Check(vm.Items().Single().Row.Tipo == "gara", "Collegamento cartelle: ricerca per parole");
        vm.Filter = "";
        Check(vm.ConfirmSafeAsync().GetAwaiter().GetResult() && gateway.Sent.Last(c => c.Action == "ConfirmSafe").Collegamenti!.Single() == new GeCoLinkItem("commessa", 1000, 1) && vm.Done == "1 collegamenti confermati.",
            "Collegamento cartelle: conferma in blocco con l'anteprima vista");
        var questions = new List<string>(); vm.Ask = q => { questions.Add(q); return true; };
        Check(vm.ConfirmAsync(movedRow).GetAwaiter().GetResult() && gateway.Sent.Last(c => c.Action == "Link") is { Tipo: "gara", Id: 3, IdCartella: 4, Valore: true } && questions.Single().Contains("Sostituire"),
            "Collegamento cartelle: ricollegare una gara già collegata chiede conferma e invia la sostituzione esplicita");
        vm.Ask = _ => false; int sent = gateway.Sent.Count;
        Check(!vm.UnlinkAsync(movedRow).GetAwaiter().GetResult() && gateway.Sent.Count == sent, "Collegamento cartelle: scollegamento annullato, nessuna richiesta");
        events.Insert(0, new(12, "18/11/2026 10:00", "GeCo2027", "CommessaModificata", 1000, null, null, "B260001", null, "Commessa B260001 modificata.", "Amministratore")); last = 12;
        int loads = gateway.Sent.Count(c => c.Action == "Links");
        vm.PushedAsync(12).GetAwaiter().GetResult();
        Check(vm.Events[0].Id == 12 && vm.Arrived == 1 && gateway.Sent.Last(c => c.Action == "SyncEvents").DopoId == 10 && gateway.Sent.Count(c => c.Action == "Links") == loads + 1,
            "Notifiche: evento nuovo da GeCo2027 in cima, chiesto dopo l'ultimo ricevuto, con l'elenco ricaricato");
        sent = gateway.Sent.Count; vm.PushedAsync(12).GetAwaiter().GetResult();
        Check(gateway.Sent.Count == sent && vm.Events.Count == 2, "Notifiche: la stessa notifica ripetuta alla riconnessione non rifà richieste");
        var collaborator = new FolderLinksViewModel(new Fake(c => c.Action == "Access" ? new GeCoAccess(true, "", "DOM\\b", "b", "Persona B", Links: true) : new GeCoSyncEvents([], 0, true)));
        collaborator.StartAsync().GetAwaiter().GetResult();
        Check(!collaborator.Allowed && collaborator.Links == null && collaborator.Denied.Contains("riservato agli amministratori"), "Collegamento cartelle: per chi non è amministratore il motivo, senza elenco");
        var oldServer = new FolderLinksViewModel(new Fake(c => c.Action == "Access" ? new GeCoAccess(true, "", "DOM\\a", Administrator: true, Links: true) : throw new ArgumentException("Comando GeCo sconosciuto.")));
        oldServer.StartAsync().GetAwaiter().GetResult();
        Check(oldServer.Error.Contains("2.38.0") && oldServer.EventsMessage.Contains("2.38.0"), "Collegamento cartelle: un servizio precedente chiede di aggiornarlo");
    }
}
