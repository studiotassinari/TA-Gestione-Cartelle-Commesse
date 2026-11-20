using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using TA_Gestione_Cartelle_Commesse.Views;

// Commesse di GeCo in GeCa (2.42.0) con un servizio simulato: indirizzi delle pagine di GeCo2027, elenco «Le mie commesse»,
// scheda (sola lettura, modifica con la versione letta, ricarica dopo un rifiuto e per un aggiornamento esterno), richieste di
// accesso. Regole e permessi veri sono del servizio (GeCoChecks) e della libreria GeCo.Dati. Render: anteprime per la CI.
internal static class ProjectClientChecks
{
    private sealed class Fake(Func<GeCoCommand, object> answer) : IGeCoGateway
    {
        public List<GeCoCommand> Sent { get; } = [];
        public Task<T> Send<T>(GeCoCommand command, CancellationToken token = default)
        {
            lock (Sent) Sent.Add(command);
            return Task.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(answer(command)))!);
        }
    }
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }

    static readonly GeCoTeamMember Capo = new(11, "Persona B", true, true, false, [], "Capocommessa", "PB");
    static readonly GeCoProjectRow Mine = new("m1000", "C26012", 2026, "012", "Comune di Esempio", "Ponte sul fiume", "Aperta", 1048576, "18/11/2026 09:30", null,
        @"\\server\lavori\2026\C26012 - Esempio - Ponte sul fiume", 1000, 1, 5, true, ["Capocommessa"], "Capocommessa", true, [Capo]);
    static readonly GeCoProjectRow Other = new("m1001", "C26013", 2026, "013", "Ditta di prova", "Scuola", "Aperta", null, null, null, null, 1001, 1, null, false, [], null, false, []);
    static readonly GeCoProjectRow Orphan = new("c9", "C26099", 2026, "099", "Senza", "Cartella senza commessa", "Cartella senza commessa", 2048, null, null, @"\\server\lavori\2026\C26099 - Senza", null, null, 9, false, [], null, false, null);
    static readonly GeCoOption[] People = [new(10, "Persona A"), new(11, "Persona B")];
    static GeCoProjectCard Card(bool editable, string version = "v1") => new(1000, 2026, "C26012", "Ponte sul fiume", "Nota sintetica", 1, "Comune di Esempio", "commessa",
        new(11, "Persona B"), null, null, new(3, "Progettazione"), null, "01/01/2026", "30/06/2026", "15/01/2026", "", "A", "Aperta", "Produttiva", false,
        new(editable, editable ? ["Capocommessa"] : [], editable ? "Capocommessa" : "", !editable), new(editable, editable, editable, editable, editable, editable),
        [new(2000, 10, 7, "Progetto", "Progetto definitivo", 11, "Persona B", "A"), new(2001, 20, 7, "Progetto", "Direzione lavori", null, null, "")],
        editable ? [new(1, 1001, "C26013", "Scuola", "Ditta di prova", "A")] : null, version,
        editable ? People : null, editable ? [new(1, "Responsabile di prova")] : null, editable ? [new(3, "Progettazione")] : null, editable ? [new(7, "Progetto")] : null, editable ? People : null, 30);
    static GeCoAccessState State(bool mine) => new(1000, true, mine, mine ? ["Capocommessa"] : [], mine ? "Capocommessa" : "", null, !mine, mine, 0);
    static GeCoLinkRow Folder => new("commessa", 1000, "C26012", 2026, "Ponte sul fiume", "Comune di Esempio", new(3, 5, "10/01/2026 08:00", "GeCo2027", "Amministratore"),
        new(5, @"\\server\lavori\2026\C26012 - Esempio - Ponte sul fiume", "C26012 - Esempio - Ponte sul fiume", "C", "C26012", 2026, "Esempio", "Ponte sul fiume", true, "18/11/2026 08:00"), null, [], "", []);

    public static void Run()
    {
        Check(GeCoPages.Url("http://server.example:8027/", GeCoPages.OfProject[0], 1000) == "http://server.example:8027/Commessa_dettaglio.aspx?IdComm=1000"
              && GeCoPages.Url("https://server.example", GeCoPages.General.Single(p => p.Pagina == "Sal.aspx"), 1000) == "https://server.example/Sal.aspx"
              && GeCoPages.Url("", GeCoPages.OfProject[0], 1000) is null && GeCoPages.Url("server:8027", GeCoPages.OfProject[0], 1) is null
              && GeCoPages.Open("", GeCoPages.OfProject[0], 1) == GeCoPages.NotConfigured,
            "GeCo2027: pagina della commessa con IdComm, pagine generali senza; senza indirizzo valido nulla si apre e si spiega perché");
        Check(GeCoPages.General.Any(p => p.Pagina == "RaccordoWindows.aspx") && GeCoPages.OfProject.All(p => p.Commessa) && GeCoPages.General.All(p => !p.Commessa),
            "GeCo2027: raccordo utenti Windows tra le pagine generali");

        // Elenco: «Le mie commesse» chiesto al servizio, testi delle colonne.
        var list = new GeCoProjectList([Mine], 1, 1, true, true, true, null);
        var gateway = new Fake(c => c.Action switch
        {
            "Access" => new GeCoAccess(true, "", "DOM\\b", "b", "Persona B"),
            "Projects" => c.Valore ? list : new GeCoProjectList([Mine, Other, Orphan], 3, 3, true, true, false, null),
            _ => true,
        });
        using var projects = new GeCoProjectsViewModel(gateway);
        projects.LoadAsync().GetAwaiter().GetResult();
        Check(gateway.Sent.Last() is { Action: "Projects", Valore: true, Testo: null } && projects.Summary.StartsWith("1 riga") && GeCoProjectsViewModel.Team(Mine) == "Persona B (capocommessa)"
              && GeCoProjectsViewModel.Folder(Mine).StartsWith("collegata: C26012") && GeCoProjectsViewModel.Folder(Other) == "nessuna cartella rilevata" && GeCoProjectsViewModel.Folder(Orphan).StartsWith("stesso codice"),
            "Commesse: le mie per prime, team e cartella (collegata o solo con lo stesso codice) nelle colonne");
        projects.Mine = false; projects.Search = " scuola ";
        projects.LoadAsync().GetAwaiter().GetResult();
        Check(gateway.Sent.Last() is { Action: "Projects", Valore: false, Testo: "scuola" } && GeCoProjectsWindow.Items(projects.List).Select(i => i.Mia).SequenceEqual(new[] { "Capocommessa", "sola lettura", "" }),
            "Commesse: «Tutte» con la ricerca; le non attribuite in sola lettura, le cartelle senza commessa senza scheda");

        // Scheda in sola lettura: niente modifica, «Richiedi accesso».
        var readOnly = new Fake(c => c.Action switch
        {
            "Access" => new GeCoAccess(true, "", "DOM\\b", "b", "Persona B"),
            "ProjectCard" or "RequestAccess" => new GeCoProjectCardResult(true, "", Card(false), c.Action == "RequestAccess" ? State(false) with { PuoRichiedere = false, Richiesta = Request("InAttesa") } : State(false), null),
            "ProjectTeam" => new GeCoProjectTeamResult(true, "", new(1000, "C26012", "Ponte sul fiume", 11, [Capo], [], [], null, null, false)),
            _ => true,
        });
        using var view = new ProjectCardViewModel(readOnly, 1000);
        view.LoadAsync().GetAwaiter().GetResult();
        Check(view is { CanEdit: false, CanRequest: true } && view.AccessText.Contains("sola lettura") && view.Team.Team?.IdCommessa == 1000 && view.FolderText.StartsWith("Situazione della cartella non disponibile"),
            "Scheda: commessa non tua in sola lettura, con «Richiedi accesso» e il team");
        Check(view.RequestAccessAsync("Preventivo da preparare").GetAwaiter().GetResult() && readOnly.Sent.Last() is { Action: "RequestAccess", IdCommessa: 1000, Testo: "Preventivo da preparare" }
              && view.AccessText.Contains("in attesa") && !view.CanRequest,
            "Scheda: richiesta di accesso inviata con il motivo, poi in attesa");

        // Scheda modificabile: la versione letta accompagna la modifica; dopo un rifiuto si rilegge e il messaggio resta.
        int reads = 0; bool refuse = true;
        var editable = new Fake(c => c.Action switch
        {
            "Access" => new GeCoAccess(true, "", "DOM\\b", "b", "Persona B"),
            "ProjectCard" => new GeCoProjectCardResult(true, "", Card(true, "v" + ++reads), State(true), Folder),
            "SaveProject" when refuse => throw new ArgumentException("Il record è stato modificato da un altro utente dopo che lo hai aperto."),
            "SaveProject" or "AddPhase" => new GeCoProjectCardResult(true, "", Card(true, "v9") with { Descrizione = c.Commessa?.Descrizione ?? "Ponte sul fiume" }, State(true), Folder),
            "ProjectTeam" => new GeCoProjectTeamResult(true, "", new(1000, "C26012", "Ponte sul fiume", 11, [Capo], [], [], null, null, true)),
            _ => true,
        });
        using var card = new ProjectCardViewModel(editable, 1000);
        card.LoadAsync().GetAwaiter().GetResult();
        var edit = new GeCoProjectEdit("ignorata", "Ponte sul fiume (rev. 1)", "", 11, null, null, 3, "01/01/2026", "", "15/01/2026", "");
        Check(card.CanEdit && card.FolderText.StartsWith("Cartella collegata: ") && !card.SaveAsync(edit).GetAwaiter().GetResult()
              && editable.Sent.Last(c => c.Action == "SaveProject").Commessa?.Versione == "v1" && card.Error.Contains("modificato da un altro utente") && card.Card?.Versione == "v2",
            "Scheda: la modifica porta la versione letta; un rifiuto lascia il messaggio e rilegge la scheda");
        refuse = false;
        Check(card.SaveAsync(edit).GetAwaiter().GetResult() && editable.Sent.Last(c => c.Action == "SaveProject").Commessa?.Versione == "v2" && card.Card?.Descrizione == "Ponte sul fiume (rev. 1)" && card.Done.Contains("GeCo2027"),
            "Scheda: dati generali salvati con la versione aggiornata");
        Check(card.AddPhaseAsync(new(7, "Rilievi", "30", null, "")).GetAwaiter().GetResult() && editable.Sent.Last() is { Action: "AddPhase", IdCommessa: 1000, Fase.Ordinamento: "30" },
            "Scheda: fase aggiunta");

        // Aggiornamento immediato: «CommessaAggiornata» di un'altra finestra fa rileggere la scheda, ma non mentre si modifica.
        GeCoViewRefresh.Quiet = TimeSpan.FromMilliseconds(10); GeCoViewRefresh.Retry = TimeSpan.FromMilliseconds(20);
        var before = reads;
        GeCoViewUpdates Update(string window) => new([new(50, "GeCo2027", "CommessaAggiornata", 0, "", "", [1000], false, window, "Persona A")], 50, true);
        card.Editing = true;
        ProjectCardViewModel.Dispatch(Update("ab12"));
        Thread.Sleep(150);
        Check(reads == before, "Aggiornamento: mentre la modifica è aperta la scheda aspetta");
        card.Editing = false;
        for (int i = 0; i < 50 && reads == before; i++) Thread.Sleep(20);
        Check(reads == before + 1, "Aggiornamento: dati della commessa salvati in GeCo2027, la scheda si rilegge");
        ProjectCardViewModel.Dispatch(new([new(51, "GeCo2027", "CommessaAggiornata", 0, "", "", [2222], false, "ab12", "Persona A")], 51, true));
        Thread.Sleep(150);
        Check(reads == before + 1, "Aggiornamento: un'altra commessa non fa rileggere la scheda");

        // Richieste di accesso: decisione con la versione letta.
        var pending = Request("InAttesa");
        var requests = new Fake(c => new GeCoAccessRequests(true, true, [], c.Action == "DecideAccess" ? [] : [pending], c.Action == "DecideAccess" ? [pending with { Stato = "Concessa", StatoTesto = "Concessa", Versione = 2 }] : [], []));
        using var access = new AccessRequestsViewModel(requests);
        access.LoadAsync().GetAwaiter().GetResult();
        access.DecideAsync(access.Requests!.DaDecidere[0], true, null).GetAwaiter().GetResult();
        Check(requests.Sent.Last() is { Action: "DecideAccess", Id: 7, Valore: true, Versione: 1 } && access.Requests!.Concessi.Single().Stato == "Concessa" && access.Done.Contains("concesso"),
            "Richieste di accesso: concessa da chi decide, con la versione letta");
    }

    static GeCoAccessRequest Request(string state) => new(7, 1000, "C26012", "Ponte sul fiume", "Comune di Esempio", 2, "Persona B", "Preventivo da preparare", state,
        state == "InAttesa" ? "In attesa" : state, new DateTime(2026, 11, 19, 10, 0, 0), null, null, null, null, null, null, "GeCa", 1);

    // Anteprime per la CI (passo «Anteprime WPF con dati di prova»): elenco, scheda modificabile, richieste. Solo su Windows.
    public static void Render()
    {
        var gateway = new Fake(c => c.Action switch
        {
            "Access" => new GeCoAccess(true, "", "DOM\\b", "b", "Persona B"),
            "Projects" => new GeCoProjectList([Mine, Other, Orphan], 3, 3, true, true, false, null),
            "ProjectCard" => new GeCoProjectCardResult(true, "", Card(true), State(true), Folder),
            "ProjectTeam" => new GeCoProjectTeamResult(true, "", new(1000, "C26012", "Ponte sul fiume", 11, [Capo, new(10, "Persona A", true, false, true, [], "Membro", "PA")], [], [], null, null, true)),
            "AccessRequests" => new GeCoAccessRequests(true, true, [Request("InAttesa")], [Request("InAttesa") with { Utente = "Persona C" }], [], []),
            _ => true,
        });
        var projects = new GeCoProjectsViewModel(gateway) { Mine = false };
        projects.LoadAsync().GetAwaiter().GetResult();
        Save(new GeCoProjectsWindow(projects, () => "http://server.example:8027"), 1400, 700, "commesse-geco.png");
        var card = new ProjectCardViewModel(gateway, 1000);
        card.LoadAsync().GetAwaiter().GetResult();
        Save(new GeCoProjectWindow(card, () => "http://server.example:8027"), 1200, 900, "scheda-commessa-geco.png");
        var access = new AccessRequestsViewModel(gateway);
        access.LoadAsync().GetAwaiter().GetResult();
        Save(new GeCoAccessRequestsWindow(access), 1000, 700, "richieste-accesso.png");
        Check(File.Exists("artifacts/checks/scheda-commessa-geco.png"), "Anteprime delle commesse di GeCo renderizzate");
    }

    static void Save(Window window, int width, int height, string name)
    {
        var body = (FrameworkElement)window.Content;
        window.Content = null;
        body.Measure(new Size(width, height)); body.Arrange(new Rect(0, 0, width, height)); body.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var back = new DrawingVisual();
        using (var dc = back.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xF6)), null, new Rect(0, 0, width, height));
        image.Render(back); image.Render(body);
        Directory.CreateDirectory("artifacts/checks");
        using var output = File.Create("artifacts/checks/" + name);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(output);
    }
}
