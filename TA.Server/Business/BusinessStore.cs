using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace TA.Server.Business;

// Archivio gestionale distinto dall'indice ricostruibile; nessuna scansione lo cancella.
public sealed class BusinessStore
{
    private readonly object gate=new();
    public string Path {get;}
    public BusinessStore(string folder)
    {
        var full=System.IO.Path.GetFullPath(folder);
        if(new Uri(full).IsUnc || new DriveInfo(System.IO.Path.GetPathRoot(full)!).DriveType==DriveType.Network)throw new IOException("Il gestionale richiede un disco locale.");
        Directory.CreateDirectory(full);Path=System.IO.Path.Combine(full,"gestionale.sqlite");
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS records(kind TEXT NOT NULL,id TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(kind,id));
            CREATE TABLE IF NOT EXISTS audit(id INTEGER PRIMARY KEY,at TEXT NOT NULL,actor TEXT NOT NULL,action TEXT NOT NULL,record_id TEXT NOT NULL,before_json TEXT,after_json TEXT);
            CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);
            """;cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open(){var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path,DefaultTimeout=30}.ToString());db.Open();return db;}
    public T Locked<T>(Func<T> action){lock(gate)return action();}
    public T[] All<T>(string kind)
    {
        lock(gate){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT payload FROM records WHERE kind=$kind ORDER BY id";cmd.Parameters.AddWithValue("$kind",kind);
        using var reader=cmd.ExecuteReader();var rows=new List<T>();while(reader.Read())rows.Add(JsonSerializer.Deserialize<T>(reader.GetString(0))!);return rows.ToArray();}
    }
    public T? Find<T>(string kind,string id) where T:class
    {
        if(id==null)throw new ArgumentException("Identificativo mancante.");
        lock(gate){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT payload FROM records WHERE kind=$k AND id=$id";
        cmd.Parameters.AddWithValue("$k",kind);cmd.Parameters.AddWithValue("$id",id);return cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<T>(json):null;}
    }
    public long ChangeNumber {get{lock(gate){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT COALESCE(MAX(id),0) FROM audit";return Convert.ToInt64(cmd.ExecuteScalar());}}}
    public Dictionary<string,DateTime> ProjectActivity()
    {
        lock(gate)
        {
            using var db=Open();using var cmd=db.CreateCommand();
            cmd.CommandText="""
                SELECT COALESCE(json_extract(after_json,'$.ProjectId'),json_extract(before_json,'$.ProjectId'),
                    CASE WHEN action IN ('ProjectDetails','ProjectAuthorization') THEN record_id END) AS project_id,MAX(at)
                FROM audit WHERE action IN ('ProjectDetails','ProjectAuthorization','Phase','Activity','TimeEntry','Delete')
                    AND actor <> 'Migrazione fasi'
                GROUP BY project_id HAVING project_id IS NOT NULL
                """;
            using var reader=cmd.ExecuteReader();var result=new Dictionary<string,DateTime>();
            while(reader.Read())result[reader.GetString(0)]=DateTime.Parse(reader.GetString(1),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind);
            return result;
        }
    }
    public void Save<T>(string kind,string id,int expected,T value,string actor,string action)
    {
        lock(gate)
        {
            using var db=Open();using var tx=db.BeginTransaction();using var cmd=db.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="SELECT revision,payload FROM records WHERE kind=$k AND id=$id";cmd.Parameters.AddWithValue("$k",kind);cmd.Parameters.AddWithValue("$id",id);
            int revision=0;string? before=null;using(var r=cmd.ExecuteReader()){if(r.Read()){revision=r.GetInt32(0);before=r.GetString(1);}}
            if(revision!=expected || expected>0 && before==null)throw new IOException("Dati modificati da un altro utente: aggiornare e riprovare.");
            var json=JsonSerializer.Serialize(value);
            cmd.CommandText="INSERT INTO records VALUES($k,$id,$revision,$json) ON CONFLICT(kind,id) DO UPDATE SET revision=excluded.revision,payload=excluded.payload";
            cmd.Parameters.AddWithValue("$revision",revision+1);cmd.Parameters.AddWithValue("$json",json);cmd.ExecuteNonQuery();
            cmd.CommandText="INSERT INTO audit(at,actor,action,record_id,before_json,after_json) VALUES($at,$actor,$action,$id,$before,$json)";
            cmd.Parameters.AddWithValue("$at",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$actor",actor);cmd.Parameters.AddWithValue("$action",action);cmd.Parameters.AddWithValue("$before",(object?)before??DBNull.Value);cmd.ExecuteNonQuery();tx.Commit();
        }
    }
    public void Delete(string kind,string id,int expected,string actor)
    {
        lock(gate){using var db=Open();using var tx=db.BeginTransaction();using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="INSERT INTO audit(at,actor,action,record_id,before_json) SELECT $at,$actor,'Delete',$id,payload FROM records WHERE kind=$k AND id=$id AND revision=$r";
        cmd.Parameters.AddWithValue("$at",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$actor",actor);cmd.Parameters.AddWithValue("$k",kind);cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$r",expected);
        if(cmd.ExecuteNonQuery()!=1)throw new IOException("Riga cambiata o assente: aggiornare.");cmd.CommandText="DELETE FROM records WHERE kind=$k AND id=$id AND revision=$r";cmd.ExecuteNonQuery();tx.Commit();}
    }
    public string BackupStatus {get;set;}="Backup gestionale in attesa.";
    public void Backup(string target)
    {
        lock(gate){using var source=Open();using var copy=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=target,Pooling=false}.ToString());copy.Open();source.BackupDatabase(copy);
        using var check=copy.CreateCommand();check.CommandText="PRAGMA integrity_check";if(check.ExecuteScalar()?.ToString()!="ok")throw new IOException("Backup gestionale non valido.");}
    }
}
