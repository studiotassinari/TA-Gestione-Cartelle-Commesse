using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace TA.Server.Archiving;

public sealed record ZipItem(string Name, long Bytes, string Hash, long ModifiedUtcTicks = 0);
public sealed record VerifiedZip(string Source, string Zip, string ZipHash, ZipItem[] Items);

// Eccezione esplicita alla sola lettura: preparazione e rimozione sono due chiamate
// distinte. Mai Delete(recursive): un file aggiunto nel frattempo non viene cancellato.
public static class VerifiedFolderZip
{
    private sealed class Tree : IDisposable
    {
        public readonly List<(string Path, SafeFileHandle Handle)> Directories = [];
        public readonly List<(string Path, FileStream Stream)> Files = [];
        public readonly List<SafeFileHandle> Ancestors = [];
        public void Dispose()
        {
            foreach (var f in Files) f.Stream.Dispose();
            foreach (var d in Directories.AsEnumerable().Reverse()) d.Handle.Dispose();
            foreach (var h in Ancestors.AsEnumerable().Reverse()) h.Dispose();
        }
    }

    public static void ValidatePath(string root, string source)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(source));
        if (Path.IsPathRooted(relative) || relative == "." || relative.Split('\\', '/').Contains(".."))
            throw new UnauthorizedAccessException("Cartella esterna alla radice lavori/gare oppure radice stessa.");
    }

    private static Tree LockTree(string root, string source, bool delete, CancellationToken token)
    {
        ValidatePath(root, source);
        var tree = new Tree();
        try
        {
            // Protegge anche i componenti superiori: nessuna rinomina del padre può
            // spostare il bersaglio fuori dalla radice autorizzata durante il lavoro.
            var parents = new Stack<string>();
            for (var p = Directory.GetParent(source); p != null; p = p.Parent) parents.Push(p.FullName);
            foreach (var p in parents) tree.Ancestors.Add(LockedFile.Open(p, true, false));
            void Visit(string directory, int depth)
            {
                token.ThrowIfCancellationRequested();
                if (depth > 128 || tree.Directories.Count + tree.Files.Count >= 20000)
                    throw new IOException("Limite di sicurezza: 20.000 elementi o 128 livelli. Nessun originale eliminato.");
                tree.Directories.Add((directory, LockedFile.Open(directory, true, delete)));
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    if (tree.Directories.Count + tree.Files.Count >= 20000) throw new IOException("Limite di 20.000 elementi superato.");
                    if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) Visit(path, depth + 1);
                    else
                    {
                        var handle = LockedFile.Open(path, false, delete);
                        try { tree.Files.Add((path, new FileStream(handle, FileAccess.Read))); }
                        catch { handle.Dispose(); throw; }
                    }
                }
            }
            Visit(source, 0);
            return tree;
        }
        catch { tree.Dispose(); throw; }
    }

    private static string EntryName(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string Hash(Stream stream, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024]; int read;
        while ((read = stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static ZipItem[] Describe(Tree tree, string source, CancellationToken token)
    {
        var result = new List<ZipItem>();
        foreach (var d in tree.Directories.Skip(1)) result.Add(new(EntryName(source, d.Path) + "/", 0, ""));
        foreach (var f in tree.Files)
        {
            f.Stream.Position = 0;
            result.Add(new(EntryName(source, f.Path), f.Stream.Length, Hash(f.Stream, token), File.GetLastWriteTimeUtc(f.Path).Ticks));
        }
        return result.OrderBy(i => i.Name, StringComparer.Ordinal).ToArray();
    }
    private static void VerifyZip(Stream stream, ZipItem[] expected, CancellationToken token)
    {
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, true);
        if (zip.Entries.Count != expected.Length) throw new IOException("Numero di elementi dello ZIP non corrispondente.");
        var entries = zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < expected.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var e = entries[i]; var item = expected[i];
            if (e.FullName != item.Name || e.Length != item.Bytes) throw new IOException("Elemento ZIP non corrispondente: " + item.Name);
            if (item.Hash.Length == 0) continue;
            using var data = e.Open();
            if (Hash(data, token) != item.Hash) throw new IOException("Contenuto ZIP non corrispondente: " + item.Name);
        }
    }
    private static void CheckTreeNames(Tree tree, string source)
    {
        var expected = tree.Files.Select(f => f.Path).Concat(tree.Directories.Skip(1).Select(d => d.Path)).ToHashSet(StringComparer.Ordinal);
        foreach (var directory in tree.Directories)
            foreach (var p in Directory.EnumerateFileSystemEntries(directory.Path))
                if (!expected.Remove(p)) throw new IOException("Cartella cambiata durante la verifica: " + p);
        if (expected.Count != 0) throw new IOException("Un elemento non è più presente nella cartella.");
    }

    public static VerifiedZip Prepare(string root, string source, CancellationToken token)
    {
        source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        var destination = source + ".zip";
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("ZIP già esistente: nessuna sovrascrittura.");
        using var tree = LockTree(root, source, false, token);
        var items = Describe(tree, source, token);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            string zipHash;
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    foreach (var directory in tree.Directories.Skip(1)) zip.CreateEntry(EntryName(source, directory.Path) + "/");
                    var buffer = new byte[128 * 1024];
                    foreach (var file in tree.Files)
                    {
                        token.ThrowIfCancellationRequested();
                        var entry = zip.CreateEntry(EntryName(source, file.Path), CompressionLevel.Optimal);
                        var date = File.GetLastWriteTimeUtc(file.Path);
                        if (date.Year is >= 1980 and <= 2107) entry.LastWriteTime = new DateTimeOffset(date);
                        using var output = entry.Open(); file.Stream.Position = 0; int count;
                        while ((count = file.Stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
                    }
                }
                stream.Flush(true);
                VerifyZip(stream, items, token);
                CheckTreeNames(tree, source);
                stream.Position = 0; zipHash = Hash(stream, token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, false);
            return new(source, destination, zipHash, items);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void DeleteOriginal(string root, VerifiedZip receipt, CancellationToken token, Action? beforeDeletion = null)
    {
        ValidatePath(root, receipt.Source);
        if (receipt.Zip != receipt.Source + ".zip") throw new IOException("Destinazione ZIP incoerente.");
        // Blocca lo ZIP fino alla fine della rimozione: non può sparire dopo la verifica.
        using var zipHandle = LockedFile.Open(receipt.Zip, false, false);
        using var zip = new FileStream(zipHandle, FileAccess.Read);
        if (Hash(zip, token) != receipt.ZipHash) throw new IOException("ZIP modificato: originali conservati.");
        using var tree = LockTree(root, receipt.Source, true, token);
        if (!Describe(tree, receipt.Source, token).SequenceEqual(receipt.Items)) throw new IOException("Cartella modificata dopo lo ZIP: originali conservati.");
        VerifyZip(zip, receipt.Items, token);
        CheckTreeNames(tree, receipt.Source);
        token.ThrowIfCancellationRequested();
        beforeDeletion?.Invoke();
        // Da qui un errore può lasciare una rimozione parziale, ma ogni elemento
        // rimosso è già stato riletto e verificato nello ZIP bloccato in lettura.
        foreach (var f in tree.Files) { LockedFile.Delete(f.Stream.SafeFileHandle); f.Stream.Dispose(); }
        foreach (var d in tree.Directories.AsEnumerable().Reverse()) { LockedFile.Delete(d.Handle); d.Handle.Dispose(); }
    }
}
