using System.IO;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TA.Transport;
using TA.ServiceManager;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Il controllo usa SCM Windows per il servizio e il canale autenticato per i registri.
public sealed class ServerManagementView : UserControl, IDisposable
{
    private readonly string host;
    private readonly ServerConnection connection;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly ServiceLogView service = new();
    private readonly CommunicationLogView communication = new();
    private readonly List<Button> buttons = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool polling, operating, disposed;
    private string communicationText = "";
    public ServerManagementView(string host, ServerConnection connection)
    {
        this.host = host; this.connection = connection;
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        Add(actions, "Avvia", () => Operate("Start"));
        Add(actions, "Arresta", () => Operate("Stop"));
        Add(actions, "Riavvia", () => Operate("Restart"));
        Add(actions, "Aggiorna diagnosi", Refresh);
        Add(actions, "Esporta registri", Export);
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) }); grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(service); Grid.SetRow(communication, 2); grid.Children.Add(communication);
        var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetRow(splitter, 1); grid.Children.Add(splitter);
        root.Children.Add(grid);
        timer.Tick += async (_, _) => await Refresh();
        Loaded += async (_, _) => { await Refresh(); if (!disposed) timer.Start(); };
        Unloaded += (_, _) => Dispose();
    }
    private void Add(Panel panel, string label, Func<Task> action)
    {
        var button = new Button { Content = label, Padding = new Thickness(10), Margin = new Thickness(5) };
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception ex) { status.Text = ex.Message; ClientLog.Write("Gestione server: " + label, ex); }
        };
        buttons.Add(button); panel.Children.Add(button);
    }
    private static void RequireAdministrator()
    {
        if (!DomainAdministration.IsEnabled) throw new UnauthorizedAccessException("Gestione riservata al token Windows di un amministratore di dominio. Se necessario, avviare il client come amministratore.");
    }
    private async Task Refresh()
    {
        if (polling || operating || disposed) return;
        polling = true;
        try
        {
            RequireAdministrator();
            var state = await Task.Run(() => { using var controller = new ServiceController("TACommesseServer", host); return controller.Status; });
            status.Text = $"Server: {host} — servizio: {state}. Account Windows corrente; controllo autorizzazioni eseguito anche da Windows sul server.";
            if (state != ServiceControllerStatus.Running) return;
            var permissions = await connection.SendAsync<ServerPermissions>(new("Permissions"));
            if (!permissions.DomainAdministrator) throw new UnauthorizedAccessException("Il server non riconosce l'account come amministratore del proprio dominio.");
            var logs = await connection.SendAsync<ServerLogs>(new("AdminLogs"));
            if (disposed) return;
            service.Output.Text = logs.Service; service.Output.ScrollToEnd();
            communicationText = logs.Communications; communication.Update(communicationText);
        }
        catch (Exception ex)
        {
            status.Text = "Gestione remota non disponibile: " + ex.Message + " Verificare autorizzazioni e gestione remota servizi Windows (RPC/SCM) sulla VM.";
            ClientLog.Write("Diagnosi server remota non disponibile", ex);
        }
        finally { polling = false; }
    }
    private async Task Operate(string operation)
    {
        if (operating || polling) return;
        RequireAdministrator();
        if (operation != "Start" && MessageBox.Show("L'operazione interrompe temporaneamente scansioni e connessioni. Proseguire?", "Servizio " + host, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        operating = true; buttons.ForEach(b => b.IsEnabled = false);
        try
        {
            ClientLog.Write($"Amministrazione remota: PC={Environment.MachineName}, utente={System.Security.Principal.WindowsIdentity.GetCurrent().Name}, server={host}, azione={operation}");
            if (operation != "Start") await connection.SendAsync<bool>(new("AdminAudit", RelativePath: operation));
            await Task.Run(() =>
            {
                RequireAdministrator();
                using var controller = new ServiceController("TACommesseServer", host);
                if (operation != "Start" && controller.Status != ServiceControllerStatus.Stopped)
                { controller.Stop(); controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60)); }
                if (operation != "Stop")
                {
                    controller.Refresh();
                    if (controller.Status != ServiceControllerStatus.Running)
                    { controller.Start(); controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60)); }
                }
            });
            status.Text = "Operazione completata: " + operation;
            ClientLog.Write("Amministrazione remota completata: " + operation);
            if (operation != "Stop")
            {
                try { await connection.SendAsync<bool>(new("AdminAudit", RelativePath: operation)); }
                catch (Exception ex) { ClientLog.Write("Servizio avviato; canale diagnostico non ancora pronto", ex); }
            }
        }
        finally { operating = false; buttons.ForEach(b => b.IsEnabled = true); await Refresh(); }
    }
    private Task Export()
    {
        RequireAdministrator();
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Registro testo|*.txt", FileName = "Diagnosi-remota-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" };
        if (dialog.ShowDialog() == true) File.WriteAllText(dialog.FileName, status.Text + "\n--- Servizio ---\n" + service.Output.Text + "\n--- Comunicazioni ---\n" + communicationText);
        return Task.CompletedTask;
    }
    public void Dispose() { disposed = true; timer.Stop(); }
}
