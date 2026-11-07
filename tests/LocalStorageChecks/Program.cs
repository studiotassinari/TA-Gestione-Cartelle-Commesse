using TA.Server;
using TA_Gestione_Cartelle_Commesse.Services;
using Microsoft.Data.Sqlite;
var root=Path.Combine(Path.GetTempPath(),"TA-LocalChecks-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
try
{
    var old=Path.Combine(root,"old");var legacy=Path.Combine(root,"legacy");Directory.CreateDirectory(Path.Combine(legacy,"geologia"));
    File.WriteAllText(Path.Combine(legacy,"geologia","posizioni.json"),"{\"value\":1}");
    Directory.CreateDirectory(Path.Combine(legacy,"ipripi"));File.WriteAllText(Path.Combine(legacy,"ipripi","indice.json"),"{\"value\":2}");
    Directory.CreateDirectory(Path.Combine(legacy,"inventario-file"));File.WriteAllText(Path.Combine(legacy,"inventario-file","broken.json"),"invalid");
    var oldStore=new InventoryStore(old);oldStore.SaveDocument("GEOLOGIA/POSIZIONI.JSON",new{value=3});oldStore.SaveDocument("snapshot:C:old",new{value=999});
    var settings=new ServerSettings{LocalStorageOnly=true,DatabaseFolder=Path.Combine(root,"new"),ArchiveFolder=Path.Combine(root,"runtime"),BackupFolder=Path.Combine(root,"backup"),LegacyCatalogFolder=legacy,LegacyDatabaseFolder=old};
    var path=Path.Combine(settings.ArchiveFolder,"geologia","posizioni.json");
    try{CatalogStorage.Exists(settings,path);throw new Exception("Consultazione prematura consentita");}catch(IOException){}
    LocalStorageTransition.Prepare(settings,CancellationToken.None,_=>{});
    var store=new InventoryStore(settings.InventoryFolder);
    Check(store.Manifests().Count==0 && store.Document("snapshot:C:old")==null,"Inventario precedente escluso");
    Check(store.Document("GEOLOGIA/POSIZIONI.JSON")=="{\"value\":3}","Versione SQL più recente preservata");
    Check(store.Document("IPRIPI/INDICE.JSON")=="{\"value\":2}","Analisi presenti solo nei JSON preservate");
    Check(File.ReadAllText(Path.Combine(legacy,"geologia","posizioni.json"))=="{\"value\":1}","Sorgenti precedenti inalterate");
    Check(File.Exists(Path.Combine(settings.BackupFolder,"prima-ricostruzione","inventario-precedente.sqlite")),"Backup precedente disponibile sulla destinazione separata");
    Check(File.Exists(Path.Combine(settings.BackupFolder,"prima-ricostruzione","cataloghi-recuperati.sqlite")),"Backup dei cataloghi recuperati disponibile");
    settings.LegacyCatalogFolder=Path.Combine(root,"non-disponibile");settings.LegacyDatabaseFolder=Path.Combine(root,"non-disponibile-db");
    LocalStorageTransition.Prepare(settings,CancellationToken.None,_=>throw new Exception("Recupero ripetuto"));
    Check(System.Text.Encoding.UTF8.GetString(CatalogStorage.Bytes(settings,path))=="{\"value\":3}","Riavvio e consultazione indipendenti dalla vecchia rete");
    var backup=DatabaseBackup.Save(settings,CancellationToken.None);
    Check(backup.Error==""&&backup.SuccessUtc!=null,"Backup eseguito senza scansione completata");
    Check(DatabaseBackup.Save(settings,CancellationToken.None).Path==backup.Path,"Una sola copia giornaliera");
    var before=Directory.GetFiles(settings.BackupFolder,"TA-Commesse-*.sqlite").Length;
    File.WriteAllText(Path.Combine(root,"blocked"),"not a folder");settings.BackupFolder=Path.Combine(root,"blocked","backup");
    var failed=DatabaseBackup.Save(settings,CancellationToken.None,true);
    Check(failed.Error.Length>0&&File.Exists(backup.Path),"Errore di rete conserva il backup precedente");
    Check(store.IntegrityCheck()=="ok","Database locale ancora integro");
}
finally{SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
