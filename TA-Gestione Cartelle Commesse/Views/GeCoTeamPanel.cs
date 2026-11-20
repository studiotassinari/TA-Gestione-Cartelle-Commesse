using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using static TA_Gestione_Cartelle_Commesse.Views.GeCoUi;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Sezione «Team della commessa (GeCo)» (2.41.0), come la sezione «Team» della scheda della commessa di GeCo2027: persone con il
// ruolo (capocommessa in blu, membri indicati uno per uno, membri dei team permanenti con il nome del team). In lettura ha
// «Modifica team…» per chi può; nella finestra di modifica (editing) ha anche «Togli» e le scelte per aggiungere. Chi può
// modificare lo dice il servizio (PuoModificare): qui si nascondono soltanto i comandi.
internal sealed class GeCoTeamPanel : Border
{
    private readonly ProjectTeamViewModel vm;
    private readonly bool editing;
    private readonly StackPanel body = new();
    private readonly Action<string> showRefreshed;
    public event Action? EditRequested;

    public GeCoTeamPanel(ProjectTeamViewModel model, bool editing = false)
    {
        vm = model; this.editing = editing;
        Background = Brushes.White; BorderBrush = Rule; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8); Padding = new Thickness(12);
        var root = new StackPanel();
        root.Children.Add(body);
        root.Children.Add(Refreshed(out showRefreshed));
        Child = root;
        vm.Updated += Render; vm.Live.Refreshed += showRefreshed;
        Unloaded += (_, _) => { vm.Updated -= Render; vm.Live.Refreshed -= showRefreshed; };
        Loaded += (_, _) => { vm.Updated -= Render; vm.Live.Refreshed -= showRefreshed; vm.Updated += Render; vm.Live.Refreshed += showRefreshed; Render(); };
        Render();
    }

    private void Render()
    {
        body.Children.Clear();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (!editing && vm.CanEdit)
        {
            var edit = Push("Modifica team…", () => EditRequested?.Invoke(), true, "Aggiungi o togli persone e team permanenti (le modifiche valgono subito anche in GeCo2027)");
            DockPanel.SetDock(edit, Dock.Right); head.Children.Add(edit);
        }
        var update = Link("Aggiorna", async () => await vm.LoadAsync()); DockPanel.SetDock(update, Dock.Right); head.Children.Add(update);
        var t = vm.Team;
        head.Children.Add(Caption(editing ? "Persone del team" : "Team della commessa (GeCo)" + (t == null ? "" : $" · {t.Componenti.Length} {(t.Componenti.Length == 1 ? "persona" : "persone")}"), 15, true, Teal));
        body.Children.Add(head);
        if (vm.Error.Length > 0) body.Children.Add(Notice(vm.Error, "danger"));
        if (vm.Message.Length > 0) body.Children.Add(Notice(vm.Message, "info"));
        if (vm.Busy && t == null) body.Children.Add(Caption("Caricamento del team…", 12, false, Grey));
        if (t == null) return;
        if (!editing) body.Children.Add(Caption($"Commessa {t.Codice}" + (t.Descrizione.Length > 0 ? " – " + t.Descrizione : ""), 12, false, Grey).With(c => c.Margin = new Thickness(0, 0, 0, 6)));
        if (t.Componenti.Length == 0) body.Children.Add(Caption("Nessuna persona nel team." + (t.PuoModificare && !editing ? " Con «Modifica team…» si aggiungono le persone o un team permanente." : ""), 12, false, Grey));
        foreach (var p in t.Componenti) body.Children.Add(Person(p, editing && t.PuoModificare && p.Diretto ? p.Id : null));
        if (!editing) return;

        // Modifica: persone indicate una per una, poi i team permanenti (dalla migrazione 9).
        var addable = vm.Addable;
        if (t.PuoModificare && addable.Length > 0)
        {
            var add = new ComboBox { MinWidth = 260, MinHeight = 32, Margin = new Thickness(0, 8, 0, 6), IsEnabled = vm.CanEdit, ToolTip = "Aggiungi una persona al team",
                ItemsSource = new[] { new GeCoTeamPerson(0, "+ Aggiungi persona…") }.Concat(addable).ToList(), DisplayMemberPath = "Nome", SelectedIndex = 0 };
            add.SelectionChanged += async (_, _) => { if (add.SelectedItem is GeCoTeamPerson { Id: > 0 } p) await vm.SetMemberAsync(p.Id, true); };
            body.Children.Add(add);
        }
        if (t.Team == null) return;
        body.Children.Add(Caption("Team permanenti", 15, true, Teal).With(c => c.Margin = new Thickness(0, 14, 0, 6)));
        if (t.Team.Length == 0) body.Children.Add(Caption("Nessun team permanente assegnato.", 12, false, Grey));
        foreach (var team in t.Team)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), Opacity = team.Attivo ? 1 : 0.6 };
            if (t.PuoModificare)
            {
                var id = team.Id;
                var remove = Push("Togli", async () => await vm.SetPermanentAsync(id, false), tip: "Togli il team " + team.Nome + " dalla commessa");
                remove.IsEnabled = vm.CanEdit; DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            }
            var text = new StackPanel();
            text.Children.Add(Caption(team.Nome + (team.Attivo ? "" : " (non attivo: non vale)"), 13, true));
            text.Children.Add(Caption(team.Membri.Length == 0 ? "nessuna persona" : string.Join(", ", team.Membri.Select(m => m.Nome)), 12, false, Grey));
            row.Children.Add(text);
            body.Children.Add(Card(row, new Thickness(10, 6, 10, 6)).With(c => c.Margin = new Thickness(0, 0, 0, 6)));
        }
        if (t.PuoModificare && t.TeamDisponibili is { Length: > 0 } free)
        {
            var assign = new ComboBox { MinWidth = 260, MinHeight = 32, Margin = new Thickness(0, 4, 0, 0), IsEnabled = vm.CanEdit, ToolTip = "Assegna un team permanente alla commessa",
                ItemsSource = new[] { new GeCoTeamPerson(0, "+ Assegna team permanente…") }.Concat(free).ToList(), DisplayMemberPath = "Nome", SelectedIndex = 0 };
            assign.SelectionChanged += async (_, _) => { if (assign.SelectedItem is GeCoTeamPerson { Id: > 0 } p) await vm.SetPermanentAsync(p.Id, true); };
            body.Children.Add(assign);
        }
    }

    // Riga di una persona: iniziali nel cerchio, nome, ruolo; «Togli» solo per le persone indicate una per una.
    private UIElement Person(GeCoTeamMember p, int? removable)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), Opacity = p.Attiva ? 1 : 0.6 };
        var circle = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = p.Capocommessa ? Night : Teal, Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock { Text = p.Iniziali, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            ToolTip = p.Nome + " – " + p.Ruolo };
        DockPanel.SetDock(circle, Dock.Left); row.Children.Add(circle);
        if (removable is int id)
        {
            var remove = Push("Togli", async () => await vm.SetMemberAsync(id, false), tip: "Togli " + p.Nome + " dal team");
            remove.IsEnabled = vm.CanEdit; DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Caption(p.Nome + (p.Attiva ? "" : " (non attiva)"), 13, true, null, false));
        text.Children.Add(Caption(p.Ruolo, 12, false, Grey, false));
        row.Children.Add(text);
        return row;
    }
}

