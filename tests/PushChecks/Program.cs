using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using TA.Server;
using TA.Transport;

static void Check(bool condition, string name)
{ if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var token = limit.Token;
var hub = new ChangeHub();
using (var first = hub.Subscribe())
using (var second = hub.Subscribe())
{
    Check(await first.ReadAsync(token) == 0 && await second.ReadAsync(token) == 0, "Ogni client riceve una notifica iniziale per risincronizzarsi");
    for (int i = 0; i < 1000; i++) hub.Publish();
    Check(await first.ReadAsync(token) == 1000 && await second.ReadAsync(token) == 1000, "Client lento: coda limitata e ultima revisione conservata");
}
using (var reconnect = hub.Subscribe()) Check(await reconnect.ReadAsync(token) == 1000, "Riconnessione recupera lo stato senza cronologia illimitata");
var batch = new ChangeBatch(); var now = DateTime.UtcNow;
batch.Mark(now); batch.Mark(now.AddSeconds(1));
Check(!batch.TryGetDue(now.AddSeconds(2), out _), "Raffiche raggruppate prima della scansione");
Check(batch.TryGetDue(now.AddSeconds(5), out var version), "Evento diventa elaborabile dopo quiete");
batch.Mark(now.AddSeconds(5)); batch.Acknowledge(version);
Check(batch.TryGetDue(now.AddSeconds(9), out version), "Modifica contemporanea non persa durante avvio scansione");
batch.Acknowledge(version);
Check(!batch.TryGetDue(now.AddSeconds(20), out _), "Evento elaborato non ripete scansioni");
for (int i = 0; i < 31; i++) batch.Mark(now.AddSeconds(i));
Check(batch.TryGetDue(now.AddSeconds(31), out _), "Flusso continuo elaborato entro trenta secondi");
var root = Path.Combine(Path.GetTempPath(), "TA-PushChecks-" + Guid.NewGuid().ToString("N"));
Check(FolderWatchService.Relevant("C", root, Path.Combine(root, "2026", "C26001 - Cliente - Argomento", "interno", "file.txt")), "Creazioni annidate ammesse dal filtro di percorso");
Check(!FolderWatchService.Relevant("C", root, Path.Combine(root, "Altro", "file.txt")), "Contenitori non annuali ignorati");
Check(!FolderWatchService.Relevant("C", root, Path.Combine(root, "..", "esterno")), "Percorsi esterni ignorati");

// Test del solo canale di notifiche; il percorso di produzione mantiene la barriera Kerberos.
using (var listener = new TcpListener(IPAddress.Loopback, 0))
using (var sessionStop = CancellationTokenSource.CreateLinkedTokenSource(token))
{
    listener.Start();
    var serving = Task.Run(async () =>
    {
        using var accepted = await listener.AcceptTcpClientAsync(token);
        try { await PushSession.RunAsync(accepted.GetStream(), hub, sessionStop.Token); }
        catch (OperationCanceledException) when (sessionStop.IsCancellationRequested) { }
    });
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, token);
    var stream = client.GetStream();
    Check((await WireProtocol.ReadAsync<Reply>(stream, token)).Success, "Sottoscrizione confermata");
    Check((await WireProtocol.ReadAsync<PushNotice>(stream, token)).Kind == "Changed", "Stato iniziale notificato senza richiesta periodica");
    hub.Publish("Aggiunta: C26001 - Cliente - Progetto");
    var notice = await WireProtocol.ReadAsync<PushNotice>(stream, token);
    Check(notice.Revision == 1001 && notice.Summary == "Aggiunta: C26001 - Cliente - Progetto", "Push trasmette al client il dettaglio della modifica");
    Check((await WireProtocol.ReadAsync<PushNotice>(stream, token)).Kind == "Heartbeat", "Connessione inattiva mantenuta con heartbeat");
    sessionStop.Cancel(); await serving.WaitAsync(token);
}

