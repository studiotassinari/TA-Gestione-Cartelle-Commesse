using System.Globalization;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Services;
public sealed partial class InventoryStore
{
    // Filtri, deduplicazione e paginazione sono eseguiti nel database, non sul client.
    public FileSearchPage Search(FileSearchQuery q,CancellationToken token)
    {
        FileSearchValidation.Validate(q); token.ThrowIfCancellationRequested();
        using var db=Open(); using var tx=db.BeginTransaction(deferred:true);
        var manifests=new List<InventoryManifest>();
        using(var cmd=Command(db,tx,"SELECT manifest FROM units WHERE active=1"))
        using(var reader=cmd.ExecuteReader())while(reader.Read())manifests.Add(JsonSerializer.Deserialize<InventoryManifest>(reader.GetString(0))!);
        if(manifests.Count==0)throw new IOException("Inventario non ancora disponibile: attendere la prima scansione.");
        InventoryRun? run;
        using(var cmd=Command(db,tx,"SELECT payload FROM state WHERE key='run'"))
            run=cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<InventoryRun>(json):null;
        var last=new[]{run?.CompletedUtc??DateTime.MinValue,manifests.Max(m=>m.CheckedUtc)}.Max();
        using var revision=Command(db,tx,"SELECT payload FROM state WHERE key='revision'");
        var version=last.ToString("O",CultureInfo.InvariantCulture)+":"+(revision.ExecuteScalar() as string??"0");
        if(q.Version!=null && q.Version!=version)throw new ArgumentException("Inventario cambiato: ripartire da offset 0 senza version.");
        const string selection="""
            WITH selected AS (
              SELECT e.payload,u.manifest,e.location_id,
              ROW_NUMBER() OVER(PARTITION BY e.location_id ORDER BY (u.code=''),u.category,u.id,e.key) AS rn
              FROM entries e JOIN units u ON u.id=e.unit_id
              WHERE u.active=1 AND e.kind<>'directory' AND ($category='' OR u.category=$category)
                AND ($year='' OR u.year=$year) AND ($code='' OR substr(upper(u.code),1,length($code))=$code)
                AND ($name='' OR instr(e.name,$name)>0) AND ($ext='' OR e.extension=$ext)
            )
            """;
        (string,object?)[] filters=[("$category",q.Category),("$year",q.Year),("$code",q.Code.ToUpperInvariant()),("$name",q.Name.ToUpperInvariant()),
            ("$ext",q.Extension.Length==0?"":"."+q.Extension.TrimStart('.').ToUpperInvariant())];
        int total;
        using(var cmd=Command(db,tx,selection+" SELECT COUNT(*) FROM selected WHERE rn=1",filters))
        { using var cancel=token.Register(cmd.Cancel);total=Convert.ToInt32(cmd.ExecuteScalar()); }
        var files=new List<FileSearchItem>();
        using(var cmd=Command(db,tx,selection+" SELECT payload,manifest FROM selected WHERE rn=1 ORDER BY location_id LIMIT $limit OFFSET $offset",[..filters,("$limit",q.Limit),("$offset",q.Offset)]))
        {
            using var cancel=token.Register(cmd.Cancel);using var reader=cmd.ExecuteReader();
            while(reader.Read())
            {
                token.ThrowIfCancellationRequested();var e=JsonSerializer.Deserialize<InventoryEntry>(reader.GetString(0))!;
                var m=JsonSerializer.Deserialize<InventoryManifest>(reader.GetString(1))!;
                files.Add(new(m.Unit.Code,m.Unit.Category,m.Unit.Year,m.Unit.Id,e.RelativePath,e.Container,e.Location!,e.Bytes,e.ModifiedUtc,m.CheckedUtc,m.Complete));
            }
        }
        var selected=manifests.Where(m=>(q.Category==""||m.Unit.Category==q.Category)&&(q.Year==""||m.Unit.Year==q.Year)&&(q.Code==""||m.Unit.Code.StartsWith(q.Code,StringComparison.OrdinalIgnoreCase)));
        bool complete=run!=null && run.Issues.Count==0 && selected.All(m=>m.Complete);
        var warnings=run?.Issues.Take(10).ToList()??[];
        if(!complete)warnings.Add("Inventario parziale: non dimostra l'assenza di altri documenti.");
        warnings.Add("Solo metadati memorizzati; nessuna lettura dei documenti durante la ricerca.");
        return new(version,last,run?.StartedUtc??manifests.Min(m=>m.CheckedUtc),total,q.Offset,q.Offset+q.Limit<total?q.Offset+q.Limit:null,complete,files,warnings);
    }
}
