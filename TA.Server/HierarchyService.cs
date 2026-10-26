using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

// Consente di navigare solo dentro una cartella già identificata e autorizzata dal server.
public static class HierarchyService
{
    public static FolderEntry[] Children(CategoryState state, string id, string? relative)
    {
        var record = state.Snapshot?.Records.SingleOrDefault(r => r.Id == id) ?? throw new KeyNotFoundException();
        relative ??= "";
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split(['/', '\\']).Any(p => p is ".." or "."))
            throw new ArgumentException("Percorso relativo non valido");
        var metadata = record.Inspection;
        if (metadata?.HierarchyCaptured != true) throw new IOException("Gerarchia non ancora disponibile: attendere il report giornaliero delle 23:00.");
        var parent = relative.Replace(Path.DirectorySeparatorChar, '/').Trim('/');
        return metadata.ArchiveDirectories.Where(p => (p.Contains('/') ? p[..p.LastIndexOf('/')] : "") == parent)
            .OrderBy(p => p).Select(p => new FolderEntry(p.Split('/').Last(), record.Path + "\\" + p.Replace('/', Path.DirectorySeparatorChar), p)).ToArray();
    }
    private static void Check(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Collegamento escluso");
    }
}
