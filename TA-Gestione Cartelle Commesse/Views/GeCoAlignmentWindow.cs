using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using static TA_Gestione_Cartelle_Commesse.Views.GeCoUi;
namespace TA_Gestione_Cartelle_Commesse.Views;

// «Allineamento» (pagina Allineamento di GeCo2027, solo amministratori di GeCo): commesse e cartelle, clienti di GeCo con il nome
// breve delle cartelle, clienti di GeCa da abbinare o importare, cartelle chieste. Ogni azione chiede conferma e la esegue il
// servizio con le regole della libreria GeCo.Dati; le cartelle sul disco si creano solo con la creazione da modello del server,
// mai si modificano. Le proposte restano proposte finché l'amministratore non le conferma.
public sealed class GeCoAlignmentWindow : Window
{
    private readonly AlignmentViewModel vm;
    private readonly StackPanel messages = new();
    private readonly TextBlock summary = Caption("", 12, false, Grey);
    private readonly ComboBox section = new() { ItemsSource = AlignmentViewModel.Sections, SelectedIndex = 0, MinWidth = 220, MinHeight = 32, Margin = new Thickness(0, 0, 8, 6) };
    private readonly ComboBox view = new() { ItemsSource = AlignmentViewModel.ProjectViews, SelectedIndex = 0, MinWidth = 130, MinHeight = 32, Margin = new Thickness(0, 0, 8, 6) };
    private readonly CheckBox archived = new() { Content = "Comprese archiviate", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 6),
        ToolTip = "Mostra anche le commesse chiuse o archiviate e le cartelle in archivio (ZIP): una cartella archiviata vale come cartella della commessa" };
    private readonly TextBox search = new() { MinWidth = 240, MinHeight = 32, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 6), ToolTip = "Cerca in tutte le colonne" };
    private readonly Button safe;
    private readonly DataGrid grid = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderThickness = new Thickness(0) };
    private readonly TextBlock detail = Caption("", 12, false, Grey);
    private readonly WrapPanel actions = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public GeCoAlignmentWindow(AlignmentViewModel model)
    {
        vm = model; Prepare(this, "Allineamento GeCo e GeCa — TA");
        vm.Ask = text => MessageBox.Show(this, text, "Allineamento", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        var root = new DockPanel { Margin = new Thickness(18) };
        var head = new StackPanel();
        head.Children.Add(Caption("Allineamento GeCo e GeCa", 20, true, Teal));
        head.Children.Add(Caption("Commesse con le loro cartelle (una cartella archiviata in ZIP vale come cartella della commessa), cartelle senza commessa, clienti di GeCo con il nome breve usato nelle cartelle e clienti di GeCa da portare in GeCo. Il sistema propone, l'amministratore conferma; i collegamenti tra cartelle e commesse si confermano in Collegamento cartelle.", 12, false, Grey));
        head.Children.Add(summary.With(t => t.Margin = new Thickness(0, 6, 0, 8)));
        head.Children.Add(messages);
        var tools = new WrapPanel();
        tools.Children.Add(section); tools.Children.Add(view); tools.Children.Add(archived); tools.Children.Add(search);
        tools.Children.Add(Push("Aggiorna", async () => await vm.LoadAsync()));
        safe = Push("Conferma i nomi brevi sicuri", async () => await vm.ConfirmSafeShortNamesAsync(), true, "Imposta in una volta i nomi brevi proposti come sicuri; il servizio li ricontrolla e li scrive tutti o nessuno");
        tools.Children.Add(safe);
        head.Children.Add(tools);
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        bottom.Children.Add(detail); bottom.Children.Add(actions);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        grid.SelectionChanged += (_, _) => ShowSelection();
        grid.MouseDoubleClick += async (_, _) => { if (actions.Children.OfType<Button>().FirstOrDefault(b => b.IsEnabled && b.Tag is "main") is { } b) await Click(b); };
        root.Children.Add(Card(grid, new Thickness(0)));
        Content = root;

        search.TextChanged += (_, _) => { searchTimer.Stop(); searchTimer.Start(); };
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); vm.Filter = search.Text; Render(); };
        section.SelectionChanged += (_, _) => { vm.Section = section.SelectedItem as string ?? AlignmentViewModel.Projects; Render(); };
        view.SelectionChanged += (_, _) => { vm.View = view.SelectedItem as string ?? AlignmentViewModel.ToFix; Render(); };
        archived.Checked += (_, _) => { vm.IncludeArchived = true; Render(); };
        archived.Unchecked += (_, _) => { vm.IncludeArchived = false; Render(); };
        vm.Changed += Render;
        Loaded += async (_, _) => await vm.StartAsync();
        Closed += (_, _) => { vm.Changed -= Render; vm.Dispose(); };
        Render();
    }

    private static Task Click(Button b) { b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); return Task.CompletedTask; }
    private object? Selected => (grid.SelectedItem as AlignmentViewModel.Row)?.Source;

    private void Render()
    {
        messages.Children.Clear();
        if (vm.Denied.Length > 0) messages.Children.Add(Notice(vm.Denied, "info"));
        if (vm.Error.Length > 0) messages.Children.Add(Notice(vm.Error, "danger"));
        else if (vm.Done.Length > 0) messages.Children.Add(Notice(vm.Done, "success"));
        if (vm.LocalMessage.Length > 0 && vm.Section == AlignmentViewModel.External) messages.Children.Add(Notice(vm.LocalMessage));
        if (vm.Busy) messages.Children.Add(Notice("Aggiornamento in corso…", "info"));
        summary.Text = vm.Summary;
        safe.Content = vm.SafeShortNames > 0 ? $"Conferma i nomi brevi sicuri ({vm.SafeShortNames})" : "Conferma i nomi brevi sicuri";
        safe.IsEnabled = vm.Allowed && !vm.Busy && vm.SafeShortNames > 0;
        section.IsEnabled = search.IsEnabled = archived.IsEnabled = vm.Allowed;
        view.Visibility = vm.Section == AlignmentViewModel.Projects ? Visibility.Visible : Visibility.Collapsed;
        var titles = AlignmentViewModel.Columns[vm.Section];
        grid.Columns.Clear();
        var widths = new[] { 160.0, 110, 300, 120, 200, 300, 320 };
        for (int i = 0; i < titles.Length; i++)
            grid.Columns.Add(new DataGridTextColumn { Header = titles[i], Binding = new Binding(((char)('A' + i)).ToString()), Width = new DataGridLength(widths[i]) });
        var keep = Selected;
        var items = vm.Items().ToList();
        grid.ItemsSource = items;
        grid.SelectedItem = items.FirstOrDefault(i => Equals(i.Source, keep));
        ShowSelection();
    }

    // Azioni della riga scelta, secondo la sezione; disattivate dove le regole della libreria non le consentono già in partenza.
    private void ShowSelection()
    {
        actions.Children.Clear();
        bool on = vm.Allowed && !vm.Busy;
        Button Add(string text, Func<Task> run, bool enabled, bool main = false, string? tip = null)
        { var b = Push(text, async () => await run(), main, tip); b.IsEnabled = on && enabled; if (main) b.Tag = "main"; actions.Children.Add(b); return b; }
        switch (Selected)
        {
            case GeCoAlignmentProject c:
                detail.Text = $"Commessa {c.Codice} — {AlignmentViewModel.Situation(c.Situazione)} ({c.Stato.ToLowerInvariant()})"
                    + (c.Cartella is { } linked ? "\nCartella collegata: " + linked.Percorso : c.Proposta is { } proposedFolder ? "\nCartella proposta (da confermare in Collegamento cartelle): " + proposedFolder.Percorso : "")
                    + (c.Motivo is { Length: > 0 } reason ? "\n" + reason : "") + (c.Richiesta is { } asked ? $"\nCartella chiesta il {asked.DataRichiesta} da {asked.Utente}: {asked.StatoTesto.ToLowerInvariant()}" + (asked.Esito is { Length: > 0 } outcome ? " — " + outcome : "") : "")
                    + (!c.PuoRichiedereCartella && c.MotivoNoRichiesta is { Length: > 0 } no ? "\nCartella non richiedibile: " + no : "");
                Add("Richiedi cartella…", () => AskFolder("commessa", c.Id, c.Codice), c.PuoRichiedereCartella, true, "Nome con le regole della creazione da modello; la cartella si crea sul server");
                Add("Annulla richiesta", () => vm.CancelRequestAsync(c.Richiesta!), c.Richiesta is { Stato: "InAttesa" });
                break;
            case GeCoAlignmentFolder f:
                detail.Text = $"{f.Cartella.Percorso}\nCommittente: {f.Cartella.Committente ?? "—"} · Oggetto: {f.Cartella.Oggetto ?? "—"}"
                    + (f.CandidataPer.Length > 0 ? "\nCandidata (da verificare) per: " + string.Join(", ", f.CandidataPer.Select(e => e.Tipo + " " + e.Codice)) + " — collegarla in Collegamento cartelle" : "")
                    + (f.MotivoNoCreazione is { Length: > 0 } why ? "\nNon si può creare: " + why : "");
                Add(f.Creabile == "gara" ? "Crea gara in GeCo…" : "Crea commessa in GeCo…", () => CreateFromFolder(f), f.Creabile != null, true);
                break;
            case GeCoAlignmentCustomer c:
                detail.Text = $"Cliente {c.Codice} {c.RagioneSociale}" + (c.Proposta is { } np ? $"\nNome breve proposto «{np.NomeBreve}» ({np.Motivo}; {np.Cartelle} cartelle su {np.Totale})" + (np.Alternative.Length > 0 ? ", alternative: " + string.Join(", ", np.Alternative) : "") : "")
                    + (c.Problemi.Length > 0 ? "\n" + string.Join("\n", c.Problemi) + "\nP.IVA e codice fiscale si correggono nella pagina Clienti di GeCo2027." : "");
                Add("Conferma il nome breve proposto", () => vm.SetShortNameAsync(c, c.Proposta!.NomeBreve), c.Proposta != null, true);
                Add("Imposta nome breve…", () => ShortName(c), true);
                break;
            case GeCoExternalCustomer c:
                detail.Text = $"{c.Nome} — {AlignmentViewModel.ExternalState(c)}: {c.Motivo}" + (c.Commesse > 0 ? $"\nCitato da {c.Commesse} commesse del gestionale di GeCa." : "");
                Add("Collega a un cliente di GeCo…", () => LinkCustomer(c), c.Collegato == null && c.Esito != "Abbinato", true);
                Add("Togli collegamento", () => vm.LinkCustomerAsync(c, null), c.Collegato != null);
                Add("Importa in GeCo…", () => ImportCustomer(c), c.Collegato == null && c.Esito is "SoloEsterno", tip: "Inserisce il cliente in GeCo con i campi obbligatori della pagina Clienti");
                break;
            case GeCoFolderRequest q:
                detail.Text = $"{q.Nome}\n{q.StatoTesto}" + (q.Esito is { Length: > 0 } result ? ": " + result : "") + (q.Percorso is { Length: > 0 } where ? "\n" + where : "");
                Add("Annulla richiesta", () => vm.CancelRequestAsync(q), q.Stato == "InAttesa", true);
                break;
            case GeCoDifferentClient d:
                detail.Text = $"La cartella {d.Cartella.Nome} della commessa {d.Codice} ha il committente «{d.Cartella.Committente}», il nome breve del cliente {d.Cliente} è «{d.NomeBreve}». Verificare il nome breve o il nome della cartella (in GeCa le cartelle non si rinominano da qui).";
                break;
            case GeCoUnknownClient u:
                detail.Text = $"«{u.Committente}» compare in {u.Cartelle} cartelle senza commessa ma non è il nome breve di nessun cliente di GeCo."
                    + (u.Proposti.Length > 0 ? " Clienti proposti (da verificare): " + string.Join(", ", u.Proposti.Select(p => p.RagioneSociale + " — " + p.Motivo)) + ". Si può impostare il nome breve dalla sezione dei clienti di GeCo." : "");
                break;
            default: detail.Text = vm.Allowed ? "Selezionare una riga." : ""; break;
        }
        var close = Push("Chiudi", Close); close.IsCancel = true; actions.Children.Add(close);
    }

    // ---------------------------------------------------------------- finestre di dialogo

    private (Window Dialog, StackPanel Body, WrapPanel Bar) Dialog(string title, double height = 640)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 760, Height = height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Back, FontSize = 13 };
        var body = new StackPanel { Margin = new Thickness(16) };
        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var dock = new DockPanel(); DockPanel.SetDock(bar, Dock.Bottom); dock.Children.Add(bar);
        dock.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = new Border { Padding = new Thickness(4), Child = dock };
        return (dialog, body, bar);
    }
    private static TextBox Field(StackPanel body, string label, string? value = "")
    {
        body.Children.Add(Caption(label, 12, true));
        var box = new TextBox { Text = value ?? "", MinHeight = 30, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 2, 0, 8) };
        body.Children.Add(box); return box;
    }
    private static ComboBox Choice<T>(StackPanel body, string label, IEnumerable<T> items, string display)
    {
        body.Children.Add(Caption(label, 12, true));
        var box = new ComboBox { ItemsSource = items.ToList(), DisplayMemberPath = display, MinHeight = 30, Margin = new Thickness(0, 2, 0, 8) };
        body.Children.Add(box); return box;
    }
    private sealed record Option(int? Id, string Text);

    private async Task AskFolder(string type, int id, string code)
    {
        var (dialog, body, bar) = Dialog("Cartella per la " + type + " " + code);
        body.Children.Add(Caption("Il nome si compone con le regole della creazione da modello di GeCa: «Codice - Cliente - [Luogo -] Oggetto». Il cliente proposto è il nome breve del cliente di GeCo.", 12, false, Grey));
        var client = Field(body, "Cliente"); var place = Field(body, "Luogo (facoltativo)"); var subject = Field(body, "Oggetto");
        var name = Caption("", 15, true, Teal); var notes = new StackPanel();
        body.Children.Add(name); body.Children.Add(notes);
        var ask = Push("Chiedi a GeCa", null, true, "La crea il servizio al giro successivo (entro 30 secondi)");
        var now = Push("Crea subito", null, false, "La crea subito il servizio con il modello sul server");
        var cancel = Push("Annulla", dialog.Close); cancel.IsCancel = true;
        bar.Children.Add(ask); bar.Children.Add(now); bar.Children.Add(cancel);
        GeCoFolderPreview? last = null; bool touched = false; bool? choice = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        async Task Refresh()
        {
            var request = touched ? new GeCoFolderAsk(type, id, client.Text, place.Text, subject.Text) : new GeCoFolderAsk(type, id);
            var p = await vm.PreviewAsync(request);
            if (p == null) { name.Text = "Anteprima non disponibile: " + vm.Error; ask.IsEnabled = now.IsEnabled = false; return; }
            last = p;
            if (!touched) { client.Text = p.Cliente; place.Text = p.Luogo ?? ""; subject.Text = p.Oggetto; touched = false; }
            name.Text = p.Nome;
            notes.Children.Clear();
            foreach (var e in p.Errori.Concat(p.Blocchi)) notes.Children.Add(Notice(e, "danger"));
            foreach (var w in p.Avvisi) notes.Children.Add(Notice(w));
            ask.IsEnabled = now.IsEnabled = p.PuoRichiedere;
        }
        bool loading = true;
        foreach (var box in new[] { client, place, subject }) box.TextChanged += (_, _) => { if (loading) return; touched = true; timer.Stop(); timer.Start(); };
        timer.Tick += async (_, _) => { timer.Stop(); await Refresh(); };
        ask.Click += (_, _) => { choice = false; dialog.Close(); };
        now.Click += (_, _) => { choice = true; dialog.Close(); };
        dialog.Loaded += async (_, _) => { await Refresh(); loading = false; };
        dialog.ShowDialog();
        timer.Stop();
        if (choice is bool immediate && last is { PuoRichiedere: true })
            await vm.RequestFolderAsync(touched ? new GeCoFolderAsk(type, id, client.Text, place.Text, subject.Text) : new GeCoFolderAsk(type, id), last.Nome, immediate);
    }

    private async Task CreateFromFolder(GeCoAlignmentFolder f)
    {
        var p = await vm.ProposalAsync(f);
        if (p == null) return;
        bool bid = p.Tipo == "gara";
        var (dialog, body, bar) = Dialog((bid ? "Nuova gara" : "Nuova commessa") + " dalla cartella " + f.Cartella.Nome, 720);
        body.Children.Add(Caption("Dati proposti dalla cartella, da verificare e correggere prima di confermare. La cartella sul disco non viene toccata.", 12, false, Grey));
        foreach (var b in p.Blocchi) body.Children.Add(Notice(b, "danger"));
        if (p.AvvisoCodice is { Length: > 0 } warn) body.Children.Add(Notice(warn));
        var code = Field(body, "Codice", p.Codice); code.IsEnabled = !bid;
        var year = Field(body, "Anno", p.Anno.ToString()); year.IsEnabled = !bid;
        var text = Field(body, bid ? "Oggetto della gara" : "Descrizione (al massimo 100 caratteri)", p.Descrizione);
        // Clienti: prima quelli proposti dal committente della cartella (indicati come proposta), poi tutti quelli di GeCo.
        var proposed = p.Clienti.Select(c => new Option(c.IdCliente, $"{c.RagioneSociale} «{c.NomeBreve}» — proposta {(c.Livello == "sicura" ? "sicura" : "da verificare")}: {c.Motivo}")).ToList();
        var all = (vm.Local?.GeCo ?? []).Where(c => proposed.All(x => x.Id != c.Id)).OrderBy(c => c.RagioneSociale).Select(c => new Option(c.Id, c.RagioneSociale + (c.NomeBreve is { Length: > 0 } nb ? " «" + nb + "»" : "")));
        var clients = (bid ? new[] { new Option(null, "Nessun cliente (indicare l'ente)") } : []).Concat(proposed).Concat(all).ToList();
        var client = Choice(body, "Cliente", clients, nameof(Option.Text));
        client.SelectedIndex = proposed.Count > 0 && proposed.Count == 1 ? (bid ? 1 : 0) : bid ? 0 : -1;
        TextBox? ente = null, place = null; ComboBox? resp = null, area = null; TextBox? note = null;
        if (bid) { ente = Field(body, "Ente appaltante (se manca, la ragione sociale del cliente)"); place = Field(body, "Luogo", p.Luogo); }
        else
        {
            resp = Choice(body, "Responsabile di commessa", p.Responsabili, nameof(GeCoChoice.Testo));
            resp.SelectedItem = p.Responsabili.FirstOrDefault(r => r.Id == p.IdResponsabile);
            if (p.Aree.Length > 0 || p.AreaObbligatoria) area = Choice(body, "Area di business" + (p.AreaObbligatoria ? "" : " (facoltativa)"), p.Aree, nameof(GeCoChoice.Testo));
            note = Field(body, "Note (facoltative)");
        }
        var ok = Push(bid ? "Crea gara" : "Crea commessa", null, true); ok.IsEnabled = p.Blocchi.Length == 0;
        var cancel = Push("Annulla", dialog.Close); cancel.IsCancel = true; bar.Children.Add(ok); bar.Children.Add(cancel);
        GeCoNewFromFolder? data = null;
        ok.Click += (_, _) =>
        {
            int.TryParse(year.Text, out var y);
            var c = (client.SelectedItem as Option)?.Id;
            data = new GeCoNewFromFolder(p.Tipo, code.Text.Trim(), y, text.Text.Trim(), c, (resp?.SelectedItem as GeCoChoice)?.Id, (area?.SelectedItem as GeCoChoice)?.Id, note?.Text, ente?.Text, place?.Text);
            dialog.Close();
        };
        dialog.ShowDialog();
        if (data != null) await vm.CreateFromFolderAsync(f, data);
    }

    private async Task ShortName(GeCoAlignmentCustomer c)
    {
        var (dialog, body, bar) = Dialog("Nome breve di " + c.RagioneSociale, 300);
        body.Children.Add(Caption("Il nome breve è la parte «Cliente» dei nomi delle cartelle (al massimo 60 caratteri, senza « - » e caratteri riservati) e deve essere unico tra i clienti.", 12, false, Grey));
        var box = Field(body, "Nome breve", c.Proposta?.NomeBreve ?? c.NomeBreve);
        string? value = null;
        var ok = Push("Imposta", () => { value = box.Text; dialog.Close(); }, true); ok.IsDefault = true;
        var cancel = Push("Annulla", dialog.Close); cancel.IsCancel = true; bar.Children.Add(ok); bar.Children.Add(cancel);
        dialog.ShowDialog();
        if (value != null) await vm.SetShortNameAsync(c, value);
    }

    private async Task LinkCustomer(GeCoExternalCustomer c)
    {
        var (dialog, body, bar) = Dialog("Collega «" + c.Nome + "» a un cliente di GeCo", 560);
        body.Children.Add(Caption($"{AlignmentViewModel.ExternalState(c)}: {c.Motivo}. I candidati sono proposte da verificare (P.IVA {c.PartitaIva ?? "—"}, codice fiscale {c.CodiceFiscale ?? "—"}).", 12, false, Grey));
        var geco = vm.Local?.GeCo ?? [];
        var find = Field(body, "Cerca tra i clienti di GeCo");
        var list = new ListBox { DisplayMemberPath = nameof(Option.Text), Height = 300 };
        body.Children.Add(list);
        IEnumerable<Option> Matches(string words)
        {
            var found = geco.Where(x => c.Candidati.Contains(x.Id)).Concat(geco.Where(x => !c.Candidati.Contains(x.Id)));
            foreach (var w in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                found = found.Where(x => new[] { x.RagioneSociale, x.NomeBreve, x.PartitaIva, x.CodiceFiscale, x.Localita, x.Codice }.Any(t => t?.Contains(w, StringComparison.CurrentCultureIgnoreCase) == true));
            return found.Take(200).Select(x => new Option(x.Id, (c.Candidati.Contains(x.Id) ? "[candidato] " : "") + $"{x.Codice} {x.RagioneSociale}" + (x.NomeBreve is { Length: > 0 } nb ? " «" + nb + "»" : "")
                + $" — P.IVA {x.PartitaIva ?? "—"}, CF {x.CodiceFiscale ?? "—"}" + (x.Localita is { Length: > 0 } l ? ", " + l : "")));
        }
        list.ItemsSource = Matches("").ToList();
        find.TextChanged += (_, _) => list.ItemsSource = Matches(find.Text).ToList();
        int? chosen = null;
        var ok = Push("Collega", () => { chosen = (list.SelectedItem as Option)?.Id; dialog.Close(); }, true);
        var cancel = Push("Annulla", dialog.Close); cancel.IsCancel = true; bar.Children.Add(ok); bar.Children.Add(cancel);
        dialog.ShowDialog();
        if (chosen is int id) await vm.LinkCustomerAsync(c, id);
    }

    private async Task ImportCustomer(GeCoExternalCustomer c)
    {
        var (dialog, body, bar) = Dialog("Importa «" + c.Nome + "» in GeCo", 720);
        body.Children.Add(Caption("Campi obbligatori della pagina Clienti di GeCo2027: ragione sociale, indirizzo, CAP, località, provincia e partita IVA. Dopo l'importazione il cliente di GeCa diventa quello di GeCo.", 12, false, Grey));
        var name = Field(body, "Ragione sociale", c.Nome); var address = Field(body, "Indirizzo", c.Indirizzo); var cap = Field(body, "CAP"); var city = Field(body, "Località");
        var prov = Field(body, "Provincia (sigla)"); var vat = Field(body, "Partita IVA", c.PartitaIva); var tax = Field(body, "Codice fiscale", c.CodiceFiscale);
        var mail = Field(body, "Email", c.Email); var pec = Field(body, "PEC"); var shortName = Field(body, "Nome breve per le cartelle (facoltativo)");
        GeCoCustomerImport? data = null;
        var ok = Push("Importa", () => { data = new(c.Id, name.Text, address.Text, cap.Text, city.Text, prov.Text, vat.Text, tax.Text, mail.Text, pec.Text, shortName.Text); dialog.Close(); }, true);
        var cancel = Push("Annulla", dialog.Close); cancel.IsCancel = true; bar.Children.Add(ok); bar.Children.Add(cancel);
        dialog.ShowDialog();
        if (data != null) await vm.ImportCustomerAsync(data);
    }
}
