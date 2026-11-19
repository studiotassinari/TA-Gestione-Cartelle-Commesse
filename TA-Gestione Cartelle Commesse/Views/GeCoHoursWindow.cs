using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using static TA_Gestione_Cartelle_Commesse.Views.GeCoUi;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Ore lavorate di GeCo con l'impostazione della Pianificazione: giorni feriali in orizzontale (sabato e domenica solo se
// contengono ore), persone una sotto l'altra, ogni elemento è una giornata commessa · fase del colore della commessa con
// sotto ore e km; più fasi nello stesso giorno stanno su righe parallele. Ore e km si modificano nella barra sopra la
// griglia quando l'elemento è scelto; le sagome tratteggiate sono le attività pianificate da consuntivare.
public sealed class GeCoHoursWindow : Window
{
    private readonly HoursViewModel vm;
    private readonly DockPanel root = new() { Margin = new Thickness(18) };
    private readonly StackPanel head = new();
    private readonly Grid body = new();
    private readonly StackPanel panel = new();
    private readonly Border panelCard;
    private readonly TextBox search = new() { MinHeight = 32, Padding = new Thickness(8, 4, 8, 4), ToolTip = "Cerca una commessa da aggiungere (almeno 2 caratteri)" };
    private readonly StackPanel found = new();
    private readonly WrapPanel info = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel timeline = new() { Margin = new Thickness(10) };
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private TextBox? hoursBox;
    private bool details;
    private double dayWidth = 112;
    private sealed record Dragged(PanelProject Project, GeCoOption? Phase, decimal? Hours);

