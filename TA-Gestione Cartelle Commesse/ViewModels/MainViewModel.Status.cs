using System.Windows.Threading;
using System.Collections.ObjectModel;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

public sealed partial class MainViewModel
{
    private bool readingProgress;
    private int statusTabIndex;
    private readonly Dictionary<string,string> lastLocations=new();
    public ObservableCollection<ScanLogEntry> ScanLog {get;}=[];
    public int StatusTabIndex {get=>statusTabIndex;set{statusTabIndex=value;Notify();}}
    private DispatcherTimer? progressTimer;
    private CancellationTokenSource? progressCancellation;
    private ScanProgress scanProgress = new(false, "Non collegato", 0, null, "Collegare il server per conoscere l'avanzamento");
    public Command ShowScanCommand { get; private set; } = null!;
    public Command ShowConnectionCommand { get; private set; } = null!;
    public string ProgressCaption => scanProgress.Phase + (scanProgress.IsRunning && scanProgress.Percentage is { } value ? $" — {value:0.#}% ({scanProgress.Completed}/{scanProgress.Total})" : "");
    public string ProgressDetail => scanProgress.Message;

    public string ProgressSchedule
    {
        get
        {
            static string Local(DateTime value) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value,DateTimeKind.Utc),TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")).ToString("dd/MM HH:mm");
            return (scanProgress.FinishedUtc is { } end ? "Ultima: "+Local(end)+" • " : "") +
                (scanProgress.NextRunUtc is { } next ? "Prossima: "+Local(next)+" (Italia)" : "Elaborazione completa all'avvio del server e alle 00:00 italiane");
        }
    }
    public void ApplyProgress(ScanProgress value)
    {

        scanProgress=value;
        if(value.IsRunning && value.Locations is {Count:>0})
            foreach(var location in value.Locations)AppendScanLog(location.Category,location.Phase,location.Path);
        else AppendScanLog("Servizio",value.Phase,value.Message);

        foreach(var property in new[]{nameof(ProgressCaption),nameof(ProgressDetail),nameof(ProgressSchedule)}) Notify(property);
    }
    private void AppendScanLog(string category,string phase,string path)
    {
        string key=phase+"|"+path;
        if(lastLocations.GetValueOrDefault(category)==key)return;
        lastLocations[category]=key;
        var label=category=="C"?"Commesse":category=="G"?"Gare":category=="A"?"Archivio":category;
        ScanLog.Insert(0,new(DateTime.Now.ToString("HH:mm:ss"),label,phase,path));
        while(ScanLog.Count>12)ScanLog.RemoveAt(ScanLog.Count-1);
    }
    private void StartProgressPolling(ServerConnection server)
    {
        StopProgressPolling(); lastLocations.Clear(); ScanLog.Clear(); progressCancellation=new();
        var token=progressCancellation.Token;
        progressTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};
        progressTimer.Tick+=async (_,_)=>await ReadProgress(server,token);
        progressTimer.Start(); _=ReadProgress(server,token);
    }
    private async Task ReadProgress(ServerConnection server,CancellationToken token)
    {
        if(readingProgress || token.IsCancellationRequested) return;
        readingProgress=true;
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var progress=await server.SendAsync<ScanProgress>(new("Progress"),timeout.Token);
            if(token.IsCancellationRequested || !ReferenceEquals(server,connection))return;
            bool finished=progress.FinishedUtc!=null && progress.FinishedUtc!=scanProgress.FinishedUtc;
            ApplyProgress(progress);
            if(progressTimer!=null) progressTimer.Interval=TimeSpan.FromSeconds(progress.IsRunning ? 3 : 15);
            // Aggiorna le viste al termine della scansione, senza avvisi Windows sulle differenze.
            if(finished)await PollAsync();
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception ex)
        {
            if(token.IsCancellationRequested || !ReferenceEquals(server,connection))return;
            ApplyProgress(new(false,"Stato non disponibile",0,null,ex.Message));
            if(progressTimer!=null) progressTimer.Interval=TimeSpan.FromSeconds(15);
            if(ex is System.Security.Authentication.AuthenticationException or ServerRejectedException) progressTimer?.Stop();
        }
        finally {readingProgress=false;}
    }
    private void StopProgressPolling(){progressTimer?.Stop();progressCancellation?.Cancel();progressCancellation?.Dispose();progressCancellation=null;}
}
