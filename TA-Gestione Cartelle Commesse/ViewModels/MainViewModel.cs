using System.IO;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Coordina solo presentazione e connessione; calcoli, archivio e report appartengono al servizio Windows.
public sealed class MainViewModel : Observable
{
    public CategoryViewModel Commesse { get; } = new("Commesse", 'C');
    public CategoryViewModel Gare { get; } = new("Gare", 'G');
    private int activeTabIndex;
    public int ActiveTabIndex { get => activeTabIndex; set { activeTabIndex = value; Notify(); Notify(nameof(ActiveCategory)); } }
    public CategoryViewModel ActiveCategory => ActiveTabIndex == 1 ? Gare : Commesse;
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
    public Command CreateJobCommand { get; }
    public Command UpdateCommand { get; }
    public Command AboutCommand { get; }
    public Command ConnectCommand { get; }
    public Command ReportCommand { get; }
    public MainViewModel()
    {
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
        pushCancellation?.Cancel();
        System.Net.NetworkCredential? credentials = null;
        if (!UseCurrentAccount)
        {
            var dialog = new Views.DomainLoginDialog { Owner = System.Windows.Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;
            credentials = dialog.Credential;
        }
        connecting = true; ConnectCommand.Refresh();
        connection = new ServerConnection(host.Trim(), number, credentials);
        Commesse.Connect(connection); Gare.Connect(connection); ReportCommand.Refresh(); CreateJobCommand.Refresh();
        try { new ClientSettings(host.Trim(), number).Save(); }
        catch (Exception ex) { ReportStatus = "Impossibile salvare la connessione: " + ex.Message; }
        ReportStatus = "Connessione TCP/Kerberos a " + host.Trim() + ":" + number + "; autenticazione Windows aziendale.";
        try
        {
            await PollAsync();
            if (!Commesse.ConnectionFailed && !Gare.ConnectionFailed)
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
        if (!Commesse.ConnectionFailed) await Gare.PollAsync();
        if (Commesse.ConnectionFailed || Gare.ConnectionFailed)
        {
            ReportStatus = "Connessione interrotta. Verificare server e credenziali, poi premere Connetti. Nessun nuovo tentativo automatico.";
        }
    }
    private async Task WatchLoopAsync(ServerConnection current, CancellationToken token)
    {
        bool established = false;
        int delay = 5;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await current.WatchAsync(async () =>
                {
                    if (token.IsCancellationRequested || !ReferenceEquals(current, connection)) return;
                    await PollAsync();
                    if (token.IsCancellationRequested || !ReferenceEquals(current, connection)) return;
                    if (Commesse.ConnectionFailed || Gare.ConnectionFailed)
                    {
                        var error = Commesse.LastConnectionError ?? Gare.LastConnectionError;
                        if (error is System.Security.Authentication.AuthenticationException or ServerRejectedException)
                            throw new System.Security.Authentication.AuthenticationException("Richiesta rifiutata: " + error.Message);
                        throw new IOException("Recupero dati interrotto.", error);
                    }
                    ReportStatus = "Dati aggiornati dal server alle " + DateTime.Now.ToString("HH:mm:ss") + ". Notifiche push attive.";
                }, () => { established = true; delay = 5; }, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (System.Security.Authentication.AuthenticationException ex)
            {
                if (!token.IsCancellationRequested && ReferenceEquals(current, connection))
                {
                    ReportStatus = "Push interrotto: " + ex.Message + " Premere Connetti dopo la verifica; nessun tentativo automatico.";
                    Commesse.Disconnected(); Gare.Disconnected();
                }
                return;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested || !ReferenceEquals(current, connection)) return;
                Commesse.Disconnected(); Gare.Disconnected();
                if (!established) { ReportStatus = "Push non disponibile: " + ex.Message + " Verificare il server e premere Connetti."; return; }
                ReportStatus = $"Connessione push interrotta; dati conservati. Riconnessione tra {delay} secondi.";
                try { await Task.Delay(TimeSpan.FromSeconds(delay), token); }
                catch (OperationCanceledException) { return; }
                delay = Math.Min(60, delay * 2);
            }
        }
    }
    public void Stop() => pushCancellation?.Cancel();


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
