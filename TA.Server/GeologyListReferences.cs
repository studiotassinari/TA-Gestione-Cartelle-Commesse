using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

public sealed record GeologyListReference(string ListId,string ListFingerprint,GeologicalReport Report,string Generation);
// Usa solo la porzione già letta su richiesta. Nessun altro documento viene aperto.
public static class GeologyListReferences
{
    private static string PathFor(ServerSettings settings)=>Path.Combine(GeologyStorage.Root(settings),"riferimenti-elenchi.json");
    public static List<GeologyListReference> Read(ServerSettings settings)=>CatalogStorage.Exists(settings,PathFor(settings))?CatalogStorage.Read<List<GeologyListReference>>(settings,PathFor(settings)):[];
    public static string? Match(string name,string text)
    {
        var stem=Path.GetFileNameWithoutExtension(name);
        if(stem.Length<3)return null;
        var pattern=@"(?<![\p{L}\p{N}])"+Regex.Escape(stem)+@"(?![\p{L}\p{N}])";
        return text.Split('\n').Select(l=>l.Trim()).Where(l=>l.Length<=600&&GeologyClassifier.MentionsGeology(l))
            .FirstOrDefault(l=>Regex.IsMatch(l,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant));
    }
    public static void Discover(ServerSettings settings,GeologicalReport list,DocumentPage page,CancellationToken token)
    {
        if(!GeologyClassifier.IsDrawingList(list.FileName))return;
        var store=new InventoryStore(settings.InventoryFolder);var manifest=store.Find(list.InventoryUnitId);
        if(manifest==null)return;
        var found=new List<GeologyListReference>();
        var entries=store.Entries(manifest).ToList();
        var containers=entries.Where(e=>e.Kind=="archive"&&e.Container==null).ToDictionary(e=>e.RelativePath,StringComparer.Ordinal);
        foreach(var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if(entry.Kind=="directory")continue;
            var location=entry.Location??FileLocator.Locate(manifest.Unit,entry);
            if(location.Id==list.Id||Match(location.Name,page.Text) is not {} quote)continue;
            var container=entry.Container!=null?containers.GetValueOrDefault(entry.Container):null;
            var stamp=container?.Bytes is {} bytes&&container.ModifiedUtc is {} modified?bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+modified.ToString("O"):"";
            var reason="Riferimento da "+list.FileName+": "+quote;
            if(GeologyClassifier.Classify(manifest,entry,stamp,container?.ModifiedUtc,reason) is {} candidate)
                found.Add(new(list.Id,list.SourceFingerprint,candidate,manifest.Generation));
        }
        if(found.Count==0)return;
        using var gate=CatalogStorage.Lock(settings,PathFor(settings));
        var previous=Read(settings);previous.RemoveAll(r=>r.ListId==list.Id&&r.ListFingerprint!=list.SourceFingerprint);
        var merged=previous.Concat(found).DistinctBy(r=>(r.ListId,r.Report.Id)).ToList();
        if(merged.Count>50000)throw new IOException("Troppi riferimenti geologici da elenchi elaborati.");
        CatalogStorage.Write(settings,PathFor(settings),merged);
    }
    public static GeologyCatalog Merge(ServerSettings settings,GeologyCatalog catalog)
    {
        var sources=catalog.Reports.ToDictionary(r=>r.Id);
        var store=new InventoryStore(settings.InventoryFolder);
        var generations=new Dictionary<string,string?>();
        var references=Read(settings).Where(r=>{
            if(!sources.TryGetValue(r.ListId,out var source)||source.SourceFingerprint!=r.ListFingerprint)return false;
            if(!generations.TryGetValue(r.Report.InventoryUnitId,out var generation))generations[r.Report.InventoryUnitId]=generation=store.Find(r.Report.InventoryUnitId)?.Generation;
            return generation==r.Generation;
        }).Select(r=>r.Report);
        return catalog with{Reports=catalog.Reports.Concat(references).DistinctBy(r=>r.Id).ToList()};
    }
}
