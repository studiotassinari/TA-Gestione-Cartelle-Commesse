using TA.Server;
using TA_Gestione_Cartelle_Commesse.Contracts;
if (args.Contains("--preview-model"))
{
    var configured = new TemplateCreationService(new ServerSettings { JobsRoot = @"\\ingtassinari.it\data\lavori" });
    var plan = configured.Preview(new("C26998", "ANTEPRIMA", "", "NESSUNA CREAZIONE"));
    Console.WriteLine($"PASS: modello reale leggibile, {plan.Directories.Length} sottocartelle; nessuna scrittura eseguita.");
    return;
}
var root = Path.Combine(Path.GetTempPath(), "TA-TemplateChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var jobs = Directory.CreateDirectory(Path.Combine(root, "lavori")).FullName;
    Directory.CreateDirectory(Path.Combine(jobs, "2026"));
    var model = Directory.CreateDirectory(Path.Combine(root, "modello")).FullName;
    Directory.CreateDirectory(Path.Combine(model, "00_LAVORO", "00-BIM"));
    Directory.CreateDirectory(Path.Combine(model, "99_AMMINISTRAZIONE"));
    File.WriteAllText(Path.Combine(model, "documento.xlsx"), "non copiare");
    File.WriteAllText(Path.Combine(model, "99_AMMINISTRAZIONE", "preventivo.pdf"), "non copiare");
    var archive = Directory.CreateDirectory(Path.Combine(root, "archivio")).FullName;
    var service = new TemplateCreationService(new() { JobsRoot = jobs, ArchiveFolder = archive, JobTemplate = model });
    var input = new CreateJobRequest("C26001", "Cliente", "Luogo", "Argomento");
    var preview = service.Preview(input);
    Check(preview.Directories.Length == 3 && !Directory.Exists(preview.Destination), "Anteprima non crea cartelle");
    var created = service.Create(input with { TemplateStamp = preview.Stamp });
    Check(created == Path.Combine(jobs, "2026", "C26001 - Cliente - Luogo - Argomento"), "Destinazione derivata dall'anno e dai campi");
    Check(Directory.GetDirectories(created, "*", SearchOption.AllDirectories).Length == 3 && Directory.GetFiles(created, "*", SearchOption.AllDirectories).Length == 0, "Struttura annidata copiata senza alcun file");
    Check(File.ReadAllText(Path.Combine(model, "documento.xlsx")) == "non copiare", "Modello originale preservato");
    Expect<IOException>(() => service.Create(input with { TemplateStamp = preview.Stamp }), "Duplicato rifiutato senza sovrascrivere");
    Expect<IOException>(() => service.Preview(input with { Client = "Diverso" }), "Codice duplicato rifiutato anche con nome diverso");
    foreach (var invalid in new[] { input with { Code = ".." }, input with { Code = "G26001" }, input with { Code = "C26002", Client = "..\\esterno" }, input with { Code = "C26002", Subject = "nome:stream" }, input with { Code = "C26002", Client = "" } })
        Expect<ArgumentException>(() => service.Preview(invalid), "Campi non validi rifiutati");
    Expect<DirectoryNotFoundException>(() => service.Preview(input with { Code = "C27001" }), "Cartella annuale mancante non creata implicitamente");
    var next = input with { Code = "C26002" }; var old = service.Preview(next);
    Directory.CreateDirectory(Path.Combine(model, "NUOVA"));
    Expect<IOException>(() => service.Create(next with { TemplateStamp = old.Stamp }), "Modello cambiato richiede nuova anteprima");
    Check(!Directory.Exists(old.Destination), "Modello cambiato non lascia destinazione parziale");
    Expect<IOException>(() => service.Create(next), "Creazione senza anteprima rifiutata");
    var current = service.Preview(next);
    var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => { try { service.Create(next with { TemplateStamp = current.Stamp }); return true; } catch (IOException) { return false; } })));
    Check(results.Count(x => x) == 1 && Directory.GetDirectories(Path.Combine(jobs, "2026"), "C26002*").Length == 1, "Richieste concorrenti non duplicano il codice");
    Check(Directory.GetDirectories(Path.Combine(jobs, "2026"), ".TA-new-*").Length == 0, "Nessuna struttura temporanea residua nei casi verificati");
}
finally
{
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("TA-TemplateChecks-")) Directory.Delete(root, true);
}
Console.WriteLine("TUTTI I CONTROLLI TEMPLATE SUPERATI; nessuna commessa reale creata.");
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message); }
static void Expect<T>(Action action, string message) where T : Exception { try { action(); } catch (T) { Console.WriteLine("PASS: " + message); return; } throw new Exception(message); }
