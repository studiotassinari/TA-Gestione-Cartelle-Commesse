using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using TA.Server;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
var stamp=new DateTime(2026,10,25,0,35,0,DateTimeKind.Utc);
Check(HourlySchedule.IsDue(stamp,null),"Prima scansione immediata senza checkpoint");
Check(!HourlySchedule.IsDue(stamp.AddMinutes(24),stamp) && HourlySchedule.IsDue(stamp.AddMinutes(25),stamp),"Una sola scansione nello slot, successiva allo scoccare dell'ora");
Check(HourlySchedule.Next(stamp)==stamp.Date.AddHours(1) && HourlySchedule.Next(stamp.AddHours(2))==stamp.Date.AddHours(3),"Scansione lunga salta gli slot occupati senza coda");
Check(DailyReports.ItalianTime(new DateTimeOffset(stamp)).Hour==2 && DailyReports.ItalianTime(new DateTimeOffset(stamp.AddHours(1))).Hour==2 && HourlySchedule.IsDue(stamp.AddHours(1),stamp),"Ore ripetute al cambio solare distinte in UTC");
Check(new ScanProgress(true,"preparazione",0,null,"").Percentage==null && new ScanProgress(true,"nomi",1,4,"").Percentage==25,"Nessuna percentuale inventata durante la preparazione");
var root=Path.Combine(Path.GetTempPath(),"TA-Hourly-"+Guid.NewGuid().ToString("N"));
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(40));
var token=timeout.Token;
var jobs=Path.Combine(root,"jobs");var year=Directory.CreateDirectory(Path.Combine(jobs,"2026")).FullName;
var bids=Directory.CreateDirectory(Path.Combine(root,"bids")).FullName;
var archives=Directory.CreateDirectory(Path.Combine(root,"archives")).FullName;
var cache=Path.Combine(root,"cache");
var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using var release=new ManualResetEventSlim();
CategoryWorker? worker=null;ScanCoordinator? coordinator=null;
try
{
    var job=Directory.CreateDirectory(Path.Combine(year,"C26198 - Cliente - Progetto")).FullName;
    File.WriteAllText(Path.Combine(job,"prova.txt"),"dato di test");
    worker=new('C',jobs,cache,NullLogger.Instance,new(false),(p,ct)=>{
        entered.TrySetResult();release.Wait(ct);
        return new FolderInspection{Complete=true,CheckedUtc=DateTime.UtcNow};
    });
    await worker.InitializeAsync();Check(worker.Start(token),"Avvio lettura metadati");
    await entered.Task.WaitAsync(token);
    Check(!worker.Start(token),"Una categoria occupata rifiuta una scansione sovrapposta");
    Check(worker.Progress.IsRunning && worker.Progress.Completed==0 && worker.Progress.Total==1,"Cartella corrente non conteggiata prima della fine della lettura");
    release.Set();await worker.Completion.WaitAsync(token);
    Check(!worker.Progress.IsRunning && worker.Progress.Percentage==100 && !worker.Progress.HasErrors,"Lettura completata aggiorna il contatore");
    var settings=new ServerSettings{JobsRoot=jobs,BidsRoot=bids,ArchivedJobsRoot=archives,ArchiveFolder=cache};
    var legacy=new LegacyInventoryStore(cache);
    var migrationUnit=new InventoryUnit(new string('E',64),"C","2026","C26198","Cliente","Progetto",job,"censita");
    legacy.Save(migrationUnit,new([new("test.txt","file",Bytes:1),new("test.txt","file",Bytes:2)],[],true),default);
    using(var blockedCoordinator=new ScanCoordinator(settings,NullLogger<ScanCoordinator>.Instance,new(false),new AnalysisService(settings,NullLogger<AnalysisService>.Instance)))
    {
        await blockedCoordinator.StartAsync(token);
        while(blockedCoordinator.Progress.Phase!="Migrazione da completare")await Task.Delay(30,token);
        Check(blockedCoordinator.Get("C").State.Snapshot?.Records.Count==1 && !blockedCoordinator.Get("C").State.Busy,"elenco salvato consultabile anche quando la migrazione file è bloccata");
        await blockedCoordinator.StopAsync(token);
    }
    legacy.Save(migrationUnit,new([new("test.txt","file",Bytes:1)],[],true),default);
    var registrations = new ServiceCollection();
    registrations.AddLogging(); registrations.AddSingleton(settings); registrations.AddSingleton(new ChangeHub(false));
    registrations.AddSingleton<AnalysisService>(); registrations.AddSingleton<ScanCoordinator>();
    registrations.AddSingleton<TA.Server.Documents.DocumentService>(); registrations.AddSingleton<IpripiService>();
    registrations.AddSingleton<IpripiIndexer>(); registrations.AddSingleton<GeologyIndexer>();
    using (var provider = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild=true, ValidateScopes=true }))
    {
        var integrated = provider.GetRequiredService<ScanCoordinator>();
        await integrated.StartAsync(token);
        while (integrated.Progress.FinishedUtc == null) await Task.Delay(30, token);
        await integrated.StopAsync(token);
        Check(CatalogStorage.Exists(settings,Path.Combine(cache,"ipripi","candidati.json")),"Host completo: nessuna dipendenza circolare e catalogo MUR aggiornato dopo inventario");
    }
    var analysis=new AnalysisService(settings,NullLogger<AnalysisService>.Instance);
    var progress=new List<ScanProgress>();
    await analysis.RunAsync([worker.State,new('G',bids,false,"","",null),new('A',archives,false,"","",null)],token,progress.Add);
    Check(progress.First().Percentage==null && progress.Any(p=>p.Phase=="Inventario" && p.Percentage==100),"Inventario pubblica preparazione e avanzamento reale");
    Check(progress.Where(p=>p.Phase=="Inventario").Select(p=>p.Completed).SequenceEqual(progress.Where(p=>p.Phase=="Inventario").Select(p=>p.Completed).Order()),"Avanzamento monotono nella fase inventario");
    coordinator=new(settings,NullLogger<ScanCoordinator>.Instance,new(false),analysis);
    await coordinator.StartAsync(token);
    while(coordinator.Progress.FinishedUtc==null)await Task.Delay(30,token);
    Check(!coordinator.Progress.IsRunning && coordinator.Progress.NextRunUtc>coordinator.Progress.FinishedUtc,"Coordinatore completa la prima scansione e pianifica la successiva");
    Check(coordinator.DailyReport().Contains("Report giornaliero"),"Report giornaliero leggibile dal server");
    var completed=coordinator.Progress.FinishedUtc;
    await coordinator.StopAsync(token);coordinator.Dispose();coordinator=null;
    Check(HourlySchedule.Load(Path.Combine(cache,"report-giornalieri"))==completed,"Checkpoint persistente solo dopo report completato");
    coordinator=new(settings,NullLogger<ScanCoordinator>.Instance,new(false),analysis);
    await coordinator.StartAsync(token);
    while(coordinator.Progress.FinishedUtc==null)await Task.Delay(30,token);
    Check(!coordinator.Progress.IsRunning && coordinator.Progress.FinishedUtc>completed,"Riavvio esegue una nuova scansione completa anche con checkpoint recente");
    await coordinator.StopAsync(token);coordinator.Dispose();coordinator=null;
    Directory.Move(jobs,jobs+"-offline");
    worker.Start(token);await worker.Completion.WaitAsync(token);
    Check(worker.Progress.HasErrors && !worker.Progress.IsRunning,"Radice assente segnalata senza falsa scansione riuscita");
}
finally
{
    release.Set();if(coordinator!=null){await coordinator.StopAsync(CancellationToken.None);coordinator.Dispose();}
    timeout.Cancel();if(worker!=null)await worker.Completion;
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("TA-Hourly-"))Directory.Delete(root,true);
}
