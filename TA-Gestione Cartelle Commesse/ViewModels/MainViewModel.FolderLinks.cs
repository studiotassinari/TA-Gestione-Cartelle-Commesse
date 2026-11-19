using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
// Collegamento cartelle e notifiche di GeCo2027 (dalla 2.38.0): finestra a istanza singola come le altre di GeCo. Il servizio
// avvisa con le notifiche push «GeCo»; le notifiche vere e proprie si chiedono con il comando, filtrate per l'utente.
public sealed partial class MainViewModel
{
    public Command GeCoFolderLinksCommand { get; private set; } = null!;
    private Views.GeCoFolderLinksWindow? folderLinksWindow;
    private FolderLinksViewModel? folderLinks;
    private long geCoRevision;
    private void ShowGeCoFolderLinks()
    {
        if (connection == null) return;
        if (folderLinksWindow != null) { folderLinksWindow.Activate(); return; }
        folderLinks = new FolderLinksViewModel(new ServerGeCoGateway(connection));
        folderLinksWindow = new(folderLinks) { Owner = System.Windows.Application.Current.MainWindow };
        folderLinksWindow.Closed += (_, _) => { folderLinksWindow = null; folderLinks = null; };
        folderLinksWindow.Show();
    }
    // Chiamata dal canale push prima del riscontro: torna subito e lascia il lavoro alla finestra, sul thread dell'interfaccia.
    // La notifica si ripete a ogni riconnessione con lo stesso ultimo evento: conta solo quando l'ultimo evento cambia.
    private Task GeCoEventsArrived(PushNotice notice)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            bool first = geCoRevision == 0, fresh = notice.Revision > geCoRevision;
            geCoRevision = Math.Max(geCoRevision, notice.Revision);
            if (alignment is { } aligned) await aligned.PushedAsync(notice.Revision);
            if (folderLinks is { } open) { await open.PushedAsync(notice.Revision); return; }
            if (fresh && !first && !string.IsNullOrWhiteSpace(notice.Summary))
            {
                ClientLog.Write("Eventi di GeCo2027: " + notice.Summary + "; ultimo=" + notice.Revision);
                ReportStatus = $"{notice.Summary} alle {DateTime.Now:HH:mm:ss}: si vedono in Gestione > Collegamento cartelle.";
            }
        });
        return Task.CompletedTask;
    }
}
