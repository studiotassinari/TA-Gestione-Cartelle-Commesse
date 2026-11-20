using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using static TA_Gestione_Cartelle_Commesse.Views.GeCoUi;
namespace TA_Gestione_Cartelle_Commesse.Views;

// «Commesse di GeCo» (2.42.0), come la scheda Commesse della pagina iniziale di GeCo2027: «Le mie commesse» e «Tutte», ricerca,
// team e cartella; il doppio clic apre la scheda della commessa. Le commesse che non sono dell'utente si aprono in sola lettura
// (con «Richiedi accesso»); le cartelle senza commessa restano visibili in «Tutte» ma non hanno una scheda di GeCo.
public sealed class GeCoProjectsWindow : Window
{
    private readonly GeCoProjectsViewModel vm;
    private readonly Func<string?> address;
    private readonly StackPanel messages = new();
    private readonly TextBlock summary = Caption("", 12, false, Grey);
    private readonly TextBox search = new() { MinWidth = 300, MinHeight = 32, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 6), ToolTip = "Cerca per codice, anno, numero, committente, argomento o cartella (tutte le parole)" };
    private readonly RadioButton mine = new() { Content = "Le mie commesse", IsChecked = true, Margin = new Thickness(0, 6, 14, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly RadioButton all = new() { Content = "Tutte", Margin = new Thickness(0, 6, 14, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid grid = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderThickness = new Thickness(0) };
    private readonly Button openCard, openWeb;
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly Action<string> showRefreshed;
    // La scheda la apre la finestra principale, che ha il collegamento al servizio.
    public event Action<int>? ProjectRequested;

    public sealed record Item(GeCoProjectRow Row, string Codice, string Anno, string Committente, string Argomento, string Stato, string Team, string Cartella, string Mia);

    public GeCoProjectsWindow(GeCoProjectsViewModel model, Func<string?> geCoAddress)
    {
        vm = model; address = geCoAddress; Prepare(this, "Commesse di GeCo — TA");
        var root = new DockPanel { Margin = new Thickness(18) };
        var head = new StackPanel();
        head.Children.Add(Caption("Commesse di GeCo", 20, true, Teal));
        head.Children.Add(Caption("Le stesse commesse della pagina iniziale di GeCo2027: le tue (capocommessa, manager, responsabile di controllo, team, accesso concesso) e tutte quelle che puoi vedere, con il team e la cartella. Doppio clic per aprire la scheda.", 12, false, Grey));
        head.Children.Add(summary.With(t => t.Margin = new Thickness(0, 6, 0, 8)));
        head.Children.Add(messages);
        var tools = new WrapPanel();
        tools.Children.Add(mine); tools.Children.Add(all); tools.Children.Add(search);
        tools.Children.Add(Push("Aggiorna", async () => await vm.LoadAsync()));
        head.Children.Add(tools);
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

        var bar = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        openCard = Push("Apri scheda…", Open, true, "Scheda della commessa: dati generali, fasi, team, cartella, accesso");
        openWeb = Push("Apri in GeCo2027", () => { if (Selected is { IdCommessa: int id }) Warn(GeCoPages.Open(address(), GeCoPages.OfProject[0], id)); },
            tip: "Dettaglio della commessa nel browser, con fatture, importi e le altre schede di GeCo2027");
        var close = Push("Chiudi", Close); close.IsCancel = true;
        bar.Children.Add(openCard); bar.Children.Add(openWeb); bar.Children.Add(close);
        var bottom = new StackPanel(); bottom.Children.Add(bar); bottom.Children.Add(Refreshed(out showRefreshed));
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);

        foreach (var (label, path, width) in new[] { ("Codice", "Codice", 110.0), ("Anno", "Anno", 60), ("Committente", "Committente", 220), ("Argomento", "Argomento", 320),
                     ("Stato", "Stato", 110), ("Team", "Team", 240), ("Cartella", "Cartella", 280), ("Tua perché", "Mia", 200) })
            grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path), Width = new DataGridLength(width) });
        grid.SelectionChanged += (_, _) => Buttons();
        grid.MouseDoubleClick += (_, _) => Open();
        root.Children.Add(Card(grid, new Thickness(0)));
        Content = root;

        search.TextChanged += (_, _) => { searchTimer.Stop(); searchTimer.Start(); };
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); vm.Search = search.Text; await vm.LoadAsync(); };
        mine.Checked += async (_, _) => { vm.Mine = true; await vm.LoadAsync(); };
        all.Checked += async (_, _) => { vm.Mine = false; await vm.LoadAsync(); };
        vm.Updated += Render; vm.Live.Refreshed += showRefreshed;
        Loaded += async (_, _) => await vm.LoadAsync();
        Closed += (_, _) => { searchTimer.Stop(); vm.Updated -= Render; vm.Live.Refreshed -= showRefreshed; vm.Dispose(); };
        Render();
    }

    private GeCoProjectRow? Selected => (grid.SelectedItem as Item)?.Row;
    private void Open() { if (Selected is { IdCommessa: int id }) ProjectRequested?.Invoke(id); }
    private void Warn(string? message) { if (message != null) MessageBox.Show(this, message, "Apri in GeCo2027", MessageBoxButton.OK, MessageBoxImage.Information); }

    public static IEnumerable<Item> Items(GeCoProjectList? list) => (list?.Righe ?? []).Select(r => new Item(r, r.Codice, r.Anno?.ToString() ?? "", r.Committente ?? "", r.Argomento ?? "",
        r.Stato ?? "", GeCoProjectsViewModel.Team(r), GeCoProjectsViewModel.Folder(r), r.IdCommessa is null ? "" : r.Attribuita ? r.Attribuzione ?? "" : "sola lettura"));

    private void Render()
    {
        messages.Children.Clear();
        if (vm.Error.Length > 0) messages.Children.Add(Notice(vm.Error, "danger"));
        if (vm.Busy && vm.List == null) messages.Children.Add(Notice("Caricamento delle commesse…", "info"));
        summary.Text = vm.Summary;
        var keep = (grid.SelectedItem as Item)?.Row.Chiave;
        var items = Items(vm.List).ToList();
        grid.ItemsSource = items;
        grid.SelectedItem = items.FirstOrDefault(i => i.Row.Chiave == keep);
        Buttons();
    }

    private void Buttons() => openCard.IsEnabled = openWeb.IsEnabled = Selected is { IdCommessa: not null };
}
