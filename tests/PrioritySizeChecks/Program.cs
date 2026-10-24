using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using TA.Server;
using TA_Gestione_Cartelle_Commesse.Models;
static void Check(bool condition, string text) { if (!condition) throw new Exception(text); Console.WriteLine("PASS: " + text); }
using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var token = limit.Token;
var root = Path.Combine(Path.GetTempPath(), "TA-Priority-" + Guid.NewGuid().ToString("N"));
var year = Directory.CreateDirectory(Path.Combine(root, "lavori", "2026")).FullName;
var old = Directory.CreateDirectory(Path.Combine(year, "C26001 - Vecchia - Lenta")).FullName;
var archive = Directory.CreateDirectory(Path.Combine(root, "json")).FullName;
using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
var hub = new ChangeHub();
var worker = new CategoryWorker('C', Path.GetDirectoryName(year)!, archive, NullLogger.Instance, hub, (path, ct) =>
{
    if (path == old) { entered.Set(); release.Wait(ct); }
    return new FolderInspection { TotalBytes = path == old ? 1 : 4096, FileCount = 1, CheckedUtc = DateTime.UtcNow, Complete = true };
});
try
{
    await worker.InitializeAsync(); worker.Start(token); Check(entered.Wait(5000, token), "Scansione vecchia bloccata per il test");
    var fresh = Directory.CreateDirectory(Path.Combine(year, "C26198 - Nuova - Commessa")).FullName;
    await worker.RefreshIndexAsync(token);
    while (worker.State.Snapshot!.Records.First(r => r.Path == fresh).Inspection == null) await Task.Delay(30, token);
    Check(!release.IsSet && worker.State.Snapshot.Records.First(r => r.Path == fresh).Inspection!.TotalBytes == 4096, "Dimensione nuova commessa aggiornata prima del completamento della scansione vecchia");
    Check(worker.State.Snapshot.Records.First(r => r.Path == fresh).SizeText.Contains("KiB"), "Dimensioni piccole mostrate senza arrotondarle a zero MiB");
}
finally
{
    release.Set(); await worker.Completion;
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("TA-Priority-")) Directory.Delete(root, true);
}
