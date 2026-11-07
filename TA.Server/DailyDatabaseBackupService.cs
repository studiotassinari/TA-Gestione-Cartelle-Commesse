using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

// Esegue il backup anche se una scansione fallisce o richiede più di un giorno.
public sealed class DailyDatabaseBackupService(ServerSettings settings, ILogger<DailyDatabaseBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if(!settings.LocalStorageOnly)return;
        while(!token.IsCancellationRequested)
        {
            try
            {
                var store=new InventoryStore(settings.InventoryFolder);
                if(store.Document(LocalStorageTransition.Marker)!=null)
                {
                    var result=await Task.Run(()=>DatabaseBackup.Save(settings,token),token);
                    if(result.Error.Length>0)logger.LogWarning("Backup giornaliero non completato: {Error}",result.Error);
                }
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogWarning(ex,"Backup giornaliero non completato; copia precedente conservata");}
            try{await Task.Delay(TimeSpan.FromMinutes(5),token);}catch(OperationCanceledException){break;}
        }
    }
}
