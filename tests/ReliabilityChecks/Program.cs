using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using TA.Server;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
var root=Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),"TA-Reliability-"+Guid.NewGuid().ToString("N"))).FullName;
try
{
    var archive=Directory.CreateDirectory(Path.Combine(root,"archivi")).FullName;
    var bad=Path.Combine(archive,"C26002 - Cliente - Data non valida.zip");
    var good=Path.Combine(archive,"C26001 - Cliente - Regolare.zip");
    foreach(var file in new[]{bad,good})
    {using var zip=ZipFile.Open(file,ZipArchiveMode.Create);using var writer=new StreamWriter(zip.CreateEntry("Progetto/prova.txt").Open());writer.Write("Documento di prova");}
    var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bad)));
    // Il FILETIME è valido per NTFS ma supera l'anno 9999 rappresentabile da DateTime.
    long invalid=long.MaxValue-1;
    using(var file=File.Open(bad,FileMode.Open,FileAccess.ReadWrite,FileShare.Read))
        Check(Native.SetFileTime(file.SafeFileHandle,IntPtr.Zero,IntPtr.Zero,ref invalid),"Preparata una data Win32 non rappresentabile su un archivio di prova");
    try{_ = new FileInfo(bad).LastWriteTimeUtc;throw new Exception("Fixture non riproduce il difetto");}catch(ArgumentOutOfRangeException){Check(true,"Riprodotto lo stesso errore fileTime del servizio");}
    var inspected=ArchivedJobsReader.Inspect(bad,default,true);
    Check(!inspected.Complete && inspected.FileCount==1 && inspected.Issues.Any(i=>i.Contains("Data ultima modifica non valida")),"Data fisica non valida: nomi interni e dimensioni conservati, esito parziale");
    var inventory=FileInventoryReader.Read(bad,new HashSet<string>(),default);
    Check(!inventory.Complete && inventory.Entries.Any(e=>e.RelativePath=="Progetto/prova.txt"),"Inventario prosegue dentro lo ZIP con data non valida");
    var cache=Path.Combine(root,"cache-archivi");
    var worker=new CategoryWorker('A',archive,cache,NullLogger.Instance,new(false));
    await worker.InitializeAsync();worker.Start(default);await worker.Completion;
    var rows=worker.State.Snapshot!.Records;
    Check(worker.Progress.Completed==2 && rows.All(r=>r.Inspection!=null),"La scansione elabora anche l'archivio successivo");
    Check(rows.Single(r=>r.Path==bad).ArchiveModifiedUtc==null && rows.Single(r=>r.Path==bad).ArchiveBytes>0 && rows.Single(r=>r.Path==good).Inspection!.Complete,"Data sconosciuta distinta da dimensione e archivio valido");
    Check(SharedArchiveService.Load(cache,archive,'A')!.Records.Count==2,"Entrambe le voci vengono salvate nel JSON");
    using(var file=File.OpenRead(bad)){Check(Native.GetFileTime(file.SafeFileHandle,out _,out _,out var stamp)&&stamp==invalid,"La scansione non corregge la data della sorgente");}
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bad)))==hash,"Contenuto dell'archivio sorgente invariato");
    var row=rows[0];
    var partial=RecordInspection.Read(row,'C',default,(_,_)=>throw new IOException("accesso temporaneamente fallito"),NullLogger.Instance);
    Check(!partial.Inspection!.Complete && partial.Inspection.Issues.Any(i=>i.Contains(row.Path)),"Errore di una singola voce isolato e identificato nel report");
    try{RecordInspection.Read(row,'C',default,(_,_)=>throw new OperationCanceledException(),NullLogger.Instance);throw new Exception("Annullamento intercettato");}catch(OperationCanceledException){Check(true,"Annullamento non nascosto come errore del file");}

    var store=Directory.CreateDirectory(Path.Combine(root,"json")).FullName;
    var jobs=Path.Combine(root,"lavori");
    var saved=DateTime.UtcNow;
    var snapshot=new ArchiveSnapshot{Root=SharedArchiveService.Normalize(jobs),Prefix='C',StartedUtc=saved,SavedUtc=saved,
        Records=[new("C26001","26","001","Cliente","Originale",Path.Combine(jobs,"2026","C26001"),"Valido")]};
    var target=SharedArchiveService.FilePath(store,jobs,'C');
    ArchiveSnapshot Revision(int n)=>snapshot with{SavedUtc=saved.AddSeconds(n),Records=[snapshot.Records[0] with{Subject="Revisione "+n}]};
    SharedArchiveService.Save(store,snapshot,default);
    using(var compatible=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete))
        Check(SharedArchiveService.Save(store,Revision(1),default),"Lettura e sostituzione compatibili con un handle condiviso esistente");
    var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.None);
    var retry=Task.Run(()=>SharedArchiveService.Save(store,Revision(2),default));
    await Task.Delay(350);Check(!retry.IsCompleted,"Salvataggio attende un blocco esclusivo temporaneo");locked.Dispose();
    Check(await retry.WaitAsync(TimeSpan.FromSeconds(12)),"Salvataggio ripreso dopo rilascio del blocco");
    using(var lockedAgain=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.None))
    using(var cancel=new CancellationTokenSource(250))
    {
        var watch=Stopwatch.StartNew();
        try{await Task.Run(()=>SharedArchiveService.Save(store,Revision(3),cancel.Token));throw new Exception("Attesa non annullata");}
        catch(OperationCanceledException){Check(watch.Elapsed<TimeSpan.FromSeconds(3),"Attesa del lock interrompibile durante l'arresto del servizio");}
    }
    Check(SharedArchiveService.Load(store,jobs,'C')!.SavedUtc==Revision(2).SavedUtc,"Annullamento conserva l'ultima generazione valida");
    using(var reader=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))
    {
        retry=Task.Run(()=>SharedArchiveService.Save(store,Revision(3),default));
        await Task.Delay(350);Check(!retry.IsCompleted,"Sostituzione atomica attende un lettore che nega DELETE");
    }
    Check(await retry.WaitAsync(TimeSpan.FromSeconds(12)),"Pubblicazione atomica ripresa senza perdere l'indice");
    var tasks=Enumerable.Range(4,16).Select(n=>Task.Run(()=>SharedArchiveService.Save(store,Revision(n),default)))
        .Concat(Enumerable.Range(0,20).Select(_=>Task.Run(()=>SharedArchiveService.Load(store,jobs,'C')!=null))).ToArray();
    await Task.WhenAll(tasks);
    Check(SharedArchiveService.Load(store,jobs,'C')!.SavedUtc==Revision(19).SavedUtc,"Lettori/scrittori concorrenti conservano la revisione più recente e tutti i JSON annuali validi");
    Check(!Directory.EnumerateFiles(store,"*.tmp",SearchOption.AllDirectories).Any(),"Nessun temporaneo residuo dopo ritentativi e annullamento");
}
finally
{
    if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("TA-Reliability-"))Directory.Delete(root,true);
}
static class Native
{
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool SetFileTime(SafeFileHandle file,IntPtr creation,IntPtr access,ref long write);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool GetFileTime(SafeFileHandle file,out long creation,out long access,out long write);
}
