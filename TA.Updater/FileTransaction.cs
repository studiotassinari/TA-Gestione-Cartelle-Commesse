using System.IO;
using System.Text.Json;
namespace TA.Updater;
public sealed record FileChange(string Path, bool Existed);
public sealed record UpdateJournal(string Root, string Backup, bool ServiceWasRunning, List<FileChange> Changes);
public static class FileTransaction
{
    public static string JournalPath(string cache) => Path.Combine(cache, "pending-update.json");
    private static void WriteAtomic(string file, byte[] bytes)
    {
        var temp = file + ".ta-new";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
        File.Move(temp, file, true);
    }
    public static UpdateJournal? Pending(string cache) => File.Exists(JournalPath(cache)) ? JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllBytes(JournalPath(cache))) : null;
    public static void Recover(string root, string cache, Action beforeRestore)
    {
        var pending = Pending(cache); if (pending == null) return;
        if (!Path.GetFullPath(pending.Root).Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFullPath(pending.Backup).StartsWith(Path.GetFullPath(cache).TrimEnd('\\') + "\\backup-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Percorso di ripristino non valido.");
        beforeRestore();
        foreach (var item in pending.Changes.AsEnumerable().Reverse())
        {
            var target = UpdatePackage.SafePath(root, item.Path);
            if (item.Existed)
            {
                var backup = UpdatePackage.SafePath(pending.Backup, item.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temp = target + ".ta-restore"; File.Copy(backup, temp, true); File.Move(temp, target, true);
            }
            else if (File.Exists(target)) File.Delete(target);
        }
        File.Delete(JournalPath(cache));
    }
    public static int Apply(string root, string stage, PackageManifest manifest, string cache, bool serviceWasRunning,
        Action verify, Action beforeRollback, Action<int>? afterWrite = null)
    {
        if (Pending(cache) != null) throw new InvalidOperationException("Ripristinare prima l'aggiornamento interrotto.");
        var changed = manifest.Files.Where(f => !File.Exists(UpdatePackage.SafePath(root, f.Path))
            || !UpdatePackage.Hash(UpdatePackage.SafePath(root, f.Path)).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)).ToList();
        var backup = Path.Combine(cache, "backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var changes = new List<FileChange>();
        foreach (var file in changed)
        {
            var target = UpdatePackage.SafePath(root, file.Path); bool exists = File.Exists(target);
            changes.Add(new(file.Path, exists));
            if (exists) { var copy = UpdatePackage.SafePath(backup, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(copy)!); File.Copy(target, copy); }
        }
        WriteAtomic(JournalPath(cache), JsonSerializer.SerializeToUtf8Bytes(new UpdateJournal(root, backup, serviceWasRunning, changes)));
        try
        {
            int count = 0;
            foreach (var file in changed)
            {
                var target = UpdatePackage.SafePath(root, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temp = target + ".ta-new";
                File.Copy(UpdatePackage.SafePath(stage, file.Path), temp, true); File.Move(temp, target, true);
                afterWrite?.Invoke(++count);
            }
            foreach (var file in manifest.Files)
                if (!UpdatePackage.Hash(UpdatePackage.SafePath(root, file.Path)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Verifica dei file aggiornati fallita.");
            verify();
            File.Delete(JournalPath(cache));
            return changed.Count;
        }
        catch { Recover(root, cache, beforeRollback); throw; }
    }
}
