using System.Security.Cryptography;
namespace TA.Server.Business;

// Backup indipendente dalla scansione degli originali: controlla le modifiche ogni cinque minuti.
public sealed class BusinessBackupService(BusinessService business,ServerSettings settings,ILogger<BusinessBackupService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try {await Task.Run(()=>Save(business.Store,settings),token);}
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception ex){business.Store.BackupStatus="Backup gestionale non riuscito: "+ex.Message;logger.LogWarning(ex,"Backup gestionale non completato; copia precedente conservata");}
            try{await Task.Delay(TimeSpan.FromMinutes(5),token);}catch(OperationCanceledException){break;}
        }
    }
    private long lastRevision=-1;private DateOnly lastDay;
    public void Save(BusinessStore store,ServerSettings configuration)
    {
        var revision=store.ChangeNumber;var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow,"W. Europe Standard Time"));
        if(revision==lastRevision&&day==lastDay)return;
        var folder=Path.Combine(configuration.InventoryFolder,"Temp","BackupGestionale");Directory.CreateDirectory(folder);
        var local=Path.Combine(folder,Guid.NewGuid().ToString("N")+".sqlite");
        var targetFolder=configuration.LocalStorageOnly ? Path.Combine(configuration.BackupDirectory,"gestionale") : Path.Combine(configuration.ArchiveFolder,"backup-gestionale");Directory.CreateDirectory(targetFolder);
        var target=Path.Combine(targetFolder,"gestionale-"+day.ToString("yyyyMMdd")+".sqlite");var temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            store.Backup(local);File.Copy(local,temp,false);
            using(var a=File.OpenRead(local))using(var b=File.OpenRead(temp))
                if(!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b)))throw new IOException("Copia di rete non verificata.");
            File.Move(temp,target,true);lastRevision=revision;lastDay=day;
            store.BackupStatus="Backup gestionale verificato: "+TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow,"W. Europe Standard Time").ToString("dd/MM/yyyy HH:mm");
            foreach(var file in Directory.EnumerateFiles(targetFolder,"gestionale-*.sqlite").Where(p=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),"^gestionale-[0-9]{8}\\.sqlite$")).OrderDescending().Skip(Math.Clamp(configuration.BackupRetention,1,30)))
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)File.Delete(file);
        }
        finally {if(File.Exists(local))File.Delete(local);if(File.Exists(temp))File.Delete(temp);}
    }
}
