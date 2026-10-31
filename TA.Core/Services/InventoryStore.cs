using System.Text.Json;
using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA_Gestione_Cartelle_Commesse.Services;

// Ogni generazione è immutabile; il puntatore viene sostituito solo dopo tutti i JSON.
public sealed class InventoryStore(string archiveFolder)
{
    public string Root { get; } = Path.Combine(archiveFolder, "inventario-file");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string UnitDirectory(InventoryUnit unit)
    {
        if (unit.Category is not ("C" or "G" or "A") || !Regex.IsMatch(unit.Id, "^[A-Fa-f0-9]{64}$"))
            throw new InvalidDataException("Identità inventario non valida.");
        return Path.Combine(Root, unit.Category, Safe(unit.Year, "_anno-non-noto"), Safe(unit.Code, "_da-classificare"), unit.Id);
    }
    private static string Safe(string value, string fallback) => Regex.IsMatch(value, @"^[A-Za-z0-9_-]{1,40}$") ? value : fallback;
    public InventoryManifest Save(InventoryUnit unit, InventoryResult result, CancellationToken token)
    {
        var folder = UnitDirectory(unit); Directory.CreateDirectory(folder);
        using var guard = new FileStream(Path.Combine(folder, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var latest = Path.Combine(folder, "latest.json");
        var previous = File.Exists(latest) ? Read<InventoryManifest>(latest) : null;
        var prior = previous == null ? [] : Entries(previous).ToList();
        static string Key(InventoryEntry e) => JsonSerializer.Serialize(new { e.Container, e.RelativePath, e.Kind });
        var before = prior.GroupBy(Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var after = result.Entries.GroupBy(Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        // Una lettura parziale non dimostra un'eliminazione: conserva i nomi precedenti nel nuovo censimento.
        var entries = result.Complete ? result.Entries : after.Values.Concat(before.Where(p => !after.ContainsKey(p.Key)).Select(p => p.Value))
            .OrderBy(e => e.Container, StringComparer.Ordinal).ThenBy(e => e.RelativePath, StringComparer.Ordinal).ToList();
        var generation = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N");
        var output = Path.Combine(folder, generation); Directory.CreateDirectory(output);
        int pages = 0;
        foreach(var chunk in entries.Chunk(1000))
        {
            token.ThrowIfCancellationRequested();
            Atomic(Path.Combine(output, $"files-{pages++:D5}.json"), chunk);
        }
        var manifest = new InventoryManifest(unit, generation, DateTime.UtcNow, result.Complete, entries.Count, pages,
            result.Issues.Take(200).ToList(), previous == null ? 0 : after.Keys.Except(before.Keys).Count(),
            previous == null || !result.Complete ? 0 : before.Keys.Except(after.Keys).Count(),
            previous == null ? 0 : after.Count(p => before.TryGetValue(p.Key, out var old) && old != p.Value), previous == null);
        Atomic(Path.Combine(output, "manifest.json"), manifest);
        token.ThrowIfCancellationRequested();
        Atomic(latest, manifest);
        return manifest;
    }
    public IEnumerable<InventoryEntry> Entries(InventoryManifest manifest)
    {
        if (!Regex.IsMatch(manifest.Generation, @"^[0-9T-]+[a-f0-9]{32}$")) throw new InvalidDataException("Generazione inventario non valida.");
        var folder = Path.Combine(UnitDirectory(manifest.Unit), manifest.Generation);
        for(int i = 0; i < manifest.PageCount; i++)
            foreach(var entry in Read<InventoryEntry[]>(Path.Combine(folder, $"files-{i:D5}.json"))) yield return entry;
    }
    public InventoryRun? Latest() => File.Exists(Path.Combine(Root, "report.json")) ? Read<InventoryRun>(Path.Combine(Root, "report.json")) : null;
    public void SaveRun(InventoryRun run)
    {
        Directory.CreateDirectory(Root);
        Atomic(Path.Combine(Root, "report-" + run.StartedUtc.ToString("yyyyMMddTHHmmss") + ".json"), run);
        Atomic(Path.Combine(Root, "report.json"), run);
    }
    public static T Read<T>(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return JsonSerializer.Deserialize<T>(file) ?? throw new InvalidDataException("JSON inventario vuoto.");
    }
    private static void Atomic<T>(string path, T value)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) JsonSerializer.Serialize(file, value, Options);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
