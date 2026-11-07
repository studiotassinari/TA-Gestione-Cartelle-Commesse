using TA.Server.Business;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using Microsoft.Data.Sqlite;

var root = Path.Combine(Path.GetTempPath(), "TA-BusinessChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or UnauthorizedAccessException or IOException) { return; } throw new Exception("Operazione non rifiutata"); }
try
{
    FattureExcelChecks.Run();
    ProjectStructureChecks.Run(Path.Combine(root,"project-structure"));
    ProjectActivityChecks.Run(Path.Combine(root,"project-activity"));
    if (args is ["--preview-excel", var excelPath])
    {
        var empty = new BusinessStore(Path.Combine(root, "excel-preview"));
        var previewService = new BusinessService(empty, () => [], s => s);
        var summary = FattureExcelImport.Import(previewService, File.ReadAllBytes(excelPath), "Verifica locale", true);
        Check(empty.ChangeNumber == 0 && empty.All<Customer>("customer").Length == 0, "Anteprima Excel ha scritto anagrafiche");
        Console.WriteLine($"Anteprima Excel reale: letti={summary.Read}; nuovi={summary.Added}; collegati={summary.Linked}; esclusi={summary.Skipped}; segnalazioni={summary.Issues.Length}; scritture anagrafiche=0.");
    }
    var store = new BusinessStore(root);
    var project = new WorkProject("C2026001", "C26001", "Prova sintetica");
    var service = new BusinessService(store, () => [project], s => s);
    const string user = @"INGTASSINARI\collaboratore", other = @"INGTASSINARI\altro", manager = @"INGTASSINARI\antonio.tassinari";
    var today = DateOnly.FromDateTime(DateTime.Today);
    T Run<T>(BusinessCommand c, string actor = manager, bool admin = true) => (T)service.Execute(c, actor, admin);
    Check(ManagementAccess.AllowsSid("S-1", x => x.StartsWith("antonio.") ? "S-1" : "S-2"), "Antonio escluso");
    Check(!ManagementAccess.AllowsSid("S-admin", x => "S-1"), "Amministratore generico ammesso");
    Check(!ManagementAccess.AllowsSid(null, x => null), "SID nullo ammesso");
    Reject(() => Run<Customer>(new("SaveCustomer", Customer: new("", "Prova")), user, false));
    var csv = "Codice;Ragione sociale;Partita IVA;Email\n1;\"Cliente; prova\";123;prova@example.invalid\n2;Secondo;456;";
    var preview = Run<CustomerImportResult>(new("PreviewMagoot", Csv: csv));
    Check(preview.Added == 2 && store.All<Customer>("customer").Length == 0 && store.ChangeNumber == 0, "Anteprima scrive dati");
    Run<CustomerImportResult>(new("ImportMagoot", Csv: csv));
    Run<CustomerImportResult>(new("ImportMagoot", Csv: csv));
    Check(store.All<Customer>("customer").Length == 2, "Reimportazione duplicata");
    var merged = Run<CustomerImportResult>(new("ImportFatture", Csv: "id,nome,piva\nf1,Nome diverso,123"));
    Check(merged.Linked == 1 && merged.Issues.Length == 1, "Riconciliazione fonte");
    var client = store.All<Customer>("customer").Single(c => c.Vat == "123");
    Check(client.Name == "Cliente; prova" && client.Sources?.Length == 2, "Persi nome o provenienza");
    var updated = Run<Customer>(new("SaveCustomer", Customer: client with { Email = "nuova@example.invalid" }));
    Reject(() => Run<Customer>(new("SaveCustomer", Customer: client with { Email = "obsoleta@example.invalid" })));
    Reject(() => Run<Customer>(new("SaveCustomer", Customer: new("", "Duplicato", "123"))));
    var conflict = Run<CustomerImportResult>(new("ImportMagoot", Csv: "id,nome,piva\n1,Conflitto,789"));
    Check(conflict.Added == 0 && conflict.Linked == 0 && conflict.Issues.Length == 1, "Sovrascrittura identificativi");
    Reject(() => Run<WorkProject>(new("SaveProject", Project: project with { HoursMode = "Quote" })));
    var activity = Run<WorkActivity>(new("SaveActivity", Activity: new("", project.Id, "Progetto", "Attività prova", user, today, today, 120)));
    var entry = new WorkTime("", project.Id, activity.Id, today, 60, "Prova ore", User: other);
    Reject(() => Run<WorkTime>(new("SaveTime", Time: entry), user, false));
    project = Run<WorkProject>(new("SaveProject", Project: project with { HoursMode = "Override", Reason = "Autorizzazione prova" }));
    var saved = Run<WorkTime>(new("SaveTime", Time: entry), user, false);
    Check(saved.User == user, "Identità client fidata");
    Reject(() => Run<WorkTime>(new("SaveTime", Time: saved with { Minutes = 30 }), other, false));
    Reject(() => Run<bool>(new("DeleteTime", Id: saved.Id, Revision: saved.Revision), other, false));
    Reject(() => Run<WorkTime>(new("SaveTime", Time: entry with { Minutes = 1440 }), user, false));
    Reject(() => Run<WorkTime>(new("SaveTime", Time: entry with { Day = today.AddDays(1) }), user, false));
    var privateView = Run<BusinessSnapshot>(new("Snapshot", new(today, today)), other, false);
    Check(privateView.Times.Length == 0 && !privateView.CanManage, "Ore altrui esposte");
    var visible = Run<BusinessSnapshot>(new("Snapshot", new(today, today)), user, false);
    Check(visible.Times.Length == 1, "Ore proprie mancanti");
    string[] members = [user, other];
    var restricted = new BusinessService(store, () => [project], s => s, () => members);
    var roster = (BusinessSnapshot)restricted.Execute(new("Snapshot", new(today,today)),manager,true);
    Check(roster.Collaborators.SequenceEqual(members), "Collaboratori non derivati dal gruppo");
    Reject(() => restricted.Execute(new("SaveActivity", Activity: activity with {Assignee=manager}),manager,true));
    members = [other];
    Reject(() => restricted.Execute(new("Snapshot", new(today,today)),user,false));
    Reject(() => restricted.Execute(new("SaveTime", Time: entry),user,false));
    Check(store.All<WorkActivity>("activity").Single().Assignee==user, "La rimozione dal gruppo ha alterato lo storico");
    var backup = Path.Combine(root, "backup.sqlite"); store.Backup(backup);
    using (var db = new SqliteConnection($"Data Source={backup};Pooling=False"))
    { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM records WHERE kind='time'"; Check(Convert.ToInt32(cmd.ExecuteScalar()) == 1, "Backup incompleto"); }
    Run<bool>(new("DeleteTime", Id: saved.Id, Revision: saved.Revision), user, false);
    Check(new BusinessStore(root).All<WorkTime>("time").Length == 0, "Cancellazione non persistita");
    var settings = new TA.Server.ServerSettings { DatabaseFolder = root, ArchiveFolder = Path.Combine(root, "share") };
    var backupService = new BusinessBackupService(service, settings, Microsoft.Extensions.Logging.Abstractions.NullLogger<BusinessBackupService>.Instance);
    backupService.Save(store, settings);
    Check(Directory.GetFiles(Path.Combine(settings.ArchiveFolder, "backup-gestionale"), "*.sqlite").Length == 1, "Copia giornaliera assente");
    Check(Directory.GetFiles(Path.Combine(root, "Temp", "BackupGestionale")).Length == 0, "Temporaneo backup residuo");
    Check(store.BackupStatus.StartsWith("Backup gestionale verificato"), "Stato backup mancante");
    Console.WriteLine("BusinessChecks: permessi, importazione, concorrenza, ore e backup verificati.");
}
finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
