using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

public sealed record DatabaseBackupStatus(DateTime AttemptUtc,DateTime? SuccessUtc,string Path,string Error);
public static class DatabaseBackup
{
    public static DatabaseBackupStatus? Status(ServerSettings settings)
    {
        var json=new InventoryStore(settings.InventoryFolder).Document("backup:status");
        return json==null?null:JsonSerializer.Deserialize<DatabaseBackupStatus>(json);
    }
    public static DatabaseBackupStatus Save(ServerSettings settings,CancellationToken token,bool force=false)
    {
        _=GeologyStorage.Root(settings);
        var store=new InventoryStore(settings.InventoryFolder);var previous=Status(settings);
        var now=DateTime.UtcNow;
        if(!force && previous?.SuccessUtc is DateTime last && DailyReports.ItalianTime(last).Date==DailyReports.ItalianTime(now).Date)return previous;
        var local=Path.Combine(settings.InventoryFolder,"backup-in-progress.sqlite");
        var folder=Path.GetFullPath(Path.Combine(settings.ArchiveFolder,"backup-database"));
        var target=Path.Combine(folder,"TA-Commesse-"+now.ToString("yyyyMMddTHHmmssfffffff")+".sqlite");
        var temporary=target+".tmp";
        try
        {
            token.ThrowIfCancellationRequested();store.Backup(local);Verify(local);token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(folder);File.Copy(local,temporary,false);
            using(var a=File.OpenRead(local))using(var b=File.OpenRead(temporary))
                if(!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b)))throw new IOException("La copia del backup sulla rete non coincide con l'originale.");
            token.ThrowIfCancellationRequested();File.Move(temporary,target);
            var status=new DatabaseBackupStatus(now,DateTime.UtcNow,target,"");store.SaveDocument("backup:status",status);
            // Si eliminano solo backup generati dall'app, dopo il completamento del nuovo backup.
            foreach(var old in Directory.EnumerateFiles(folder).Where(p=>Regex.IsMatch(Path.GetFileName(p),@"^TA-Commesse-\d{8}T\d{13}\.sqlite$"))
                .OrderByDescending(Path.GetFileName,StringComparer.Ordinal).Skip(Math.Clamp(settings.BackupRetention,1,30)))
            {
                token.ThrowIfCancellationRequested();
                if(!Path.GetFullPath(old).StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Percorso backup inatteso");
                if((File.GetAttributes(old)&FileAttributes.ReparsePoint)==0)File.Delete(old);
            }
            return status;
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            var status=new DatabaseBackupStatus(now,previous?.SuccessUtc,previous?.Path??"",ex.Message);
            store.SaveDocument("backup:status",status);return status;
        }
        finally
        {
            if(File.Exists(temporary))File.Delete(temporary);
            if(File.Exists(local))File.Delete(local);
        }
    }
    public static void Verify(string path)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        db.Open();using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA integrity_check";
        if(Convert.ToString(cmd.ExecuteScalar())!="ok")throw new IOException("Verifica di integrità del backup fallita");
    }
}
