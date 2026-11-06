using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TA.Server;
using TA.Server.Documents;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;

public static class GeologyIndexChecks
{
    public static void Run(string root)
    {
        void Check(bool ok, string text) { if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text); }
        var settings=new ServerSettings{JobsRoot=Path.Combine(root,"originali"),BidsRoot=Path.Combine(root,"gare"),ArchivedJobsRoot=Path.Combine(root,"archivi"),ArchiveFolder=Path.Combine(root,"catalogo-autonomo")};
        var store=new InventoryStore(settings.ArchiveFolder);
        var folder=Path.Combine(settings.JobsRoot,"2026","C26001 - TEST - PROVA");
        var unit=new InventoryUnit(InventoryPlanner.Id(folder),"C","2026","C26001","Test","Prova",folder,"censita");
        var manifest=store.Save(unit,new([
            new("Relazione_geologica_2026.pdf","file"),new("Relazione_geotecnica.pdf","archive-file","documenti.zip"),
            new("geologia/dati.txt","file"),new("pianta.dwg","file"),new("preventivo.pdf","file")],[],true),default);
        var partial=new FileSearchService(settings).Search(new(Name:"geologica",Year:"2026"));
        Check(partial.Total==1&&!partial.Complete&&partial.Warnings.Any(s=>s.Contains("parziale")),"MCP può cercare i file delle unità già censite prima del report generale");
        var indexer=new GeologyIndexer(settings,NullLogger<GeologyIndexer>.Instance);
        var catalog=indexer.Refresh(default);
        Check(catalog.Reports.Count==3&&catalog.Reports.All(r=>r.DetectionStatus=="Da verificare"),"Catalogo autonomo da nomi e cartelle tematiche, senza falsi positivi DWG o preventivi generici");
        Check(!Directory.Exists(settings.JobsRoot),"Il catalogo si genera senza aprire o creare alcuna sorgente originale");
        Check(catalog.Reports.Single(r=>r.FileName=="Relazione_geotecnica.pdf").ArchiveEntry=="Relazione_geotecnica.pdf","Documento interno allo ZIP mantiene posizione fisica e percorso interno");
        Check(catalog.Reports.All(r=>!r.HasPosition),"Numeri di anno o commessa non diventano coordinate");
        var unclassified=GeologyClassifier.Classify(manifest with{Unit=unit with{Classification="proposta-non-confermata"}},new("Relazione_geologica.pdf","file"))!;
        Check(unclassified.ProjectCode==""&&unclassified.SuggestedProjectCode=="C26001","Una proposta di commessa non diventa associazione confermata");
        Check(CatalogStorage.Exists(settings,Path.Combine(GeologyStorage.Root(settings),"unita","C","2026","C26001",unit.Id+".json")),"Salvataggi geologici suddivisi per categoria, anno e commessa");
        var analysis=new AnalysisService(settings,NullLogger<AnalysisService>.Instance);var scans=new ScanCoordinator(settings,NullLogger<ScanCoordinator>.Instance,new(false),analysis);
        var service=new GeologyService(settings,new DocumentService(settings,scans));var page=service.Query(new(),default);var id=page.Reports[0].Id;
        var change=new GeologicalPositionUpdate(id,44.06,12.56,"Rimini","Posizione di collaudo","Indicata dall'utente",page.Version);
        service.SavePosition(change,"DOMINIO\\utente",default);indexer.Refresh(default);
        var updated=service.Query(new(),default).Reports.Single(r=>r.Id==id);
        Check(updated.HasPosition&&updated.Municipality=="Rimini"&&updated.PositionAccuracy.Contains("DOMINIO\\utente"),"Posizione condivisa con autore conservata dopo la rigenerazione del catalogo");
        try{service.SavePosition(change,"altro",default);throw new Exception("Versione vecchia accettata");}catch(IOException){}
        Check(!Directory.Exists(settings.JobsRoot),"La localizzazione modifica soltanto JSON del catalogo");
        var unsafeSettings=new ServerSettings{JobsRoot=settings.JobsRoot,ArchiveFolder=settings.JobsRoot};
        try{GeologyStorage.Root(unsafeSettings);throw new Exception("Sorgente scrivibile");}catch(InvalidOperationException){}
        try { TA.Transport.LocalReadOnlyPolicy.Validate(new("SaveGeologicalPosition", McpTool:"leggi_relazione_geologica", CorrelationId:Guid.NewGuid().ToString("N"))); throw new Exception("Scrittura via MCP consentita"); } catch(UnauthorizedAccessException) { }
        var stamp = new DateTime(2026,1,2,10,0,0,DateTimeKind.Utc);
        var entries = new List<InventoryEntry>{new("relazione-geologica.txt","file",Bytes:100,ModifiedUtc:stamp),new("rilievi.zip","archive",Bytes:1000,ModifiedUtc:stamp),new("relazione-geotecnica.txt","file","rilievi.zip",100,stamp)};
        store.Save(unit,new(entries,[],true),default); var baseline=indexer.Refresh(default);
        store.Save(unit,new(entries,[],true),default); var unchanged=indexer.Refresh(default);
        Check(baseline.Reports.Select(r=>r.SourceFingerprint).SequenceEqual(unchanged.Reports.Select(r=>r.SourceFingerprint)),"Nuova scansione senza differenze conserva le firme e non invalida le analisi");
        entries[1]=entries[1] with{Bytes=1100,ModifiedUtc=stamp.AddHours(1)};
        store.Save(unit,new(entries,[],true),default);var archiveChange=indexer.Refresh(default);
        Check(archiveChange.Reports.Single(r=>r.ArchiveEntry!=null).SourceFingerprint!=baseline.Reports.Single(r=>r.ArchiveEntry!=null).SourceFingerprint && archiveChange.Reports.Single(r=>r.ArchiveEntry==null).SourceFingerprint==baseline.Reports.Single(r=>r.ArchiveEntry==null).SourceFingerprint,"Archivio modificato invalida solo le sue relazioni, anche con date interne invariate");
        store.Save(unit,new([],[],true),default);
        Check(indexer.Refresh(default).Reports.Count==0,"Rimozioni confermate dall'inventario aggiornano il catalogo senza notifiche");
    }
}
