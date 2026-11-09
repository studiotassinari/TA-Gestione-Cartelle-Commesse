namespace TA.Server;

// Due lettori indipendenti dell'inventario SQLite: nessuna lettura documentale o trasmissione MCP.
public sealed class CatalogRefreshService(ScanCoordinator scans,GeologyIndexer geology,IpripiIndexer mur,ILogger<CatalogRefreshService> logger):BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken token)=>Task.WhenAll(
        Run("Geologia",()=>geology.Refresh(token),token),Run("MUR/IPRiPI",()=>mur.Refresh(token),token));
    private async Task Run(string name,Action refresh,CancellationToken token)
    {
        try
        {
            while(!scans.InventoryReady)await Task.Delay(TimeSpan.FromSeconds(2),token);
            DateTime next=DateTime.MinValue, retryAfter=DateTime.MinValue;
            while(!token.IsCancellationRequested)
            {
                if(DateTime.UtcNow>=retryAfter&&(DateTime.UtcNow>=next||scans.Progress.IsRunning))
                {
                    try{await Task.Run(refresh,token);next=DailyScanSchedule.Next(DateTime.UtcNow);}
                    catch(Exception ex) when(ex is not OperationCanceledException)
                    {logger.LogWarning(ex,"Catalogo {Name}: aggiornamento non completato",name);retryAfter=DateTime.UtcNow.AddMinutes(5);}
                }
                await Task.Delay(TimeSpan.FromMinutes(1),token);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
    }
}
