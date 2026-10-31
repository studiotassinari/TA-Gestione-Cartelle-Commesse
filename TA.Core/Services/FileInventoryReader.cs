using SharpCompress.Archives;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA_Gestione_Cartelle_Commesse.Services;

// Non estrae archivi e non apre stream dei documenti: solo directory e intestazioni ZIP/7z.
public static class FileInventoryReader
{
    public static InventoryResult Read(string root, ISet<string> excludedRoots, CancellationToken token)
    {
        var entries = new List<InventoryEntry>(); var issues = new List<string>();
        const int maximum = 1_000_000;
        void Add(InventoryEntry entry) { if (entries.Count >= maximum) throw new InvalidDataException("Limite di un milione di voci: censimento parziale."); entries.Add(entry); }
        string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
        void Archive(string path, string container)
        {
            try
            {
                var before = new FileInfo(path); long size = before.Length; var stamp = before.LastWriteTimeUtc;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var archive = ArchiveFactory.OpenArchive(stream);
                var dirs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    var key = (item.Key ?? "").Replace('\\', '/').TrimEnd('/');
                    if (key.Length == 0 || key.StartsWith('/') || key.Split('/').Any(p => p is "." or ".." || p.Contains(':')))
                    { issues.Add(container + ": percorso interno non valido"); continue; }
                    var segments = key.Split('/');
                    for (int i = 1; i < segments.Length; i++)
                    {
                        var dir = string.Join('/', segments.Take(i));
                        if (dirs.Add(dir)) Add(new(dir, "directory", container));
                    }
                    if (item.IsDirectory) { if (dirs.Add(key)) Add(new(key, "directory", container)); continue; }
                    Add(new(key, IsArchive(key) ? "nested-archive" : "file", container, item.Size, item.LastModifiedTime?.ToUniversalTime()));
                    if (item.IsEncrypted) issues.Add(container + "!" + key + ": voce cifrata, solo nome/metadati visibili");
                    if (IsArchive(key)) issues.Add(container + "!" + key + ": archivio annidato censito ma non aperto (richiederebbe decompressione)");
                }
                var after = new FileInfo(path);
                if (after.Length != size || after.LastWriteTimeUtc != stamp) issues.Add(container + ": modificato durante la lettura");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { issues.Add(container + ": archivio parziale/non leggibile: " + ex.Message); }
        }
        void FileEntry(string path, string relative)
        {
            var file = new FileInfo(path); DateTime? modified = null;
            try { modified = file.LastWriteTimeUtc; } catch (ArgumentOutOfRangeException) { issues.Add(relative + ": data non valida"); }
            Add(new(relative, IsArchive(path) ? "archive" : "file", null, file.Length, modified));
            if (IsArchive(path)) Archive(path, relative);
        }
        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Radice collegamento esclusa.");
            if (File.Exists(root)) FileEntry(root, Path.GetFileName(root));
            else
            {
                var pending = new Stack<string>(); pending.Push(root);
                while (pending.TryPop(out var dir))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        foreach (var path in Directory.EnumerateFileSystemEntries(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                        {
                            token.ThrowIfCancellationRequested();
                            if (FolderReader.IsExcluded(path) || excludedRoots.Contains(Path.GetFullPath(path))) continue;
                            try
                            {
                                var attributes = File.GetAttributes(path);
                                if ((attributes & FileAttributes.ReparsePoint) != 0) { issues.Add(Relative(path) + ": collegamento escluso"); continue; }
                                if ((attributes & FileAttributes.Directory) != 0) { Add(new(Relative(path), "directory")); pending.Push(path); }
                                else FileEntry(path, Relative(path));
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
                            { issues.Add(Relative(path) + ": " + ex.Message); }
                            if (entries.Count >= maximum) throw new InvalidDataException("Limite di voci raggiunto: censimento parziale.");
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { issues.Add(dir + ": " + ex.Message); }
                    if (entries.Count >= maximum) break;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { issues.Add(root + ": " + ex.Message); }
        return new(entries.OrderBy(e => e.Container, StringComparer.Ordinal).ThenBy(e => e.RelativePath, StringComparer.Ordinal).ThenBy(e => e.Kind).ToList(), issues.Distinct().ToList(), issues.Count == 0);
    }
    public static bool IsArchive(string path) => Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".7z", StringComparison.OrdinalIgnoreCase);
}
