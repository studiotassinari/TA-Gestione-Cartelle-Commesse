using System.IO;
using System.IO.Compression;
using TA.Server;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.Contracts;
internal static class DeepInspectionChecks
{
    public static void Run()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var root=Path.Combine(Path.GetTempPath(),"TA-Deep-"+Guid.NewGuid().ToString("N"));
        var settings=new ServerSettings{JobsRoot=Path.Combine(root,"lavori"),BidsRoot=Path.Combine(root,"gare"),ArchivedJobsRoot=Path.Combine(root,"archivio"),ArchiveFolder=Path.Combine(root,"data")};
        var folder=Directory.CreateDirectory(Path.Combine(settings.JobsRoot,"2026","C26001 - Cliente - Progetto")).FullName;
        var admin=Directory.CreateDirectory(Path.Combine(folder,"99_AMMINISTRAZIONE")).FullName;
        File.WriteAllText(Path.Combine(admin,"Preventivo.txt"),"TESTO RISERVATO NON DA TRASMETTERE AUTOMATICAMENTE");
        var simulation=Directory.CreateDirectory(Path.Combine(folder,"calcolo")).FullName;
        File.WriteAllText(Path.Combine(simulation,"main.scj"),"");File.WriteAllText(Path.Combine(simulation,"preventivo-escluso.txt"),"escluso");
        using(var zip=ZipFile.Open(Path.Combine(folder,"documenti.zip"),ZipArchiveMode.Create)){using var w=new StreamWriter(zip.CreateEntry("Contratto.pdf").Open());w.Write("fixture");}
        var record=new FolderRecord("C26001","26","001","Cliente","Progetto",folder,"Valido");
        var before=File.GetLastWriteTimeUtc(Path.Combine(admin,"Preventivo.txt"));
        var limited=FileInventoryReader.Read(folder,new HashSet<string>(),default,maximum:1);
        Check(!limited.Complete && limited.Entries.Count==1 && limited.Issues.Any(i=>i.Contains("Limite di voci")),"Limite su cartelle restituisce inventario parziale senza interrompere le altre scansioni");
        var limitedZip=FileInventoryReader.Read(Path.Combine(folder,"documenti.zip"),new HashSet<string>(),default,maximum:1);
        Check(!limitedZip.Complete && limitedZip.Entries.Count==1,"Limite su ZIP dichiarato parziale senza superare il massimo");
        var result=DeepInspectionService.Inspect(settings,"C",record,"preventivo;contratto",default);
        Check(result.GetProperty("Documents").GetArrayLength()==2,"Approfondimento mirato include sottocartelle e ZIP, esclude Sismicad");
        Check(!result.ToString().Contains("TESTO RISERVATO"),"Nessun contenuto letto o inviato automaticamente");
        Check(result.GetProperty("Documents").EnumerateArray().Any(r=>r.GetProperty("ReadRelativePath").GetString()=="documenti.zip"&&r.GetProperty("ArchiveEntry").GetString()=="Contratto.pdf"),"Percorsi pronti per leggi_documento");
        Check(File.GetLastWriteTimeUtc(Path.Combine(admin,"Preventivo.txt"))==before,"Documento originale invariato");
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        try{DeepInspectionService.Inspect(settings,"C",record,"",canceled.Token);throw new Exception("Annullamento ignorato");}catch(OperationCanceledException){}
        try{DeepInspectionService.ValidateRoot(settings,"C",Path.Combine(root,"estranea"));throw new Exception("Radice estranea ammessa");}catch(UnauthorizedAccessException){}
        Check(ArchiveScope.Contains(settings.ArchivedJobsRoot,Path.Combine(settings.ArchivedJobsRoot,"_lavori","2025","a.zip"))&&!ArchiveScope.Contains(settings.ArchivedJobsRoot,Path.Combine(settings.ArchivedJobsRoot,"_lavori","2025","profonda","a.zip")),"Approfondimento Archivio rispetta il limite di profondità");
        var store=new InventoryStore(settings.InventoryFolder);
        var keep=new InventoryUnit(new string('A',64),"C","2026","C26001","Cliente","Progetto",folder,"censita");
        store.Save(keep,new([],[],true),default);store.Save(keep with{Id=new string('B',64),Category="A"},new([],[],true),default);
        store.SaveDocument("snapshot:C:"+SharedArchiveService.Normalize(settings.JobsRoot),new ArchiveSnapshot{Root=settings.JobsRoot,Prefix='C',StartedUtc=DateTime.UtcNow,SavedUtc=DateTime.UtcNow,Records=[record]});
        var scans=new ScanCoordinator(settings,Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanCoordinator>.Instance,new ChangeHub(false),new AnalysisService(settings,Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalysisService>.Instance));
        scans.Get("C").InitializeAsync().GetAwaiter().GetResult();
        using(var service=new DeepInspectionService(settings,scans))
        {
            var start=service.Execute("C",new(FolderId:record.Id,Terms:"preventivo"));var jobId=start.GetProperty("Id").GetString()!;
            var deadline=DateTime.UtcNow.AddSeconds(10);System.Text.Json.JsonElement state;
            do{Thread.Sleep(25);state=service.Execute("C",new("stato",JobId:jobId));}while(state.GetProperty("Finished").ValueKind==System.Text.Json.JsonValueKind.Null&&DateTime.UtcNow<deadline);
            Check(state.GetProperty("State").GetString()=="Completato"&&state.GetProperty("Total").GetInt32()==1,"Comando avvia/stato completa il lavoro mirato");
            Check(store.Document("approfondimento:C:"+record.Id)!=null,"Riepilogo persistente nel database locale");
        }
        store.RemoveConfirmedMany([new string('B',64)],default);
        Check(store.Find(new string('B',64))==null&&store.Find(new string('A',64))!=null,"Pulizia a lotti rimuove solo i metadati richiesti");
    }
}