    public GeCoHoursWindow(HoursViewModel model)
    {
        vm = model; Prepare(this, "Ore lavorate — TA");
        vm.Ask = question => MessageBox.Show(this, question, "Ore lavorate", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        Content = root; root.Background = Back;
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        // Aggiornamenti esterni (GeCo2027 o un altro client di GeCa): avviso discreto in fondo. La ricarica tiene le modifiche non
        // salvate e aspetta che l'utente finisca di trascinare, di scrivere in una casella o di compilare i dettagli.
        var refreshed = Refreshed(out var showRefreshed); DockPanel.SetDock(refreshed, Dock.Bottom); root.Children.Add(refreshed);
        root.Children.Add(body);
        vm.Interacting = () => Dragging || details || (IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox);
        vm.Live.Refreshed += showRefreshed;
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        panelCard = Card(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); body.Children.Add(panelCard);
        var right = new DockPanel(); Grid.SetColumn(right, 2); body.Children.Add(right);
        var infoCard = Card(info, new Thickness(12, 6, 12, 6)); infoCard.MinHeight = 50; infoCard.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(infoCard, Dock.Top); right.Children.Add(infoCard);
        var note = Caption("Tab per passare da un controllo all'altro · Invio per scrivere le ore della giornata scelta · F2 o doppio clic per note e dettagli · Canc per togliere la giornata · Ctrl+S per salvare. Sotto ogni persona il totale ore del giorno: in giallo oltre 8, in rosso oltre 10.", 12, false, Grey);
        note.Margin = new Thickness(0, 8, 0, 0); DockPanel.SetDock(note, Dock.Bottom); right.Children.Add(note);
        scroll.Content = timeline; right.Children.Add(Card(scroll, new Thickness(0)));
        search.TextChanged += (_, _) => { searchTimer.Stop(); searchTimer.Start(); };
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); var text = search.Text; var result = await vm.SearchAsync(text); if (text == search.Text) ShowFound(result, text.Trim().Length >= 2); };
        scroll.SizeChanged += (_, e) => { if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 1) Render(); };
        vm.Changed += Render;
        Loaded += async (_, _) => { if (vm.Access == null) await vm.StartAsync(); };
        Closing += (_, e) => { if (!vm.CanLeave()) e.Cancel = true; };
        Closed += (_, _) => { vm.Changed -= Render; vm.Live.Refreshed -= showRefreshed; searchTimer.Stop(); vm.Dispose(); };
        PreviewKeyDown += Keys;
        head.Children.Add(Caption("Caricamento delle ore lavorate…", 14, false, Grey));
        // Un modello già avviato (dalle verifiche) si disegna subito; altrimenti si parte al caricamento della finestra.
        if (vm.Access != null) Render();
    }

    private void Keys(object sender, KeyEventArgs e)
    {
        bool typing = Keyboard.FocusedElement is TextBox;
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; _ = vm.SaveAsync(); return; }
        if (typing || vm.SelectedElement is not { } s) return;
        var p = vm.PersonOf(s.P);
        if (e.Key == Key.Delete && vm.Modifiable(p, s.Cell)) { e.Handled = true; vm.Remove(s); }
        else if (e.Key == Key.F2) { e.Handled = true; Details(s); }
        else if (e.Key == Key.Enter && hoursBox is { IsReadOnly: false } box) { e.Handled = true; box.Focus(); box.SelectAll(); }
    }

    private void Render()
    {
        head.Children.Clear(); panel.Children.Clear(); info.Children.Clear(); timeline.Children.Clear(); hoursBox = null;
        if (vm.Start == null)
        {
            body.Visibility = Visibility.Collapsed;
            head.Children.Add(Caption("Ore lavorate", 22, true, Teal));
            // Nessun utente di GeCo per l'account Windows, funzione non assegnata o servizio non aggiornato: nulla è permesso.
            if (vm.Access != null) head.Children.Add(Notice(vm.AccessMessage.Length > 0 ? vm.AccessMessage : "Ore lavorate non disponibili.", "danger"));
            return;
        }
        body.Visibility = Visibility.Visible;
        RenderHead(); RenderPanel(); RenderInfo(); RenderTimeline();
    }
    // Dopo ogni tasto nelle caselle della barra si aggiornano griglia e intestazione, non la barra: il cursore resta dov'è.
    private void RenderAfterEdit() { head.Children.Clear(); timeline.Children.Clear(); RenderHead(); RenderTimeline(); }

    private void RenderHead()
    {
        var start = vm.Start!; var perms = vm.Permissions;
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
        bar.Children.Add(Caption(start.Titolo, 22, true, Teal, false).With(t => t.Margin = new Thickness(0, 0, 12, 6)));
        if (vm.Changes > 0) bar.Children.Add(Caption(vm.Changes == 1 ? "1 modifica non salvata" : vm.Changes + " modifiche non salvate", 13, true, Warning, false).With(t => t.Margin = new Thickness(0, 6, 12, 0)));
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Right); bar.Children.Add(buttons);
        if (vm.People.Any(p => !HoursViewModel.ReadOnly(p)) && (perms.Ins || perms.Mod))
            buttons.Children.Add(Push(vm.Saving ? "Salvataggio…" : "Salva", () => _ = vm.SaveAsync(), vm.Changes > 0, "Salva (Ctrl+S)").With(b => b.IsEnabled = vm.Changes > 0 && !vm.Saving));
        if (perms.Ins && vm.People.Any(p => vm.Ghosts(p, vm.Elements(p)).Count > 0))
            buttons.Children.Add(Push("Usa le ore pianificate", () => _ = vm.UsePlanAsync(), tip: "Trasforma tutte le attività pianificate del periodo in giornate con le ore stimate"));
        buttons.Children.Add(Push("‹ Settimana precedente", () => _ = vm.Go(-7)));
        buttons.Children.Add(Push("Oggi", () => _ = vm.Today()));
        buttons.Children.Add(Push("Settimana successiva ›", () => _ = vm.Go(7)));
        foreach (var weeks in new[] { 1, 2, 4, 6, 8 }) { int w = weeks; buttons.Children.Add(Push(w == 1 ? "1 settimana" : w + " settimane", () => _ = vm.SetWeeks(w), vm.Weeks == w)); }
        head.Children.Add(bar);
        if (vm.Error.Length > 0) head.Children.Add(Notice(vm.Error, "danger"));
        if (vm.Note is { } n) head.Children.Add(Notice(n.Text, n.Kind));
        if (start.Cdc.Length == 0) head.Children.Add(Notice("Nessuna persona da mostrare: all'utente non è abbinato un centro di costo con tariffe."));
        if (start.PuoCambiareCdc && start.Cdc.Length > 1)
        {
            var ids = vm.Ids();
            var people = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
            people.Children.Add(Caption("Persone nel diagramma:", 13, false, Grey, false).With(t => t.Margin = new Thickness(0, 6, 8, 0)));
            if (vm.People.Count == 0) people.Children.Add(Caption("nessuna", 13, false, Grey).With(t => t.Margin = new Thickness(0, 6, 8, 0)));
            foreach (var p in vm.People) { var id = p.Id; people.Children.Add(Push(p.Nome + (p.Io ? " (io)" : "") + "  ×", () => _ = vm.Choose(ids.Where(x => x != id).ToArray()), true, "Togli " + p.Nome + " dal diagramma")); }
            var missing = start.Cdc.Where(c => !ids.Contains(c.Value)).ToArray();
            if (missing.Length > 0)
            {
                var add = new ComboBox { MinWidth = 220, MaxWidth = 280, Margin = new Thickness(0, 0, 8, 6), ToolTip = "Aggiungi una persona al diagramma" };
                add.Items.Add(new ComboBoxItem { Content = "+ Aggiungi persona…", IsSelected = true });
                foreach (var c in missing) add.Items.Add(new ComboBoxItem { Content = c.Text + (c.Cdc_IntExt == "E" ? " (esterno)" : "") + (c.Cdc_Attivo ? "" : " (non attivo)"), Tag = c.Value });
                add.SelectionChanged += (_, _) => { if ((add.SelectedItem as ComboBoxItem)?.Tag is int id) _ = vm.Choose(ids.Append(id).ToArray()); };
                people.Children.Add(add);
                if (missing.Any(c => c.Cdc_Attivo)) people.Children.Add(Link("tutte", () => _ = vm.Choose(start.Cdc.Where(c => c.Cdc_Attivo || ids.Contains(c.Value)).Select(c => c.Value).ToArray()), "Tutte le persone attive"));
            }
            people.Children.Add(Link("solo io", () => _ = vm.Choose(null)));
            head.Children.Add(people);
        }
    }

    private void RenderPanel()
    {
        var p = vm.ChosenPerson;
        bool show = p != null && !HoursViewModel.ReadOnly(p) && vm.Permissions.Ins;
        panelCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        body.ColumnDefinitions[0].Width = new GridLength(show ? 300 : 0); body.ColumnDefinitions[1].Width = new GridLength(show ? 16 : 0);
        if (!show) return;
        var title = new TextBlock { Inlines = { new Bold(new Run("Commesse e fasi")) }, FontSize = 14 };
        if (vm.People.Count > 1) title.Inlines.Add(new Run(" di " + p!.Nome) { Foreground = Grey });
        panel.Children.Add(title);
        panel.Children.Add(Caption("Trascina una fase sul giorno in cui hai lavorato, oppure premila per aggiungerla al giorno scelto; poi scrivi le ore sulla giornata.", 12, false, Grey).With(t => t.Margin = new Thickness(0, 2, 0, 8)));
        var items = vm.NewItems();
        if (items.Length > 1)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(Caption("Voce delle nuove giornate ", 12, false, null, false).With(t => t.VerticalAlignment = VerticalAlignment.Center));
            var choice = new ComboBox(); choice.Items.Add(new ComboBoxItem { Content = "predefinita", IsSelected = vm.NewItem == null });
            foreach (var v in items) choice.Items.Add(new ComboBoxItem { Content = v.Codice, Tag = v.Id, IsSelected = vm.NewItem == v.Id });
            choice.SelectionChanged += (_, _) => vm.NewItem = (choice.SelectedItem as ComboBoxItem)?.Tag as int?;
            row.Children.Add(choice); panel.Children.Add(row);
        }
        panel.Children.Add(search); panel.Children.Add(found);
        var list = vm.Panel();
        if (list.Length == 0) panel.Children.Add(Caption("Nessuna commessa del team o pianificata nel periodo: cercane una qui sopra.", 12, false, Grey).With(t => t.Margin = new Thickness(0, 10, 0, 0)));
        foreach (var c in list)
        {
            var header = new DockPanel { Margin = new Thickness(0, 12, 0, 2), ToolTip = c.Cliente };
            header.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Background = Hex(vm.Color(c.IdCommessa).Back), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(Caption(c.Codice, 13, true, null, false).With(t => t.Margin = new Thickness(0, 0, 6, 0)));
            var marks = (c.Team ? " 👥" : "") + (c.Pianificata ? " 📅" : "");
            if (marks.Length > 0) { var m = Caption(marks, 12, false, Grey, false); m.ToolTip = (c.Team ? "Commessa del team. " : "") + (c.Pianificata ? "Pianificata nel periodo." : ""); DockPanel.SetDock(m, Dock.Right); header.Children.Add(m); }
            header.Children.Add(Caption(c.Descrizione, 12, false, Grey, false));
            panel.Children.Add(header);
            // Senza fase si può registrare se la commessa non ha fasi o se la fase non è obbligatoria.
            var phases = c.Fasi.Cast<GeCoOption?>().ToList(); if (c.Fasi.Length == 0 || !c.FaseRichiesta) phases.Add(null);
            foreach (var f in phases)
            {
                var (project, phase) = (c, f);
                var b = Push("≡  " + (f?.Text ?? "Commessa (senza fase)"), () => _ = vm.AddAsync(vm.ChosenPerson!.Id, project, phase, vm.Day ?? Math.Max(0, vm.TodayIndex)),
                    tip: "Trascina sul giorno della persona, oppure premi per aggiungerla alla persona e al giorno scelti");
                b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Background = Track; b.Margin = new Thickness(0, 4, 0, 0); b.Cursor = Cursors.SizeAll;
                DragSource(b, new Dragged(project, phase, null)); panel.Children.Add(b);
            }
        }
        var quick = vm.Recents.Recenti.Concat(vm.Recents.Preferite).Where(c => !list.Any(x => x.IdCommessa == c.IdCommessa)).Take(10).ToArray();
        if (quick.Length > 0)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            chips.Children.Add(Caption("Recenti e preferite:", 12, false, Grey, false).With(t => t.Margin = new Thickness(0, 6, 6, 0)));
            foreach (var c in quick) { var r = c; chips.Children.Add(Push((c.Preferita ? "★ " : "") + c.Codice, () => _ = vm.AddToPanelAsync(r.IdCommessa, r.Codice), tip: c.Descrizione + (c.Cliente.Length > 0 ? " – " + c.Cliente : "")).With(b => { b.MinHeight = 26; b.Padding = new Thickness(8, 2, 8, 2); })); }
            panel.Children.Add(chips);
        }
        panel.Children.Add(Push("Copia la settimana precedente", () => _ = vm.CopyPreviousWeekAsync(), tip: "Copia ore e km della settimana precedente nella prima settimana mostrata, da controllare e salvare").With(b => { b.Margin = new Thickness(0, 14, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Stretch; }));
    }
    private void ShowFound(GeCoFoundProject[] result, bool searched)
    {
        found.Children.Clear();
        if (searched && result.Length == 0) found.Children.Add(Caption("Nessuna commessa aperta trovata", 12, false, Grey).With(t => t.Margin = new Thickness(0, 4, 0, 0)));
        foreach (var c in result)
        {
            var r = c;
            var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Inlines = { new Bold(new Run(c.Codice + " ")), new Run(c.Descrizione), new Run(string.IsNullOrEmpty(c.Cliente) ? "" : "  " + c.Cliente) { Foreground = Grey, FontSize = 11 } } };
            var b = Push(text, () => { search.Text = ""; found.Children.Clear(); _ = vm.AddToPanelAsync(r.IdCommessa, r.Codice); });
            b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 2, 0, 0); found.Children.Add(b);
        }
    }

    // Barra della giornata scelta: dati della giornata, ore e km modificabili, dettagli e rimozione.
    private void RenderInfo()
    {
        if (vm.SelectedElement is not { } e)
        {
            bool km = vm.People.Any(p => HoursViewModel.KmOf(p) != null);
            info.Children.Add(Caption($"Ogni elemento è una giornata di lavoro su una commessa · fase: sceglila e scrivi qui le ore (anche 7:30){(km ? " e i km percorsi" : "")}. Le sagome tratteggiate sono le attività pianificate: premile o trascinale per consuntivarle con le ore stimate.", 13, false, Grey));
            return;
        }
        var p = vm.PersonOf(e.P)!; var km0 = HoursViewModel.KmOf(p);
        bool editable = vm.Modifiable(p, e.Cell), kmEditable = vm.KmModifiable(p, e);
        var what = new StackPanel { Margin = new Thickness(0, 0, 18, 0), MinWidth = 240, MaxWidth = 520 };
        what.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Inlines = { new Bold(new Run(Label(e))), new Run("  " + e.Row.Descrizione) { Foreground = Grey } } });
        var nota = vm.Field(e.Key, e.Cell, "Note"); bool confirmed = e.Cell is { Conf: true, Qta: decimal q } && q != 0;
        what.Children.Add(Caption((vm.People.Count > 1 ? p.Nome + " · " : "") + GeCoCalendar.Long(e.Cell.Data) + (confirmed ? " · confermata" : "") + (e.Cell.Bloccata != null && !confirmed ? " · " + e.Cell.Bloccata : "") + (string.IsNullOrEmpty(nota) ? "" : " · nota: " + nota), 12, false, Grey));
        foreach (var m in Errors(e)) what.Children.Add(Caption("⚠ " + m, 12, true, Error));
        info.Children.Add(what);
        hoursBox = Box(vm.Value(e.Key, e.Cell), !editable, text => vm.Modify(e.Key, "Qta", text));
        info.Children.Add(Labelled(HoursViewModel.CountsHours(p, e.Row.IdVdc) ? "Ore" : e.Row.Voce, hoursBox));
        if (e.KmKey != null)
        {
            var kmBox = Box(vm.Value(e.KmKey, e.KmCell), !kmEditable, text => vm.Modify(e.KmKey, "Qta", text));
            if (km0 is { Disponibile: false }) kmBox.ToolTip = $"Nessuna tariffa valida per la voce {km0.Codice} nel periodo";
            info.Children.Add(Labelled("Km", kmBox));
        }
        info.Children.Add(Push("Note e dettagli", () => Details(e)));
        if (editable) info.Children.Add(Push("Togli la giornata", () => vm.Remove(e)).With(b => b.Foreground = Danger));
    }
    private TextBox Box(string value, bool readOnly, Action<string> changed)
    {
        var box = new TextBox { Text = value, IsReadOnly = readOnly, Width = 70, MinHeight = 30, Padding = new Thickness(6, 2, 6, 2), FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right, Background = readOnly ? Brushes.Transparent : Brushes.White };
        box.TextChanged += (_, _) =>
        {
            var clean = GeCoCalendar.Clean(box.Text);
            if (clean != box.Text) { int caret = Math.Min(box.CaretIndex, clean.Length); box.Text = clean; box.CaretIndex = caret; return; }
            if (!readOnly) { changed(clean); RenderAfterEdit(); }
        };
        box.KeyDown += (_, k) => { if (k.Key is Key.Enter or Key.Escape) { k.Handled = true; scroll.Focus(); } };
        return box;
    }
    private static StackPanel Labelled(string label, UIElement control)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(Caption(label + " ", 13, false, null, false).With(t => t.VerticalAlignment = VerticalAlignment.Center)); s.Children.Add(control); return s;
    }
    private string[] Errors(HoursViewModel.Element e) => new[] { vm.Errors.GetValueOrDefault(e.Key), e.KmKey != null && vm.Errors.TryGetValue(e.KmKey, out var k) ? "Km: " + k : null }.OfType<string>().ToArray();
    private static string Label(HoursViewModel.Element e) => e.Row.Codice + (e.Row.Fase.Length > 0 ? " · " + e.Row.Fase : "");
    private void Details(HoursViewModel.Element e)
    {
        var p = vm.PersonOf(e.P)!;
        details = true;
        try { new GeCoDayDialog(this, vm, p, e).ShowDialog(); }
        finally { details = false; }
        Render();
    }

    private void RenderTimeline()
    {
        var days = vm.VisibleDays(); if (days.Length == 0) return;
        double minimum = vm.Weeks == 1 ? 112 : vm.Weeks == 2 ? 92 : 80;
        dayWidth = Math.Max(minimum, ((scroll.ViewportWidth > 0 ? scroll.ViewportWidth : scroll.ActualWidth) - 24) / days.Length);
        var column = days.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        double width = dayWidth * days.Length;
        bool anyKm = vm.People.Any(p => HoursViewModel.KmOf(p) != null);
        double rowHeight = anyKm ? 56 : 46;
        // Intestazione per settimana con la riga scura tra le settimane, poi i giorni.
        var weeks = new Canvas { Width = width, Height = 20 };
        for (int w = 0; w < vm.Weeks; w++)
        {
            var own = days.Where(d => d / 7 == w).ToArray(); if (own.Length == 0) continue;
            var first = vm.DateOf(w * 7);
            var label = Caption($"Settimana {GeCoCalendar.IsoWeek(first)} · {GeCoCalendar.Short(first)} – {GeCoCalendar.Short(vm.DateOf(w * 7 + 6))}", 12, true, Grey, false);
            label.Width = own.Length * dayWidth - 8; Canvas.SetLeft(label, column[own[0]] * dayWidth + 6); weeks.Children.Add(label);
            if (w > 0) { var line = new Rectangle { Width = 2, Height = 20, Fill = Night }; Canvas.SetLeft(line, column[own[0]] * dayWidth); weeks.Children.Add(line); }
        }
        timeline.Children.Add(weeks);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        int today = vm.TodayIndex;
        foreach (var d in days)
        {
            int day = d; var date = vm.DateOf(d); bool weekend = d % 7 >= 5;
            var content = new StackPanel();
            content.Children.Add(Caption(GeCoCalendar.DayNames[d % 7], 12, true, weekend ? Grey : null, false).With(t => t.TextAlignment = TextAlignment.Center));
            content.Children.Add(Caption(GeCoCalendar.Date(date).Day.ToString(), 11, false, Grey, false).With(t => t.TextAlignment = TextAlignment.Center));
            var b = Push(content, () => { vm.Day = vm.Day == day ? null : day; Render(); }, tip: GeCoCalendar.Long(date) + ": scegli il giorno per aggiungere una fase con un clic");
            b.Width = dayWidth - 2; b.Margin = new Thickness(1, 0, 1, 0); b.Padding = new Thickness(0, 2, 0, 2); b.BorderThickness = new Thickness(2);
            b.Background = d == today ? TodayBack : weekend ? Hex("#F1F3F4") : Brushes.White; b.BorderBrush = vm.Day == d ? Teal : Brushes.Transparent;
            header.Children.Add(b);
        }
        timeline.Children.Add(header);
        foreach (var p in vm.People) timeline.Children.Add(PersonTrack(p, days, column, width, rowHeight));
    }

    private UIElement PersonTrack(HoursViewModel.Person p, int[] days, Dictionary<int, int> column, double width, double rowHeight)
    {
        var box = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        bool ro = HoursViewModel.ReadOnly(p);
        var elements = vm.Elements(p); var ghosts = ro ? new List<HoursViewModel.Ghost>() : vm.Ghosts(p, elements);
        if (vm.People.Count > 1 || ro || p.Error != null)
        {
            decimal total = p.Period == null ? 0 : Enumerable.Range(0, vm.DayCount).Sum(g => vm.HoursOf(p, elements, g));
            var label = new TextBlock { Inlines = { new Bold(new Run(p.Nome)), new Run((p.Io ? " (io)" : "") + (p.Esterno ? " · centro esterno" : "")) } };
            if (p.Period != null) label.Inlines.Add(new Run("    " + GeCoCalendar.Total(total) + " ore nel periodo") { Foreground = Grey, FontSize = 12 });
            if (ro && p.Period != null) label.Inlines.Add(new Run("    👁 sola lettura") { Foreground = Grey, FontSize = 12 });
            var b = Push(label, () => _ = vm.ChoosePerson(p.Id), tip: "Scegli questa persona per il pannello delle commesse e per aggiungere una fase con un clic");
            b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Width = width; b.Margin = new Thickness(0, 0, 0, 3); b.Background = PersonBack;
            b.BorderBrush = vm.People.Count > 1 && vm.ChosenPerson?.Id == p.Id ? Teal : Brushes.Transparent; box.Children.Add(b);
        }
        if (p.Error != null || p.Period == null) { box.Children.Add(Notice(p.Error ?? "Ore non disponibili.")); return box; }
        var lanes = HoursViewModel.Lanes(elements.Select(e => (e.Key, $"{e.Row.IdCommessa}|{e.Row.IdFase ?? 0}", e.Index, false))
            .Concat(ghosts.Select(g => (g.Key, $"{g.Project.IdCommessa}|{g.Phase?.Value ?? 0}", g.Index, true))), vm.DayCount);
        int rows = Math.Max(vm.People.Count > 1 ? 1 : 3, lanes.Count + (ro ? 0 : 1));
        var track = new Canvas { Width = width, Height = 8 + rows * rowHeight, Background = Track, ClipToBounds = true, AllowDrop = !ro };
        for (int i = 1; i < days.Length; i++)
        {
            bool newWeek = days[i] / 7 != days[i - 1] / 7;
            var line = new Rectangle { Width = newWeek ? 2 : 1, Height = track.Height, Fill = newWeek ? Night : Rule }; Canvas.SetLeft(line, i * dayWidth - (newWeek ? 1 : 0)); track.Children.Add(line);
        }
        foreach (var g in ghosts) if (column.TryGetValue(g.Index, out var c)) track.Children.Add(GhostVisual(g, c, lanes.Row[g.Key], rowHeight));
        foreach (var e in elements) if (column.TryGetValue(e.Index, out var c)) track.Children.Add(ElementVisual(p, e, c, lanes.Row[e.Key], rowHeight));
        var target = new Rectangle { Width = dayWidth, Height = track.Height, Fill = Hex("#2E135F6E"), Stroke = Teal, StrokeThickness = 2, StrokeDashArray = [3, 2], Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        track.Children.Add(target);
        int ColumnAt(DragEventArgs e) => Math.Clamp((int)(e.GetPosition(track).X / dayWidth), 0, days.Length - 1);
        track.DragOver += (_, e) =>
        {
            bool ok = !ro && e.Data.GetData(DragFormat) is Dragged;
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
            target.Visibility = ok ? Visibility.Visible : Visibility.Collapsed; Canvas.SetLeft(target, ColumnAt(e) * dayWidth);
        };
        track.DragLeave += (_, _) => target.Visibility = Visibility.Collapsed;
        track.Drop += (_, e) => { target.Visibility = Visibility.Collapsed; if (e.Data.GetData(DragFormat) is Dragged d) _ = vm.AddAsync(p.Id, d.Project, d.Phase, days[ColumnAt(e)], d.Hours); };
        box.Children.Add(new Border { BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = track, Width = width + 2, HorizontalAlignment = HorizontalAlignment.Left });
        // Totali del giorno: giallo oltre 8 ore, rosso oltre 10.
        var totals = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        foreach (var d in days)
        {
            decimal h = vm.HoursOf(p, elements, d), k = vm.KmTotal(elements, d);
            var (back, fore) = h > 10 ? (DangerBack, Danger) : h > 8 ? (WarningBack, Warning) : (Neutral, Night);
            var cell = new Border { Width = dayWidth - 2, Margin = new Thickness(1, 0, 1, 0), CornerRadius = new CornerRadius(6), Padding = new Thickness(0, 2, 0, 2), Background = back,
                ToolTip = h > 10 ? "Più di 10 ore nella giornata: controlla" : h > 8 ? "Più di 8 ore nella giornata" : null };
            cell.Child = Caption(h == 0 && k == 0 ? "–" : GeCoCalendar.Total(h) + " h" + (k > 0 ? " · " + GeCoCalendar.Total(k) + " km" : ""), 11, h > 0, fore, false).With(t => t.TextAlignment = TextAlignment.Center);
            totals.Children.Add(cell);
        }
        box.Children.Add(totals);
        // Riga delle settimane: totale e conferma settimanale (riservata a chi ha la conferma degli statini).
        var weekRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        for (int w = 0; w < vm.Weeks; w++)
        {
            int week = w; var own = days.Where(d => d / 7 == w).ToArray(); if (own.Length == 0) continue;
            var mine = elements.Where(e => e.Index / 7 == w).ToList();
            decimal tw = Enumerable.Range(w * 7, 7).Sum(g => vm.HoursOf(p, elements, g));
            bool confirmed = mine.Any(e => e.Cell is { Conf: true, Qta: decimal q } && q != 0), toConfirm = mine.Any(e => e.Cell is { Conf: false, Qta: decimal q } && q != 0);
            var cell = new DockPanel { Width = own.Length * dayWidth, LastChildFill = false };
            var label = new TextBlock { FontSize = 12, Foreground = Grey, Margin = new Thickness(6, 2, 6, 2), Inlines = { new Run($"Settimana {GeCoCalendar.IsoWeek(vm.DateOf(w * 7))}: "), new Bold(new Run(GeCoCalendar.Total(tw) + " h")) { Foreground = Night } } };
            if (confirmed && !toConfirm) label.Inlines.Add(new Run(" · 🔒 confermata"));
            cell.Children.Add(label);
            if (vm.Start!.PuoConfermare && !ro && toConfirm) cell.Children.Add(Link("🔒 Conferma", () => _ = vm.ConfirmAsync(p, week, true), "Conferma le ore della settimana").With(DockRight));
            if (vm.Start!.PuoConfermare && !ro && confirmed) cell.Children.Add(Link("🔓 Sblocca", () => _ = vm.ConfirmAsync(p, week, false), "Sblocca le ore confermate della settimana").With(DockRight));
            weekRow.Children.Add(cell);
        }
        box.Children.Add(weekRow);
        return box;
    }
    private static void DockRight(Button b) { DockPanel.SetDock(b, Dock.Right); b.Margin = new Thickness(0); }

    private Border ElementVisual(HoursViewModel.Person p, HoursViewModel.Element e, int column, int lane, double rowHeight)
    {
        var (back, fore) = vm.Color(e.Row.IdCommessa);
        var errors = Errors(e); bool error = errors.Length > 0;
        var foreground = error ? Night : Hex(fore);
        var km = HoursViewModel.KmOf(p);
        bool confirmed = (e.Cell is { Conf: true, Qta: decimal q } && q != 0) || (e.KmCell is { Conf: true, Qta: decimal k } && k != 0);
        var nota = vm.Field(e.Key, e.Cell, "Note"); var missing = vm.Missing(p, e);
        bool modified = vm.Edits.ContainsKey(e.Key) || (e.KmKey != null && vm.Edits.ContainsKey(e.KmKey));
        var marks = (confirmed ? "🔒" : "") + (string.IsNullOrEmpty(nota) ? "" : "✎") + (error ? "⚠" : missing.Length > 0 ? "!" : "");
        var hours = vm.Value(e.Key, e.Cell); var kmValue = e.KmKey != null ? vm.Value(e.KmKey, e.KmCell) : "";
        var unit = HoursViewModel.CountsHours(p, e.Row.IdVdc) ? "h" : e.Row.Voce;
        var voce = (p.Period?.VociSelezionate.Count(v => v != km?.Id) ?? 0) > 1 ? " " + e.Row.Voce : "";
        var text = new StackPanel { Margin = new Thickness(7, 3, 5, 3) };
        var top = new DockPanel();
        if (marks.Length > 0) { var m = Caption(marks, 11, true, error ? Error : foreground, false); DockPanel.SetDock(m, Dock.Right); top.Children.Add(m); }
        top.Children.Add(Caption(Label(e) + voce, 11.5, true, foreground, false));
        text.Children.Add(top);
        text.Children.Add(Caption((hours.Length > 0 ? hours : "–") + " " + unit + (e.KmKey != null && kmValue.Length > 0 ? " · " + kmValue + " km" : ""), 12, false, foreground, false));
        bool selected = vm.Selected == e.Key || vm.Selected == e.KmKey;
        var visual = new Border { Child = text, Width = dayWidth - 4, Height = rowHeight - 6, CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand, Focusable = true,
            Background = error ? ErrorBack : Hex(back), BorderBrush = selected ? Selection : error ? Error : modified ? Selection : Brushes.Transparent,
            BorderThickness = new Thickness(selected ? 3 : error || modified ? 2 : 0),
            ToolTip = errors.FirstOrDefault() ?? e.Cell.Bloccata ?? (missing.Length > 0 ? missing : null) ?? (string.IsNullOrEmpty(nota) ? Label(e) + " – " + e.Row.Descrizione : "Nota: " + nota) };
        AutomationName(visual, $"{Label(e)}, {GeCoCalendar.Long(e.Cell.Data)}: {(hours.Length > 0 ? hours : "0")} {(unit == "h" ? "ore" : unit)}{(e.KmKey != null ? $", {(kmValue.Length > 0 ? kmValue : "0")} km" : "")}{(confirmed ? ", confermata" : "")}{(error ? ", non salvata: " + errors[0] : "")}");
        Canvas.SetLeft(visual, column * dayWidth + 2); Canvas.SetTop(visual, 4 + lane * rowHeight);
        visual.MouseLeftButtonDown += (_, m) =>
        {
            m.Handled = true;
            if (m.ClickCount == 2) { Details(e); return; }
            vm.Selected = e.Key; vm.PersonId = p.Id; Render();
        };
        return visual;
    }
    private Grid GhostVisual(HoursViewModel.Ghost g, int column, int lane, double rowHeight)
    {
        var (back, _) = vm.Color(g.Project.IdCommessa);
        var color = (Color)ColorConverter.ConvertFromString(back);
        var grid = new Grid { Width = dayWidth - 4, Height = rowHeight - 6, Cursor = Cursors.Hand, Opacity = 0.65,
            ToolTip = $"Pianificata: {g.Project.Codice}{(g.Phase != null ? " · " + g.Phase.Text : "")}, circa {GeCoCalendar.Format(g.Hours)} ore. Premi per consuntivarla con queste ore, o trascinala su un altro giorno." };
        grid.Children.Add(new Rectangle { RadiusX = 7, RadiusY = 7, Stroke = new SolidColorBrush(color), StrokeThickness = 2, StrokeDashArray = [4, 2], Fill = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B)) });
        var text = new StackPanel { Margin = new Thickness(7, 3, 5, 3) };
        text.Children.Add(Caption(g.Project.Codice + (g.Phase != null ? " · " + g.Phase.Text : ""), 11.5, true, Night, false));
        text.Children.Add(Caption("circa " + GeCoCalendar.Format(g.Hours) + " h", 12, false, Night, false));
        grid.Children.Add(text);
        AutomationName(grid, $"Attività pianificata {g.Project.Codice}{(g.Phase != null ? " " + g.Phase.Text : "")}, {GeCoCalendar.Long(vm.DateOf(g.Index))}, circa {GeCoCalendar.Format(g.Hours)} ore: premi per consuntivarla");
        Canvas.SetLeft(grid, column * dayWidth + 2); Canvas.SetTop(grid, 4 + lane * rowHeight);
        // Un trascinamento riuscito termina dentro DoDragDrop: il rilascio arriva qui solo per un clic semplice.
        grid.MouseLeftButtonUp += (_, m) => { m.Handled = true; _ = vm.AddAsync(g.P, g.Project, g.Phase, g.Index, g.Hours); };
        DragSource(grid, new Dragged(g.Project, g.Phase, g.Hours));
        return grid;
    }
    private static void AutomationName(UIElement element, string name) => System.Windows.Automation.AutomationProperties.SetName(element, name);
}
