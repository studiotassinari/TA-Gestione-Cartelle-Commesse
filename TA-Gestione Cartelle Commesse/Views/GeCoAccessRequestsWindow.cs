using System.Windows;
using System.Windows.Controls;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using static TA_Gestione_Cartelle_Commesse.Views.GeCoUi;
namespace TA_Gestione_Cartelle_Commesse.Views;

// «Richieste di accesso alle commesse» (2.42.0), come la pagina RichiesteAccesso di GeCo2027: le richieste dell'utente e, per chi
// decide, quelle da decidere (Concedi, Rifiuta con una nota), gli accessi in vigore (Revoca) e le ultime decise. I pulsanti li
// vede solo chi può usarli secondo il servizio; un passaggio non più possibile (richiesta cambiata nel frattempo) è rifiutato.
public sealed class GeCoAccessRequestsWindow : Window
{
    private readonly AccessRequestsViewModel vm;
    private readonly StackPanel content = new();
    public event Action<int>? ProjectRequested;

    public GeCoAccessRequestsWindow(AccessRequestsViewModel model)
    {
        vm = model; Prepare(this, "Richieste di accesso alle commesse — TA"); Width = 1000; Height = 760;
        var root = new DockPanel { Margin = new Thickness(18) };
        var head = new StackPanel();
        head.Children.Add(Caption("Richieste di accesso alle commesse", 20, true, Teal));
        head.Children.Add(Caption("Chi non ha una commessa tra le proprie la consulta in sola lettura e ne chiede l'accesso dalla scheda. Decidono amministratori, capocommessa della commessa e team manager; ogni passaggio arriva come notifica a chi interessa, in GeCo2027 e in GeCa.", 12, false, Grey).With(t => t.Margin = new Thickness(0, 2, 0, 10)));
        var tools = new WrapPanel(); tools.Children.Add(Push("Aggiorna", async () => await vm.LoadAsync()));
        head.Children.Add(tools);
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        var close = Push("Chiudi", Close); close.IsCancel = true; close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        vm.Updated += Render;
        Loaded += async (_, _) => await vm.LoadAsync();
        Closed += (_, _) => { vm.Updated -= Render; vm.Dispose(); };
        Render();
    }

    private void Render()
    {
        content.Children.Clear();
        if (vm.Error.Length > 0) content.Children.Add(Notice(vm.Error, "danger"));
        else if (vm.Done.Length > 0) content.Children.Add(Notice(vm.Done, "success"));
        if (vm.Requests is not { } r) { if (vm.Busy) content.Children.Add(Notice("Caricamento delle richieste…", "info")); return; }
        Section("Le mie richieste", r.Mie, "Nessuna richiesta.", x => x.Stato == "InAttesa" ? [("Annulla", async () => { if (Confirm($"Annullare la richiesta per la commessa {x.Codice}?")) await vm.CancelAsync(x); })] : []);
        if (!r.Decide) return;
        Section("Da decidere", r.DaDecidere, "Nessuna richiesta da decidere.", x =>
        [
            ("Concedi", async () => { if (Note($"Concedere a {x.Utente} l'accesso alla commessa {x.Codice}?", false) is { } n) await vm.DecideAsync(x, true, n); }),
            ("Rifiuta…", async () => { if (Note($"Motivo del rifiuto per {x.Utente} (commessa {x.Codice}):", true) is { } n) await vm.DecideAsync(x, false, n); }),
        ]);
        Section("Accessi concessi", r.Concessi, "Nessun accesso in vigore.", x =>
            [("Revoca…", async () => { if (Note($"Revocare a {x.Utente} l'accesso alla commessa {x.Codice}? Nota facoltativa:", false) is { } n) await vm.RevokeAsync(x, n); })]);
        Section("Decise negli ultimi 90 giorni", r.Decise, "Nessuna richiesta decisa di recente.", _ => []);
    }

    private void Section(string name, GeCoAccessRequest[] items, string empty, Func<GeCoAccessRequest, (string Text, Action Click)[]> actions)
    {
        var panel = new StackPanel();
        panel.Children.Add(Caption($"{name} ({items.Length})", 15, true, Teal).With(t => t.Margin = new Thickness(0, 0, 0, 6)));
        if (items.Length == 0) panel.Children.Add(Caption(empty, 12, false, Grey));
        foreach (var x in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            foreach (var (label, click) in actions(x).Reverse()) { var b = Push(label, click); b.IsEnabled = !vm.Busy; DockPanel.SetDock(b, Dock.Right); row.Children.Add(b); }
            var id = x.IdCommessa;
            var open = Link("Scheda", () => ProjectRequested?.Invoke(id), "Apri la scheda della commessa"); DockPanel.SetDock(open, Dock.Right); row.Children.Add(open);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Caption(AccessRequestsViewModel.Line(x), 13, true));
            var notes = string.Join(" · ", new[] { "Motivo: " + x.Motivo, x.NotaDecisione is { Length: > 0 } d ? "Nota: " + d : null, x.NotaRevoca is { Length: > 0 } v ? "Revoca: " + v : null }.Where(s => s != null));
            text.Children.Add(Caption(notes, 12, false, Grey));
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        content.Children.Add(Card(panel).With(b => b.Margin = new Thickness(0, 0, 0, 12)));
    }

    private bool Confirm(string text) => MessageBox.Show(this, text, "Richieste di accesso", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    // Nota della decisione: obbligatoria per il rifiuto (la regola la controlla anche il servizio); null se l'utente annulla.
    private string? Note(string question, bool required)
    {
        var dialog = new Window { Owner = this, Title = "Richieste di accesso — TA", Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Back, FontSize = 13, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Caption(question, 13).With(t => t.Margin = new Thickness(0, 0, 0, 8)));
        var box = new TextBox { MinHeight = 60, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(6, 3, 6, 3) };
        panel.Children.Add(box);
        var ok = Push("Conferma", null, true); ok.IsDefault = true; ok.IsEnabled = !required;
        var cancel = Push("Annulla", dialog.Close); cancel.IsCancel = true;
        box.TextChanged += (_, _) => ok.IsEnabled = !required || box.Text.Trim().Length > 0;
        string? result = null;
        ok.Click += (_, _) => { result = box.Text.Trim(); dialog.Close(); };
        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) }; bar.Children.Add(ok); bar.Children.Add(cancel);
        panel.Children.Add(bar);
        dialog.Content = panel;
        dialog.ShowDialog();
        return result;
    }
}
