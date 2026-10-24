using System.IO;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Archives;
using TA_Gestione_Cartelle_Commesse.Models;
namespace TA_Gestione_Cartelle_Commesse.Services;

// Censimento in sola lettura: non estrae, rinomina, ripara o scrive negli archivi.
public static class ArchivedJobsReader
{
    public static ScanResult Scan(string root, CancellationToken token)
    {
        var records = new List<FolderRecord>(); var warnings = new List<string>();
        var pending = new Stack<string>(); pending.Push(root); bool incomplete = false;
        var pattern = new Regex(@"^(?<code>C(?<year>\d{2})(?<number>\d{3}))\s*-\s*(?<client>.+?)\s+-\s+(?<subject>.+)$", RegexOptions.IgnoreCase);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0 || FolderReader.IsExcluded(path)) continue;
                        if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                        if (!new[] { ".zip", ".7z" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
                        var name = Path.GetFileNameWithoutExtension(path); var match = pattern.Match(name);
                        var code = Regex.Match(name, @"^C(?<year>\d{2})(?<number>\d{3})", RegexOptions.IgnoreCase);
                        var file = new FileInfo(path); DateTime? modified = null;
                        try { modified = file.LastWriteTimeUtc; } catch (ArgumentOutOfRangeException) { warnings.Add("Data archivio non valida: " + path); }
                        var row = match.Success
                            ? new FolderRecord(match.Groups["code"].Value.ToUpperInvariant(), match.Groups["year"].Value, match.Groups["number"].Value, match.Groups["client"].Value.Trim(), match.Groups["subject"].Value.Trim(), path, "Valido")
                            : new FolderRecord(code.Success ? code.Value.ToUpperInvariant() : "", code.Groups["year"].Value, code.Groups["number"].Value, "Da verificare", name, path, "Non conforme");
                        records.Add(row with { ArchiveBytes = file.Length, ArchiveModifiedUtc = modified });
                        if (!match.Success) warnings.Add("Nome archivio non conforme: " + path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
                    { incomplete = true; warnings.Add(path + ": " + ex.Message); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { if (directory == root) return new([], [], Error: ex.Message); incomplete = true; warnings.Add(directory + ": " + ex.Message); }
        }
        return new(records.OrderByDescending(r => r.Year).ThenByDescending(r => r.Number).ToList(), warnings, Incomplete: incomplete);
    }
    public static FolderInspection Inspect(string path, CancellationToken token) => Inspect(path, token, false);
    public static FolderInspection Inspect(string path, CancellationToken token, bool includeHierarchy)
    {
        long bytes = 0, count = 0; DateTime? latest = null;
        var issues = new List<string>(); var quotes = new List<string>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var signature = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        bool administration = false, strongQuote = false;
        try
        {
            var before = new FileInfo(path); var length = before.Length; var modified = before.LastWriteTimeUtc;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var archive = ArchiveFactory.OpenArchive(file);
            int entries = 0;
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (++entries > 1_000_000) throw new InvalidDataException("Limite di un milione di voci superato.");
                var key = (entry.Key ?? "").Replace('\\', '/').TrimEnd('/');
                var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (key.StartsWith('/') || segments.Any(s => s is "." or ".." || s.Contains(':')))
                { issues.Add("Percorso interno non valido: " + key); continue; }
                var dirCount = entry.IsDirectory ? segments.Length : segments.Length - 1;
                for (int i = 1; i <= dirCount; i++)
                    if (includeHierarchy && directories.Count < 50000) directories.Add(string.Join('/', segments.Take(i)));
                bool admin = segments.Take(Math.Max(0, dirCount)).Any(s => new[] { "amministraione", "amministrazione", "99_AMMINISTRAZIONE" }.Contains(s, StringComparer.OrdinalIgnoreCase));
                administration |= admin;
                signature.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { key, entry.Size, entry.LastModifiedTime }));
                if (entry.IsDirectory) continue;
                count++; bytes = checked(bytes + entry.Size);
                if (entry.LastModifiedTime is { } date)
                { var utc = date.ToUniversalTime(); if (latest == null || utc > latest) latest = utc; }
                if (entry.IsEncrypted && !issues.Contains("Contenuti cifrati: verificati soltanto i metadati leggibili."))
                    issues.Add("Contenuti cifrati: verificati soltanto i metadati leggibili.");
                if (admin && Regex.IsMatch(Path.GetFileName(key), @"(?i)(preventiv|offerta|proposta[ _-]?economic)")
                    && new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".odt", ".ods", ".msg", ".eml", ".p7m", ".txt", ".rtf" }.Contains(Path.GetExtension(key), StringComparer.OrdinalIgnoreCase))
                { quotes.Add(key); strongQuote |= !Regex.IsMatch(key, @"(?i)(richiest|modell|fac[ _-]?simile|bozza)"); }
            }
            var after = new FileInfo(path);
            if (after.Length != length || after.LastWriteTimeUtc != modified) issues.Add("Archivio modificato durante la lettura: verifica da ripetere.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { issues.Add("Archivio non completamente leggibile (danneggiato, protetto o formato non supportato): " + ex.Message); }
        return new FolderInspection { TotalBytes = bytes, FileCount = count, LastFileWriteUtc = latest, CheckedUtc = DateTime.UtcNow,
            Complete = issues.Count == 0, Issues = issues, QuoteFiles = quotes, ArchiveDirectories = directories.OrderBy(x => x).ToList(),
            ContentSignature = Convert.ToHexString(signature.GetHashAndReset()),
            QuoteStatus = issues.Count > 0 ? "Da verificare (lettura archivio parziale)" : strongQuote ? "Presente (da nome file)" :
                quotes.Count > 0 ? "Da verificare (nome ambiguo)" : administration ? "Non trovato nei nomi" : "Cartella amministrazione non trovata" };
    }
}
