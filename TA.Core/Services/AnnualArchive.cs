using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Models;
namespace TA_Gestione_Cartelle_Commesse.Services;

// L'indice viene sostituito solo quando tutti i file annuali sono pronti.
internal static class AnnualArchive
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private sealed record Index
    {
        public int SchemaVersion { get; init; } = 2;
        public string Root { get; init; } = "";
        public char Prefix { get; init; }
        public DateTime StartedUtc { get; init; }
        public DateTime SavedUtc { get; init; }
        public List<string> Warnings { get; init; } = [];
        public Dictionary<string, string> Years { get; init; } = [];
    }
    private sealed record YearData(string Root, char Prefix, string Year, List<FolderRecord> Records);
    private static string YearFolder(string target) => Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target) + ".years");
    public static ArchiveSnapshot? Load(string target, string root, char prefix)
    {
        // Lettori contemporanei ammessi; una scrittura esclusiva non elimina file ancora in lettura.
        using var lease = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        return ReadUnlocked(target, root, prefix);
    }
    private static ArchiveSnapshot? ReadUnlocked(string target, string root, char prefix)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(target); }
        catch (FileNotFoundException) { return null; }
        using var json = JsonDocument.Parse(bytes);
        if (!json.RootElement.TryGetProperty("SchemaVersion", out var schema)) throw new InvalidDataException("Schema archivio assente.");
        ArchiveSnapshot data;
        if (schema.GetInt32() == 1) data = JsonSerializer.Deserialize<ArchiveSnapshot>(bytes)!;
        else if (schema.GetInt32() == 2)
        {
            var index = JsonSerializer.Deserialize<Index>(bytes) ?? throw new InvalidDataException("Indice vuoto.");
            if (index.Root != SharedArchiveService.Normalize(root) || index.Prefix != prefix || index.Years == null || index.Warnings == null)
                throw new InvalidDataException("Indice annuale non compatibile.");
            var records = new List<FolderRecord>();
            foreach (var (year, file) in index.Years)
            {
                if (!Regex.IsMatch(year, @"^(19|20)[0-9]{2}$|^senza-anno$")
                    || file == null || !Regex.IsMatch(file, @"^(?:[0-9]{4}|senza-anno)-[A-F0-9]{64}\.json$")
                    || !file.StartsWith(year + "-", StringComparison.Ordinal))
                    throw new InvalidDataException("Riferimento annuale non valido.");
                byte[] content;
                try { content = File.ReadAllBytes(Path.Combine(YearFolder(target), file)); }
                catch (FileNotFoundException ex) { throw new InvalidDataException("Archivio annuale mancante: " + year, ex); }
                if (file != year + "-" + Convert.ToHexString(SHA256.HashData(content)) + ".json")
                    throw new InvalidDataException("Archivio annuale alterato o incompleto: " + year);
                var part = JsonSerializer.Deserialize<YearData>(content);
                if (part == null || part.Root != index.Root || part.Prefix != prefix || part.Year != year || part.Records == null)
                    throw new InvalidDataException("Dati annuali non compatibili: " + year);
                Validate(new ArchiveSnapshot { Root = part.Root, Prefix = part.Prefix, Records = part.Records }, root, prefix);
                records.AddRange(part.Records);
            }
            data = new ArchiveSnapshot { Root = index.Root, Prefix = prefix, StartedUtc = index.StartedUtc, SavedUtc = index.SavedUtc,
                Warnings = index.Warnings, Records = records.OrderByDescending(r => r.Year).ThenByDescending(r => r.Number).ToList() };
        }
        else throw new InvalidDataException("Schema archivio non supportato.");
        Validate(data, root, prefix);
        return data;
    }
    private static void Validate(ArchiveSnapshot? data, string root, char prefix)
    {
        if (data == null || data.SchemaVersion != 1 || data.Root != SharedArchiveService.Normalize(root) || data.Prefix != prefix || data.Records == null || data.Warnings == null)
            throw new InvalidDataException("Formato o radice dell'archivio non compatibili.");
        if (data.Records.Any(r => r == null || string.IsNullOrWhiteSpace(r.Path) || r.Code == null || r.Client == null || r.Subject == null
            || (r.Inspection is { } i && (i.QuoteFiles == null || i.Issues == null || i.TotalBytes < 0 || i.FileCount < 0))))
            throw new InvalidDataException("Record incompleti nell'archivio JSON.");
    }
    private static string Year(ArchiveSnapshot snapshot, FolderRecord record)
    {
        if (snapshot.Prefix == 'C')
        {
            var first = Path.GetRelativePath(snapshot.Root, record.Path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (Regex.IsMatch(first, @"^(19|20)[0-9]{2}$")) return first;
        }
        return int.TryParse(record.Year, out var year) && year is >= 0 and <= 99 ? (2000 + year).ToString() : "senza-anno";
    }
    public static bool Save(string target, ArchiveSnapshot data, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var lease = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = ReadUnlocked(target, data.Root, data.Prefix);
        if (previous != null && (previous.StartedUtc > data.StartedUtc || (previous.StartedUtc == data.StartedUtc && previous.SavedUtc > data.SavedUtc))) return false;
        Validate(data, data.Root, data.Prefix);
        if (File.Exists(target))
        {
            using var old = JsonDocument.Parse(File.ReadAllBytes(target));
            if (old.RootElement.GetProperty("SchemaVersion").GetInt32() == 1 && !File.Exists(target + ".legacy-v1.bak"))
                File.Copy(target, target + ".legacy-v1.bak");
        }
        var directory = YearFolder(target); Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("La directory degli archivi annuali non puo essere un collegamento.");
        var index = new Index { Root = data.Root, Prefix = data.Prefix, StartedUtc = data.StartedUtc, SavedUtc = data.SavedUtc, Warnings = data.Warnings };
        foreach (var group in data.Records.GroupBy(r => Year(data, r)))
        {
            token.ThrowIfCancellationRequested();
            var content = JsonSerializer.SerializeToUtf8Bytes(new YearData(data.Root, data.Prefix, group.Key, group.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList()), Options);
            var filename = group.Key + "-" + Convert.ToHexString(SHA256.HashData(content)) + ".json";
            var destination = Path.Combine(directory, filename);
            if (!File.Exists(destination)) AtomicWrite(destination, content, token);
            index.Years.Add(group.Key, filename);
        }
        AtomicWrite(target, JsonSerializer.SerializeToUtf8Bytes(index, Options), token);
        // Solo file generati, nella directory dedicata e sotto lock; nessuna cancellazione ricorsiva.
        var keep = index.Years.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                var name = Path.GetFileName(file);
                if (!keep.Contains(name) && Regex.IsMatch(name, @"^(?:[0-9]{4}|senza-anno)-[A-F0-9]{64}\.json$"))
                    try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        return true;
    }
    private static void AtomicWrite(string target, byte[] content, CancellationToken token)
    {
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(content); file.Flush(true); }
            token.ThrowIfCancellationRequested();
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
