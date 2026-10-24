using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;
var folder = new FolderRecord("C26001", "26", "001", "Cliente", "Oggetto", @"C:\lavori\2026\C26001 - Cliente - Oggetto", "Valido")
{ Inspection = new() { TotalBytes = 1, FileCount = 1, Complete = true, CheckedUtc = DateTime.UtcNow, ContentSignature = "A" } };
Check(FolderChanges.Compare([], [folder]).Single().StartsWith("Aggiunta:"), "Cartella aggiunta descritta con percorso");
Check(FolderChanges.Compare([folder], []).Single().StartsWith("Rimossa"), "Cartella rimossa descritta");
Check(FolderChanges.Compare([folder], [folder with { Inspection = folder.Inspection! with { CheckedUtc = DateTime.UtcNow.AddHours(1) } }]).Count == 0, "Scansione identica non produce notifiche");
var edited = folder with { Inspection = folder.Inspection! with { TotalBytes = 8, FileCount = 2, QuoteStatus = "Presente" } };
var change = FolderChanges.Compare([folder], [edited]).Single();
Check(change.Contains("1 → 2") && change.Contains("1 → 8") && change.Contains("preventivo"), "Dettaglio di conteggio, dimensione e preventivo");
var renamed = folder with { Path = @"C:\lavori\2026\C26001 - Cliente - Nuovo nome" };
Check(FolderChanges.Compare([folder], [renamed]).Count == 2, "Cambio percorso presentato come rimozione e aggiunta senza inventare un'identita");
var root = Path.Combine(Path.GetTempPath(), "TA-ChangeChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    File.WriteAllText(Path.Combine(root, "a.txt"), "test");
    var first = FolderInspectionService.Inspect(root, default);
    var repeat = FolderInspectionService.Inspect(root, default);
    Check(first.ContentSignature == repeat.ContentSignature, "Firma metadati stabile tra scansioni");
    Directory.CreateDirectory(Path.Combine(root, "vuota"));
    var emptyFolder = FolderInspectionService.Inspect(root, default);
    Check(first.TotalBytes == emptyFolder.TotalBytes && first.ContentSignature != emptyFolder.ContentSignature, "Sottocartella vuota rilevata anche con dimensione invariata");
    File.Move(Path.Combine(root, "a.txt"), Path.Combine(root, "b.txt"));
    var moved = FolderInspectionService.Inspect(root, default);
    Check(emptyFolder.TotalBytes == moved.TotalBytes && emptyFolder.ContentSignature != moved.ContentSignature, "Rinomina file rilevata a parita di dimensione e data");
    Check(FolderChanges.Compare([folder with { Inspection = emptyFolder }], [folder with { Inspection = moved }]).Single().Contains("struttura"), "Differenza struttura descritta al client");
}
finally
{
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("TA-ChangeChecks-")) Directory.Delete(root, true);
}
static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
