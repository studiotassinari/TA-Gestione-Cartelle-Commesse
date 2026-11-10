using TA.Server;
using TA_Gestione_Cartelle_Commesse.Contracts;
internal static class AnalysisStatusChecks
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"TA-AnalysisStatus-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings=new ServerSettings{ArchiveFolder=root,DatabaseFolder=Path.Combine(root,"db")};
            Check(AnalysisStatusReader.Geology(settings,null).Candidates==null,"Catalogo assente distinto da zero");
            var hash=new string('A',64);var now=DateTime.UtcNow;
            var reports=new List<GeologicalReport>{new(){Id="old",Year="2025",ProjectCode="C25999",SourceFingerprint=hash},new(){Id="new",Year="2026",ProjectCode="C26005",SourceFingerprint=hash}};
            CatalogStorage.Write(settings,GeologyStorage.CatalogPath(settings),new GeologyCatalog(1,now,reports,[]));
            var geology=AnalysisStatusReader.Geology(settings,new(false,"In attesa",0,null,"Test"));
            Check(geology.Candidates==2&&geology.PendingAnalyses==2&&geology.CurrentAnalyses==0&&geology.NextProjectCode=="C26005","Stato geologia ordinato per anno prima del numero");
            var doc=new IpripiDocument("doc","C","folder","C26007","2026","MUR_A1_D1.pdf","inesistente.pdf",null,hash,true);
            var draft=new IpripiAnalysisDraft("doc","C26007",hash,hash,"MUR A.1/D.1","Proposta","Sintesi",[],[],[]);
            var saved=new IpripiAnalysisRecord("rev",now,"test",doc,draft);
            var murRoot=Path.Combine(root,"ipripi");
            CatalogStorage.Write(settings,Path.Combine(murRoot,"candidati.json"),new IpripiCandidateCatalog(1,now,false,[doc,doc with{Id="new",ProjectCode="C26008"}],[]));
            CatalogStorage.Write(settings,Path.Combine(murRoot,"indice.json"),new IpripiAnalysisIndex(1,[saved]));
            var mur=AnalysisStatusReader.Mur(settings,null);
            Check(mur.CurrentAnalyses==1&&mur.PendingAnalyses==1&&mur.LastAnalysisSavedUtc==now&&mur.NextProjectCode=="C26008","Stato MUR separa candidati e risultati salvati senza aprire documenti");
            Check(mur.CatalogDocuments==0&&mur.ExcludedOrUncertainAnalyses==1,"Un'analisi salvata non equivale a un modulo IPRiPI ammesso al catalogo");
            CatalogStorage.Write(settings,Path.Combine(murRoot,"candidati.json"),new IpripiCandidateCatalog(1,now,false,[doc with{SourceFingerprint=new string('B',64)}],[]));
            Check(AnalysisStatusReader.Mur(settings,null).OutdatedAnalyses==1,"Documento modificato richiede nuova analisi");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("TA-AnalysisStatus-"))Directory.Delete(root,true);
        }
    }
    private static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
}
