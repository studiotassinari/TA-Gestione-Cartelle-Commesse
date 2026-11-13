using System.IO;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Coordina solo presentazione e connessione; calcoli, archivio e report appartengono al servizio Windows.
public sealed partial class MainViewModel : Observable
{
    public CategoryViewModel Commesse { get; } = new("Commesse", 'C');
    public CategoryViewModel Gare { get; } = new("Gare", 'G'); public CategoryViewModel Archivio { get; } = new("Archivio", 'A');
    private int activeTabIndex;
    public int ActiveTabIndex { get => activeTabIndex; set { activeTabIndex = value; Notify(); Notify(nameof(ActiveCategory)); ArchiveFolderCommand?.Refresh(); } }
    public CategoryViewModel ActiveCategory => ActiveTabIndex == 2 ? Archivio : ActiveTabIndex == 1 ? Gare : Commesse;
    private string host = "", port = "5443", reportStatus = "Configurare nome DNS del server e porta TCP dedicata.";
    private ServerConnection? connection;
    private bool reporting, initialized, connecting;
    private bool useCurrentAccount = true;
    // Nome dell'account alternativo ricordato tra un avvio e l'altro; la password è in SavedCredential.
    private string accountName = "";
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
    public Command CreateBidCommand { get; }
    public Command UpdateCommand { get; }
    public Command AboutCommand { get; }
    public Command ConnectCommand { get; }
    public Command DailyReportCommand { get; }
    public Command AnalysisReportCommand { get; }
    public Command ReportCommand { get; }
    public Command NonconformityReportCommand {get;}
    public MainViewModel()
    {
        ArchiveFolderCommand = new(() => ShowFolderArchive(ActiveCategory.SelectedProject), () => connection != null && CanManageServer && ActiveCategory.SelectedProject?.Model.Category is "C" or "G");
        foreach (var category in new[] { Commesse, Gare, Archivio }) category.PropertyChanged += (_, _) => ArchiveFolderCommand.Refresh();
        Commesse.ArchiveRequested += ShowFolderArchive; Gare.ArchiveRequested += ShowFolderArchive;
        BusinessCommand = new(ShowBusiness, () => connection != null);
        CurriculumCommand = new(() => ShowCurriculum(), () => connection != null);
        foreach (var category in new[] { Commesse, Gare, Archivio }) category.CurriculumRequested += project => ShowCurriculum(project);
        CustomersCommand = new(() => ShowCustomers(""), () => connection != null);
        Commesse.ProjectRequested += ShowProject;
        Commesse.CustomerRequested += p => ShowCustomers(p.CustomerId);
        Gare.ProjectRequested += ShowProject; Archivio.ProjectRequested += ShowProject;
        Gare.CustomerRequested += p => ShowCustomers(p.CustomerId);
        Archivio.CustomerRequested += p => ShowCustomers(p.CustomerId);
        GeologyCommand = new(ShowGeology);
        IpripiCommand = new(ShowIpripi);
        ShowScanCommand=new(()=>StatusTabIndex=0); ShowConnectionCommand=new(()=>StatusTabIndex=1);
        NonconformityReportCommand=new(ShowNonconformityReport,()=>connection!=null);
        ManageServerCommand = new(ShowServerManagement, () => CanManageServer); CheckCommunicationCommand = new(() => _ = CheckCommunicationAsync()); ChangesCommand = new(ShowChanges);
        CreateJobCommand = new(() => Create(Commesse), () => connection != null);
        CreateBidCommand = new(() => Create(Gare), () => connection != null);
        Commesse.CreateRequested += Create; Gare.CreateRequested += Create;
        UpdateCommand = new(() => TA.Desktop.UpdateLauncher.Check("Client", true));
        AboutCommand = new(() => _ = ShowAboutAsync());
        ConnectCommand = new(() => _ = ConnectAsync(), () => !connecting);
        AnalysisReportCommand = new(() => _ = CreateReport(analysis: true), () => connection != null && !reporting);
        DailyReportCommand = new(() => _ = CreateReport(true), () => connection != null && !reporting);
        ReportCommand = new(() => _ = CreateReport(), () => connection != null && !reporting);

        try { var settings = ClientSettings.Load(); host = settings.Host; port = settings.Port.ToString(); useCurrentAccount = settings.UseCurrentAccount; accountName = settings.AccountName; }
        catch (Exception ex) { reportStatus = "Configurazione non leggibile: " + ex.Message; }
        statusTabIndex=string.IsNullOrWhiteSpace(host)?1:0;
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
        System.Net.NetworkCredential? credentials = null; bool storedCredential = false;
        if (!UseCurrentAccount)
        {
            // Una password ricordata evita la finestra di accesso; se manca o non è leggibile si chiede di nuovo.
            try { credentials = SavedCredential.Read(host); storedCredential = credentials != null; }
            catch (System.ComponentModel.Win32Exception ex) { ClientLog.Write("Credenziale memorizzata non leggibile: " + ex.Message); }
            if (credentials == null)
            {
                var dialog = new Views.DomainLoginDialog(accountName) { Owner = System.Windows.Application.Current.MainWindow };
                if (dialog.ShowDialog() != true) return;
                credentials = dialog.Credential; accountName = dialog.AccountName;
                try { if (dialog.Remember && credentials != null) SavedCredential.Save(host, credentials); else SavedCredential.Delete(host); }
                catch (System.ComponentModel.Win32Exception ex) { ReportStatus = "Impossibile ricordare la password: " + ex.Message; }
            }
        }
        archiveWindow?.Close(); archiveSessions.Clear();
        // I permessi appartengono alla connessione precedente: si ripartono da zero.
        ApplyManagementAccess(false);
        CloseProjectWindows(); businessWindow?.Close(); curriculumWindow?.Close(); ipripiWindow?.Close(); geologyWindow?.Close(); nonconformityWindow?.Close(); StopProgressPolling(); pushCancellation?.Cancel(); connecting = true; ConnectCommand.Refresh();
        ClientLog.Write($"Connessione a {host.Trim()}:{number}; account corrente={UseCurrentAccount}"); connection = new ServerConnection(host.Trim(), number, credentials);
        Commesse.Connect(connection); Gare.Connect(connection); Archivio.Connect(connection); ReportCommand.Refresh(); DailyReportCommand.Refresh(); AnalysisReportCommand.Refresh(); CreateJobCommand.Refresh(); BusinessCommand.Refresh(); CurriculumCommand.Refresh(); NonconformityReportCommand.Refresh();
        CustomersCommand.Refresh(); ArchiveFolderCommand.Refresh();
        try { new ClientSettings(host.Trim(), number, UseCurrentAccount, accountName).Save(); }
        catch (Exception ex) { ReportStatus = "Impossibile salvare la connessione: " + ex.Message; }
        ReportStatus = "Connessione TCP/Kerberos a " + host.Trim() + ":" + number + "; autenticazione Windows aziendale.";
        try
        {
            await PollAsync();
            // Password cambiata in AD: la credenziale ricordata viene eliminata subito, così non viene ritentata
            // a ogni avvio fino al blocco dell'account. Il nome utente resta per il nuovo accesso.
            if (storedCredential && (Commesse.LastConnectionError ?? Gare.LastConnectionError) is System.Security.Authentication.AuthenticationException)
            {
                try { SavedCredential.Delete(host); } catch (System.ComponentModel.Win32Exception) { }
                ReportStatus = "La password ricordata per " + accountName + " non è più valida ed è stata eliminata: premere Connetti e inserirla di nuovo.";
            }
            if (Commesse.LastConnectionError is not System.Security.Authentication.AuthenticationException and not ServerRejectedException && Gare.LastConnectionError is not System.Security.Authentication.AuthenticationException and not ServerRejectedException)
            {
                await RefreshManagementAccessAsync(connection);
                StartProgressPolling(connection);
                pushCancellation = new CancellationTokenSource();
                _ = WatchLoopAsync(connection, pushCancellation.Token);
            }
        }
        finally { connecting = false; ConnectCommand.Refresh(); }
    }
    // Chiede al server se l'account autenticato è tra gli utenti di gestione straordinaria configurati.
    private async Task RefreshManagementAccessAsync(ServerConnection current)
    {
        bool allowed = false;
        try { allowed = (await current.SendAsync<TA_Gestione_Cartelle_Commesse.Contracts.ServerPermissions>(new("Permissions"))).DomainAdministrator; }
        catch (Exception ex) { ClientLog.Write("Permessi di gestione non disponibili", ex); }
        if (ReferenceEquals(current, connection)) ApplyManagementAccess(allowed);
    }
    private void ApplyManagementAccess(bool allowed)
    {
        DomainAdministration.Set(allowed); Notify(nameof(CanManageServer)); ManageServerCommand.Refresh(); ArchiveFolderCommand.Refresh();
        // Le righe già visibili rivalutano il comando del menu contestuale.
        foreach (var category in new[] { Commesse, Gare, Archivio })
            foreach (var row in category.Projects) row.ArchiveFolderCommand.Refresh();
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
                    ClientLog.Write($"Push ricevuto: revisione={notice.Revision}; dettaglio={notice.Summary}; righe C={Commesse.Records.Count}, G={Gare.Records.Count}"); ReportStatus = "Dati aggiornati dal server alle " + DateTime.Now.ToString("HH:mm:ss") + ". Differenze disponibili nel report; notifiche sospese.";
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
    public void Stop() { CloseProjectWindows(); businessWindow?.Close(); curriculumWindow?.Close(); ipripiWindow?.Close(); geologyWindow?.Close(); StopProgressPolling(); ClientLog.Write("Chiusura client."); pushCancellation?.Cancel(); }


    private Views.NonconformityReportWindow? nonconformityWindow;
    private void ShowNonconformityReport()
    {
        if(connection==null)return;
        if(nonconformityWindow!=null){nonconformityWindow.Activate();return;}
        nonconformityWindow=new(new NonconformityReportViewModel(connection)){Owner=System.Windows.Application.Current.MainWindow};
        nonconformityWindow.Closed+=(_,_)=>nonconformityWindow=null;
        nonconformityWindow.Show();
    }
    private void ShowServerManagement()
    {
        if (!CanManageServer) return;
        if (!UseCurrentAccount) { ReportStatus = "La gestione del servizio richiede l'accesso con il proprio account Windows, non con un account alternativo."; return; }
        if (Uri.CheckHostName(host.Trim()) != UriHostNameType.Dns || !int.TryParse(port, out var number)) { ReportStatus = "Configurare prima il server."; return; }
        var panel = new Views.ServerManagementView(host.Trim(), new ServerConnection(host.Trim(), number));
        var window = new System.Windows.Window { Title = "Gestione server — " + host.Trim(), Width = 1200, Height = 800, Content = panel, Owner = System.Windows.Application.Current.MainWindow };
        window.Closed += (_, _) => panel.Dispose(); window.Show();
    }
    private async Task CheckCommunicationAsync()
    {
        StatusTabIndex=1;
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
    private void Create(CategoryViewModel category)
    {
        if (connection == null || !category.CanCreate) return;
        var dialog = new Views.CreateJobDialog(connection, category.CategoryCode, category.HighestNumber(DateTime.Today.ToString("yy"))) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() == true)
            ReportStatus = (category.CategoryCode == "G" ? "Gara creata: " : "Commessa creata: ") + dialog.CreatedPath + ". L'elenco si aggiornera al termine della verifica server.";
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
        System.Windows.MessageBox.Show($"TA-Gestione Cartelle Commesse\n\nClient: {version}\nServer: {serverVersion}\nConnessione: {ServerHost}:{ServerPort}\n\nScansione completa all'avvio del server e alle 00:00 italiane: commesse, gare e archivio, nomi dei file e voci ZIP/7z. Log delle cartelle correnti nelle schede inferiori, senza scansioni sovrapposte. Differenze nel report, notifiche sospese.\n\nStudio Tassinari", "Informazioni", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }
    private async Task CreateReport(bool daily = false, bool analysis = false)
    {
        if (connection == null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Report HTML (*.html)|*.html", FileName = "Report-cartelle-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".html" };
        if (dialog.ShowDialog() != true) return;
        reporting = true; ReportCommand.Refresh(); DailyReportCommand.Refresh(); AnalysisReportCommand.Refresh(); ReportStatus = "Richiesta report al server...";
        try
        {
            var html = await connection.SendAsync<string>(new(analysis ? "AnalysisReport" : daily ? "DailyReport" : "Report"));
            await File.WriteAllTextAsync(dialog.FileName, html, new System.Text.UTF8Encoding(false));
            ReportStatus = "Report salvato: " + dialog.FileName;
        }
        catch (Exception ex) { StatusTabIndex=3; ReportStatus = "Report non creato: " + ex.Message; }
        finally { reporting = false; ReportCommand.Refresh(); DailyReportCommand.Refresh(); AnalysisReportCommand.Refresh(); }
    }
}
