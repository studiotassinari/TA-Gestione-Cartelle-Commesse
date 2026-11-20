using System.Windows;
using System.Windows.Controls;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using static TA_Gestione_Cartelle_Commesse.Views.GeCoUi;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Scheda della commessa di GeCo (2.42.0), come il dettaglio di GeCo2027 senza costi e ricavi: a sinistra dati generali, fasi,
// cartella e commesse collegate; a destra il team (la sezione della 2.41.0). «Modifica dati…» e i comandi delle fasi compaiono
// solo a chi li può usare (lo dice il servizio); a chi non ha la commessa tra le proprie resta la lettura con «Richiedi accesso».
// Fatture, importi, rilevazioni e le altre schede si aprono in GeCo2027 nel browser, già sulla commessa.
public sealed class GeCoProjectWindow : Window
{
    private readonly ProjectCardViewModel vm;
    private readonly Func<string?> address;
    private readonly StackPanel body = new();
    private readonly StackPanel messages = new();
    private readonly TextBlock title = Caption("Commessa di GeCo", 20, true, Teal);
    private readonly TextBlock subtitle = Caption("", 12, false, Grey);
    private readonly WrapPanel commands = new();
    private readonly Action<string> showRefreshed;
    // Un'altra commessa (commessa collegata) si apre in una nuova scheda, creata dalla finestra principale.
    public event Action<int>? ProjectRequested;

