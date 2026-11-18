using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using TA_Gestione_Cartelle_Commesse.Views;

// Finestre Pianificazione e Ore lavorate con un servizio simulato e dati sintetici: costruzione, contenuti principali e
// anteprime PNG (artifacts/checks), anche per l'utente Windows senza utente di GeCo.
internal static class GeCoWindowChecks
{
    private sealed class Fake(Func<GeCoCommand, object> answer) : IGeCoGateway
    {
        public Task<T> Send<T>(GeCoCommand command, CancellationToken token = default) => Task.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(answer(command)))!);
    }
    public static void Run()
    {
        var ok = new GeCoAccess(true, "", "DOMINIO\\prova", "prova", "Persona di prova", Planning: true, Hours: true);
        var plan = new GeCoPlan("2026-11-16", 2, "2026-11-18", 8, "amministratore", [new(10, "Persona A", true), new(11, "Persona B", false)], [new(10, "Persona A", true, true), new(11, "Persona B", false, true)], true, true,
            [new(1000, "C26130", "Progetto di prova", "Cliente", true, [new(2000, "Progetto"), new(2001, "Direzione lavori")]), new(1001, "C26131", "Seconda", "Cliente", true, [])],
            [new(1, 10, 1000, 2000, "Progetto", "2026-11-16", "2026-11-20", true), new(2, 10, 1001, null, "", "2026-11-18", "2026-11-24", true), new(3, 11, 1000, 2001, "Direzione lavori", "2026-11-23", "2026-11-27", true)]);
        var planningModel = new PlanningViewModel(new Fake(c => c.Action == "Access" ? ok : plan)); planningModel.StartAsync().GetAwaiter().GetResult();
        var planning = new GeCoPlanningWindow(planningModel); Render(planning, "pianificazione");
        if (!Texts(planning).Any(t => t.Contains("C26130 · Progetto")) || !Texts(planning).Any(t => t.Contains("Commesse e fasi"))) throw new Exception("Pianificazione: barre o pannello mancanti");

        string[] days = Enumerable.Range(0, 7).Select(i => new DateTime(2026, 11, 16).AddDays(i).ToString("yyyy-MM-dd")).ToArray();
        GeCoCell[] Cells(params (int Day, decimal Q)[] v) => days.Select((d, i) => new GeCoCell(d, v.Where(x => x.Day == i).Select(x => (decimal?)x.Q).FirstOrDefault(), false, null, null, null, null, null, null, null, null)).ToArray();
        var perms = new GeCoPagePermissions("RilevazioneStatini.aspx", "Ore lavorate", true, true, false);
        var start = new GeCoHoursStart("Ore lavorate", perms, [new(10, "Persona A", "I", true)], 10, false, false, true, false, "2026-11-16", "2026-11-18", new(101, "KM", "Chilometri", false, false));
        var period = new GeCoPeriod("2026-11-16", "2026-11-22", "47/2026", 1, days.Select(d => new GeCoDay(d, "", "")).ToArray(), 10, "Persona A", false, "I",
            [new(100, "ORE", "Ore", true, true), new(101, "KM", "Chilometri", false, false)], [100, 101], new(101, "KM", "Chilometri", true),
            [new(1000, "C26130", "Progetto di prova", "Cliente", 100, "ORE", 2000, "Progetto", [new(2000, "Progetto")], false, false, Cells((0, 6), (1, 9), (2, 11))),
             new(1000, "C26130", "Progetto di prova", "Cliente", 101, "KM", 2000, "Progetto", [new(2000, "Progetto")], false, false, Cells((0, 35))),
             new(1001, "C26131", "Seconda", "Cliente", 100, "ORE", null, "", [], false, false, Cells((0, 2), (6, 3)))], false, [], perms);
        var proposals = new GeCoProposals([new(1000, "C26130", "Progetto di prova", "Cliente", true, [new(2000, "Progetto")], false)], [new(1000, 2000, "2026-11-19", 8)]);
        var hoursModel = new HoursViewModel(new Fake(c => c.Action switch
        { "Access" => ok, "HoursStart" => start, "HoursPeriod" => period, "HoursProposals" => proposals, "HoursRecent" => new GeCoRecents([], []), _ => true }));
        hoursModel.StartAsync().GetAwaiter().GetResult();
        var hours = new GeCoHoursWindow(hoursModel); Render(hours, "ore-lavorate");
        var texts = Texts(hours).ToArray();
        if (!texts.Any(t => t.Contains("C26130 · Progetto")) || !texts.Any(t => t.Contains("6 h · 35 km")) || !texts.Any(t => t.Contains("circa 8 h")) || !texts.Any(t => t == "Dom"))
            throw new Exception("Ore lavorate: giornate, km, sagome o domenica con ore mancanti");

        var deniedModel = new HoursViewModel(new Fake(_ => new GeCoAccess(false, "L'account Windows DOMINIO\\x non corrisponde a nessun utente attivo di GeCo", "DOMINIO\\x")));
        deniedModel.StartAsync().GetAwaiter().GetResult();
        var denied = new GeCoHoursWindow(deniedModel); Render(denied, "ore-senza-utente");
        if (!Texts(denied).Any(t => t.Contains("non corrisponde a nessun utente")) || Texts(denied).Any(t => t.Contains("Commesse e fasi"))) throw new Exception("Ore lavorate: utente non riconosciuto senza messaggio");
    }
    // Il modello è già avviato: la finestra si disegna subito, senza essere mostrata.
    private static void Render(Window window, string name)
    {
        var body = (FrameworkElement)window.Content;
        body.Measure(new Size(1400, 860)); body.Arrange(new Rect(0, 0, 1400, 860)); body.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1400, 860, 96, 96, PixelFormats.Pbgra32); bitmap.Render(body);
        Directory.CreateDirectory("artifacts/checks"); using var stream = File.Create($"artifacts/checks/{name}.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
    }
    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock t) yield return t.Text;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var s in Texts(child)) yield return s;
    }
}
