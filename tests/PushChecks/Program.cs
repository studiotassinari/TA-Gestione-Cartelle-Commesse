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
Check(FolderWatchService.Relevant("C", root, Path.Combine(root, "2026", "C26001 - Cliente - Argomento", "interno", "file.txt")), "Modifiche annidate ammesse per metadati");
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
    hub.Publish();
    Check((await WireProtocol.ReadAsync<PushNotice>(stream, token)).Revision == 1001, "Modifica inviata al client sulla connessione persistente");
    Check((await WireProtocol.ReadAsync<PushNotice>(stream, token)).Kind == "Heartbeat", "Connessione inattiva mantenuta con heartbeat");
    sessionStop.Cancel(); await serving.WaitAsync(token);
}

// Integra il vero FileSystemWatcher con il coordinatore, usando solo cartelle sintetiche locali.
var jobs = Path.Combine(root, "lavori"); var bids = Path.Combine(root, "gare"); var archive = Path.Combine(root, "archivio");
var job = Directory.CreateDirectory(Path.Combine(jobs, "2026", "C26001 - Cliente - Progetto")).FullName;
Directory.CreateDirectory(bids); Directory.CreateDirectory(archive);
var settings = new ServerSettings { JobsRoot = jobs, BidsRoot = bids, ArchiveFolder = archive, RefreshMinutes = 999 };
using var scans = new ScanCoordinator(settings, NullLogger<ScanCoordinator>.Instance, hub);
using var watcher = new FolderWatchService(settings, scans, NullLogger<FolderWatchService>.Instance);
await scans.StartAsync(token); await watcher.StartAsync(token);
try
{
    await Task.Delay(2500, token);
    var nested = Directory.CreateDirectory(Path.Combine(job, "C26999 - Non - Commessa")).FullName;
    File.WriteAllText(Path.Combine(nested, "file.txt"), "test");
    while (scans.Get("C").State.Snapshot?.Records.SingleOrDefault()?.Inspection?.TotalBytes != 4)
        await Task.Delay(200, token);
    Check(scans.Get("C").State.Snapshot!.Records.Count == 1, "Watcher aggiorna dimensioni senza censire commesse annidate");
    File.Delete(Path.Combine(nested, "file.txt"));
    while (scans.Get("C").State.Snapshot?.Records.Single().Inspection?.TotalBytes != 0)
        await Task.Delay(200, token);
    Check(true, "Eliminazione file rilevata senza polling client o scansione periodica");
}
finally
{
    await watcher.StopAsync(CancellationToken.None); await scans.StopAsync(CancellationToken.None);
    // Cartella temporanea con nome casuale creata esclusivamente da questo test.
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(root).StartsWith("TA-PushChecks-")) Directory.Delete(root, true);
}
Console.WriteLine("TUTTI I CONTROLLI PUSH SUPERATI; AD e watcher SMB da collaudare sulla VM.");
