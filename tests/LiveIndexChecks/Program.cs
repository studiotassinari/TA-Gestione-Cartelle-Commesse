using Microsoft.Extensions.Logging.Abstractions;
using TA.Server;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;

static void Check(bool condition, string name)
{ if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var token = timeout.Token;
var root = Path.Combine(Path.GetTempPath(), "TA-LiveIndex-" + Guid.NewGuid().ToString("N"));
try
{
    foreach (var prefix in new[] { 'C', 'G' })
    {
        var category = Directory.CreateDirectory(Path.Combine(root, prefix.ToString())).FullName;
        var container = prefix == 'C' ? Directory.CreateDirectory(Path.Combine(category, "2026")).FullName : category;
        var original = Directory.CreateDirectory(Path.Combine(container, prefix + "26197 - Cliente - Progetto")).FullName;
        var archive = Directory.CreateDirectory(Path.Combine(root, "archive-" + prefix)).FullName;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var hub = new ChangeHub();
        var worker = new CategoryWorker(prefix, category, archive, NullLogger.Instance, hub, (path, ct) =>
        {
            entered.Set(); release.Wait(ct);
            return new FolderInspection { Complete = true, CheckedUtc = DateTime.UtcNow };
        });
        await worker.InitializeAsync();
        worker.Start(token);
        Check(entered.Wait(TimeSpan.FromSeconds(5), token), prefix + ": lettura metadati deliberatamente bloccata");
        Check(worker.State.Snapshot!.Records.Count == 1, prefix + ": primo elenco disponibile prima dei metadati");
        using var subscriber = hub.Subscribe();
        await subscriber.ReadAsync(token);
        var created = Directory.CreateDirectory(Path.Combine(container, prefix + "26198 - prov prova")).FullName;
        Check(await worker.RefreshIndexAsync(token), prefix + ": elenco aggiornabile durante lettura ricorsiva");
        await subscriber.ReadAsync(token);
        Check(worker.State.Busy && worker.State.Snapshot!.Records.Any(r => r.Path == created && r.Status == "Non conforme"), prefix + "26198: presente e notificata mentre metadati ancora occupati");
        Check(hub.Summary?.Contains("26198 - prov prova") == true, prefix + ": notifica identifica la cartella aggiunta");
                ArchiveSnapshot? savedDuringInspection = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try { savedDuringInspection = SharedArchiveService.Load(archive, category, prefix); }
            catch (IOException) { }
            if (savedDuringInspection?.Records.Any(r => r.Path == created) == true) break;
            await Task.Delay(50, token);
        }
        Check(savedDuringInspection?.Records.Any(r => r.Path == created) == true, prefix + ": JSON aggiornato prima che termini la lettura metadati");
        Directory.Delete(original); // Cartella vuota creata esclusivamente da questo test.
        await worker.RefreshIndexAsync(token);
        release.Set();
        await worker.Completion.WaitAsync(token);
        Check(worker.State.Snapshot!.Records.Count == 1 && worker.State.Snapshot.Records[0].Path == created,
            prefix + ": fine scansione vecchia non ripristina cartella rimossa e non perde quella nuova");
        worker.Start(token); await worker.Completion.WaitAsync(token);
        Check(worker.State.Snapshot!.Records[0].Inspection != null, prefix + ": metadati nuova cartella completati nel giro successivo");
        var cached = SharedArchiveService.Load(archive, category, prefix);
        Check(cached?.Records.Single().Path == created, prefix + ": archivio conserva elenco corretto");
        Directory.Move(category, category + "-offline");
        Check(!await worker.RefreshIndexAsync(token) && worker.State.Snapshot.Records.Count == 1,
            prefix + ": radice irraggiungibile conserva elenco e segnala errore");
    }
}
finally
{
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(root).StartsWith("TA-LiveIndex-")) Directory.Delete(root, true);
}
