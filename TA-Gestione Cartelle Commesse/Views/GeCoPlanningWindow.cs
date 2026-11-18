using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using static TA_Gestione_Cartelle_Commesse.Views.GeCoUi;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Pianificazione delle attività di GeCo, con la stessa impostazione della pagina Pianificazione di GeCo2027: giorni feriali in
// orizzontale con intestazione per settimana e riga scura tra le settimane, persone una sotto l'altra con le barre su righe
// parallele, pannello «Commesse e fasi» da trascinare sul giorno, barre trascinabili e allungabili, sotto ogni persona il
// numero di attività in corso e le ore medie. Ogni modifica passa dal servizio, che applica le regole della libreria GeCo.Dati.
public sealed class GeCoPlanningWindow : Window
{
    private const double RowHeight = 54;
    private readonly PlanningViewModel vm;
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
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private double dayWidth = 56;
    private (PlanningViewModel.Bar Bar, bool Resize, double X, Border Visual)? pull;
    private sealed record Dragged(GeCoPlanProject Project, GeCoPhase? Phase);

    public GeCoPlanningWindow(PlanningViewModel model)
    {
        vm = model; Prepare(this, "Pianificazione delle attività — TA");
        Content = root; root.Background = Back;
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head); root.Children.Add(body);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        var panelScroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panelCard = Card(panelScroll); body.Children.Add(panelCard);
        var right = new DockPanel(); Grid.SetColumn(right, 2); body.Children.Add(right);
        var infoCard = Card(info, new Thickness(12, 6, 12, 6)); infoCard.MinHeight = 50; infoCard.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(infoCard, Dock.Top); right.Children.Add(infoCard);
        var note = Caption("Sotto la riga di ogni persona: il numero di attività in corso e le ore medie per ciascuna (le ore della giornata divise per le attività contemporanee). L'impegno stimato di un'attività è la somma delle sue quote giornaliere. Sabati, domeniche e festività non sono considerati.", 12, false, Grey);
        note.Margin = new Thickness(0, 8, 0, 0); DockPanel.SetDock(note, Dock.Bottom); right.Children.Add(note);
        scroll.Content = timeline; right.Children.Add(Card(scroll, new Thickness(0)));
        search.TextChanged += (_, _) => { searchTimer.Stop(); searchTimer.Start(); };
        searchTimer.Tick += async (_, _) =>
        {
            searchTimer.Stop(); var text = search.Text;
            var result = await vm.SearchAsync(text);
            if (text == search.Text) ShowFound(result);
        };
        scroll.SizeChanged += (_, e) => { if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 1) Render(); };
        vm.Changed += Render;
        Loaded += async (_, _) => { if (vm.Access == null) await vm.StartAsync(); };
        Closed += (_, _) => { vm.Changed -= Render; searchTimer.Stop(); vm.Dispose(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox && Selected() is { } s && s.Item.Modificabile) { e.Handled = true; _ = Remove(s.Item); } };
        head.Children.Add(Caption("Caricamento della pianificazione…", 14, false, Grey));
        // Un modello già avviato (dalle verifiche) si disegna subito; altrimenti si parte al caricamento della finestra.
        if (vm.Access != null) Render();
    }

    private PlanningViewModel.Bar? Selected() => vm.Bars().FirstOrDefault(b => b.Item.Id == vm.Selected);
    private void Alert(string? message) { if (!string.IsNullOrEmpty(message)) MessageBox.Show(this, message, "Pianificazione"); }

    private void Render()
    {
        head.Children.Clear(); panel.Children.Clear(); info.Children.Clear(); timeline.Children.Clear();
        if (vm.Access is { } access && (!access.Recognized || !access.Planning))
        {
            // Nessun utente di GeCo per l'account Windows (o database non pronto): si mostra il motivo e non si permette nulla.
            body.Visibility = Visibility.Collapsed;
            head.Children.Add(Caption("Pianificazione delle attività", 22, true, Teal));
            head.Children.Add(Notice(access.Message.Length > 0 ? access.Message : "Pianificazione non disponibile.", "danger"));
            return;
        }
        if (vm.Plan is not { } plan) { if (vm.Error.Length > 0) head.Children.Add(Notice(vm.Error, "danger")); body.Visibility = Visibility.Collapsed; return; }
        body.Visibility = Visibility.Visible;
        RenderToolbar(plan); RenderPanel(plan); RenderInfo(plan); RenderTimeline(plan);
    }

    private void RenderToolbar(GeCoPlan plan)
    {
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
        var title = Caption("Pianificazione delle attività", 22, true, Teal, false); title.Margin = new Thickness(0, 0, 12, 6); bar.Children.Add(title);
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Right); bar.Children.Add(buttons);
        if (plan.PuoOrganizzare || plan.Ruolo == "teammanager") buttons.Children.Add(Push("Ruoli e team", () => new GeCoRolesWindow(this, vm, plan.PuoOrganizzare).ShowDialog()));
        buttons.Children.Add(Push("‹ Settimana precedente", () => _ = vm.Go(-7)));
        buttons.Children.Add(Push("Oggi", () => _ = vm.Today()));
        buttons.Children.Add(Push("Settimana successiva ›", () => _ = vm.Go(7)));
        foreach (var weeks in new[] { 2, 4, 6, 8 }) { int w = weeks; buttons.Children.Add(Push(w + " settimane", () => _ = vm.SetWeeks(w), plan.Settimane == w)); }
        head.Children.Add(bar);
        if (vm.Error.Length > 0) head.Children.Add(Notice(vm.Error, "danger"));
        if (vm.Access is { Message.Length: > 0 } a) head.Children.Add(Notice(a.Message));
        if (plan.Elenco.Length == 0) head.Children.Add(Notice("Nessuna persona da mostrare: all'utente non è abbinato un centro di costo."));
        if (plan.Ruolo == "teammanager") head.Children.Add(Caption("Come team manager vedi la pianificazione di tutti; puoi modificare solo la tua, sulle commesse di cui sei capocommessa.", 13, false, Grey));
        if (plan.Elenco.Length > 1)
        {
            var people = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
            people.Children.Add(Caption("Persone nel diagramma:", 13, false, Grey, false).With(t => t.Margin = new Thickness(0, 6, 8, 0)));
            if (plan.Persone.Length == 0) people.Children.Add(Caption("nessuna", 13, false, Grey).With(t => t.Margin = new Thickness(0, 6, 8, 0)));
            var shown = vm.Shown;
            foreach (var p in plan.Persone) { var id = p.Id; people.Children.Add(Push(p.Nome + (p.Io ? " (io)" : "") + "  ×", () => _ = vm.Choose(shown.Where(x => x != id).ToArray()), true, "Togli " + p.Nome + " dal diagramma")); }
            var missing = plan.Elenco.Where(p => !shown.Contains(p.Id)).ToArray();
            if (missing.Length > 0)
            {
                var add = new ComboBox { MinWidth = 220, MaxWidth = 280, Margin = new Thickness(0, 0, 8, 6), ToolTip = "Aggiungi una persona al diagramma" };
                add.Items.Add(new ComboBoxItem { Content = "+ Aggiungi persona…", IsSelected = true });
                foreach (var p in missing) add.Items.Add(new ComboBoxItem { Content = p.Nome, Tag = p.Id });
                add.SelectionChanged += (_, _) => { if ((add.SelectedItem as ComboBoxItem)?.Tag is int id) _ = vm.Choose(shown.Append(id).ToArray()); };
                people.Children.Add(add);
                people.Children.Add(Link("tutte", () => _ = vm.Choose(plan.Elenco.Select(p => p.Id).ToArray())));
            }
            people.Children.Add(Link("solo io", () => _ = vm.Choose(null)));
            head.Children.Add(people);
        }
    }

    private void RenderPanel(GeCoPlan plan)
    {
        panelCard.Visibility = plan.PuoPianificare ? Visibility.Visible : Visibility.Collapsed;
        body.ColumnDefinitions[0].Width = new GridLength(plan.PuoPianificare ? 300 : 0); body.ColumnDefinitions[1].Width = new GridLength(plan.PuoPianificare ? 16 : 0);
        if (!plan.PuoPianificare) return;
        panel.Children.Add(Caption("Commesse e fasi", 14, true));
        panel.Children.Add(Caption("Trascina una fase sulla riga della persona, nel giorno in cui inizia; poi allungala sui giorni che servono.", 12, false, Grey).With(t => t.Margin = new Thickness(0, 2, 0, 8)));
        panel.Children.Add(search); panel.Children.Add(found);
        var projects = vm.Projects();
        if (projects.Length == 0) panel.Children.Add(Caption("Cerca una commessa per iniziare.", 13, false, Grey).With(t => t.Margin = new Thickness(0, 10, 0, 0)));
        foreach (var c in projects)
        {
            var title = new DockPanel { Margin = new Thickness(0, 12, 0, 2), ToolTip = c.Cliente };
            title.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Background = Hex(vm.Color(c.Id).Back), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(Caption(c.Codice, 13, true, null, false).With(t => t.Margin = new Thickness(0, 0, 6, 0)));
            title.Children.Add(Caption(c.Descrizione, 12, false, Grey, false));
            panel.Children.Add(title);
            foreach (var f in c.Fasi.Length > 0 ? c.Fasi.Cast<GeCoPhase?>() : [null])
            {
                var phase = f; var project = c;
                var b = Push("≡  " + (f?.Descrizione ?? "Commessa (senza fasi)"), () => _ = Add(vm.ChosenPerson, project, phase, vm.Day ?? Math.Max(0, vm.TodayIndex)),
                    tip: "Trascina sulla riga della persona, oppure premi per aggiungerla alla persona e al giorno scelti");
                b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Background = Track; b.Margin = new Thickness(0, 4, 0, 0); b.Cursor = Cursors.SizeAll;
                DragSource(b, new Dragged(project, phase));
                panel.Children.Add(b);
            }
        }
    }
    private void ShowFound(GeCoPlanProject[] result)
    {
        found.Children.Clear();
        foreach (var c in result)
        {
            var project = c;
            var b = Push(new TextBlock { Inlines = { new System.Windows.Documents.Bold(new System.Windows.Documents.Run(c.Codice + " ")), new System.Windows.Documents.Run(c.Descrizione) }, TextTrimming = TextTrimming.CharacterEllipsis },
                () => { vm.Keep(project); search.Text = ""; found.Children.Clear(); });
            b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 2, 0, 0); found.Children.Add(b);
        }
    }
    private async Task Add(GeCoShownPerson? person, GeCoPlanProject project, GeCoPhase? phase, int day) => Alert(await vm.AddAsync(person, project, phase, day));
    private async Task Remove(GeCoActivity activity)
    {
        if (MessageBox.Show(this, "Togliere questa attività dal piano?", "Pianificazione", MessageBoxButton.YesNo) == MessageBoxResult.Yes) Alert(await vm.RemoveAsync(activity));
    }

    private void RenderInfo(GeCoPlan plan)
    {
        if (Selected() is not { } s)
        {
            info.Children.Add(Caption("Trascina un'attività per spostarla; trascina il suo bordo destro per allungarla su più giornate. Le attività possono sovrapporsi: le ore della giornata si dividono in parti uguali tra quelle in corso.", 13, false, Grey));
            return;
        }
        var person = vm.PlanOf(s.Item.IdCdc);
        var what = new StackPanel { Margin = new Thickness(0, 0, 18, 0), MinWidth = 220 };
        what.Children.Add(Caption(vm.Code(s.Item.IdCommessa) + (s.Item.Fase.Length > 0 ? " · " + s.Item.Fase : ""), 13, true));
        string first = vm.DateOf(s.Start), last = vm.DateOf(s.Start + s.Length - 1);
        what.Children.Add(Caption(s.Length == 1 ? GeCoCalendar.Short(first) : $"dal {GeCoCalendar.Short(first)} al {GeCoCalendar.Short(last)}", 12, false, Grey));
        info.Children.Add(what);
        if (s.Item.Modificabile)
        {
            info.Children.Add(Caption("Giorni", 13).With(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 6, 6); }));
            info.Children.Add(Push("−", () => _ = Move(s.Item, s.Start, s.Length - 1), tip: "Un giorno in meno").With(b => b.IsEnabled = s.Length > 1));
            info.Children.Add(Caption(s.Length.ToString(), 13, true).With(t => { t.MinWidth = 24; t.TextAlignment = TextAlignment.Center; t.Margin = new Thickness(0, 6, 8, 0); }));
            info.Children.Add(Push("+", () => _ = Move(s.Item, s.Start, s.Length + 1), tip: "Un giorno in più"));
        }
        info.Children.Add(Caption("Impegno stimato circa " + Hours(person.Estimate(s)), 13, true).With(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(10, 0, 12, 6); }));
        if (s.Item.Modificabile) info.Children.Add(Push("Togli dal piano", () => _ = Remove(s.Item)).With(b => b.Foreground = Danger));
    }
    private async Task Move(GeCoActivity activity, int start, int length) => Alert(await vm.MoveAsync(activity, start, Math.Max(1, length)));
    private static string Hours(decimal hours) => GeCoCalendar.Total(Math.Round(hours, 1)) + " h";

    private void RenderTimeline(GeCoPlan plan)
    {
        int n = vm.Days; if (n == 0) return;
        dayWidth = Math.Max(56, (scroll.ViewportWidth > 0 ? scroll.ViewportWidth : scroll.ActualWidth) - 24) / n;
        double width = dayWidth * n;
        var weeks = new Canvas { Width = width, Height = 20 };
        for (int w = 0; w < plan.Settimane; w++)
        {
            var label = Caption($"{GeCoCalendar.Short(vm.DateOf(w * 5))} – {GeCoCalendar.Short(vm.DateOf(w * 5 + 4))}", 12, true, Grey, false);
            label.Width = dayWidth * 5 - 8; Canvas.SetLeft(label, w * 5 * dayWidth + 6); weeks.Children.Add(label);
            if (w > 0) { var line = new Rectangle { Width = 2, Height = 20, Fill = Night }; Canvas.SetLeft(line, w * 5 * dayWidth); weeks.Children.Add(line); }
        }
        timeline.Children.Add(weeks);
        var days = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        int today = vm.TodayIndex;
        for (int g = 0; g < n; g++)
        {
            int day = g; var date = vm.DateOf(g);
            var content = new StackPanel(); content.Children.Add(Caption(GeCoCalendar.DayNames[g % 5], 12, true, null, false).With(t => t.TextAlignment = TextAlignment.Center));
            content.Children.Add(Caption(GeCoCalendar.Date(date).Day.ToString(), 11, false, Grey, false).With(t => t.TextAlignment = TextAlignment.Center));
            var b = Push(content, () => { vm.Day = day; Render(); }, tip: GeCoCalendar.Long(date) + ": scegli il giorno per aggiungere una fase con un clic");
            b.Width = dayWidth - 2; b.Margin = new Thickness(1, 0, 1, 0); b.Padding = new Thickness(0, 2, 0, 2); b.BorderThickness = new Thickness(2);
            b.Background = g == today ? TodayBack : Brushes.White; b.BorderBrush = vm.Day == g ? Teal : Brushes.Transparent;
            days.Children.Add(b);
        }
        timeline.Children.Add(days);
        foreach (var p in plan.Persone) timeline.Children.Add(PersonTrack(plan, p, n, width));
    }

    private UIElement PersonTrack(GeCoPlan plan, GeCoShownPerson p, int n, double width)
    {
        var pp = vm.PlanOf(p.Id);
        var box = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (plan.Persone.Length > 1)
        {
            bool chosen = vm.ChosenPerson?.Id == p.Id;
            var label = new TextBlock { Inlines = { new System.Windows.Documents.Bold(new System.Windows.Documents.Run(p.Nome)), new System.Windows.Documents.Run(p.Io ? " (io)" : ""),
                new System.Windows.Documents.Run("    " + (pp.Bars.Length == 0 ? "nessuna attività nel periodo" : pp.Bars.Length == 1 ? "1 attività" : pp.Bars.Length + " attività")) { Foreground = Grey, FontSize = 12 } } };
            var b = Push(label, () => { vm.Person = p.Id; Render(); }, tip: "Scegli questa persona per aggiungere una fase con un clic");
            b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Width = width; b.Margin = new Thickness(0, 0, 0, 3); b.Background = PersonBack;
            b.BorderBrush = chosen && plan.PuoPianificare ? Teal : Brushes.Transparent; box.Children.Add(b);
        }
        var track = new Canvas { Width = width, Height = 12 + pp.Rows * RowHeight, Background = Track, ClipToBounds = true, AllowDrop = true };
        for (int g = 1; g < n; g++) { var line = new Rectangle { Width = g % 5 == 0 ? 2 : 1, Height = track.Height, Fill = g % 5 == 0 ? Night : Rule }; Canvas.SetLeft(line, g * dayWidth - (g % 5 == 0 ? 1 : 0)); track.Children.Add(line); }
        foreach (var bar in pp.Bars.Where(b => b.Start < n && b.Start + b.Length > 0)) track.Children.Add(BarVisual(bar, pp, track));
        // Bersaglio del trascinamento dal pannello: il giorno sotto il puntatore si evidenzia.
        var target = new Rectangle { Width = dayWidth, Height = track.Height, Fill = Hex("#2E135F6E"), Stroke = Teal, StrokeThickness = 2, StrokeDashArray = [3, 2], Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        track.Children.Add(target);
        int DayAt(DragEventArgs e) => Math.Clamp((int)(e.GetPosition(track).X / dayWidth), 0, n - 1);
        track.DragOver += (_, e) =>
        {
            var ok = e.Data.GetData(DragFormat) is Dragged d && PlanningViewModel.CanPlanOn(p, d.Project);
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
            target.Visibility = ok ? Visibility.Visible : Visibility.Collapsed; Canvas.SetLeft(target, DayAt(e) * dayWidth);
        };
        track.DragLeave += (_, _) => target.Visibility = Visibility.Collapsed;
        track.Drop += (_, e) => { target.Visibility = Visibility.Collapsed; if (e.Data.GetData(DragFormat) is Dragged d) _ = Add(p, d.Project, d.Phase, DayAt(e)); };
        box.Children.Add(new Border { BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = track, Width = width + 2, HorizontalAlignment = HorizontalAlignment.Left });
        var load = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        for (int g = 0; g < n; g++)
        {
            int count = pp.Running(g);
            var cell = new Border { Width = dayWidth - 2, Margin = new Thickness(1, 0, 1, 0), CornerRadius = new CornerRadius(6), Padding = new Thickness(0, 2, 0, 2), Background = count > 3 ? DangerBack : Neutral };
            cell.Child = Caption(count == 0 ? "–" : $"{count} · {Hours((decimal)plan.OreGiorno / count)}", 11, count > 0, count > 3 ? Danger : Night, false).With(t => t.TextAlignment = TextAlignment.Center);
            load.Children.Add(cell);
        }
        box.Children.Add(load);
        return box;
    }

    private Border BarVisual(PlanningViewModel.Bar bar, PlanningViewModel.PersonPlan pp, Canvas track)
    {
        var a = bar.Item; var (back, fore) = vm.Color(a.IdCommessa); var foreground = Hex(fore);
        var text = new StackPanel { Margin = new Thickness(9, 3, 16, 3) };
        text.Children.Add(Caption(vm.Code(a.IdCommessa) + (a.Fase.Length > 0 ? " · " + a.Fase : ""), 12, true, foreground, false));
        text.Children.Add(Caption((bar.Length == 1 ? "1 giorno" : bar.Length + " giorni") + " · circa " + Hours(pp.Estimate(bar)), 11, false, foreground, false));
        var grid = new Grid(); grid.Children.Add(text);
        var visual = new Border { Background = Hex(back), CornerRadius = new CornerRadius(7), Child = grid, Height = RowHeight - 8, Cursor = a.Modificabile ? Cursors.SizeAll : Cursors.Hand,
            BorderBrush = vm.Selected == a.Id ? Selection : Brushes.Transparent, BorderThickness = new Thickness(vm.Selected == a.Id ? 3 : 0), ToolTip = vm.Code(a.IdCommessa) + (a.Fase.Length > 0 ? " · " + a.Fase : "") };
        Place(visual, bar.Start, bar.Length); Canvas.SetTop(visual, 6 + pp.Row[a.Id] * RowHeight);
        if (a.Modificabile)
        {
            var handle = new Border { Width = 14, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, Cursor = Cursors.SizeWE, ToolTip = "Trascina per allungare o accorciare l'attività",
                Child = new Border { Width = 3, Height = 20, CornerRadius = new CornerRadius(2), Background = foreground, Opacity = 0.75 } };
            grid.Children.Add(handle);
            handle.MouseLeftButtonDown += (_, e) => { Begin(bar, true, e, visual); e.Handled = true; };
        }
        visual.MouseLeftButtonDown += (_, e) => { if (a.Modificabile) Begin(bar, false, e, visual); else { vm.Selected = a.Id; Render(); } e.Handled = true; };
        visual.MouseMove += (_, e) =>
        {
            if (pull is not { } t || !ReferenceEquals(t.Visual, visual)) return;
            int delta = (int)Math.Round((e.GetPosition(track).X - t.X) / dayWidth);
            vm.Draft = t.Resize ? (a.Id, t.Bar.Start, Math.Clamp(t.Bar.Length + delta, 1, 250)) : (a.Id, t.Bar.Start + delta, t.Bar.Length);
            Place(visual, vm.Draft.Value.Start, vm.Draft.Value.Length);
        };
        visual.MouseLeftButtonUp += async (_, _) =>
        {
            if (pull is not { } t || !ReferenceEquals(t.Visual, visual)) return;
            pull = null; visual.ReleaseMouseCapture();
            if (vm.Draft is { } d && (d.Start != t.Bar.Start || d.Length != t.Bar.Length)) await Move(a, d.Start, d.Length);
            else { vm.Draft = null; Render(); }
        };
        visual.LostMouseCapture += (_, _) => { if (pull is { } t && ReferenceEquals(t.Visual, visual)) { pull = null; vm.Draft = null; Render(); } };
        return visual;
        void Begin(PlanningViewModel.Bar b, bool resize, MouseButtonEventArgs e, Border v)
        {
            vm.Selected = a.Id; pull = (b, resize, e.GetPosition(track).X, v); v.CaptureMouse();
            v.BorderBrush = Selection; v.BorderThickness = new Thickness(3); info.Children.Clear(); RenderInfo(vm.Plan!);
        }
    }
    private void Place(FrameworkElement element, int start, int length)
    {
        Canvas.SetLeft(element, start * dayWidth + 2); element.Width = Math.Max(8, length * dayWidth - 4);
    }
}

internal static class GeCoUiExtensions
{
    // Imposta qualche proprietà e restituisce l'elemento: tiene compatta la costruzione delle viste in codice.
    public static T With<T>(this T element, Action<T> set) { set(element); return element; }
}
