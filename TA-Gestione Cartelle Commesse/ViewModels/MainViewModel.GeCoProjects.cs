using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Lavoro quotidiano sulle commesse di GeCo (2.42.0): «Commesse di GeCo» (le mie e tutte), scheda della commessa, richieste di
// accesso e, per tutto ciò che GeCa non porta nel programma (fatture, SAL, analisi, WIP, tabelle, raccordo utenti Windows), le
// pagine di GeCo2027 nel browser all'indirizzo configurato sul server. Elenco e richieste sono finestre a istanza singola; le
// schede una per commessa (la stessa commessa riattiva la sua scheda).
public sealed partial class MainViewModel
{
    public sealed record GeCoPageCommand(string Text, string Tip, Command Command);
    public Command GeCoProjectsCommand { get; private set; } = null!;
    public Command GeCoProjectCardCommand { get; private set; } = null!;
    public Command GeCoAccessRequestsCommand { get; private set; } = null!;
    public IReadOnlyList<GeCoPageCommand> GeCoPageCommands { get; private set; } = [];
    // Indirizzo del sito GeCo2027 configurato sul server (comando «Permissions»); vuoto = comandi «Apri in GeCo2027» spenti.
    private string geCoAddress = "";
    public string GeCoAddress => geCoAddress;
    private Views.GeCoProjectsWindow? projectsWindow;
    private Views.GeCoAccessRequestsWindow? requestsWindow;
    private readonly Dictionary<int, Views.GeCoProjectWindow> cardWindows = [];

    private void InitializeGeCoProjects()
    {
        GeCoProjectsCommand = new(ShowGeCoProjects, () => connection != null);
        GeCoProjectCardCommand = new(() => { if (ActiveCategory.SelectedProject is { Model.Category: "C" } row) ShowGeCoProject(0, row.Code); },
            () => connection != null && ActiveCategory.SelectedProject?.Model.Category == "C");
        foreach (var category in new[] { Commesse, Gare, Archivio }) category.PropertyChanged += (_, _) => GeCoProjectCardCommand.Refresh();
        GeCoAccessRequestsCommand = new(ShowGeCoAccessRequests, () => connection != null);
        GeCoPageCommands = GeCoPages.General.Select(p => new GeCoPageCommand(p.Testo, p.Spiegazione, new Command(() => OpenGeCoPage(p), () => geCoAddress.Length > 0))).ToList();
    }
    private void RefreshGeCoProjectCommands()
    {
        GeCoProjectsCommand.Refresh(); GeCoProjectCardCommand.Refresh(); GeCoAccessRequestsCommand.Refresh();
        foreach (var p in GeCoPageCommands) p.Command.Refresh();
    }
    private void SetGeCoAddress(string? address)
    {
        geCoAddress = (address ?? "").Trim(); Notify(nameof(GeCoAddress));
        foreach (var p in GeCoPageCommands) p.Command.Refresh();
    }
    private void OpenGeCoPage(GeCoPage page)
    {
        if (GeCoPages.Open(geCoAddress, page) is { } message)
            System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, message, "Apri in GeCo2027", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void ShowGeCoProjects()
    {
        if (connection == null) return;
        if (projectsWindow != null) { projectsWindow.Activate(); return; }
        projectsWindow = new(new GeCoProjectsViewModel(new ServerGeCoGateway(connection)), () => geCoAddress) { Owner = System.Windows.Application.Current.MainWindow };
        projectsWindow.ProjectRequested += id => ShowGeCoProject(id);
        projectsWindow.Closed += (_, _) => projectsWindow = null;
        projectsWindow.Show();
    }

    // Scheda per Id (elenchi, commesse collegate, richieste) o per codice della cartella selezionata nella finestra principale.
    public void ShowGeCoProject(int projectId, string? code = null)
    {
        if (connection == null || projectId <= 0 && string.IsNullOrWhiteSpace(code)) return;
        if (projectId > 0 && cardWindows.TryGetValue(projectId, out var opened)) { opened.Activate(); return; }
        var model = new ProjectCardViewModel(new ServerGeCoGateway(connection), projectId, code);
        var window = new Views.GeCoProjectWindow(model, () => geCoAddress) { Owner = System.Windows.Application.Current.MainWindow };
        window.ProjectRequested += id => ShowGeCoProject(id);
        int key = projectId;
        void Track() { if (key <= 0 && model.ProjectId > 0 && !cardWindows.ContainsKey(model.ProjectId)) { key = model.ProjectId; cardWindows[key] = window; } }
        model.Updated += Track;
        if (key > 0) cardWindows[key] = window;
        window.Closed += (_, _) => { model.Updated -= Track; if (key > 0 && cardWindows.TryGetValue(key, out var w) && ReferenceEquals(w, window)) cardWindows.Remove(key); };
        window.Show();
    }

    private void ShowGeCoAccessRequests()
    {
        if (connection == null) return;
        if (requestsWindow != null) { requestsWindow.Activate(); return; }
        requestsWindow = new(new AccessRequestsViewModel(new ServerGeCoGateway(connection))) { Owner = System.Windows.Application.Current.MainWindow };
        requestsWindow.ProjectRequested += id => ShowGeCoProject(id);
        requestsWindow.Closed += (_, _) => requestsWindow = null;
        requestsWindow.Show();
    }

    private void CloseGeCoProjectWindows()
    {
        projectsWindow?.Close(); requestsWindow?.Close();
        foreach (var w in cardWindows.Values.ToList()) w.Close();
        cardWindows.Clear();
    }
}
