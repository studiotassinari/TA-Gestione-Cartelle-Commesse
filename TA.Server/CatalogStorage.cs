using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

// I percorsi storici sono chiavi logiche; le classi sono serializzate nel database locale.
public static class CatalogStorage
{
    private static readonly ConcurrentDictionary<string,object> Gates=new(StringComparer.OrdinalIgnoreCase);
    private static string Key(ServerSettings s,string path)
    {
        var root=Path.GetFullPath(s.ArchiveFolder).TrimEnd('\\','/');var full=Path.GetFullPath(path);
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Il catalogo esterno deve essere importato nell'archivio del servizio prima della migrazione SQL.");
        return Path.GetRelativePath(root,full).Replace('\\','/').ToUpperInvariant();
    }
    private static InventoryStore Store(ServerSettings s)=>new(s.InventoryFolder);
    private static bool External(ServerSettings s,string path)=>!string.IsNullOrWhiteSpace(s.GeologyCatalogPath) && Path.GetFullPath(path).Equals(Path.GetFullPath(s.GeologyCatalogPath),StringComparison.OrdinalIgnoreCase);
    public static bool Exists(ServerSettings s,string path)=>External(s,path)?File.Exists(path):Store(s).Document(Key(s,path))!=null || File.Exists(path);
    public static byte[] Bytes(ServerSettings s,string path)
    {
        if(External(s,path))return File.ReadAllBytes(path);
        var store=Store(s);var key=Key(s,path);var json=store.Document(key);
        if(json==null)
        {
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            if(stream.Length>64*1024*1024)throw new IOException("Catalogo oltre il limite di migrazione.");
            using var reader=new StreamReader(stream);json=reader.ReadToEnd();store.SaveDocumentJson(key,json);
        }
        return Encoding.UTF8.GetBytes(json);
    }
    public static T Read<T>(ServerSettings s,string path)=>JsonSerializer.Deserialize<T>(Bytes(s,path))??throw new IOException("Catalogo vuoto");
    public static void Write<T>(ServerSettings s,string path,T value)=>Store(s).SaveDocument(Key(s,path),value);
    public static IDisposable Lock(ServerSettings s,string path)
    {var gate=Gates.GetOrAdd(Path.GetFullPath(s.InventoryFolder)+":"+Key(s,path),_=>new());Monitor.Enter(gate);return new Lease(gate);}
    private sealed class Lease(object gate):IDisposable {public void Dispose()=>Monitor.Exit(gate);}
}
