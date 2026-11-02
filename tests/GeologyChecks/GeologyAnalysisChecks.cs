using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TA.Server;
using TA.Server.Documents;
using TA.Transport;
using TA.Mcp;
using TA_Gestione_Cartelle_Commesse.Contracts;

internal static class GeologyAnalysisChecks
{
    private static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    private static async Task Reject<T>(Func<Task> work) where T:Exception {try{await work();}catch(T){return;}throw new Exception("Atteso "+typeof(T).Name);}
    public static async Task Run(string root)
    {
        var source=Path.Combine(root,"analisi-sorgenti");Directory.CreateDirectory(source);
        const string text="Relazione geologica per ampliamento del deposito. Sito: Rimini, Via Esempio 1. Coordinate WGS84: latitudine 44.06, longitudine 12.56.";
        var first=Path.Combine(source,"geologica.txt");var second=Path.Combine(source,"geotecnica.txt");File.WriteAllText(first,text);File.WriteAllText(second,text+" Allegato.");
        var original=SHA256.HashData(File.ReadAllBytes(first));var stamp=File.GetLastWriteTimeUtc(first);
        var signature=new string('A',64);var reports=new List<GeologicalReport>{new(){Id="test-analysis-1",Title="geologica",FileName="geologica.txt",PhysicalPath=first,ProjectCode="C26001",Year="2026",SourceFingerprint=signature,SourceModifiedUtc=DateTime.UtcNow},new(){Id="test-analysis-2",Title="geotecnica",FileName="geotecnica.txt",PhysicalPath=second,ProjectCode="C26001",Year="2026",SourceFingerprint=signature}};
        var settings=new ServerSettings{ArchiveFolder=Path.Combine(root,"analisi-json"),JobsRoot=source,BidsRoot=Path.Combine(root,"gare"),ArchivedJobsRoot=Path.Combine(root,"archivio")};
        var path=GeologyStorage.CatalogPath(settings);var catalog=new GeologyCatalog(1,DateTime.UtcNow,reports,[]);GeologyStorage.Atomic(path,catalog);
        var scans=new ScanCoordinator(settings,NullLogger<ScanCoordinator>.Instance,new ChangeHub(false),new AnalysisService(settings,NullLogger<AnalysisService>.Instance));
        var service=new GeologyService(settings,new DocumentService(settings,scans));
        var pending=service.Pending(new(),default);Check(pending.TotalPending==2,"Coda JSON contiene i soli documenti ancora da analizzare");
        var page=await service.ReadAsync(new(reports[0].Id),default);
        GeologicalAnalysisDraft Draft(string id,string version)=>new(id,signature,version,"Ampliamento deposito","Geologica","Relazione per il deposito.","","Rimini","Via Esempio 1",44.06,12.56,"Coordinate nel documento","Relazione sintetica di collaudo","","Deposito di prova","Stesso intervento e sito indicati nella relazione",[
            new("classificazione",0,"Relazione geologica"),new("fascicolo",0,"Relazione geologica per ampliamento del deposito."),
            new("localita",text.IndexOf("Sito:"),"Sito: Rimini, Via Esempio 1."),new("coordinate",text.IndexOf("Coordinate"),"Coordinate WGS84: latitudine 44.06, longitudine 12.56.")],[]);
        var draft=Draft(reports[0].Id,page.Version);
        await Reject<UnauthorizedAccessException>(async()=>await service.SaveAnalysisAsync(draft,"utente-test",default));
        settings.EnableMcpGeologyWrites=true;
        await Reject<ArgumentException>(async()=>await service.SaveAnalysisAsync(draft with{Evidence=[new("classificazione",0,"Citazione inventata"),new("fascicolo",0,"Citazione inventata")]},"utente-test",default));
        var saved=await service.SaveAnalysisAsync(draft,"utente-test",default);
        Check(saved.SavedBy=="utente-test"&&Guid.TryParseExact(saved.Analysis.DossierId,"N",out _),"Analisi salvata con autore Windows, revisione e fascicolo generato dal server");
        var current=service.Query(new(),default);var row=current.Reports.Single(r=>r.Id==reports[0].Id);
        Check(row.AnalysisCurrent&&row.Latitude==44.06&&row.DossierTitle=="Deposito di prova"&&row.PositionAccuracy.Contains("da verificare"),"Catalogo applica analisi e coordinate come proposte tracciabili");
        Check(current.UpdatedUtc >= saved.SavedUtc && row.AnalysisSavedUtc == saved.SavedUtc,"Data del catalogo e della scheda include il risultato appena salvato");
        Check(service.Pending(new(),default).TotalPending==1,"Documenti invariati già analizzati esclusi dalla coda");
        Check(Directory.EnumerateFiles(Path.Combine(GeologyStorage.Root(settings),"analisi","2026","C26001"),"*.json",SearchOption.AllDirectories).Count()==1,"Revisione JSON strutturata per anno e commessa");
        await Reject<IOException>(async()=>await service.SaveAnalysisAsync(draft,"altro-utente",default));
        var secondPage=await service.ReadAsync(new(reports[1].Id),default);
        var attached=await service.SaveAnalysisAsync(Draft(reports[1].Id,secondPage.Version) with{DossierId=saved.Analysis.DossierId},"secondo-utente",default);
        var dossiers=service.Dossiers(new(),default);Check(dossiers.Total==1&&dossiers.Dossiers.Single().ReportIds.Count==2,"Due relazioni collegate a un fascicolo omogeneo persistente");
        Check(SHA256.HashData(File.ReadAllBytes(first)).SequenceEqual(original)&&File.GetLastWriteTimeUtc(first)==stamp,"Salvataggi e raggruppamenti lasciano immutati contenuto e data originali");
        Check(Directory.EnumerateFiles(Path.Combine(GeologyStorage.Root(settings),"fascicoli"),"*.json",SearchOption.AllDirectories).Count()==2,"Revisioni del fascicolo memorizzate in file JSON separati");
        var restarted=new GeologyService(settings,new DocumentService(settings,scans));Check(restarted.Pending(new(),default).TotalPending==0,"Analisi mantenute dopo riavvio del servizio");
        var updated=catalog with{Reports=[reports[0] with{SourceFingerprint=new string('B',64)},reports[1]]};GeologyStorage.Atomic(path,updated);
        Check(service.Pending(new(),default).Reports.Single().Id==reports[0].Id,"Solo il documento modificato torna da analizzare");
        Check(service.Dossiers(new(),default).Dossiers.Single().StaleDocuments==1,"Fascicolo segnala la relazione con analisi superata");
        GeologyStorage.Atomic(path,catalog with{Reports=[reports[1]]});
        Check(service.Query(new(),default).Reports.Single(r=>r.Id==reports[0].Id).OriginalAvailable==false && service.Dossiers(new(),default).Dossiers.Single().ReportIds.Count==2,"Rimozione della fonte conserva analisi e fascicolo segnalando il documento assente");
        GeologyStorage.Atomic(path,catalog);
        await service.ReadAsync(new(reports[0].Id),default);File.AppendAllText(first," Modifica dopo la lettura.");
        await Reject<IOException>(async()=>await service.SaveAnalysisAsync(draft with{ExpectedRevision=saved.Revision},"utente-test",default));
        File.WriteAllText(first,text);File.SetLastWriteTimeUtc(first,stamp);
        Check(SHA256.HashData(File.ReadAllBytes(first)).SequenceEqual(original),"La catalogazione non modifica il contenuto degli originali");
        var forged=new Request("SaveGeologicalAnalysis",McpTool:"cerca_cartelle",CorrelationId:Guid.NewGuid().ToString("N"));
        await Reject<UnauthorizedAccessException>(()=>Task.Run(()=>LocalCatalogPolicy.Validate(forged)));
        await Reject<UnauthorizedAccessException>(()=>Task.Run(()=>LocalReadOnlyPolicy.Validate(forged with{McpTool="salva_analisi_geologica"})));
        var names=JsonSerializer.SerializeToElement(ConsultationSessions.Shared.Open(false)).GetProperty("consultazione").GetString()!;
        await Reject<ModelContextProtocol.McpException>(()=>Task.Run(()=>McpInvocation.Begin("salva_analisi_geologica",names)));
        var allowed=JsonSerializer.SerializeToElement(ConsultationSessions.Shared.Open(true,true)).GetProperty("consultazione").GetString()!;
        using(var invocation=McpInvocation.Begin("salva_analisi_geologica",allowed))LocalCatalogPolicy.Validate(McpInvocation.Tag(new("SaveGeologicalAnalysis")));
        Check(true,"Scrittura catalogo richiede sessione esplicita; vecchio canale readonly e comandi generici restano vietati");
    }
}
