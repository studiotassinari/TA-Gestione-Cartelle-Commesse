using System.IO;
using System.IO.Compression;
using System.Text.Json;
using TA.Updater;
static void Check(bool condition, string text) { if (!condition) throw new Exception(text); Console.WriteLine("PASS: " + text); }
var root = Path.Combine(Path.GetTempPath(), "TA-UpdaterChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var installed = Directory.CreateDirectory(Path.Combine(root, "installed")).FullName;
    var stage = Directory.CreateDirectory(Path.Combine(root, "stage")).FullName;
    var cache = Directory.CreateDirectory(Path.Combine(root, "cache")).FullName;
    Directory.CreateDirectory(Path.Combine(stage, "Updater"));
    File.WriteAllText(Path.Combine(installed, "TA-Gestione Cartelle Commesse.exe"), "old");
    File.WriteAllText(Path.Combine(installed, "appsettings.json"), "user configuration");
    File.WriteAllText(Path.Combine(stage, "TA-Gestione Cartelle Commesse.exe"), "new");
    File.WriteAllText(Path.Combine(stage, "Updater", "TA.Updater.exe"), "updater");
    var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Select(p => new PackageFile(Path.GetRelativePath(stage, p).Replace('\\', '/'), new FileInfo(p).Length, UpdatePackage.Hash(p))).ToList();
    var manifest = new PackageManifest(1, "Client", "2.7.0", files);
    try { FileTransaction.Apply(installed, stage, manifest, cache, false, () => throw new IOException("health failed"), () => { }); throw new Exception("Rollback non eseguito"); }
    catch (IOException) { }
    Check(File.ReadAllText(Path.Combine(installed, "TA-Gestione Cartelle Commesse.exe")) == "old" && !File.Exists(Path.Combine(installed, "Updater", "TA.Updater.exe")), "Rollback ripristina file precedenti e rimuove soltanto quelli aggiunti");
    Check(FileTransaction.Pending(cache) == null && File.ReadAllText(Path.Combine(installed, "appsettings.json")) == "user configuration", "Configurazione conservata e transazione conclusa");
    Check(FileTransaction.Apply(installed, stage, manifest, cache, false, () => { }, () => { }) == 2, "Aggiornamento diretto sostituisce i file senza installatore");
    Check(FileTransaction.Apply(installed, stage, manifest, cache, false, () => { }, () => { }) == 0, "File invariati non vengono sovrascritti");
    foreach (var path in new[] { "../outside.exe", @"C:\Windows\test.dll", "UpdateCache/test.exe", "appsettings.json", "unins000.exe", "file:stream", "x/../a" })
    {
        try { UpdatePackage.SafePath(installed, path); throw new Exception("Percorso non rifiutato: " + path); }
        catch (InvalidDataException) { Check(true, "Percorso protetto o esterno rifiutato: " + path); }
    }
    var recoveryBackup = Directory.CreateDirectory(Path.Combine(cache, "backup-recovery")).FullName;
    File.WriteAllText(Path.Combine(recoveryBackup, "TA-Gestione Cartelle Commesse.exe"), "before crash");
    File.WriteAllText(FileTransaction.JournalPath(cache), JsonSerializer.Serialize(new UpdateJournal(installed, recoveryBackup, false, [new("TA-Gestione Cartelle Commesse.exe", true)])));
    FileTransaction.Recover(installed, cache, () => { });
    Check(File.ReadAllText(Path.Combine(installed, "TA-Gestione Cartelle Commesse.exe")) == "before crash", "Ripristino da giornale dopo interruzione del processo");
    var zipPath = Path.Combine(root, "package.zip");
    using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
        using (var json = zip.CreateEntry("update-manifest.json").Open()) JsonSerializer.Serialize(json, manifest);
        foreach (var file in files) zip.CreateEntryFromFile(UpdatePackage.SafePath(stage, file.Path), "payload/" + file.Path);
    }
    var verified = UpdatePackage.Stage(zipPath, Path.Combine(root, "verified"), "Client", "2.7.0");
    Check(verified.Files.Count == 2, "ZIP estratto solo dopo verifica di manifest e impronte");
    try { UpdatePackage.Stage(zipPath, Path.Combine(root, "wrong"), "Server", "2.7.0"); throw new Exception("Componente errato accettato"); }
    catch (InvalidDataException) { Check(true, "Pacchetto per altro componente rifiutato"); }
}
finally
{
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("TA-UpdaterChecks-")) Directory.Delete(root, true);
}
