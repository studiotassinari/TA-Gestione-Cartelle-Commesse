using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using TA.Server;
using TA.Server.Archiving;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;

internal static class FolderArchiveChecks
{
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS: " + text); }
    private static void Denied<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Atteso " + typeof(T).Name); }
    public static void Run()
    {
        // Tutte le operazioni distruttive sono confinate a questa fixture appena creata.
        var root = Path.Combine(Path.GetTempPath(), "TA-ZipChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string Folder(string name)
        {
            var source = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            Directory.CreateDirectory(Path.Combine(source, "vuota"));
            var calc = Directory.CreateDirectory(Path.Combine(source, "sismicad")).FullName;
            File.WriteAllText(Path.Combine(calc, "main.scj"), "non escludere dalla copia");
            File.WriteAllText(Path.Combine(source, "originale.txt"), "contenuto di prova");
            return source;
        }
        var source = Folder("C26001 - Test");
        var receipt = VerifiedFolderZip.Prepare(root, source, default);
        Check(Directory.Exists(source) && File.Exists(receipt.Zip), "Preparazione ZIP conserva gli originali");
        using (var zip = ZipFile.OpenRead(receipt.Zip))
            Check(zip.GetEntry("vuota/") != null && zip.GetEntry("sismicad/main.scj") != null, "ZIP include cartelle vuote e file Sismicad, senza esclusioni dell'inventario");
        Denied<IOException>(() => VerifiedFolderZip.Prepare(root, source, default));
        VerifiedFolderZip.DeleteOriginal(root, receipt, default);
        Check(!Directory.Exists(source) && File.Exists(receipt.Zip), "Originale eliminato solo dopo verifica completa dello ZIP");

        source = Folder("G26002 - Modificata"); receipt = VerifiedFolderZip.Prepare(root, source, default);
        File.WriteAllText(Path.Combine(source, "originale.txt"), "modificato dopo lo zip");
        Denied<IOException>(() => VerifiedFolderZip.DeleteOriginal(root, receipt, default));
        Check(File.ReadAllText(Path.Combine(source, "originale.txt")) == "modificato dopo lo zip", "Modifica dopo preparazione blocca tutta l'eliminazione");

        source = Folder("G26003 - Zip alterato"); receipt = VerifiedFolderZip.Prepare(root, source, default);
        File.AppendAllText(receipt.Zip, "alterazione");
        Denied<IOException>(() => VerifiedFolderZip.DeleteOriginal(root, receipt, default));
        Check(Directory.Exists(source), "ZIP alterato blocca l'eliminazione");

        source = Folder("C26004 - Concorrenza"); receipt = VerifiedFolderZip.Prepare(root, source, default);
        var added = Path.Combine(source, "nuovo.txt");
        Denied<IOException>(() => VerifiedFolderZip.DeleteOriginal(root, receipt, default, () => File.WriteAllText(added, "non eliminare")));
        Check(File.ReadAllText(added) == "non eliminare" && File.Exists(receipt.Zip), "File creato durante la rimozione resta intatto; ZIP verificato conservato");

        source = Folder("C26005 - File aperto");
        using (var locked = new FileStream(Path.Combine(source, "originale.txt"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            Denied<IOException>(() => VerifiedFolderZip.Prepare(root, source, default));
        Check(!File.Exists(source + ".zip") && Directory.Exists(source), "File in scrittura impedisce la preparazione sicura");
        File.WriteAllText(Path.Combine(source, "originale.txt") + ":dati", "flusso alternativo");
        Denied<IOException>(() => VerifiedFolderZip.Prepare(root, source, default));
        Check(Directory.Exists(source), "Flusso alternativo non perduto in uno ZIP incompleto");
        source = Folder("C26006 - Provenienza");
        File.WriteAllText(Path.Combine(source, "originale.txt") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3");
        File.WriteAllText(Path.Combine(source, "Thumbs.db"), "anteprime"); File.WriteAllText(Path.Combine(source, "Thumbs.db") + ":encryptable", "");
        Check(File.Exists(VerifiedFolderZip.Prepare(root, source, default).Zip), "Marcatura di provenienza e segnaposto di Thumbs.db non bloccano l'archiviazione");
        source = Folder("C26007 - Flusso anomalo");
        File.WriteAllText(Path.Combine(source, "Thumbs.db"), "anteprime"); File.WriteAllText(Path.Combine(source, "Thumbs.db") + ":encryptable", "dati");
        Denied<IOException>(() => VerifiedFolderZip.Prepare(root, source, default));
        Check(!File.Exists(source + ".zip") && Directory.Exists(source), "Un flusso di sistema con dati inattesi blocca ancora l'archiviazione");
        Denied<UnauthorizedAccessException>(() => VerifiedFolderZip.ValidatePath(root, root));
        Denied<UnauthorizedAccessException>(() => VerifiedFolderZip.ValidatePath(root, Path.GetTempPath()));

        var settings = new ServerSettings { JobsRoot = Path.Combine(root, "lavori"), BidsRoot = Path.Combine(root, "gare"), ArchiveFolder = Path.Combine(root, "db") };
        source = Folder("lavori/2026/C26006 - Comando");
        var record = new FolderRecord("C26006", "26", "006", "Test", "Test", source, "Valido");
        var store = new InventoryStore(settings.InventoryFolder);
        store.SaveDocument("snapshot:C:" + SharedArchiveService.Normalize(settings.JobsRoot), new ArchiveSnapshot { Root = settings.JobsRoot, Prefix = 'C', SavedUtc = DateTime.UtcNow, Records = [record] });
        var scans = new ScanCoordinator(settings, NullLogger<ScanCoordinator>.Instance, new ChangeHub(false), new AnalysisService(settings, NullLogger<AnalysisService>.Instance));
        scans.Get("C").InitializeAsync().GetAwaiter().GetResult();
        using var service = new FolderArchiveService(settings, scans);
        var request = new Request("PrepareFolderZip", FolderArchive: new(FolderId: record.Id));
        Denied<UnauthorizedAccessException>(() => service.Execute(request, "Administrator", false));
        Denied<UnauthorizedAccessException>(() => service.Execute(request with { Category = "A" }, "Antonio", true));
        var state = service.Execute(request, "Antonio", true);
        FolderArchiveStatus Wait()
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do { Thread.Sleep(25); state = service.Execute(new("FolderZipStatus", FolderArchive: new(JobId: state.JobId)), "Antonio", true); }
            while (state.FinishedUtc == null && DateTime.UtcNow < deadline);
            return state;
        }
        state = Wait(); Check(state.State == "ZIP verificato" && state.Confirmation != null, "Servizio prepara e restituisce conferma separata");
        var delete = new Request("DeleteZippedFolder", FolderArchive: new(JobId: state.JobId, Confirmation: state.Confirmation!));
        Denied<ArgumentException>(() => service.Execute(delete, "Giuseppe", true));
        Denied<UnauthorizedAccessException>(() => service.Execute(delete with { FolderArchive = new(JobId: state.JobId) }, "Antonio", true));
        service.Execute(delete, "Antonio", true);
        state = Wait(); Check(state.State == "Completato", "Conferma autorizzata completa il lavoro");
        Denied<UnauthorizedAccessException>(() => service.Execute(delete, "Antonio", true));
        foreach (var pair in new[] { ("PrepareFolderZip", "prepara_zip_cartella"), ("FolderZipStatus", "stato_zip_cartella"), ("DeleteZippedFolder", "elimina_cartella_zippata") })
        {
            var q = new Request(pair.Item1, McpTool: pair.Item2, CorrelationId: Guid.NewGuid().ToString("N"));
            Denied<UnauthorizedAccessException>(() => LocalCatalogPolicy.Validate(q));
            Denied<UnauthorizedAccessException>(() => service.Execute(q, "Antonio", true));
            Denied<UnauthorizedAccessException>(() => LocalCatalogPolicy.Validate(q with { McpTool = "cerca_cartelle" }));
            Denied<UnauthorizedAccessException>(() => LocalReadOnlyPolicy.Validate(q));
        }
        Check(true, "Conferma monouso, identità e policy in sola lettura rispettate");
    }
}
