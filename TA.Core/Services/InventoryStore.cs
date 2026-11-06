using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Services;

// Database locale con una sola versione corrente; gli originali non vengono incorporati.
public sealed partial class InventoryStore
{
    public string Root { get; }
    public string DatabasePath => Path.Combine(Root,"inventario.sqlite");
    private static readonly ConcurrentDictionary<string,object> Gates=new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string,bool> Initialized=new(StringComparer.OrdinalIgnoreCase);
    private object Gate=>Gates.GetOrAdd(DatabasePath,_=>new());
    public InventoryStore(string folder)
    {
        Root=Path.GetFullPath(Path.Combine(folder,"inventario-file"));
        if(new Uri(Root).IsUnc || new DriveInfo(Path.GetPathRoot(Root)!).DriveType==DriveType.Network)
            throw new IOException("SQLite richiede un disco locale del server.");
        Directory.CreateDirectory(Root);
        lock(Gate) { if(Initialized.ContainsKey(DatabasePath)&&File.Exists(DatabasePath))return; using var db=Open(); Execute(db,null,"""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS units(id TEXT PRIMARY KEY,category TEXT NOT NULL,year TEXT NOT NULL,code TEXT NOT NULL,manifest TEXT NOT NULL,active INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS entries(unit_id TEXT NOT NULL REFERENCES units(id) ON DELETE CASCADE,key TEXT NOT NULL,
                location_id TEXT NOT NULL,name TEXT NOT NULL,extension TEXT NOT NULL,kind TEXT NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(unit_id,key));
            CREATE INDEX IF NOT EXISTS ix_units_filter ON units(category,year,code);
            CREATE INDEX IF NOT EXISTS ix_entries_extension ON entries(extension,unit_id);
            CREATE INDEX IF NOT EXISTS ix_entries_location ON entries(location_id);
            CREATE TABLE IF NOT EXISTS state(key TEXT PRIMARY KEY,payload TEXT NOT NULL);
            """); Initialized[DatabasePath]=true; }
    }
    private SqliteConnection Open()
    { var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=DatabasePath,DefaultTimeout=60,ForeignKeys=true}.ToString()); db.Open(); return db; }
    private static SqliteCommand Command(SqliteConnection db,SqliteTransaction? tx,string sql,params (string,object?)[] values)
    { var cmd=db.CreateCommand(); cmd.Transaction=tx; cmd.CommandText=sql; foreach(var(k,v) in values)cmd.Parameters.AddWithValue(k,v??DBNull.Value); return cmd; }
    private static void Bump(SqliteConnection db,SqliteTransaction tx)=>Execute(db,tx,"INSERT INTO state VALUES('revision','1') ON CONFLICT(key) DO UPDATE SET payload=CAST(state.payload AS INTEGER)+1");
    private static void Execute(SqliteConnection db,SqliteTransaction? tx,string sql,params (string,object?)[] values)
    { using var cmd=Command(db,tx,sql,values); cmd.ExecuteNonQuery(); }
    public string UnitDirectory(InventoryUnit unit)=>new LegacyInventoryStore(Path.GetDirectoryName(Root)!).UnitDirectory(unit);
    private static string Key(InventoryEntry e)=>JsonSerializer.Serialize(new{e.Container,e.RelativePath,e.Kind});
    public InventoryManifest? Find(string id)
    { using var db=Open(); using var cmd=Command(db,null,"SELECT manifest FROM units WHERE id=$id",("$id",id)); return cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<InventoryManifest>(json):null; }
    public List<InventoryManifest> Manifests()
    {
        using var db=Open(); using var cmd=Command(db,null,"SELECT manifest FROM units WHERE active=1 ORDER BY category,year DESC,code,id");
        using var reader=cmd.ExecuteReader(); var result=new List<InventoryManifest>();
        while(reader.Read())result.Add(JsonSerializer.Deserialize<InventoryManifest>(reader.GetString(0))!); return result;
    }
    public InventoryManifest Save(InventoryUnit unit,InventoryResult result,CancellationToken token)
    {
        _=UnitDirectory(unit); token.ThrowIfCancellationRequested();
        // Riserva prudenziale per crescita, journal e almeno una copia locale di backup.
        var estimated=result.Entries.Sum(e=>1024L+4L*(e.RelativePath.Length+(e.Container?.Length??0)));
        EnsureSpace(estimated*2);
        lock(Gate)
        {
            using var db=Open(); using var tx=db.BeginTransaction();
            InventoryManifest? previous;
            using(var cmd=Command(db,tx,"SELECT manifest FROM units WHERE id=$id",("$id",unit.Id)))
                previous=cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<InventoryManifest>(json):null;
            Execute(db,tx,"DROP TABLE IF EXISTS temp.incoming; CREATE TEMP TABLE incoming(key TEXT PRIMARY KEY)");
            Execute(db,tx,"INSERT OR IGNORE INTO units(id,category,year,code,manifest) VALUES($id,$cat,$year,$code,'{}')",("$id",unit.Id),("$cat",unit.Category),("$year",unit.Year),("$code",unit.Code));
            int added=0,changed=0;
            using var lookup=Command(db,tx,"SELECT payload FROM entries WHERE unit_id=$id AND key=$key",("$id",unit.Id),("$key",""));
            using var mark=Command(db,tx,"INSERT OR IGNORE INTO incoming VALUES($key)",("$key",""));
            using var upsert=Command(db,tx,"""
                INSERT INTO entries VALUES($id,$key,$loc,$name,$ext,$kind,$json)
                ON CONFLICT(unit_id,key) DO UPDATE SET location_id=excluded.location_id,name=excluded.name,extension=excluded.extension,kind=excluded.kind,payload=excluded.payload
                WHERE entries.payload<>excluded.payload
                """,("$id",unit.Id),("$key",""),("$loc",""),("$name",""),("$ext",""),("$kind",""),("$json",""));
            foreach(var raw in result.Entries)
            {
                token.ThrowIfCancellationRequested(); var e=raw with{Location=FileLocator.Locate(unit,raw)}; var key=Key(e);
                mark.Parameters["$key"].Value=key; if(mark.ExecuteNonQuery()==0)continue;
                lookup.Parameters["$key"].Value=key; var oldJson=lookup.ExecuteScalar() as string;
                if(oldJson==null)added++; else { var old=JsonSerializer.Deserialize<InventoryEntry>(oldJson)!; if(old.Bytes!=e.Bytes||old.ModifiedUtc!=e.ModifiedUtc)changed++; }
                upsert.Parameters["$key"].Value=key; upsert.Parameters["$loc"].Value=e.Location!.Id;
                upsert.Parameters["$name"].Value=e.Location.Name.ToUpperInvariant(); upsert.Parameters["$ext"].Value=e.Location.Extension.ToUpperInvariant();
                upsert.Parameters["$kind"].Value=e.Kind; upsert.Parameters["$json"].Value=JsonSerializer.Serialize(e); upsert.ExecuteNonQuery();
            }
            int removed=0;
            if(result.Complete){using var del=Command(db,tx,"DELETE FROM entries WHERE unit_id=$id AND key NOT IN(SELECT key FROM incoming)",("$id",unit.Id));removed=del.ExecuteNonQuery();}
            using var count=Command(db,tx,"SELECT COUNT(*) FROM entries WHERE unit_id=$id",("$id",unit.Id)); var total=Convert.ToInt32(count.ExecuteScalar());
            var generation=previous!=null && added+removed+changed==0 && previous.Complete==result.Complete && previous.Unit==unit?previous.Generation:Guid.NewGuid().ToString("N");
            var manifest=new InventoryManifest(unit,generation,DateTime.UtcNow,result.Complete,total,0,result.Issues.Take(200).ToList(),previous==null?0:added,previous==null?0:removed,previous==null?0:changed,previous==null);
            Execute(db,tx,"UPDATE units SET active=1,category=$cat,year=$year,code=$code,manifest=$json WHERE id=$id",("$cat",unit.Category),("$year",unit.Year),("$code",unit.Code),("$json",JsonSerializer.Serialize(manifest)),("$id",unit.Id));
            token.ThrowIfCancellationRequested();Bump(db,tx);tx.Commit();return manifest;
        }
    }
    public IEnumerable<InventoryEntry> Entries(InventoryManifest manifest)
    {
        using var db=Open(); using var tx=db.BeginTransaction(deferred:true);
        using(var cmd=Command(db,tx,"SELECT manifest FROM units WHERE id=$id",("$id",manifest.Unit.Id)))
            if(cmd.ExecuteScalar() is not string json || JsonSerializer.Deserialize<InventoryManifest>(json)!.Generation!=manifest.Generation)
                throw new IOException("Inventario aggiornato durante la consultazione: ripetere la ricerca.");
        using var read=Command(db,tx,"SELECT payload FROM entries WHERE unit_id=$id ORDER BY key",("$id",manifest.Unit.Id));
        using var reader=read.ExecuteReader();while(reader.Read())yield return JsonSerializer.Deserialize<InventoryEntry>(reader.GetString(0))!;
    }
    public InventoryRun? Latest()
    {
        using var db=Open();using var cmd=Command(db,null,"SELECT payload FROM state WHERE key='run'");
        var run=cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<InventoryRun>(json):null; var rows=Manifests();
        if(rows.Count==0)return run;
        return run==null?new(rows.Min(m=>m.CheckedUtc),rows.Max(m=>m.CheckedUtc),rows,[],["Prima scansione in corso: risultati parziali."])
            :run with{Inventories=rows,CompletedUtc=new[]{run.CompletedUtc,rows.Max(m=>m.CheckedUtc)}.Max()};
    }
    public void SaveRun(InventoryRun run)
    {
        lock(Gate)
        {
            using var db=Open();using var tx=db.BeginTransaction();
            if(run.Issues.Count==0)Execute(db,tx,"UPDATE units SET active=0");
            foreach(var m in run.Inventories)
                Execute(db,tx,"UPDATE units SET active=1,manifest=$json WHERE id=$id AND json_extract(manifest,'$.Generation')=$generation",("$json",JsonSerializer.Serialize(m)),("$id",m.Unit.Id),("$generation",m.Generation));
            Execute(db,tx,"INSERT INTO state VALUES('run',$json) ON CONFLICT(key) DO UPDATE SET payload=excluded.payload",("$json",JsonSerializer.Serialize(run with{Inventories=[]})));
            Bump(db,tx);tx.Commit();
        }
    }
    public void Backup(string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        EnsureSpace(0,Path.GetPathRoot(Path.GetFullPath(target)));
        using var source=Open(); using var destination=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=target,Pooling=false}.ToString());
        destination.Open();source.BackupDatabase(destination);
    }
    public string IntegrityCheck(){using var db=Open();using var cmd=Command(db,null,"PRAGMA integrity_check");return Convert.ToString(cmd.ExecuteScalar())!;}
    private void EnsureSpace(long growth,string? drive=null)
    {
        var free=new DriveInfo(drive??Path.GetPathRoot(DatabasePath)!).AvailableFreeSpace;
        var required=(File.Exists(DatabasePath)?new FileInfo(DatabasePath).Length:0)+growth+256L*1024*1024;
        if(free<required)throw new IOException($"Spazio insufficiente per database e backup: liberi {free/1024/1024:N0} MiB; riserva richiesta almeno {required/1024/1024:N0} MiB. Configurare un disco più capiente; dati precedenti conservati.");
    }
    public static T Read<T>(string path)=>LegacyInventoryStore.Read<T>(path);
}
