using System.IO;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;


var testRoot = Path.Combine(AppContext.BaseDirectory, "case-" + Guid.NewGuid().ToString("N"));
var root = Path.Combine(testRoot, "lavori");
var job = Directory.CreateDirectory(Path.Combine(root, "2026", "C26001 - Cliente - Progetto")).FullName;
var admin = Directory.CreateDirectory(Path.Combine(job, "99_AMMINISTRAZIONE", "Documenti")).FullName;
var nested = Directory.CreateDirectory(Path.Combine(job, "C26999 - Altro - Interno")).FullName;
var quote = Path.Combine(admin, "Preventivo_2026.pdf");
File.WriteAllBytes(quote, new byte[101]);
File.WriteAllBytes(Path.Combine(nested, "disegno.dwg"), new byte[55]);
File.SetLastWriteTimeUtc(quote, new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));
var modified = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
File.SetLastWriteTimeUtc(Path.Combine(nested, "disegno.dwg"), modified);
var scan = FolderInspectionService.Enrich(FolderReader.Scan(root, 'C', default), default);
Check(scan.Records.Count == 1, "Non identifica commesse annidate");
var details = scan.Records.Single().Inspection!;
Check(details.Complete && details.TotalBytes == 156 && details.FileCount == 2, "Dimensione e conteggio ricorsivi esatti");
Check(details.LastFileWriteUtc == modified, "Ultima modifica ricavata dai file annidati");
Check(details.QuoteStatus == "Presente (da nome file)" && details.QuoteFiles.Single() == quote, "Riconosce preventivo in 99_AMMINISTRAZIONE");
var invalidDate = FolderInspectionService.Inspect(job, default, file =>
    file.FullName == quote ? throw new ArgumentOutOfRangeException("fileTime") : file.LastWriteTimeUtc);
Check(!invalidDate.Complete && invalidDate.TotalBytes == 156 && invalidDate.FileCount == 2,
    "Data non valida non interrompe scansione, dimensioni o conteggio");
Check(invalidDate.LastFileWriteUtc == modified && invalidDate.QuoteFiles.Contains(quote)
    && invalidDate.Issues.Any(issue => issue.Contains(quote)) && invalidDate.QuoteStatus.Contains("parziale"),
    "Data non valida segnala percorso, conserva preventivo e usa le altre date valide");
var allInvalid = FolderInspectionService.Inspect(job, default, _ => throw new ArgumentOutOfRangeException("fileTime"));
Check(allInvalid.LastFileWriteUtc == null && !allInvalid.Complete && allInvalid.FileCount == 2,
    "Tutte le date non valide restano sconosciute senza inventare un valore");
foreach (var variant in new[] { "amministraione", "AMMINISTRAZIONE" })
{
    var variantRoot = Path.Combine(testRoot, variant);
    Directory.CreateDirectory(Path.Combine(variantRoot, variant));
    File.WriteAllText(Path.Combine(variantRoot, variant, "Offerta economica.docx"), "test");
    Check(FolderInspectionService.Inspect(variantRoot, default).QuoteStatus == "Presente (da nome file)", "Riconosce variante " + variant);
}
File.Move(quote, Path.Combine(admin, "Richiesta_preventivo.pdf"));
Check(FolderInspectionService.Inspect(job, default).QuoteStatus == "Da verificare (nome ambiguo)", "Richiesta preventivo non equivale a preventivo");
File.Move(Path.Combine(admin, "Richiesta_preventivo.pdf"), Path.Combine(job, "Preventivo.pdf"));
Check(FolderInspectionService.Inspect(job, default).QuoteStatus == "Non trovato nei nomi", "Ignora preventivo fuori amministrazione");
var missing = FolderInspectionService.Inspect(Path.Combine(testRoot, "inesistente"), default);
Check(!missing.Complete && missing.QuoteStatus.Contains("parziale"), "Accesso fallito non equivale ad assenza preventivo");
using var cancel = new CancellationTokenSource(); cancel.Cancel();
Expect<OperationCanceledException>(() => FolderInspectionService.Inspect(job, cancel.Token), "Annullamento scansione");
var archive = Directory.CreateDirectory(Path.Combine(testRoot, "archive")).FullName;
var first = new ArchiveSnapshot { Root = SharedArchiveService.Normalize(root), Prefix = 'C', StartedUtc = DateTime.UtcNow,
    SavedUtc = DateTime.UtcNow, Records = scan.Records, Warnings = scan.Warnings };
Check(SharedArchiveService.Load(archive, root, 'C') == null, "Archivio inizialmente assente");
Check(SharedArchiveService.Save(archive, first, default), "Salvataggio archivio");
var loaded = SharedArchiveService.Load(archive, root, 'C')!;
Check(loaded.Records.Single().Inspection!.TotalBytes == 156 && loaded.Records.Single().Inspection!.QuoteFiles.Single() == quote, "JSON preserva metadati ed evidenze");
Check(!SharedArchiveService.Save(archive, first with { StartedUtc = first.StartedUtc.AddMinutes(-1), Records = [] }, default), "Una scansione vecchia non sovrascrive dati nuovi");
var json = SharedArchiveService.FilePath(archive, root, 'C');
var before = File.ReadAllText(json);
using (var lease = new FileStream(json + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
    Expect<IOException>(() => SharedArchiveService.Save(archive, first, default), "Lock tra processi rispettato");
Expect<OperationCanceledException>(() => SharedArchiveService.Save(archive, first, cancel.Token), "Salvataggio annullato");
Check(File.ReadAllText(json) == before, "Lock e annullamento preservano archivio precedente");
File.WriteAllText(json, "{ JSON non valido");
Expect<JsonException>(() => SharedArchiveService.Save(archive, first, default), "Archivio corrotto non sovrascritto");
Check(File.ReadAllText(json) == "{ JSON non valido", "Preserva il file corrotto per recupero");
File.WriteAllText(json, before.Replace("\"SchemaVersion\": 2", "\"SchemaVersion\": 999"));
Expect<InvalidDataException>(() => SharedArchiveService.Load(archive, root, 'C'), "Rifiuta schema sconosciuto");
File.WriteAllText(json, before);
Console.WriteLine("TUTTI I CONTROLLI SUPERATI");

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}
static void Expect<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { Console.WriteLine("PASS: " + name); return; }
    throw new Exception("FAIL: " + name);
}
