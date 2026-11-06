using System.Text.Json;
using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Services;

// Importa solo latest.json e le pagine a cui punta: non attraversa le generazioni obsolete.
public static class InventoryMigration
{
    public static List<string> Units(string folder)
    {
        static IEnumerable<string> Children(string path)=>Directory.Exists(path)?Directory.EnumerateDirectories(path)
            .Where(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)==0):[];
        _=Directory.GetFileSystemEntries(folder); // Accesso negato/rete assente non significa archivio vuoto.
        var root=Path.Combine(folder,"inventario-file");
        return Children(root).Where(p=>Path.GetFileName(p) is "C" or "G" or "A")
            .SelectMany(Children).SelectMany(Children).SelectMany(Children)
            .Where(p=>Regex.IsMatch(Path.GetFileName(p),"^[A-Fa-f0-9]{64}$")).ToList();
    }
    public static void Import(string source,InventoryStore destination,CancellationToken token,Action<string>? progress=null)
    {
        var legacy=new LegacyInventoryStore(source);var imported=new List<InventoryManifest>();
        foreach(var path in Units(source))
        {
            token.ThrowIfCancellationRequested();var latest=Path.Combine(path,"latest.json");if(!File.Exists(latest))continue;
            progress?.Invoke(path);
            var old=InventoryStore.Read<InventoryManifest>(latest);
            if(!Path.GetFullPath(legacy.UnitDirectory(old.Unit)).Equals(Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))
                throw new IOException("Manifest fuori dalla posizione prevista: "+path);
            var marker="migration:"+old.Unit.Id;
            if(destination.Document(marker)==JsonSerializer.Serialize(old.Generation) && destination.Find(old.Unit.Id)!=null)continue;
            var entries=legacy.Entries(old).ToList();
            if(entries.Count!=old.EntryCount)throw new IOException("Numero di voci incoerente: "+path);
            var saved=destination.Save(old.Unit,new(entries,old.Issues,old.Complete),token);
            var actual=destination.Entries(saved).ToDictionary(Key);
            if(actual.Count!=entries.Count || entries.Any(e=>!actual.TryGetValue(Key(e),out var a)||a.Bytes!=e.Bytes||a.ModifiedUtc!=e.ModifiedUtc))
                throw new IOException("Verifica migrazione fallita: "+path);
            destination.SaveDocument(marker,old.Generation);imported.Add(saved);
        }
        if(destination.IntegrityCheck()!="ok")throw new IOException("Verifica integrità SQLite fallita");
        var run=legacy.Latest();var current=destination.Manifests();
        if(current.Count>0)destination.SaveRun(new(current.Min(m=>m.CheckedUtc),current.Max(m=>m.CheckedUtc),current,run?.Candidates??[],run?.Issues??["Inventario precedente importato; verifica delle sorgenti in attesa."]));
        destination.SaveDocument("migration:inventory-complete",DateTime.UtcNow);
    }
    private static string Key(InventoryEntry e)=>JsonSerializer.Serialize(new{e.Container,e.RelativePath,e.Kind});
    // Richiede servizio e vecchio writer arrestati. Il chiamante esegue un backup prima della pulizia.
    public static int CleanMigrated(string source,InventoryStore destination,CancellationToken token,Action<string>? progress=null)
    {
        if(destination.IntegrityCheck()!="ok")throw new IOException("Database non valido: pulizia vietata");
        int deleted=0;var legacy=new LegacyInventoryStore(source);
        foreach(var directory in Units(source))
        {
            token.ThrowIfCancellationRequested();var latest=Path.Combine(directory,"latest.json");
            var hasLatest=File.Exists(latest);
            var old=hasLatest?InventoryStore.Read<InventoryManifest>(latest):destination.Find(Path.GetFileName(directory));
            if(old==null)continue;
            if(!Path.GetFullPath(legacy.UnitDirectory(old.Unit)).Equals(Path.GetFullPath(directory),StringComparison.OrdinalIgnoreCase)
                || (hasLatest && destination.Document("migration:"+old.Unit.Id)!=JsonSerializer.Serialize(old.Generation))
                || destination.Document("migration:"+old.Unit.Id)==null
                || destination.Find(old.Unit.Id)==null)throw new IOException("Unità non migrata o cambiata: "+directory);
            if(hasLatest){File.Delete(latest);deleted++;}
            // I file sono eliminati singolarmente, senza cancellazioni ricorsive e senza seguire link.
            foreach(var generation in Directory.EnumerateDirectories(directory))
            {
                if((File.GetAttributes(generation)&FileAttributes.ReparsePoint)!=0 || !Regex.IsMatch(Path.GetFileName(generation),@"^[0-9T-]+[a-f0-9]{32}$"))continue;
                foreach(var file in Directory.EnumerateFiles(generation))
                {
                    token.ThrowIfCancellationRequested();var name=Path.GetFileName(file);
                    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0 || !(name=="manifest.json"||Regex.IsMatch(name,@"^files-\d{5,}\.json$")))continue;
                    File.Delete(file);deleted++;
                }
                if(!Directory.EnumerateFileSystemEntries(generation).Any())Directory.Delete(generation);
            }
            var guard=Path.Combine(directory,".writer.lock");
            if(File.Exists(guard)){File.Delete(guard);deleted++;}
            progress?.Invoke(directory);
        }
        return deleted;
    }
}
