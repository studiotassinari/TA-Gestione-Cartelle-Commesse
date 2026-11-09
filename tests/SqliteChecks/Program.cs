using TA.Server;
using Microsoft.Data.Sqlite;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
var root=Path.Combine(Path.GetTempPath(),"TA-Sqlite-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
 var retentionNow=new DateTime(2026,11,9,12,0,0,DateTimeKind.Utc);
 var daily=Enumerable.Range(0,8).Select(i=>"gestionale-"+retentionNow.AddDays(-i).ToString("yyyyMMdd")+".sqlite").ToArray();
 var obsolete=BackupRetentionPolicy.Obsolete(daily,7,retentionNow,false);
 Check(obsolete.Length==3&&obsolete.Contains("gestionale-20261104.sqlite")&&!obsolete.Contains("gestionale-20261105.sqlite"),"vecchia configurazione 7 limitata a cinque giorni incluso oggi");
 Check(BackupRetentionPolicy.Obsolete([daily[0],daily[7]],5,retentionNow,false).SequenceEqual([daily[7]]),"backup scaduti rimossi anche con giorni mancanti");
 var inventoryDays=Enumerable.Range(0,8).Select(i=>"TA-Commesse-"+retentionNow.AddDays(-i).ToString("yyyyMMdd'T'HHmmssfffffff")+".sqlite").ToArray();
 Check(BackupRetentionPolicy.Obsolete(inventoryDays,30,retentionNow,true).Length==3,"stesso limite per backup inventario");
 Check(BackupRetentionPolicy.Obsolete(["TA-Commesse-20261104T2330000000000.sqlite"],5,retentionNow,true).Length==0,"confine giorno inventario convertito in ora italiana");
 var store=new InventoryStore(Path.Combine(root,"db"));
 var unit=new InventoryUnit(new string('A',64),"C","2026","C26001","Cliente","Progetto",Path.Combine(root,"source"),"censita");
 var stamp=DateTime.UtcNow;
 var entries=Enumerable.Range(0,100000).Select(n=>new InventoryEntry($"cartella-{n%100}/File-{n:D6}.pdf","file",Bytes:100,ModifiedUtc:stamp)).ToList();
 var first=store.Save(unit,new(entries,[],true),default);
 var second=store.Save(unit,new(entries,[],true),default);
 Check(first.Generation==second.Generation && store.Entries(second).Count()==100000,"100.000 file, una versione corrente e nessuna duplicazione per scansioni invariate");
 Check(!Directory.EnumerateFiles(store.Root,"*.json",SearchOption.AllDirectories).Any(),"nessuna pagina JSON generata");
 Check(store.PhysicalMatches(second,new(entries,[],true)),"metadati invariati riconosciuti");
 entries[0]=entries[0] with{ModifiedUtc=stamp.AddSeconds(1)};
 Check(!store.PhysicalMatches(second,new(entries,[],true)),"modifica rilevata anche con dimensioni e numero file invariati");
 var updated=store.Save(unit,new(entries,[],true),default);
 Check(updated.Changed==1 && updated.EntryCount==100000,"aggiornata soltanto la voce modificata");
 using(var cancelled=new CancellationTokenSource())
 {cancelled.CancelAfter(10);try{store.Save(unit,new(entries,[],true),cancelled.Token);throw new Exception("Annullamento ignorato");}catch(OperationCanceledException){} }
 Check(store.Find(unit.Id)!.Generation==updated.Generation && store.Entries(updated).Count()==100000,"annullamento durante una transazione conserva l'inventario precedente");
 await Task.WhenAll(Enumerable.Range(1,4).Select(n=>Task.Run(()=>store.Save(unit with{Id=new string((char)('A'+n),64),Code="C2600"+n},new(entries.Take(1000).ToList(),[],true),default))));
 Check(store.Manifests().Count==5 && store.IntegrityCheck()=="ok","scritture concorrenti serializzate senza perdita di unità");
 store.SaveRun(new(stamp,DateTime.UtcNow,store.Manifests(),[],[]));
 var page=store.Search(new(Name:"File-",Extension:"pdf",Code:"C26001",Limit:100),default);
 Check(page.Total==100000 && page.Files.Count==100 && page.NextOffset==100,"ricerca SQL con totale e pagina limitata su 100.000 file");
 var backup=new InventoryStore(Path.Combine(root,"backup"));store.Backup(backup.DatabasePath);
 Check(backup.IntegrityCheck()=="ok" && backup.Search(new(Code:"C26001"),default).Total==100000,"backup coerente e ripristinabile del database aperto");
 var backupSettings=new ServerSettings{DatabaseFolder=Path.Combine(root,"small-db"),ArchiveFolder=Path.Combine(root,"network-backup"),BackupRetention=2};
 new InventoryStore(backupSettings.InventoryFolder).SaveDocument("example",new{Count=10});
 for(int i=0;i<3;i++)Check(DatabaseBackup.Save(backupSettings,default,true).Error=="","backup verificato sulla destinazione");
 Check(Directory.GetFiles(Path.Combine(backupSettings.ArchiveFolder,"backup-database"),"*.sqlite").Length==2,"conservazione limitata dei backup");
 var lastBackup=DatabaseBackup.Status(backupSettings)!;
 var blocked=Path.Combine(root,"not-a-directory");File.WriteAllText(blocked,"fixture");backupSettings.ArchiveFolder=blocked;
 var failure=DatabaseBackup.Save(backupSettings,default,true);
 Check(failure.Error!="" && failure.SuccessUtc==lastBackup.SuccessUtc && File.Exists(lastBackup.Path),"backup fallito conserva la precedente copia valida e segnala l'errore");
 var source=Path.Combine(root,"legacy");var legacy=new LegacyInventoryStore(source);
 legacy.Save(unit,new(entries.Take(2).ToList(),[],true),default);
 var latest=legacy.Save(unit,new(entries.Take(3).ToList(),[],true),default);
 File.WriteAllText(Path.Combine(legacy.UnitDirectory(unit),"non-eliminare.txt"),"file estraneo");
 var migrated=new InventoryStore(Path.Combine(root,"migrated"));InventoryMigration.Import(source,migrated,default);
 Check(migrated.Entries(migrated.Find(unit.Id)!).Count()==3,"migrazione importa solo la generazione corrente");
 migrated.Backup(Path.Combine(root,"migration-backup.sqlite"));
 Check(InventoryMigration.CleanMigrated(source,migrated,default)>0 && File.Exists(Path.Combine(legacy.UnitDirectory(unit),"non-eliminare.txt")),"pulizia mirata rimuove lo storico verificato e preserva file estranei");
 Check(migrated.IntegrityCheck()=="ok" && migrated.Entries(migrated.Find(unit.Id)!).Count()==3,"database consultabile dopo la pulizia dei JSON");
 var duplicatesSource=Path.Combine(root,"duplicates");var duplicates=new LegacyInventoryStore(duplicatesSource);
 duplicates.Save(unit,new([entries[0],entries[0],entries[1]],[],true),default);
 var normalized=new InventoryStore(Path.Combine(root,"normalized"));
 InventoryMigration.Import(duplicatesSource,normalized,default);
 Check(normalized.Find(unit.Id)!.EntryCount==2 && normalized.Document("migration:"+unit.Id)!=null,"duplicati identici migrati e verificati come voci uniche");
 var conflictingSource=Path.Combine(root,"conflicting");var conflicting=new LegacyInventoryStore(conflictingSource);
 conflicting.Save(unit,new([entries[0],entries[0] with{Bytes=999}],[],true),default);
 var rejected=new InventoryStore(Path.Combine(root,"rejected"));
 try {InventoryMigration.Import(conflictingSource,rejected,default);throw new Exception("Conflitto ignorato");}
 catch(IOException ex) when(ex.Message.Contains("discordanti")) { }
 Check(rejected.Document("migration:"+unit.Id)==null && File.Exists(Path.Combine(conflicting.UnitDirectory(unit),"latest.json")),"duplicati discordanti rifiutati senza autorizzare la pulizia");
}
finally
{
 SqliteConnection.ClearAllPools();
 if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("TA-Sqlite-"))Directory.Delete(root,true);
}
