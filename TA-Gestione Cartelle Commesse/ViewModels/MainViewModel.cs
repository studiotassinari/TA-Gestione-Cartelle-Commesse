using System.IO;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Coordina solo presentazione e connessione; calcoli, archivio e report appartengono al servizio Windows.
public sealed class MainViewModel : Observable
{
    public CategoryViewModel Commesse { get; } = new("Commesse", 'C');
    public CategoryViewModel Gare { get; } = new("Gare", 'G'); public CategoryViewModel Archivio { get; } = new("Archivio", 'A');
    private int activeTabIndex;
    public int ActiveTabIndex { get => activeTabIndex; set { activeTabIndex = value; Notify(); Notify(nameof(ActiveCategory)); } }
    public CategoryViewModel ActiveCategory => ActiveTabIndex == 2 ? Archivio : ActiveTabIndex == 1 ? Gare : Commesse;
    private string host = "", port = "5443", reportStatus = "Configurare nome DNS del server e porta TCP dedicata.";
    private ServerConnection? connection;
    private bool reporting, initialized, connecting;
    private bool useCurrentAccount = true;
    public bool UseCurrentAccount { get => useCurrentAccount; set { useCurrentAccount = value; Notify(); } }
    private CancellationTokenSource? pushCancellation;
    public string ServerHost { get => host; set { host = value; Notify(); } }
    public string ServerPort { get => port; set { port = value; Notify(); } }
    public string ReportStatus { get => reportStatus; private set { reportStatus = value; Notify(); } }
    public string ArchiveLocation => "Archivio e scansioni gestiti dal servizio Windows; trasporto TCP/Kerberos con account di dominio.";
    public string CommunicationStatus { get => communicationStatus; private set { communicationStatus = value; Notify(); } }
    private string communicationStatus = "Comunicazione non ancora verificata.";
    public bool CanManageServer => DomainAdministration.IsEnabled;
    public Command ManageServerCommand { get; }
    public Command CheckCommunicationCommand { get; }
    public Command ChangesCommand { get; }
    public Command CreateJobCommand { get; }
    public Command UpdateCommand { get; }
    public Command AboutCommand { get; }
    public Command ConnectCommand { get; }
    public Command ReportCommand { get; }
    public MainViewModel()
    {
        ManageServerCommand = new(ShowServerManagement, () => CanManageServer); CheckCommunicationCommand = new(() => _ = CheckCommunicationAsync()); ChangesCommand = new(ShowChanges);
        CreateJobCommand = new(CreateJob, () => connection != null);
        UpdateCommand = new(() => TA.Desktop.UpdateLauncher.Check("Client", true));
        AboutCommand = new(() => _ = ShowAboutAsync());
        ConnectCommand = new(() => _ = ConnectAsync(), () => !connecting);
        ReportCommand = new(() => _ = CreateReport(), () => connection != null && !reporting);

        try { var settings = ClientSettings.Load(); host = settings.Host; port = settings.Port.ToString(); }
        catch (Exception ex) { reportStatus = "Configurazione non leggibile: " + ex.Message; }
    }
    public async Task InitializeAsync()
    {
        if (initialized) return; initialized = true;
        if (!string.IsNullOrWhiteSpace(host)) await ConnectAsync();
    }
    private async Task ConnectAsync()
    {
        if (Uri.CheckHostName(host.Trim()) != UriHostNameType.Dns || !int.TryParse(port, out var number) || number is < 1024 or > 65535 or 8080 or 8443)
        { ReportStatus = "Indicare il nome DNS del server (senza http/https) e una porta TCP dedicata, per esempio 5443."; return; }
        System.Net.NetworkCredential? credentials = null;
        if (!UseCurrentAccount)
        {
            var dialog = new Views.DomainLoginDialog { Owner = System.Windows.Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;
            credentials = dialog.Credential;
        }
        pushCancellation?.Cancel(); connecting = true; ConnectCommand.Refresh();
        ClientLog.Write($"Connessione a {host.Trim()}:{number}; account corrente={UseCurrentAccount}"); connection = new ServerConnection(host.Trim(), number, credentials);
        Commesse.Connect(connection); Gare.Connect(connection); Archivio.Connect(connection); ReportCommand.Refresh(); CreateJobCommand.Refresh();
        try { new ClientSettings(host.Trim(), number).Save(); }
        catch (Exception ex) { ReportStatus = "Impossibile salvare la connessione: " + ex.Message; }
        ReportStatus = "Connessione TCP/Kerberos a " + host.Trim() + ":" + number + "; autenticazione Windows aziendale.";
        try
        {
            await PollAsync();
            if (Commesse.LastConnectionError is not System.Security.Authentication.AuthenticationException and not ServerRejectedException && Gare.LastConnectionError is not System.Security.Authentication.AuthenticationException and not ServerRejectedException)
            {
                pushCancellation = new CancellationTokenSource();
                _ = WatchLoopAsync(connection, pushCancellation.Token);
            }
        }
        finally { connecting = false; ConnectCommand.Refresh(); }
    }
    // Dopo un errore non ripete credenziali errate: evita tentativi automatici fino al blocco dell'account AD.
    private async Task PollAsync()
    {
        await Commesse.PollAsync();
        await Gare.PollAsync(); await Archivio.PollAsync();
        if (Commesse.ConnectionFailed || Gare.ConnectionFailed || Archivio.ConnectionFailed)
        {
            ReportStatus = "Recupero dati non riuscito. Verifica della connessione in corso.";
        }
    }
    private async Task WatchLoopAsync(ServerConnection current, CancellationToken token)
    {
        // Errori di rete temporanei vengono ritentati anche al primo collegamento.
        int delay = 5;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await current.WatchAsync(notice => System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (token.IsCancellationRequested || !ReferenceEquals(current, connection)) return;
                    await PollAsync();
                    if (token.IsCancellationRequested || !ReferenceEquals(current, connection)) return;
                    if (Commesse.ConnectionFailed || Gare.ConnectionFailed || Archivio.ConnectionFailed)
                    {
                        var error = Commesse.LastConnectionError ?? Gare.LastConnectionError ?? Archivio.LastConnectionError;
                        if (error is System.Security.Authentication.AuthenticationException or ServerRejectedException)
                            throw new System.Security.Authentication.AuthenticationException("Richiesta rifiutata: " + error.Message);
                        throw new IOException("Recupero dati interrotto.", error);
                    }
                    ClientLog.Write($"Push ricevuto: revisione={notice.Revision}; dettaglio={notice.Summary}; righe C={Commesse.Records.Count}, G={Gare.Records.Count}"); ReportStatus = "Dati aggiornati dal server alle " + DateTime.Now.ToString("HH:mm:ss") + ". Notifiche push attive.";
                }).Task.Unwrap(), () => { delay = 5; ClientLog.Write("Sottoscrizione push accettata."); }, token, notice =>
                {
                    CommunicationStatus = $"Canale verificato alle {DateTime.Now:HH:mm:ss}: {notice.Kind}; " + (notice.ProbeId != null ? "riscontro inviato al server." : "server precedente senza riscontro.");
                    ClientLog.Write(CommunicationStatus);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (System.Security.Authentication.AuthenticationException ex)
            {
                if (!token.IsCancellationRequested && ReferenceEquals(current, connection))
                {
                    ClientLog.Write("Push rifiutato: controllo credenziali/gruppo richiesto.", ex); ReportStatus = "Push interrotto: " + ex.Message + " Premere Connetti dopo la verifica; nessun tentativo automatico.";
                    Commesse.Disconnected(); Gare.Disconnected(); Archivio.Disconnected();
                }
                return;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested || !ReferenceEquals(current, connection)) return;
                Commesse.Disconnected(); Gare.Disconnected(); Archivio.Disconnected();
                CommunicationStatus = "Canale interrotto; riconnessione in corso."; ClientLog.Write("Push interrotto; riconnessione automatica.", ex);
                ReportStatus = $"Connessione push interrotta; dati conservati. Riconnessione tra {delay} secondi.";
                try { await Task.Delay(TimeSpan.FromSeconds(delay), token); }
                catch (OperationCanceledException) { return; }
                delay = Math.Min(60, delay * 2);
            }
        }
    }
    public void Stop() { ClientLog.Write("Chiusura client."); pushCancellation?.Cancel(); }


    private void ShowServerManagement()
    {
        if (!CanManageServer) return;
        if (!UseCurrentAccount) { ReportStatus = "La gestione del servizio richiede l'account Windows corrente di un amministratore di dominio."; return; }
        if (Uri.CheckHostName(host.Trim()) != UriHostNameType.Dns || !int.TryParse(port, out var number)) { ReportStatus = "Configurare prima il server."; return; }
        var panel = new Views.ServerManagementView(host.Trim(), new ServerConnection(host.Trim(), number));
        var window = new System.Windows.Window { Title = "Gestione server — " + host.Trim(), Width = 1200, Height = 800, Content = panel, Owner = System.Windows.Application.Current.MainWindow };
        window.Closed += (_, _) => panel.Dispose(); window.Show();
    }
    private async Task CheckCommunicationAsync()
    {
        if (connection == null) { CommunicationStatus = "Collegare prima il server."; return; }
        var id = Guid.NewGuid().ToString("N");
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var reply = await connection.SendAsync<string>(new("Ping", CorrelationId: id));
            if (reply != id) throw new IOException("Riscontro della prova non corrispondente.");
            CommunicationStatus = $"Comando e risposta verificati in {elapsed.ElapsedMilliseconds} ms. Prova {id}.";
            ClientLog.Write(CommunicationStatus);
        }
        catch (Exception ex) { CommunicationStatus = "Verifica comunicazione fallita: " + ex.Message; ClientLog.Write(CommunicationStatus, ex); }
    }
    private void ShowChanges()
    {
        var tabs = new System.Windows.Controls.TabControl();
        foreach (var category in new[] { Commesse, Gare, Archivio })
        {
            var list = new System.Windows.Controls.ListBox { ItemsSource = category.ChangesLog };
            tabs.Items.Add(new System.Windows.Controls.TabItem { Header = category.Title, Content = list });
        }
        new System.Windows.Window { Title = "Modifiche ricevute - ultime 200 per categoria nella sessione", Width = 1000, Height = 520,
            Owner = System.Windows.Application.Current.MainWindow, Content = tabs, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner }.Show();
    }
    private void CreateJob()
    {
        if (connection == null) return;
        var dialog = new Views.CreateJobDialog(connection) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() == true)
            ReportStatus = "Commessa creata: " + dialog.CreatedPath + ". L'elenco si aggiornera al termine della verifica server.";
    }
    private async Task ShowAboutAsync()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Sconosciuta";
        var serverVersion = "Non collegato";
        if (connection != null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { serverVersion = await connection.SendAsync<string>(new("Info"), timeout.Token); }
            catch { serverVersion = "Non disponibile (server non raggiungibile o versione precedente)"; }
        }
        System.Windows.MessageBox.Show($"TA-Gestione Cartelle Commesse\n\nClient: {version}\nServer: {serverVersion}\nConnessione: {ServerHost}:{ServerPort}\n\nScansione server di commesse e gare ogni minuto, senza sovrapporre scansioni. Aggiornamento client tramite notifiche push.\n\nStudio Tassinari", "Informazioni", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }
    private async Task CreateReport()
    {
        if (connection == null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Report HTML (*.html)|*.html", FileName = "Report-cartelle-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".html" };
        if (dialog.ShowDialog() != true) return;
        reporting = true; ReportCommand.Refresh(); ReportStatus = "Richiesta report al server...";
        try
        {
            var html = await connection.SendAsync<string>(new("Report"));
            await File.WriteAllTextAsync(dialog.FileName, html, new System.Text.UTF8Encoding(false));
            ReportStatus = "Report salvato: " + dialog.FileName;
        }
        catch (Exception ex) { ReportStatus = "Report non creato: " + ex.Message; }
        finally { reporting = false; ReportCommand.Refresh(); }
    }
}