// Finestra «Team della commessa (GeCo)»: la sezione in modifica, con il testo che spiega a cosa serve il team.
internal sealed class GeCoTeamWindow : Window
{
    public GeCoTeamWindow(ProjectTeamViewModel model, bool shared = false)
    {
        Prepare(this, "Team della commessa — TA"); Width = 720; Height = 760; MinWidth = 520; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(18) };
        var head = new StackPanel();
        var title = Caption("Team della commessa (GeCo)", 20, true, Teal);
        head.Children.Add(title);
        head.Children.Add(Caption("Le persone del team, indicate una per una o tramite un team permanente, trovano la commessa con le sue fasi già pronta nelle ore lavorate. È lo stesso team della scheda della commessa di GeCo2027: le modifiche valgono subito anche là.", 12, false, Grey).With(c => c.Margin = new Thickness(0, 4, 0, 12)));
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        var close = Push("Chiudi", Close, true); close.IsCancel = true; close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = new GeCoTeamPanel(model, editing: true), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        void Title() { if (model.Team is { } t) title.Text = $"Team della commessa {t.Codice} (GeCo)"; }
        model.Updated += Title; Title();
        Loaded += async (_, _) => { if (model.Team == null) await model.LoadAsync(); };
        // La finestra aperta da un elenco ha un proprio modello; quella aperta dalla sezione lo condivide e non lo chiude.
        Closed += (_, _) => { model.Updated -= Title; if (!shared) model.Dispose(); };
    }
}
