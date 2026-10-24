using System.Windows;
using Forms = System.Windows.Forms;
namespace TA.Desktop;
// Una sola icona per finestra: chiudere nasconde, Esci termina esplicitamente l'interfaccia.
public sealed class TrayLifetime : IDisposable
{
    private readonly Window window;
    private readonly Forms.NotifyIcon icon;
    private bool exit;
    private DateTime lastNotice;
    private (string Title, string Message)? pending;
    private readonly System.Windows.Threading.DispatcherTimer notices = new() { Interval = TimeSpan.FromSeconds(10) };
    public TrayLifetime(Window window, string title, string component)
    {
        this.window = window; notices.Tick += (_, _) => { notices.Stop(); if (pending is { } next) { pending = null; Notify(next.Title, next.Message); } };
        icon = new Forms.NotifyIcon { Text = title, Icon = System.Drawing.SystemIcons.Application, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Apri", null, (_, _) => Open());
        menu.Items.Add(component == "Server" ? "Esci dal gestore (servizio attivo)" : "Esci", null, (_, _) => { exit = true; Application.Current.Shutdown(); });
        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => Open();
        icon.BalloonTipClicked += (_, _) => Open();
        window.StateChanged += (_, _) => { if (window.WindowState == WindowState.Minimized) window.Hide(); };
        window.Closing += (_, args) =>
        {
            if (exit) return;
            var marker = UpdateLauncher.ExitMarker(component);
            if (System.IO.File.Exists(marker) && System.IO.File.GetLastWriteTimeUtc(marker) > DateTime.UtcNow.AddMinutes(-5))
            { System.IO.File.Delete(marker); exit = true; return; }
            args.Cancel = true; window.Hide();
            Notify("Applicazione ancora attiva", "Usa l'icona nell'area di notifica per riaprire la finestra o scegliere Esci.");
        };
        Application.Current.SessionEnding += (_, _) => exit = true;
        window.Closed += (_, _) => Dispose();
    }
    private void Open() { window.Show(); window.WindowState = WindowState.Normal; window.Activate(); }
    public void Notify(string title, string message)
    {
        // Non disturba con una notifica per ogni file o per ogni scansione periodica.
        if (DateTime.UtcNow - lastNotice < TimeSpan.FromSeconds(10)) { pending = (title, message); notices.Start(); return; }
        lastNotice = DateTime.UtcNow;
        icon.ShowBalloonTip(6000, title, message.Length > 240 ? message[..240] : message, Forms.ToolTipIcon.Info);
    }
    public void Dispose() { notices.Stop(); icon.Visible = false; icon.Dispose(); }
}
