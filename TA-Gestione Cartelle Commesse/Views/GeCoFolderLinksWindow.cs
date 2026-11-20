using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using static TA_Gestione_Cartelle_Commesse.Views.GeCoUi;
namespace TA_Gestione_Cartelle_Commesse.Views;

// «Collegamento cartelle» (pagina CollegamentoCartelle di GeCo2027): commesse e gare con la cartella collegata o proposta,
// azioni per gli amministratori (conferma, scelta di un'altra cartella, scollegamento, conferma in blocco delle proposte
// sicure) e, a destra, le notifiche arrivate da GeCo2027. Le cartelle sul disco non vengono mai toccate: il collegamento è
// una riga del database comune, scritta dal servizio con le regole della libreria GeCo.Dati.
public sealed class GeCoFolderLinksWindow : Window
{
    private readonly FolderLinksViewModel vm;
    private readonly StackPanel messages = new();
    private readonly TextBlock summary = Caption("", 12, false, Grey);
    private readonly TextBox search = new() { MinWidth = 260, MinHeight = 32, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 6), ToolTip = "Cerca per codice, anno, descrizione, cliente o cartella" };
    private readonly ComboBox view = new() { ItemsSource = FolderLinksViewModel.Views, SelectedIndex = 0, MinWidth = 170, MinHeight = 32, Margin = new Thickness(0, 0, 8, 6) };
    private readonly Button safe;
    private readonly DataGrid grid = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderThickness = new Thickness(0) };
    private readonly TextBlock detail = Caption("Selezionare una commessa o una gara.", 12, false, Grey);
    private readonly Button confirm, choose, unlink, team;
    // «Team della commessa…» (2.41.0): la finestra del team la apre la finestra principale, che ha il collegamento al servizio.
    public event Action<int>? TeamRequested;
    private readonly StackPanel events = new();
    private readonly TextBlock eventsTitle = Caption("Notifiche da GeCo2027", 15, true, Teal);
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public GeCoFolderLinksWindow(FolderLinksViewModel model)
    {
        vm = model; Prepare(this, "Collegamento cartelle — TA");
        vm.Ask = text => MessageBox.Show(this, text, "Collegamento cartelle", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        var root = new DockPanel { Margin = new Thickness(18) };
        var head = new StackPanel();
        head.Children.Add(Caption("Collegamento cartelle", 20, true, Teal));
        head.Children.Add(Caption("Collegamento definitivo tra le cartelle rilevate dal servizio e le commesse e le gare di GeCo, lo stesso della pagina Collegamento cartelle di GeCo2027. Il sistema propone, l'amministratore conferma: una proposta non è un collegamento finché non viene confermata.", 12, false, Grey));
        head.Children.Add(summary.With(t => t.Margin = new Thickness(0, 6, 0, 8)));
        head.Children.Add(messages);
        var tools = new WrapPanel();
        tools.Children.Add(search); tools.Children.Add(view);
        tools.Children.Add(Push("Aggiorna", async () => { await vm.LoadAsync(); await vm.LoadEventsAsync(); }));
        safe = Push("Conferma tutte le proposte sicure", async () => await vm.ConfirmSafeAsync(), true, "Collega in un'unica volta le proposte con lo stesso codice e senza concorrenti; il servizio le ricalcola prima di scrivere");
        tools.Children.Add(safe);
        head.Children.Add(tools);
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

        var side = new DockPanel { Width = 380, Margin = new Thickness(16, 0, 0, 0) };
        var sideHead = new DockPanel();
        var read = Link("Segna come viste", vm.Seen); DockPanel.SetDock(read, Dock.Right); sideHead.Children.Add(read); sideHead.Children.Add(eventsTitle);
        DockPanel.SetDock(sideHead, Dock.Top); side.Children.Add(sideHead);
        side.Children.Add(Card(new ScrollViewer { Content = events, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
        DockPanel.SetDock(side, Dock.Right); root.Children.Add(side);

        var actions = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttons = new WrapPanel();
        confirm = Push("Conferma proposta", async () => { if (Selected is { } r) await vm.ConfirmAsync(r); });
        choose = Push("Scegli cartella…", async () => await Choose());
        unlink = Push("Scollega", async () => { if (Selected is { } r) await vm.UnlinkAsync(r); });
        team = Push("Team della commessa…", () => { if (Selected is { Tipo: "commessa" } r) TeamRequested?.Invoke(r.Id); }, tip: "Persone del team della commessa di GeCo, con la modifica per chi può");
        buttons.Children.Add(confirm); buttons.Children.Add(choose); buttons.Children.Add(unlink); buttons.Children.Add(team);
        var close = Push("Chiudi", Close); close.IsCancel = true; buttons.Children.Add(close);
        actions.Children.Add(detail); actions.Children.Add(buttons.With(b => b.Margin = new Thickness(0, 8, 0, 0)));
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);

        foreach (var (label, path, width) in new[] { ("Tipo", "Elemento", 110.0), ("Codice", "Codice", 110), ("Anno", "Anno", 60), ("Descrizione e cliente", "Descrizione", 320), ("Cartella", "Cartella", 280), ("Stato", "Stato", 200), ("Segnalazioni", "Segnalazioni", 320) })
            grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path), Width = new DataGridLength(width) });
        grid.SelectionChanged += (_, _) => ShowSelection();
        root.Children.Add(Card(grid, new Thickness(0)));
        Content = root;

        search.TextChanged += (_, _) => { searchTimer.Stop(); searchTimer.Start(); };
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); vm.Filter = search.Text; Render(); };
        view.SelectionChanged += (_, _) => { vm.View = view.SelectedItem as string ?? FolderLinksViewModel.All; Render(); };
        vm.Changed += Render;
        Loaded += async (_, _) => await vm.StartAsync();
        Closed += (_, _) => { vm.Changed -= Render; vm.Dispose(); };
        Render();
    }

    private GeCoLinkRow? Selected => (grid.SelectedItem as FolderLinksViewModel.Item)?.Row;

    private void Render()
    {
        messages.Children.Clear();
        if (vm.Denied.Length > 0) messages.Children.Add(Notice(vm.Denied, "info"));
        if (vm.Error.Length > 0) messages.Children.Add(Notice(vm.Error, "danger"));
        else if (vm.Done.Length > 0) messages.Children.Add(Notice(vm.Done, "success"));
        if (vm.Busy) messages.Children.Add(Notice("Aggiornamento in corso…", "info"));
        summary.Text = vm.Summary;
        safe.Content = vm.SafeCount > 0 ? $"Conferma tutte le proposte sicure ({vm.SafeCount})" : "Conferma tutte le proposte sicure";
        safe.IsEnabled = vm.Allowed && vm.SafeCount > 0 && !vm.Busy;
        search.IsEnabled = view.IsEnabled = vm.Allowed;
        var keep = Selected is { } s ? (s.Tipo, s.Id) : default;
        var items = vm.Items().ToList();
        grid.ItemsSource = items;
        grid.SelectedItem = items.FirstOrDefault(i => (i.Row.Tipo, i.Row.Id) == keep);
        ShowSelection();
        RenderEvents();
    }

    private void ShowSelection()
    {
        var item = grid.SelectedItem as FolderLinksViewModel.Item;
        var r = item?.Row;
        confirm.IsEnabled = vm.Allowed && !vm.Busy && r?.Proposta != null && (r.Collegamento == null || r.Cartella?.Presente != true);
        choose.IsEnabled = vm.Allowed && !vm.Busy && r != null;
        unlink.IsEnabled = vm.Allowed && !vm.Busy && r?.Collegamento != null;
        team.IsEnabled = r?.Tipo == "commessa";
        if (item == null || r == null) { detail.Text = vm.Allowed ? "Selezionare una commessa o una gara." : ""; return; }
        var lines = new List<string> { $"{item.Elemento} {item.Codice} — {item.Stato}" + (item.Cartella.Length > 0 ? ": " + item.Cartella : "") };
        if (r.Cartella is { } f) lines.Add("Percorso: " + f.Percorso);
        if (item.Motivo.Length > 0) lines.Add(item.Motivo);
        foreach (var c in r.Candidati.Skip(r.Proposta == null ? 0 : 1).Take(4)) lines.Add($"Altra candidata: {c.Cartella?.Nome ?? "#" + c.IdCartella} ({c.Punteggio}, {c.Motivo})");
        foreach (var c in r.Conflitti) lines.Add("Segnalazione: " + c.Testo);
        detail.Text = string.Join(Environment.NewLine, lines);
    }

    private void RenderEvents()
    {
        eventsTitle.Text = vm.Arrived > 0 ? $"Notifiche da GeCo2027 ({vm.Arrived} nuove)" : "Notifiche da GeCo2027";
        events.Children.Clear();
        if (vm.EventsMessage.Length > 0) events.Children.Add(Notice(vm.EventsMessage, "info"));
        if (vm.Events.Count == 0 && vm.EventsMessage.Length == 0)
            events.Children.Add(Caption("Nessuna notifica recente. Qui compaiono commesse e gare create, modificate o eliminate in GeCo2027 e i collegamenti decisi là.", 12, false, Grey));
        foreach (var e in vm.Events)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            block.Children.Add(Caption(e.Data + (string.IsNullOrWhiteSpace(e.Utente) ? "" : " · " + e.Utente), 11, false, Grey));
            block.Children.Add(Caption(e.Testo, 13));
            events.Children.Add(block);
        }
    }

    // Scelta di un'altra cartella: ricerca tra le cartelle presenti della categoria ammessa, con quelle già collegate indicate.
    private async Task Choose()
    {
        if (Selected is not { } r) return;
        var dialog = new Window { Owner = this, Title = "Scegli cartella — " + r.Codice, Width = 720, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Back, FontSize = 13 };
        var panel = new DockPanel { Margin = new Thickness(14) };
        var text = new TextBox { Text = new string(r.Codice.TakeWhile(char.IsLetterOrDigit).ToArray()), MinHeight = 30, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 0, 8) };
        var list = new ListBox { DisplayMemberPath = "Text" };
        var ok = Push("Collega", null, true); ok.IsDefault = true; ok.IsEnabled = false;
        var cancel = Push("Annulla", dialog.Close); cancel.IsCancel = true;
        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) }; bar.Children.Add(ok); bar.Children.Add(cancel);
        DockPanel.SetDock(text, Dock.Top); DockPanel.SetDock(bar, Dock.Bottom);
        panel.Children.Add(text); panel.Children.Add(bar); panel.Children.Add(list);
        dialog.Content = panel;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        async Task Find()
        {
            var asked = text.Text;
            var found = await vm.FoldersAsync(asked, r.Tipo);
            if (asked != text.Text) return;
            list.ItemsSource = found.Select(f => new Choice(f, $"{f.Cartella.Nome}  ·  {Category(f.Cartella.Categoria)}" + (f.CollegataA != null ? $"  ·  già collegata a una {f.CollegataA.Tipo}" : ""))).ToList();
        }
        text.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        timer.Tick += async (_, _) => { timer.Stop(); await Find(); };
        list.SelectionChanged += (_, _) => ok.IsEnabled = list.SelectedItem is Choice { Folder.CollegataA: null };
        Choice? chosen = null;
        ok.Click += (_, _) => { chosen = list.SelectedItem as Choice; dialog.Close(); };
        list.MouseDoubleClick += (_, _) => { if (ok.IsEnabled) { chosen = list.SelectedItem as Choice; dialog.Close(); } };
        dialog.Loaded += async (_, _) => await Find();
        dialog.ShowDialog();
        timer.Stop();
        if (chosen != null) await vm.LinkAsync(r, chosen.Folder.Cartella.Id, chosen.Folder.Cartella.Nome);
    }
    private sealed record Choice(GeCoFolderChoice Folder, string Text);
    private static string Category(string c) => c switch { "C" => "commesse", "G" => "gare", "A" => "archivio", _ => c };
}
