using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;
static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
var folder = Path.Combine(Path.GetTempPath(), "TA-AnnualChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var root = Path.Combine(folder, "lavori");
    var records = new[] { 1999, 2025, 2026 }.Select(year => new FolderRecord("C" + year.ToString()[2..] + "001", year.ToString()[2..], "001", "Cliente", "Progetto", Path.Combine(root, year.ToString(), "commessa"), "Valido")).ToList();
    var data = new ArchiveSnapshot { Root = SharedArchiveService.Normalize(root), Prefix = 'C', Records = records, StartedUtc = DateTime.UtcNow, SavedUtc = DateTime.UtcNow };
    var target = SharedArchiveService.FilePath(folder, root, 'C');
    File.WriteAllText(target, JsonSerializer.Serialize(data));
    Check(SharedArchiveService.Load(folder, root, 'C')!.Records.Count == 3, "Legge archivio precedente monolitico");
    SharedArchiveService.Save(folder, data, default);
    var index = JsonNode.Parse(File.ReadAllText(target))!;
    Check(index["SchemaVersion"]!.GetValue<int>() == 2 && index["Years"]!.AsObject().Count == 3, "Indice con tre JSON annuali");
    Check(index["Years"]!["1999"] != null, "Commesse storiche: anno reale del contenitore conservato");
    Check(File.Exists(target + ".legacy-v1.bak"), "Backup della precedente versione conservato");
    var old2025 = index["Years"]!["2025"]!.GetValue<string>();
    var updated = data with { SavedUtc = data.SavedUtc.AddSeconds(1), Records = [records[0], records[1], records[2] with { Subject = "Aggiornato" }] };
    SharedArchiveService.Save(folder, updated, default);
    var next = JsonNode.Parse(File.ReadAllText(target))!;
    Check(next["Years"]!["2025"]!.GetValue<string>() == old2025, "Anno invariato riusa il proprio file");
    Check(SharedArchiveService.Load(folder, root, 'C')!.Records.Any(r => r.Subject == "Aggiornato"), "Ricarica tutti gli anni con dati aggiornati");
    Check(!SharedArchiveService.Save(folder, data, default), "Non sovrascrive una revisione piu recente della stessa scansione");
    var yearFolder = Path.Combine(folder, Path.GetFileNameWithoutExtension(target) + ".years");
    var missing = Path.Combine(yearFolder, next["Years"]!["2026"]!.GetValue<string>());
    File.Delete(missing);
    try { SharedArchiveService.Load(folder, root, 'C'); throw new Exception("Anno mancante non rilevato"); }
    catch (InvalidDataException) { Check(true, "File annuale mancante non interpretato come archivio vuoto"); }
    try { SharedArchiveService.Save(folder, updated, default); throw new Exception("Archivio danneggiato sovrascritto"); }
    catch (InvalidDataException) { Check(true, "Archivio annuale danneggiato preservato per diagnosi"); }
}
finally
{
    if (Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(folder).StartsWith("TA-AnnualChecks-")) Directory.Delete(folder, true);
}
