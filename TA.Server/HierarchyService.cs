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
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(record.Path));
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (target != root && !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Percorso fuori dalla cartella");
        // Controlla ogni segmento: anche un collegamento intermedio potrebbe uscire dalla radice.
        var cursor = root;
        Check(cursor);
        foreach (var segment in relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)) { cursor = Path.Combine(cursor, segment); Check(cursor); }
        return Directory.EnumerateDirectories(target).Where(p => !FolderReader.IsExcluded(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0)
            .OrderBy(p => p).Select(p => new FolderEntry(Path.GetFileName(p), p, Path.GetRelativePath(root, p))).ToArray();
    }
    private static void Check(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Collegamento escluso");
    }
}
