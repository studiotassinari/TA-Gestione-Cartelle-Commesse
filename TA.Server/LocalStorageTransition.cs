using Microsoft.Data.Sqlite;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

// Recupera le analisi senza importare l'inventario difettoso. Non cancella nessuna sorgente.
public static class LocalStorageTransition
{
    public const string Marker="local-storage:catalogs-preserved-v1";
    public static void Prepare(ServerSettings settings,CancellationToken token,Action<string> progress)
    {
        var store=new InventoryStore(settings.InventoryFolder);
        if(store.Document(Marker)!=null)return;
        if(store.Manifests().Count>0)throw new IOException("La ricostruzione richiede una nuova cartella database vuota.");
        if(string.IsNullOrWhiteSpace(settings.LegacyCatalogFolder)||string.IsNullOrWhiteSpace(settings.LegacyDatabaseFolder))
            throw new IOException("Indicare le sorgenti precedenti per preservare cataloghi e analisi prima della ricostruzione.");
        var oldDb=Path.GetFullPath(Path.Combine(settings.LegacyDatabaseFolder,"inventario-file","inventario.sqlite"));
        if(oldDb.Equals(store.DatabasePath,StringComparison.OrdinalIgnoreCase)||!File.Exists(oldDb))throw new IOException("Database precedente mancante o coincidente con il nuovo.");
        var recovery=Path.Combine(settings.InventoryFolder,"Recovery");Directory.CreateDirectory(recovery);
        var originalBackup=Path.Combine(recovery,"prima-ricostruzione.sqlite");
        if(!File.Exists(originalBackup))
        {
            progress("Copia coerente del database precedente");
            var temp=originalBackup+".tmp";
            if(File.Exists(temp))File.Delete(temp);
            using(var source=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=oldDb,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            using(var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temp,Pooling=false}.ToString()))
            {source.Open();target.Open();source.BackupDatabase(target);}
            DatabaseBackup.Verify(temp);File.Move(temp,originalBackup);
        }
        else DatabaseBackup.Verify(originalBackup);
        token.ThrowIfCancellationRequested();
        progress("Recupero locale delle sole analisi; inventari e cache di rete esclusi");
        int files=0;
        // Verifica l'accesso alla sorgente: un errore di rete non equivale a un catalogo vuoto.
        var existingAreas=Directory.GetDirectories(settings.LegacyCatalogFolder);
        foreach(var area in new[]{"geologia","ipripi"})
        {
            var folder=Path.Combine(settings.LegacyCatalogFolder,area);
            if(!existingAreas.Contains(folder,StringComparer.OrdinalIgnoreCase))continue;
            var pending=new Stack<string>();pending.Push(folder);
            while(pending.TryPop(out var directory))
            {
                token.ThrowIfCancellationRequested();
                if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Collegamento nel catalogo precedente: verificare prima del recupero.");
                foreach(var child in Directory.EnumerateDirectories(directory))
                    if(!Path.GetFileName(child).Equals("unita",StringComparison.OrdinalIgnoreCase))pending.Push(child);
                foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
                {
                    token.ThrowIfCancellationRequested();
                    if(++files>100000 || (File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0 || new FileInfo(file).Length>64*1024*1024)throw new IOException("Catalogo precedente oltre i limiti di recupero.");
                    var relative=Path.GetRelativePath(settings.LegacyCatalogFolder,file).Replace('\\','/');
                    progress("Recupero cataloghi: "+relative);
                    store.SaveDocumentJson(relative.ToUpperInvariant(),File.ReadAllText(file));
                }
            }
        }
        // Le versioni già salvate in SQL prevalgono sui JSON precedenti.
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=originalBackup,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
        {
            db.Open();using var command=db.CreateCommand();
            command.CommandText="SELECT key,payload FROM state WHERE key LIKE 'doc:GEOLOGIA/%' OR key LIKE 'doc:IPRIPI/%'";
            using var reader=command.ExecuteReader();
            while(reader.Read()){token.ThrowIfCancellationRequested();store.SaveDocumentJson(reader.GetString(0)[4..],reader.GetString(1));files++;}
        }
        if(store.IntegrityCheck()!="ok")throw new IOException("Verifica cataloghi recuperati fallita.");
        var catalogs=Path.Combine(recovery,"cataloghi-recuperati.sqlite");
        store.Backup(catalogs);DatabaseBackup.Verify(catalogs);
        store.SaveDocument(Marker,new{CompletedUtc=DateTime.UtcNow,RecoveredDocuments=files});
        progress("Cataloghi preservati localmente; avvio scansione. Backup di rete indipendente.");
    }
}
