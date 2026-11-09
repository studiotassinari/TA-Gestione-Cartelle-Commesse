using System.IO;
using TA.Server;
using TA.Server.Documents;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using Microsoft.Extensions.Logging.Abstractions;
internal static class AnalysisPacingChecks
{
    public static void Run(string root)
    {
        void Check(bool value,string text){if(!value)throw new Exception(text);}
        void Reject(Action action){try{action();}catch(IOException){return;}throw new Exception("Lettura concorrente o anticipata ammessa");}
        var now=new DateTime(2026,11,7,10,0,0,DateTimeKind.Utc);
        var schedule=new AnalysisReadSchedule(()=>now);
        var separate=new AnalysisReadSchedule(()=>now);
        using(var geology=schedule.Enter("geologia"))using(var mur=separate.Enter("mur")){Reject(()=>schedule.Enter("altro"));Reject(()=>separate.Enter("altro"));}

        using(var first=schedule.Enter("geologia")){Reject(()=>schedule.Enter("mur"));Reject(()=>schedule.Enter("geologia"));}
        using(schedule.Enter("geologia")){} // Altre pagine dello stesso documento.
        now=now.AddSeconds(59);Reject(()=>schedule.Enter("mur"));
        now=now.AddSeconds(1);using(schedule.Enter("mur")){}
        Reject(()=>schedule.Enter("geologia"));
        AnalysisReadState? saved=null;
        using(new AnalysisReadSchedule(()=>now,save:s=>saved=s).Enter("primo")){}
        Reject(()=>new AnalysisReadSchedule(()=>now,load:()=>saved).Enter("secondo"));
        Check(AnalysisOrder.Year("1999","C99001")<AnalysisOrder.Year("2026","C26001"),"Ordine secolo non corretto");
        Check(GeologyListReferences.Match("R01.pdf","R01 - Relazione geologico-geotecnica")!=null,"Riferimento codice non trovato");
        Check(GeologyListReferences.Match("R01.pdf","R010 - Relazione geologica")==null,"Codice parziale accettato");
        Check(GeologyListReferences.Match("R01.pdf","R01 - Relazione tecnica")==null,"Relazione tecnica spacciata per geologica");
        var settings=new ServerSettings{ArchiveFolder=Path.Combine(root,"elenchi"),JobsRoot=Path.Combine(root,"originali-inesistenti")};
        var documentService=new DocumentService(settings,null!);
        using(documentService.GeologySchedule.Enter("geo-1"))using(documentService.MurSchedule.Enter("mur-1"))
        {
            Check(documentService.GeologySchedule.Status("GEOLOGIA").IsReading,"Stato canale geologia non occupato");
            Check(documentService.MurSchedule.Status("MUR").IsReading,"Stato canale MUR non occupato");
        }
        Check(!documentService.GeologySchedule.Status("GEOLOGIA").IsReading,"Canale non rilasciato");
        var restarted=new DocumentService(settings,null!);
        Reject(()=>restarted.GeologySchedule.Enter("geo-2"));
        Reject(()=>restarted.MurSchedule.Enter("mur-2"));
        var store=new InventoryStore(settings.InventoryFolder);
        var unit=new InventoryUnit(new string('A',64),"C","2026","C26199","Cliente","Progetto",Path.Combine(settings.JobsRoot,"2026","C26199"),"censita");
        var entries=new List<InventoryEntry>{new("Elenco_elaborati.pdf","file",Bytes:10,ModifiedUtc:now),new("R01.pdf","file",Bytes:20,ModifiedUtc:now)};
        var manifest=store.Save(unit,new(entries,[],true),default);
        var indexer=new GeologyIndexer(settings,NullLogger<GeologyIndexer>.Instance);
        var catalog=indexer.Refresh(default);Check(catalog.Reports.Count==1&&catalog.Reports[0].Kind=="Elenco elaborati","Elenco non distinto dalla relazione");
        const string text="R01 - Relazione geologico-geotecnica";
        GeologyListReferences.Discover(settings,catalog.Reports[0],new(unit.Id,"Elenco_elaborati.pdf","Elenco_elaborati.pdf",null,new string('B',64),now,text.Length,0,null,text,false,[]),default);
        Check(GeologyListReferences.Merge(settings,catalog).Reports.Count==2,"Documento rinominato non aggiunto");
        store.Save(unit,new(entries,[],true),default);
        Check(GeologyListReferences.Merge(settings,indexer.Refresh(default)).Reports.Any(r=>r.FileName=="R01.pdf"),"Riferimento perso al ricensimento invariato");
        entries[1]=entries[1] with{Bytes=30};store.Save(unit,new(entries,[],true),default);
        Check(indexer.Refresh(default).Reports.Single(r=>r.FileName=="R01.pdf").SourceBytes==30,"Metadati riferimento non aggiornati");
        entries.RemoveAt(1);store.Save(unit,new(entries,[],true),default);
        Check(GeologyListReferences.Merge(settings,indexer.Refresh(default)).Reports.Count==1,"Riferimento eliminato resuscitato");
        Check(!Directory.Exists(settings.JobsRoot),"Sono state aperte o create sorgenti originali");
        Console.WriteLine("PASS: intervallo 60 secondi per canale, geologia e MUR indipendenti, esclusione concorrente, ordine anni, elenchi e riferimenti senza apertura originali");
    }
}
