using System.Diagnostics;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Services;

// Consente di consultare le unità JSON già completate anche durante il primo censimento generale.
public static class InventoryAvailability
{
    public static InventoryRun? Load(InventoryStore store, string category = "", string year = "", string code = "", CancellationToken token = default)
    {
        if (store.Latest() is { } completed) return completed;
        var manifests = new List<InventoryManifest>(); var watch = Stopwatch.StartNew();
        foreach (var c in Children(store.Root).Where(p => Path.GetFileName(p) is "C" or "G" or "A").Where(p => category.Length == 0 || Path.GetFileName(p) == category))
        foreach (var y in Children(c).Where(p => year.Length == 0 || Path.GetFileName(p) == year))
        foreach (var n in Children(y).Where(p => code.Length == 0 || Path.GetFileName(p).StartsWith(code, StringComparison.OrdinalIgnoreCase)))
        foreach (var u in Children(n))
        {
            token.ThrowIfCancellationRequested();
            if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new IOException("Inventario parziale: restringere categoria, anno e codice per consultare le unità già censite.");
            var file = Path.Combine(u, "latest.json"); if (!File.Exists(file)) continue;
            var manifest = InventoryStore.Read<InventoryManifest>(file);
            if (!Path.GetFullPath(store.UnitDirectory(manifest.Unit)).Equals(Path.GetFullPath(u), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Manifest inventario in posizione inattesa.");
            manifests.Add(manifest);
        }
        return manifests.Count == 0 ? null : new(manifests.Min(m => m.CheckedUtc), manifests.Max(m => m.CheckedUtc), manifests, [],
            ["Prima scansione generale non completata: consultazione parziale delle unità JSON già censite. Nessuna assenza di file può essere conclusa da questo elenco."]);
    }
    private static IEnumerable<string> Children(string directory) => Directory.Exists(directory) ? Directory.EnumerateDirectories(directory).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0) : [];
}
