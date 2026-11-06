using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

public static class DatabaseMigration
{
    public static void Import(ServerSettings settings,CancellationToken token,Action<string> progress)
    {
        _=GeologyStorage.Root(settings); // Nessuna scrittura nelle sorgenti originali.
        var store=new InventoryStore(settings.InventoryFolder);
        if(store.Document("migration:inventory-complete")==null)
            InventoryMigration.Import(settings.ArchiveFolder,store,token,progress);
        // Gli indici delle analisi contengono tutte le schede correnti e i collegamenti ai fascicoli.
        foreach(var relative in new[]{"geologia/catalogo.json","geologia/posizioni.json","geologia/analisi/indice.json","ipripi/indice.json","ipripi/candidati.json"})
        {
            token.ThrowIfCancellationRequested();var path=Path.Combine(settings.ArchiveFolder,relative.Replace('/',Path.DirectorySeparatorChar));
            if(CatalogStorage.Exists(settings,path)){progress(path);_ = CatalogStorage.Bytes(settings,path);}
        }
        if(store.IntegrityCheck()!="ok")throw new IOException("Verifica del database migrato fallita");
    }
    public static void BackupAndCleanInventory(ServerSettings settings,CancellationToken token,Action<string> progress)
    {
        var store=new InventoryStore(settings.InventoryFolder);
        if(store.Document("migration:inventory-cleaned")!=null)return;
        if(store.Document("migration:inventory-complete")==null)throw new IOException("Migrazione non completata");
        var backup=Path.Combine(settings.InventoryFolder,"migrazione.sqlite");
        progress("Backup del database prima della pulizia");store.Backup(backup);DatabaseBackup.Verify(backup);
        // Copia soltanto il backup coerente creato con SQLite, mai il database attivo o il suo WAL.
        var destination=Path.Combine(settings.ArchiveFolder,"backup-database");Directory.CreateDirectory(destination);
        var copy=Path.Combine(destination,"migrazione.sqlite");File.Copy(backup,copy,true);
        using(var a=File.OpenRead(backup))using(var b=File.OpenRead(copy))
            if(!System.Security.Cryptography.SHA256.HashData(a).SequenceEqual(System.Security.Cryptography.SHA256.HashData(b)))throw new IOException("Backup di migrazione non verificato sulla rete");
        File.Delete(backup);
        var deleted=InventoryMigration.CleanMigrated(settings.ArchiveFolder,store,token,progress);
        deleted+=LegacyDataCleanup.Clean(settings,store,token);
        store.SaveDocument("migration:inventory-cleaned",new{CompletedUtc=DateTime.UtcNow,Deleted=deleted});
    }
}
