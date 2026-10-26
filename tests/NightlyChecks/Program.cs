using Microsoft.Extensions.Logging.Abstractions;
using TA.Server;
using TA_Gestione_Cartelle_Commesse.Services;
static void Check(bool condition, string text) { if (!condition) throw new Exception(text); Console.WriteLine("PASS: " + text); }
var root = Path.Combine(Path.GetTempPath(), "TA-Nightly-" + Guid.NewGuid().ToString("N"));
var jobs = Path.Combine(root, "jobs"); var year = Directory.CreateDirectory(Path.Combine(jobs, "2026")).FullName;
var bids = Directory.CreateDirectory(Path.Combine(root, "bids")).FullName;
var archives = Directory.CreateDirectory(Path.Combine(root, "compressed")).FullName;
var cache = Directory.CreateDirectory(Path.Combine(root, "cache")).FullName;
var reports = Path.Combine(cache, "report-giornalieri");
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var workers = new[] { new CategoryWorker('C', jobs, cache, NullLogger.Instance, new()), new CategoryWorker('G', bids, cache, NullLogger.Instance, new()), new CategoryWorker('A', archives, cache, NullLogger.Instance, new()) };
try
{
    foreach(var worker in workers) await worker.InitializeAsync();
    var job = Directory.CreateDirectory(Path.Combine(year, "C26198 - Cliente - Progetto")).FullName;
    var nested = Directory.CreateDirectory(Path.Combine(job, "Interna")).FullName;
    File.WriteAllText(Path.Combine(nested, "documento.txt"), "test");
    await workers[0].RefreshIndexAsync(stop.Token); await workers[0].Completion;
    Check(workers[0].State.Snapshot!.Records.Single().Inspection == null, "Censimento nuova commessa senza calcolare dimensioni o gerarchia");
    File.AppendAllText(Path.Combine(nested, "documento.txt"), "file modificato");
    await workers[0].RefreshIndexAsync(stop.Token);
    Check(workers[0].State.Snapshot!.Records.Single().Inspection == null, "Aggiornamento elenco non avvia analisi file");
    Check(!DailyReports.IsDue(new DateTimeOffset(2026,10,26,21,59,0,TimeSpan.Zero), reports), "Prima delle 23 italiane nessuna scansione approfondita");
    Check(DailyReports.IsDue(new DateTimeOffset(2026,10,26,22,0,0,TimeSpan.Zero), reports), "Scadenza alle 23 in inverno");
    Check(DailyReports.IsDue(new DateTimeOffset(2026,7,26,21,0,0,TimeSpan.Zero), reports), "Scadenza alle 23 anche con ora legale");
    var started = DateTimeOffset.UtcNow;
    foreach(var worker in workers) worker.Start(stop.Token);
    await Task.WhenAll(workers.Select(w => w.Completion));
    var record = workers[0].State.Snapshot!.Records.Single();
    Check(record.Inspection?.TotalBytes == 19 && record.Inspection.HierarchyCaptured, "Elaborazione notturna raccoglie dimensioni e gerarchia");
    // La consultazione successiva usa il censimento salvato anche se la sorgente non esiste piu.
    Directory.Move(job, job + "-spostata");
    Check(HierarchyService.Children(workers[0].State, record.Id, "").Single().Name == "Interna", "Gerarchia consultabile dalla cache senza accesso alla sorgente");
    DailyReports.Save(reports, new DateTime(2026,10,26), workers, started);
    Check(DailyReports.Latest(reports).Contains("Interna") && DailyReports.Latest(reports).Contains("Report giornaliero"), "Report persistente include dimensioni e gerarchie");
    Check(!DailyReports.IsDue(new DateTimeOffset(2026,10,26,22,30,0,TimeSpan.Zero), reports), "Report completato impedisce una seconda esecuzione dopo riavvio");
    var restored = new CategoryWorker('C', jobs, cache, NullLogger.Instance, new()); await restored.InitializeAsync();
    Check(restored.State.Snapshot!.Records.Single().Inspection!.ArchiveDirectories.Contains("Interna"), "Gerarchia conservata nei JSON annuali");
}
finally
{
    stop.Cancel(); await Task.WhenAll(workers.Select(w => w.Completion));
    if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath())) && Path.GetFileName(root).StartsWith("TA-Nightly-")) Directory.Delete(root,true);
}