// Eventi di GeCo2027 (2.38.0): solo i client che li chiedono ricevono le notifiche «GeCo», insieme a quelle delle cartelle e con
// il riscontro; l'ultimo evento noto arriva anche a chi si collega dopo, una lettura ripetuta non rimanda nulla.
var geCo = new TA.Server.SharedDatabase.GeCoEventHub();
using (var early = geCo.Subscribe()) Check(!early.TryRead(out _), "Eventi di GeCo: nessuna notifica prima del primo evento letto");
geCo.Publish(40, "2 novità da GeCo2027"); geCo.Publish(40); geCo.Publish(39);
using (var late = geCo.Subscribe()) Check(late.TryRead(out var lastId) && lastId == 40 && !late.TryRead(out _) && geCo.Summary == "2 novità da GeCo2027", "Eventi di GeCo: ultimo evento a chi si collega, nessuna ripetizione");
foreach (var withGeCo in new[] { true, false })
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    using var sessionStop = CancellationTokenSource.CreateLinkedTokenSource(token);
    var cards = new ChangeHub();
    listener.Start();
    var serving = Task.Run(async () =>
    {
        using var accepted = await listener.AcceptTcpClientAsync(token);
        try { await PushSession.RunAsync(accepted.GetStream(), cards, sessionStop.Token, true, geCo: withGeCo ? geCo : null); }
        catch (OperationCanceledException) when (sessionStop.IsCancellationRequested) { }
    });
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, token);
    var stream = client.GetStream();
    async Task<PushNotice> Receive()
    {
        var n = await WireProtocol.ReadAsync<PushNotice>(stream, token);
        await WireProtocol.WriteAsync(stream, new PushAcknowledgement(n.ProbeId!, n.Kind, n.Revision, n.Kind == "Changed"), token);
        return n;
    }
    Check((await WireProtocol.ReadAsync<Reply>(stream, token)).Success, "Sottoscrizione con riscontro confermata" + (withGeCo ? " (con eventi di GeCo)" : ""));
    var initial = new List<PushNotice> { await Receive() };
    if (withGeCo) initial.Add(await Receive());
    Check(initial.Any(n => n.Kind == "Changed") && (withGeCo ? initial.Single(n => n.Kind == PushNotice.GeCo).Revision == 40 : initial.All(n => n.Kind != PushNotice.GeCo)),
        withGeCo ? "Notifica iniziale delle cartelle e ultimo evento di GeCo" : "Client precedente: nessuna notifica di GeCo");
    geCo.Publish(withGeCo ? 41 : 42, "1 novità da GeCo2027");
    if (withGeCo)
    {
        var arrived = await Receive();
        Check(arrived is { Kind: PushNotice.GeCo, Revision: 41, Summary: "1 novità da GeCo2027" }, "Nuovo evento di GeCo notificato al client, con riscontro senza viste da aggiornare");
        cards.Publish("C: aggiunta");
        Check((await Receive()) is { Kind: "Changed", Summary: "C: aggiunta" }, "Le modifiche delle cartelle continuano ad arrivare insieme agli eventi di GeCo");
    }
    else Check((await Receive()).Kind == "Heartbeat", "Client precedente: dopo un evento di GeCo solo heartbeat");
    sessionStop.Cancel(); await serving.WaitAsync(token);
}

