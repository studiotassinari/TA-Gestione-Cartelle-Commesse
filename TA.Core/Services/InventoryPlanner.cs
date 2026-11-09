using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;

namespace TA_Gestione_Cartelle_Commesse.Services;

public static class InventoryPlanner
{
    public static string Id(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
    public static string Year(string path, string shortYear = "")
    {
        var full = path.Replace('\\','/').Split('/').LastOrDefault(p => Regex.IsMatch(p, @"^(19|20)\d{2}$"));
        return full ?? (Regex.IsMatch(shortYear, @"^\d{2}$") ? "anno-" + shortYear : "_anno-non-noto");
    }
    public static (List<InventoryUnit> Units, List<ProjectCandidate> Candidates, List<string> Issues) Plan(
        IEnumerable<(string Category, string Root, List<FolderRecord> Records)> sources, string jobsRoot, CancellationToken token)
    {
        var units = new Dictionary<string,InventoryUnit>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<ProjectCandidate>(); var issues = new List<string>();
        var all = sources.ToArray();
        foreach(var source in all)
        {
            foreach(var record in source.Records)
                units.TryAdd(record.Path, new(record.Id, source.Category, Year(record.Path, record.Year), record.Code,
                    record.Client, record.Subject, record.Path, "censita"));
            if(source.Category=="A")continue; // Solo archivi esplicitamente censiti: nessuna esplorazione residua.
            // Anche i file non assegnati nelle cartelle annuali conservano il proprio anno.
            try
            {
                foreach (var year in Directory.EnumerateDirectories(source.Root).Where(p => Regex.IsMatch(Path.GetFileName(p), @"^(19|20)\d{2}$")))
                    if ((File.GetAttributes(year) & FileAttributes.ReparsePoint) == 0)
                        units.TryAdd(year, new(Id(year), source.Category, Path.GetFileName(year), "", "", "File non assegnati dell'anno", year, "residui-anno"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { issues.Add(source.Root + ": " + ex.Message); }
            // Copre anche file sciolti e cartelle non classificabili, senza riclassificare le commesse annidate.
            units.TryAdd(source.Root, new(Id(source.Root), source.Category, "_anno-non-noto", "", "", "File non assegnati", source.Root, "residui-radice"));
        }
        static string Normalize(string s) => Regex.Replace(s.ToUpperInvariant(), @"[^\p{L}\p{Nd}]", "");
        var clients = all.Where(s => s.Category == "C").SelectMany(s => s.Records).Where(r => !string.IsNullOrWhiteSpace(r.Client))
            .GroupBy(r => Normalize(r.Client)).Where(g => g.Count() > 1 && g.Key.Length >= 4)
            .Select(g => g.First().Client).ToArray();
        try
        {
            foreach(var folder in Directory.EnumerateDirectories(jobsRoot))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(folder);
                if (FolderReader.IsExcluded(folder) || Regex.IsMatch(name, @"^(19|20)\d{2}$") || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                var label = Regex.Replace(name, @"^[A-Z0-9]{2,6}\s*-\s*", "", RegexOptions.IgnoreCase);
                var normalized = Normalize(label);
                var matches = clients.Where(c => Normalize(c) == normalized || normalized.Length >= 5 && Normalize(c).Contains(normalized)).ToArray();
                var client = matches.Length == 1 ? matches[0] : label;
                var evidence = matches.Length == 1 ? "Nome compatibile con committente presente in più commesse" :
                    "Cartella fuori dagli anni; committente suggerito dal nome, da confermare";
                units.TryAdd(folder, new(Id(folder), "C", "_anno-non-noto", "", client, name, folder, "contenitore-da-verificare"));
                try
                {
                    foreach(var child in Directory.EnumerateDirectories(folder))
                    {
                        if (FolderReader.IsExcluded(child) || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                        var title = Path.GetFileName(child);
                        var code = Regex.Match(title, @"^[CG]\d{5}(?=\D|$)", RegexOptions.IgnoreCase);
                        candidates.Add(new(child, client, title, code.Success ? code.Value.ToUpperInvariant() : null,
                            evidence + "; possibile commessa o argomento, non aggiunto all'elenco ufficiale", matches.Length == 1 ? "media" : "bassa"));
                        units.TryAdd(child, new(Id(child), "C", Year(child, code.Success ? code.Value.Substring(1,2) : ""), code.Success ? code.Value.ToUpperInvariant() : "",
                            client, title, child, "proposta-non-confermata"));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { issues.Add(folder + ": " + ex.Message); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { issues.Add(jobsRoot + ": " + ex.Message); }
        return (units.Values.ToList(), candidates, issues);
    }
}