    public GeCoProjectWindow(ProjectCardViewModel model, Func<string?> geCoAddress)
    {
        vm = model; address = geCoAddress; Prepare(this, "Commessa di GeCo — TA"); Width = 1200; Height = 860;
        var root = new DockPanel { Margin = new Thickness(18) };
        var head = new StackPanel();
        head.Children.Add(title); head.Children.Add(subtitle.With(s => s.Margin = new Thickness(0, 2, 0, 8)));
        head.Children.Add(messages); head.Children.Add(commands);
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        var bottom = new StackPanel();
        var close = Push("Chiudi", Close); close.IsCancel = true; close.HorizontalAlignment = HorizontalAlignment.Right;
        bottom.Children.Add(close); bottom.Children.Add(Refreshed(out showRefreshed));
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 340 });
        var left = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 14, 0) };
        grid.Children.Add(left);
        var team = new GeCoTeamPanel(vm.Team) { VerticalAlignment = VerticalAlignment.Top };
        team.EditRequested += () => new GeCoTeamWindow(vm.Team, shared: true) { Owner = this }.ShowDialog();
        var right = new ScrollViewer { Content = team, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(right, 1); grid.Children.Add(right);
        root.Children.Add(grid);
        Content = root;

        vm.Updated += Render; vm.Live.Refreshed += showRefreshed;
        Loaded += async (_, _) => { if (vm.Card == null) await vm.LoadAsync(); };
        Closed += (_, _) => { vm.Updated -= Render; vm.Live.Refreshed -= showRefreshed; vm.Dispose(); };
        Render();
    }

    private void Render()
    {
        messages.Children.Clear(); commands.Children.Clear(); body.Children.Clear();
        if (vm.Error.Length > 0) messages.Children.Add(Notice(vm.Error, "danger"));
        else if (vm.Done.Length > 0) messages.Children.Add(Notice(vm.Done, "success"));
        if (vm.Message.Length > 0) messages.Children.Add(Notice(vm.Message, "info"));
        if (vm.Busy && vm.Card == null) messages.Children.Add(Notice("Caricamento della commessa…", "info"));
        var c = vm.Card;
        if (c == null) return;
        Title = $"Commessa {c.Codice} — TA";
        title.Text = $"{Capital(c.TipoDocumento)} {c.Codice}" + (c.Descrizione.Length > 0 ? " – " + c.Descrizione : "");
        subtitle.Text = $"{c.Cliente} · {c.StatoTesto}" + (c.Archiviata ? " (archiviata)" : "") + (c.TipoProd.Length > 0 ? " · " + c.TipoProd : "");
        if (vm.AccessText.Length > 0) messages.Children.Add(Notice(vm.AccessText, c.Attribuzione.SolaLettura ? "warning" : "info"));

        if (vm.CanEdit) commands.Children.Add(Push("Modifica dati…", EditData, true, "Descrizione, note, capocommessa, responsabili, tipologia e date (le modifiche valgono subito anche in GeCo2027)"));
        if (vm.CanRequest) commands.Children.Add(Push("Richiedi accesso…", RequestAccess, true, "Chiedi di lavorare sulla commessa: decidono amministratori, capocommessa e team manager"));
        var web = Push("Apri in GeCo2027 ▾", null, tip: "Pagine della commessa nel browser: fatture, importi, rilevazioni, documenti…");
        var menu = new ContextMenu();
        foreach (var page in GeCoPages.OfProject)
        {
            var p = page;
            var item = new MenuItem { Header = p.Testo, ToolTip = p.Spiegazione };
            item.Click += (_, _) => Warn(GeCoPages.Open(address(), p, c.Id));
            menu.Items.Add(item);
        }
        web.ContextMenu = menu; web.Click += (_, _) => { menu.PlacementTarget = web; menu.IsOpen = true; };
        commands.Children.Add(web);
        commands.Children.Add(Link("Aggiorna", async () => await vm.LoadAsync()));

        // Dati generali.
        var data = new Grid();
        data.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        data.ColumnDefinitions.Add(new ColumnDefinition());
        void Row(string label, string value)
        {
            data.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Caption(label, 12, false, Grey).With(t => t.Margin = new Thickness(0, 3, 8, 3)); Grid.SetRow(l, data.RowDefinitions.Count - 1); data.Children.Add(l);
            var v = Caption(value.Length == 0 ? "—" : value, 13).With(t => t.Margin = new Thickness(0, 3, 0, 3)); Grid.SetRow(v, data.RowDefinitions.Count - 1); Grid.SetColumn(v, 1); data.Children.Add(v);
        }
        Row("Codice e anno", $"{c.Codice} · {c.Anno}");
        Row("Descrizione", c.Descrizione);
        Row("Cliente", c.Cliente);
        Row("Capocommessa", ProjectCardViewModel.Person(c.Capocommessa));
        Row("Manager", ProjectCardViewModel.Person(c.Manager));
        Row("Responsabile di controllo", ProjectCardViewModel.Person(c.RespContr));
        Row("Tipologia", ProjectCardViewModel.Person(c.Tipo));
        if (c.AreaBusiness is { Length: > 0 } area) Row("Area di business", area);
        Row("Inizio e fine previsti", $"{ProjectCardViewModel.Date(c.DataIniPrev)} – {ProjectCardViewModel.Date(c.DataFinePrev)}");
        Row("Inizio e fine effettivi", $"{ProjectCardViewModel.Date(c.DataIniEff)} – {ProjectCardViewModel.Date(c.DataFineEff)}");
        Row("Stato", c.StatoTesto + (c.Archiviata ? " (archiviata)" : ""));
        if (c.Note.Length > 0) Row("Note", c.Note);
        Section("Dati generali", data);

        // Fasi: complete per chi ha la commessa e la scheda Fasi; soltanto i nomi per chi la vede in sola lettura.
        var phases = new StackPanel();
        if (c.Fasi == null) phases.Children.Add(Caption("Le fasi si vedono con la scheda Fasi di GeCo2027 (funzione non assegnata).", 12, false, Grey));
        else if (c.Fasi.Length == 0) phases.Children.Add(Caption("Nessuna fase.", 12, false, Grey));
        foreach (var f in c.Fasi ?? [])
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var id = f.Id;
            if (c.Permessi.DelFasi) { var del = Push("Elimina", async () => { if (Confirm($"Eliminare la fase «{f.Descrizione}»?")) await vm.RemovePhaseAsync(id); }); del.IsEnabled = !vm.Busy; DockPanel.SetDock(del, Dock.Right); row.Children.Add(del); }
            if (c.Permessi.ModFasi) { var mod = Push("Modifica…", () => EditPhase(f)); mod.IsEnabled = !vm.Busy; DockPanel.SetDock(mod, Dock.Right); row.Children.Add(mod); }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Caption((f.Ordinamento is int o ? o + " · " : "") + f.Descrizione + (f.Stato == "C" ? " (chiusa)" : ""), 13, true));
            if (f.TipoFase != null || f.Responsabile != null)
                text.Children.Add(Caption(string.Join(" · ", new[] { f.TipoFase, f.Responsabile is { } r ? "responsabile " + r : null }.Where(x => x != null)), 12, false, Grey));
            row.Children.Add(text);
            phases.Children.Add(row);
        }
        if (c.Permessi.InsFasi) phases.Children.Add(Push("Aggiungi fase…", () => EditPhase(null), tip: "Nuova fase della commessa (gli importi si gestiscono in GeCo2027)").With(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 8, 0, 0); }));
        Section("Fasi", phases);

        Section("Cartella", Caption(vm.FolderText, 13));

        if (c.Collegate != null)
        {
            var linked = new StackPanel();
            if (c.Collegate.Length == 0) linked.Children.Add(Caption("Nessuna commessa collegata. Si collegano in GeCo2027 (scheda «Commesse collegate»).", 12, false, Grey));
            foreach (var l in c.Collegate)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var id = l.IdCommessa;
                var open = Push("Apri", () => ProjectRequested?.Invoke(id)); DockPanel.SetDock(open, Dock.Right); row.Children.Add(open);
                row.Children.Add(Caption($"{l.Codice} – {l.Descrizione} · {l.Cliente} · {ServizioStato(l.Stato)}", 13, false, null, false).With(t => t.VerticalAlignment = VerticalAlignment.Center));
                linked.Children.Add(row);
            }
            Section("Commesse collegate", linked);
        }
    }

    private static string ServizioStato(string s) => s switch { "A" => "aperta", "C" => "chiusa", "F" => "fatturata", _ => "non iniziata" };
    private static string Capital(string s) => s.Length == 0 ? "Commessa" : char.ToUpperInvariant(s[0]) + s[1..];

    private void Section(string name, UIElement content)
    {
        var panel = new StackPanel();
        panel.Children.Add(Caption(name, 15, true, Teal).With(t => t.Margin = new Thickness(0, 0, 0, 6)));
        panel.Children.Add(content);
        body.Children.Add(Card(panel).With(b => b.Margin = new Thickness(0, 0, 0, 12)));
    }

    private void Warn(string? message) { if (message != null) MessageBox.Show(this, message, "Apri in GeCo2027", MessageBoxButton.OK, MessageBoxImage.Information); }
    private bool Confirm(string text) => MessageBox.Show(this, text, "Commessa di GeCo", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    // Finestra di modifica comune: mentre è aperta la scheda non si ricarica da sola (Editing).
    private bool Dialog(string caption, FrameworkElement form, Func<Task<bool>> save)
    {
        var dialog = new Window { Owner = this, Title = caption + " — TA", Width = 620, SizeToContent = SizeToContent.Height, MaxHeight = 820, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Back, FontSize = 13, ResizeMode = ResizeMode.NoResize };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var head = Caption(caption, 17, true, Teal).With(t => t.Margin = new Thickness(0, 0, 0, 10)); DockPanel.SetDock(head, Dock.Top); panel.Children.Add(head);
        var error = Caption("", 12, false, Danger).With(t => t.Margin = new Thickness(0, 6, 0, 0));
        var ok = Push("Salva", null, true); ok.IsDefault = true;
        var cancel = Push("Annulla", dialog.Close); cancel.IsCancel = true;
        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) }; bar.Children.Add(ok); bar.Children.Add(cancel);
        var foot = new StackPanel(); foot.Children.Add(error); foot.Children.Add(bar); DockPanel.SetDock(foot, Dock.Bottom); panel.Children.Add(foot);
        panel.Children.Add(form);
        dialog.Content = panel;
        bool saved = false;
        ok.Click += async (_, _) =>
        {
            ok.IsEnabled = false;
            if (await save()) { saved = true; dialog.Close(); }
            else { error.Text = vm.Error; ok.IsEnabled = true; }
        };
        vm.Editing = true;
        try { dialog.ShowDialog(); } finally { vm.Editing = false; }
        return saved;
    }

    private static Grid Form(out Action<string, FrameworkElement> add)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        add = (label, control) =>
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Caption(label, 12.5, false, Night).With(t => { t.Margin = new Thickness(0, 6, 8, 6); t.VerticalAlignment = VerticalAlignment.Center; });
            Grid.SetRow(l, g.RowDefinitions.Count - 1); g.Children.Add(l);
            control.Margin = new Thickness(0, 3, 0, 3); Grid.SetRow(control, g.RowDefinitions.Count - 1); Grid.SetColumn(control, 1); g.Children.Add(control);
        };
        return g;
    }
    private static TextBox Text(string value, bool multi = false) => new() { Text = value, MinHeight = multi ? 70 : 30, Padding = new Thickness(6, 3, 6, 3),
        AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalScrollBarVisibility = multi ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled };
    private static ComboBox Choice(GeCoOption[]? options, int? selected, bool empty)
    {
        var items = (empty ? new[] { new GeCoOption(0, "—") } : []).Concat(options ?? []).ToList();
        return new ComboBox { ItemsSource = items, DisplayMemberPath = "Text", MinHeight = 30, SelectedItem = items.FirstOrDefault(o => o.Value == (selected ?? 0)) ?? (empty ? items[0] : null) };
    }
    private static int? Value(ComboBox box) => box.SelectedItem is GeCoOption { Value: > 0 } o ? o.Value : null;

    private void EditData()
    {
        if (vm.Card is not { } c) return;
        var form = Form(out var add);
        var descr = Text(c.Descrizione); var note = Text(c.Note, true);
        var capo = Choice(c.Capicommessa, c.Capocommessa?.Id, false); var rc = Choice(c.Capicommessa, c.RespContr?.Id, true);
        var manager = Choice(c.Managers, c.Manager?.Id, true); var tipo = Choice(c.Tipologie, c.Tipo?.Id, true);
        var iniPrev = Text(c.DataIniPrev); var finePrev = Text(c.DataFinePrev); var iniEff = Text(c.DataIniEff); var fineEff = Text(c.DataFineEff);
        add("Descrizione", descr); add("Note", note); add("Capocommessa", capo); add("Responsabile di controllo", rc); add("Manager", manager); add("Tipologia", tipo);
        add("Inizio previsto (gg/mm/aaaa)", iniPrev); add("Fine prevista", finePrev); add("Inizio effettivo", iniEff); add("Fine effettiva", fineEff);
        var panel = new StackPanel(); panel.Children.Add(form);
        panel.Children.Add(Caption("Cliente, codice, anno, archiviazione e gli altri dati si cambiano in GeCo2027. I controlli sono gli stessi della scheda di GeCo2027.", 12, false, Grey).With(t => t.Margin = new Thickness(0, 8, 0, 0)));
        Dialog($"Dati della commessa {c.Codice}", panel, () => vm.SaveAsync(new GeCoProjectEdit(c.Versione, descr.Text, note.Text, Value(capo), Value(rc), Value(manager), Value(tipo),
            iniPrev.Text, finePrev.Text, iniEff.Text, fineEff.Text)));
    }

    private void EditPhase(GeCoCardPhase? f)
    {
        if (vm.Card is not { } c) return;
        var form = Form(out var add);
        var tipo = Choice(c.TipiFase, f?.IdTipoFase, false);
        var descr = Text(f?.Descrizione ?? ""); var ord = Text(f?.Ordinamento?.ToString() ?? c.NuovoOrdinamento.ToString());
        var resp = Choice(c.Responsabili, f?.IdResponsabile, true);
        var stati = new[] { new GeCoOption(0, "—"), new GeCoOption(1, "Aperta"), new GeCoOption(2, "Chiusa") };
        var stato = new ComboBox { ItemsSource = stati, DisplayMemberPath = "Text", MinHeight = 30, SelectedIndex = f?.Stato == "A" ? 1 : f?.Stato == "C" ? 2 : 0 };
        add("Tipo fase", tipo); add("Descrizione (vuota = quella del tipo)", descr); add("Ordinamento", ord); add("Responsabile", resp); add("Stato", stato);
        GeCoPhaseEdit Edit() => new(Value(tipo), descr.Text, ord.Text, Value(resp), stato.SelectedIndex switch { 1 => "A", 2 => "C", _ => "" });
        Dialog(f == null ? $"Nuova fase della commessa {c.Codice}" : $"Fase «{f.Descrizione}»", form, () => f == null ? vm.AddPhaseAsync(Edit()) : vm.SavePhaseAsync(f.Id, Edit()));
    }

    private void RequestAccess()
    {
        if (vm.Card is not { } c) return;
        var reason = Text("", true);
        var panel = new StackPanel();
        panel.Children.Add(Caption("Perché ti serve l'accesso? Lo leggono amministratori, capocommessa e team manager, che decidono. L'accesso concesso non ti iscrive al team.", 12, false, Grey).With(t => t.Margin = new Thickness(0, 0, 0, 8)));
        panel.Children.Add(reason);
        Dialog($"Richiesta di accesso alla commessa {c.Codice}", panel, () => vm.RequestAccessAsync(reason.Text));
    }
}
