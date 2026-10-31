using System.Windows.Threading;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

public sealed partial class MainViewModel
{
    private bool diagnosticsOpen, readingProgress;
    private DispatcherTimer? progressTimer;
    private CancellationTokenSource? progressCancellation;
    private ScanProgress scanProgress = new(false, "Non collegato", 0, null, "Collegare il server per conoscere l'avanzamento");
    public bool DiagnosticsOpen { get => diagnosticsOpen; set { diagnosticsOpen=value; Notify(); } }
    public Command ToggleDiagnosticsCommand { get; private set; } = null!;
    public string ProgressCaption => scanProgress.Phase + (scanProgress.IsRunning && scanProgress.Percentage is { } value ? $" — {value:0.#}% ({scanProgress.Completed}/{scanProgress.Total})" : "");
    public string ProgressDetail => scanProgress.Message;
    public double ProgressValue => scanProgress.Percentage ?? 0;
    public bool ProgressIndeterminate => scanProgress.IsRunning && scanProgress.Percentage == null;
    public string ProgressSchedule
    {
        get
        {
            static string Local(DateTime value) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value,DateTimeKind.Utc),TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")).ToString("dd/MM HH:mm");
            return (scanProgress.FinishedUtc is { } end ? "Ultima: "+Local(end)+" • " : "") +
                (scanProgress.NextRunUtc is { } next ? "Prossima: "+Local(next)+" (Italia)" : "Elaborazione completa ogni ora");
        }
    }
    public void ApplyProgress(ScanProgress value)
    {
        scanProgress=value;
        foreach(var property in new[]{nameof(ProgressCaption),nameof(ProgressDetail),nameof(ProgressValue),nameof(ProgressIndeterminate),nameof(ProgressSchedule)}) Notify(property);
    }
    private void StartProgressPolling(ServerConnection server)
    {
        StopProgressPolling(); progressCancellation=new();
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
