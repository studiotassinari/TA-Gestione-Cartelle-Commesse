using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace TA.ServiceManager;
// I controlli di servizio e i log vengono letti fuori dal thread dell'interfaccia.
public sealed class ManagerWindow : Window
{
    private readonly TextBlock status = new() { FontSize = 18, Margin = new Thickness(0, 8, 0, 12) };
    private readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas") };
    private readonly List<Button> actions = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string history = "", lastLog = "";
    private bool busy, polling;
    private readonly TA.Desktop.TrayLifetime tray;
    private static string ServerFolder => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    public ManagerWindow()
    {
        tray = new TA.Desktop.TrayLifetime(this, "TA - Gestore server", "Server");
        Title = "TA - Gestione servizio e diagnostica"; Width = 1000; Height = 720; MinWidth = 750; MinHeight = 500;
        Background = new SolidColorBrush(Color.FromRgb(241, 246, 246));
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "TASSINARI | Gestione del server", FontSize = 25, Foreground = new SolidColorBrush(Color.FromRgb(0, 95, 99)) });
        top.Children.Add(status);
        top.Children.Add(new TextBlock { Text = "Avvio e arresto del servizio Windows. Il registro sotto mostra le fasi e gli errori; nessuna percentuale stimata.", TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) }; top.Children.Add(buttons);
        Add(buttons, "Avvia", () => Operate(true)); Add(buttons, "Arresta", () => Operate(false));
        Add(buttons, "Diagnosi", Diagnose);
        Add(buttons, "Configura", () => LaunchConfiguration());
        Add(buttons, "Account del servizio", () => Open("services.msc"));
        Add(buttons, "Esporta registro", Export);
        Add(buttons, "Aggiornamenti", () => { TA.Desktop.UpdateLauncher.Check("Server", true); return Task.CompletedTask; });
        root.Children.Add(output);
        timer.Tick += async (_, _) => await Refresh();
        Loaded += async (_, _) => { TA.Desktop.UpdateLauncher.Check("Server"); Note("Utilita pronta. Configurare account e gruppo prima dell'avvio."); await Refresh(); timer.Start(); };
        Closed += (_, _) => timer.Stop();
    }
    private void Add(Panel panel, string label, Func<Task> action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(12, 7, 12, 7) };
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { Note("ERRORE: " + ex.Message); MessageBox.Show(this, ex.Message, "Operazione non riuscita", MessageBoxButton.OK, MessageBoxImage.Error); } };
        actions.Add(button); panel.Children.Add(button);
    }
    private void Note(string message) { history += $"{DateTime.Now:HH:mm:ss} {message}\n"; Render(); }
    private void Render() { output.Text = history + "\n--- Registro del servizio (ultimi 64 KB) ---\n" + lastLog; output.ScrollToEnd(); }
    private async Task Refresh()
    {
        if (polling) return; polling = true;
        try
        {
            var result = await Task.Run(() =>
            {
                string state;
                try { using var service = new ServiceController("TACommesseServer"); state = service.Status switch { ServiceControllerStatus.Running => "In esecuzione", ServiceControllerStatus.Stopped => "Arrestato", ServiceControllerStatus.StartPending => "Avvio in corso", ServiceControllerStatus.StopPending => "Arresto in corso", var other => other.ToString() }; }
                catch (Exception ex) { state = "Servizio non disponibile: " + ex.GetBaseException().Message; }
                string log; try { log = ServiceDiagnostics.Tail(); } catch (Exception ex) { log = "Registro non leggibile: " + ex.Message; }
                return (state, log);
            });
            status.Text = result.state;
            if (lastLog != result.log)
            {
                var added = result.log.StartsWith(lastLog) ? result.log[lastLog.Length..] : result.log;
                if (lastLog.Length > 0 && (added.Contains("Evento filesystem ricevuto") || added.Contains("Warning") || added.Contains("ERRORE FATALE")))
                    tray.Notify("Novita dal server", "Rilevate modifiche alle cartelle o segnalazioni da verificare. Apri il gestore per i dettagli.");
                lastLog = result.log; Render();
            }
        }
        finally { polling = false; }
    }
    private async Task Operate(bool start)
    {
        if (busy) return;
        if (!start && MessageBox.Show(this, "Arrestare il servizio? Le scansioni vengono annullate e i client si disconnettono.", "Arresto servizio", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        busy = true; actions.ForEach(b => b.IsEnabled = false);
        Note(start ? "Richiesta di avvio inviata a Windows..." : "Richiesta di arresto inviata a Windows...");
        try
        {
            await Task.Run(() =>
            {
                using var service = new ServiceController("TACommesseServer");
                var target = start ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped;
                if (service.Status == target) return;
                if (start && service.Status != ServiceControllerStatus.StartPending) service.Start();
                if (!start && service.Status != ServiceControllerStatus.StopPending) service.Stop();
                service.WaitForStatus(target, TimeSpan.FromSeconds(60));
            });
            Note(start ? "Windows segnala il servizio in esecuzione. Verificare nel registro il messaggio TCP pronto." : "Servizio arrestato.");
        }
        catch (Exception ex)
        {
            Note("Operazione fallita: " + ex.Message + " | " + ex.GetBaseException().Message);
            Note(await Task.Run(ServiceDiagnostics.Inspect));
            MessageBox.Show(this, "Il servizio non ha completato l'operazione. Gli errori sono nel registro; usa Esporta registro per condividerli.", "Diagnostica servizio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { busy = false; actions.ForEach(b => b.IsEnabled = true); await Refresh(); }
    }
    private async Task Diagnose() { Note("Lettura configurazione e registri Windows..."); Note(await Task.Run(ServiceDiagnostics.Inspect)); }
    private Task Open(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return Task.CompletedTask; }
    private Task LaunchConfiguration()
    {
        var script = Path.Combine(ServerFolder, "Configure-Server.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("Aprire l'utilita dalla cartella installata del server.", script);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false };
        foreach (var argument in new[] { "-NoProfile", "-NoExit", "-ExecutionPolicy", "Bypass", "-File", script, "-OpenDomainFirewall" }) start.ArgumentList.Add(argument);
        Process.Start(start); Note("Configurazione aperta: indicare il gruppo AD. La console mostra eventuali errori."); return Task.CompletedTask;
    }
    private async Task Export()
    {
        await Diagnose();
        await Refresh();
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Registro testo|*.txt", FileName = "Diagnosi-TACommesse-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, output.Text);
    }
}
