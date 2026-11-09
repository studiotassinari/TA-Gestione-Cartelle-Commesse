using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;

// Legge esclusivamente i cataloghi salvati nel database; non enumera originali né avvia lavori.
public static class AnalysisStatusReader
{
    public static CatalogAnalysisStatus Geology(ServerSettings settings,ScanProgress? indexing)
    {
        try
        {
            var path=GeologyStorage.CatalogPath(settings);
            if(!CatalogStorage.Exists(settings,path))return Missing("Geologia",indexing,"Catalogo non ancora disponibile");
            var catalog=CatalogStorage.Read<GeologyCatalog>(settings,path);
            var saved=new GeologyAnalysisStore(settings).Read();var byId=saved.ToDictionary(x=>x.Analysis.ReportId);
            var rows=catalog.Reports.Where(r=>r.OriginalAvailable).ToArray();
            bool Current(GeologicalReport r)=>r.SourceFingerprint.Length==64&&byId.TryGetValue(r.Id,out var a)&&a.Analysis.SourceFingerprint==r.SourceFingerprint;
            var pending=rows.Where(r=>!Current(r)).OrderByDescending(r=>AnalysisOrder.Year(r.Year,r.ProjectCode)).ThenByDescending(r=>AnalysisOrder.Number(r.ProjectCode)).ToArray();
            return new("Geologia",indexing,catalog.UpdatedUtc,rows.Length,saved.Count,rows.Count(Current),pending.Length,
                pending.Count(r=>byId.ContainsKey(r.Id)),saved.Select(r=>(DateTime?)r.SavedUtc).Max(),pending.FirstOrDefault()?.ProjectCode,catalog.Warnings.ToArray());
        }
        catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {return Missing("Geologia",indexing,ex.Message);}
    }
    public static CatalogAnalysisStatus Mur(ServerSettings settings,ScanProgress? indexing)
    {
        try
        {
            var root=Path.Combine(settings.ArchiveFolder,"ipripi");var path=Path.Combine(root,"candidati.json");
            if(!CatalogStorage.Exists(settings,path))return Missing("MUR A.1/D.1 — IPRiPI",indexing,"Catalogo non ancora disponibile");
            var catalog=CatalogStorage.Read<IpripiCandidateCatalog>(settings,path);var index=Path.Combine(root,"indice.json");
            var saved=CatalogStorage.Exists(settings,index)?CatalogStorage.Read<IpripiAnalysisIndex>(settings,index).Records:[];
            var byId=saved.ToDictionary(r=>r.Source.Id);
            var rows=catalog.Documents.Where(r=>r.OriginalAvailable&&r.Category is "C" or "A").Select(r=>r with{Saved=byId.GetValueOrDefault(r.Id)}).ToArray();
            var pending=rows.Where(r=>!r.AnalysisCurrent).OrderByDescending(r=>AnalysisOrder.Year(r.Year,r.ProjectCode)).ThenByDescending(r=>AnalysisOrder.Number(r.ProjectCode)).ToArray();
            return new("MUR A.1/D.1 — IPRiPI",indexing,catalog.CheckedUtc,rows.Length,saved.Count,rows.Count(r=>r.AnalysisCurrent),pending.Length,
                pending.Count(r=>r.Saved!=null),saved.Select(r=>(DateTime?)r.SavedUtc).Max(),pending.FirstOrDefault()?.ProjectCode,catalog.Warnings.ToArray());
        }
        catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {return Missing("MUR A.1/D.1 — IPRiPI",indexing,ex.Message);}
    }
    private static CatalogAnalysisStatus Missing(string name,ScanProgress? indexing,string reason)=>new(name,indexing,null,null,null,null,null,null,null,null,[reason]);
}