// Pianificazione e ore aggiornate (2.39.0): corsia separata «GeCoViste» solo per chi la chiede, senza scavalcare le notifiche
// di GeCo nella coda di un elemento; il punto di partenza arriva a chi si collega.
geCo.PublishViews(70); geCo.PublishViews(69);
using (var lane = geCo.Subscribe(views: true))
using (var plain = geCo.Subscribe())
{
    Check(lane.TryReadViews(out var startView) && startView == 70 && !plain.TryReadViews(out _), "Viste di GeCo: ultimo evento tecnico solo a chi chiede le viste");
    lane.TryRead(out _); plain.TryRead(out _);
    geCo.PublishViews(71); geCo.Publish(43, "1 novità da GeCo2027");
    Check(lane.TryReadViews(out var v71) && v71 == 71 && lane.TryRead(out var n43) && n43 == 43 && !plain.TryReadViews(out _) && plain.TryRead(out _),
        "Viste di GeCo: evento tecnico e notifica arrivano entrambi, ciascuno nella sua corsia");
}
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    using var sessionStop = CancellationTokenSource.CreateLinkedTokenSource(token);
    var cards = new ChangeHub();
    listener.Start();
    var serving = Task.Run(async () =>
    {
        using var accepted = await listener.AcceptTcpClientAsync(token);
        try { await PushSession.RunAsync(accepted.GetStream(), cards, sessionStop.Token, true, geCo: geCo, views: true); }
        catch (OperationCanceledException) when (sessionStop.IsCancellationRequested) { }
    });
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, token);
    var stream = client.GetStream();
    async Task<PushNotice> Receive()
    {
        var n = await WireProtocol.ReadAsync<PushNotice>(stream, token);
        await WireProtocol.WriteAsync(stream, new PushAcknowledgement(n.ProbeId!, n.Kind, n.Revision, n.Kind == "Changed"), token);
        return n;
    }
    Check((await WireProtocol.ReadAsync<Reply>(stream, token)).Success, "Sottoscrizione con le viste di GeCo confermata");
    var initial = new List<PushNotice> { await Receive(), await Receive(), await Receive() };
    Check(initial.Any(n => n.Kind == "Changed") && initial.Single(n => n.Kind == PushNotice.GeCo).Revision == 43 && initial.Single(n => n.Kind == PushNotice.GeCoViews).Revision == 71,
        "Viste di GeCo: all'avvio cartelle, ultima notifica e ultimo evento tecnico");
    geCo.PublishViews(72);
    Check((await Receive()) is { Kind: PushNotice.GeCoViews, Revision: 72, Summary: null }, "Viste di GeCo: nuovo salvataggio notificato senza testi, con riscontro");
    sessionStop.Cancel(); await serving.WaitAsync(token);
}

// Integra il vero FileSystemWatcher con il coordinatore, usando solo cartelle sintetiche locali.
var jobs = Path.Combine(root, "lavori"); var bids = Path.Combine(root, "gare"); var archive = Path.Combine(root, "archivio");
var job = Directory.CreateDirectory(Path.Combine(jobs, "2026", "C26001 - Cliente - Progetto")).FullName;
Directory.CreateDirectory(bids); Directory.CreateDirectory(archive);
var settings = new ServerSettings { JobsRoot = jobs, BidsRoot = bids, ArchivedJobsRoot = bids, ArchiveFolder = archive, RefreshMinutes = 999 };
using var scans = new ScanCoordinator(settings, NullLogger<ScanCoordinator>.Instance, hub, new AnalysisService(settings, NullLogger<AnalysisService>.Instance));
using var watcher = new FolderWatchService(settings, scans, NullLogger<FolderWatchService>.Instance);
foreach(var category in new[] { "C", "G", "A" }) await scans.Get(category).InitializeAsync();
await watcher.StartAsync(token);
try
{
    await Task.Delay(2500, token);
    var nested = Directory.CreateDirectory(Path.Combine(job, "C26999 - Non - Commessa")).FullName;
    File.WriteAllText(Path.Combine(nested, "file.txt"), "test");
    var fresh = Directory.CreateDirectory(Path.Combine(jobs, "2026", "C26002 - Nuova - Cartella")).FullName;
    while (scans.Get("C").State.Snapshot?.Records.Count != 2) await Task.Delay(200, token);
    Check(scans.Get("C").State.Snapshot!.Records.All(r => r.Inspection == null), "Creazione rilevata senza analizzare dimensioni o commesse annidate");
    Directory.Delete(fresh);
    while (scans.Get("C").State.Snapshot?.Records.Count != 1) await Task.Delay(200, token);
    Check(true, "Eliminazione cartella rilevata dal watcher");
    File.Delete(Path.Combine(nested, "file.txt"));
    await Task.Delay(2500, token);
    Check(scans.Get("C").State.Snapshot!.Records.Single().Inspection == null, "Eliminazione file non avvia analisi ricorsive");
}
finally
{
    await watcher.StopAsync(CancellationToken.None); await scans.StopAsync(CancellationToken.None);
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    // Cartella temporanea con nome casuale creata esclusivamente da questo test.
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(root).StartsWith("TA-PushChecks-")) Directory.Delete(root, true);
}
Console.WriteLine("TUTTI I CONTROLLI PUSH SUPERATI; AD e watcher SMB da collaudare sulla VM.");
