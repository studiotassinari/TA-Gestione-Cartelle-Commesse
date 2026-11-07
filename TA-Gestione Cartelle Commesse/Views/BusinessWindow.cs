using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;

public sealed class BusinessWindow : Window
{
    private readonly BusinessViewModel vm;
    private readonly DataGrid customers = Table(("Cliente", "Name"), ("Partita IVA", "Vat"), ("Codice fiscale", "TaxCode"), ("Indirizzo", "Address"), ("Email", "Email"));
    private readonly DataGrid projects = Table(("Codice", "Code"), ("Anno", "Item.Year"), ("Numero", "Item.Number"), ("Cliente associato", "Customer"), ("Argomento", "Item.Subject"), ("Inserimento ore", "Mode"), ("Riferimento", "Evidence"), ("Deroga", "Reason"));
    private readonly DataGrid activities = Table(("Commessa", "Code"), ("Fase", "Phase"), ("Attività", "Title"), ("Collaboratore", "Assignee"), ("Inizio", "Start"), ("Fine", "End"), ("Ore previste", "Hours"), ("Avanzamento %", "Progress"));
    private readonly DataGrid times = Table(("Data", "Day"), ("Commessa", "Code"), ("Attività", "Activity"), ("Ore", "Hours"), ("Lavoro svolto", "Description"), ("Collaboratore", "User"));
    private readonly TextBox search = new();
    private readonly GanttView gantt = new();
    private readonly ComboBox group = new() { ItemsSource = new[] { "Per commessa", "Per collaboratore" }, SelectedIndex = 0, MinWidth = 160 };
    public BusinessWindow(BusinessViewModel model)
    {
        vm = model; DataContext = vm;
        Title = "Clienti, pianificazione e ore — TA"; Width = 1300; Height = 850; MinWidth = 900; MinHeight = 600;
        var main = Application.Current.MainWindow;
        Resources = main.Resources; Background = main.Background; Foreground = main.Foreground; FontFamily = main.FontFamily; Icon = main.Icon;
        var dock = new DockPanel { Margin = new Thickness(18), Background = Background }; Content = dock;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) }; status.SetBinding(TextBlock.TextProperty, new Binding(nameof(vm.Status)));
        DockPanel.SetDock(status, Dock.Bottom); dock.Children.Add(status);
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = "Clienti, attività e consuntivi", FontSize = 26, Margin = new Thickness(0, 0, 0, 12) });
        var dates = new StackPanel { Orientation = Orientation.Horizontal };
        var from = new DatePicker { Width = 140 }; from.SetBinding(DatePicker.SelectedDateProperty, new Binding(nameof(vm.From)) { Mode = BindingMode.TwoWay });
        var to = new DatePicker { Width = 140 }; to.SetBinding(DatePicker.SelectedDateProperty, new Binding(nameof(vm.To)) { Mode = BindingMode.TwoWay });
        dates.Children.Add(new TextBlock { Text = "Periodo attività e ore  ", VerticalAlignment = VerticalAlignment.Center }); dates.Children.Add(from); dates.Children.Add(to);
        dates.Children.Add(Button("Aggiorna", vm.Refresh)); header.Children.Add(dates);
        header.Children.Add(new TextBlock { Text = "Cerca cliente, commessa, attività o collaboratore", Margin = new Thickness(0, 10, 0, 3) });
        search.TextChanged += (_, _) => Render(); header.Children.Add(search);
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        var tabs = new TabControl(); dock.Children.Add(tabs);
        tabs.Items.Add(Tab("Clienti", customers, Button("Nuovo cliente", () => EditCustomer(null), true), Button("Modifica", () => EditCustomer(customers.SelectedItem as Customer), true), Button("Importa Magoot…", () => Import("Magoot"), true), Button("Importa Fatture in Cloud…", () => Import("Fatture"), true)));
        tabs.Items.Add(Tab("Commesse e autorizzazioni", projects, Button("Apri commessa…", OpenProject), Button("Cliente e autorizzazione ore…", EditProject, true)));
        projects.MouseDoubleClick += async (_, _) => await OpenProject();
        tabs.Items.Add(Tab("Attività", activities, Button("Nuova attività", () => EditActivity(null), true), Button("Modifica attività", () => EditActivity((activities.SelectedItem as ActivityRow)?.Item), true)));
        group.SelectionChanged += (_, _) => Render();
        gantt.HorizontalAlignment = HorizontalAlignment.Left; gantt.VerticalAlignment = VerticalAlignment.Top;
        tabs.Items.Add(Tab("Pianificazione", new ScrollViewer { Content = gantt, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, group));
        tabs.Items.Add(Tab("Ore lavorate", times, Button("Inserisci ore", () => EditTime(null)), Button("Modifica ore", () => EditTime((times.SelectedItem as TimeRow)?.Item)), Button("Elimina riga", DeleteTime)));
        vm.Updated += Render; Loaded += async (_, _) => await vm.Refresh(); Closed += (_, _) => { vm.Updated -= Render; vm.Dispose(); };
    }
    private Button Button(string title, Func<Task> action, bool manager = false)
    {
        var b = new Button { Content = title }; if (manager) b.SetBinding(IsEnabledProperty, new Binding(nameof(vm.CanEdit)));
        b.Click += async (_, _) => { if (vm.Busy) return; try { await action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Operazione non completata"); } }; return b;
    }
    private static TabItem Tab(string title, UIElement body, params UIElement[] actions)
    {
        var dock = new DockPanel { Margin = new Thickness(8) }; System.Windows.Documents.TextElement.SetFontWeight(dock, FontWeights.Normal);
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var a in actions) bar.Children.Add(a); DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar); dock.Children.Add(body); return new TabItem { Header = title, Content = dock };
    }
    private static DataGrid Table(params (string Label, string Path)[] columns)
    {
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
        foreach (var c in columns) grid.Columns.Add(new DataGridTextColumn { Header = c.Label, Binding = new Binding(c.Path), MinWidth = 100, Width = DataGridLength.SizeToCells }); return grid;
    }
    private bool Matches(params string[] text) => text.Any(t => t.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
    private string Code(string id) => vm.Snapshot?.Projects.FirstOrDefault(p => p.Id == id)?.Code ?? id;
    private static string Mode(string mode) => mode switch { "Quote" => "Preventivo accettato", "Contract" => "Contratto", "Override" => "Deroga", _ => "Bloccato" };
    private sealed record ProjectRow(WorkProject Item, string Code, string Title, string Mode, string Evidence, string Reason,string Customer);
    private sealed record ActivityRow(WorkActivity Item, string Code, string Phase, string Title, string Assignee, DateOnly Start, DateOnly End, decimal Hours, int Progress);
    private sealed record TimeRow(WorkTime Item, DateOnly Day, string Code, string Activity, decimal Hours, string Description, string User);
    private void Render()
    {
        if (vm.Snapshot is not { } s) return;
        customers.ItemsSource = s.Customers.Where(c => Matches(c.Name, c.Vat, c.TaxCode, c.Address)).OrderBy(c => c.Name).ToArray();
        projects.ItemsSource = s.Projects.Select(p => new ProjectRow(p, p.Code, p.Title, Mode(p.HoursMode), p.Evidence, p.Reason,s.Customers.FirstOrDefault(c=>c.Id==p.CustomerId)?.Name??"Da associare")).Where(p=>Matches(p.Code,p.Title,p.Customer,p.Item.Subject,p.Item.Location)).ToArray();
        var rows = s.Activities.Where(a => Matches(Code(a.ProjectId), a.Title, a.Phase, a.Assignee)).ToArray();
        activities.ItemsSource = rows.Select(a => new ActivityRow(a, Code(a.ProjectId), a.Phase, a.Title, a.Assignee, a.Start, a.End, a.PlannedMinutes / 60m, a.Progress)).ToArray();
        times.ItemsSource = s.Times.Where(t => Matches(Code(t.ProjectId), t.Description, t.User)).OrderByDescending(t => t.Day).Select(t => new TimeRow(t, t.Day, Code(t.ProjectId), s.Activities.FirstOrDefault(a => a.Id == t.ActivityId)?.Title ?? "Attività fuori periodo", t.Minutes / 60m, t.Description, t.User)).ToArray();
        gantt.Update(rows, DateOnly.FromDateTime(vm.From), DateOnly.FromDateTime(vm.To), group.SelectedIndex == 1, Code, a => { if (vm.CanEdit) _ = EditActivity(a); });
    }
    private async Task EditCustomer(Customer? old)
    {
        if (!vm.CanEdit) return;
        var editor=new CustomerManagementViewModel(vm,old?.Id??"");
        new CustomerWindow(editor,startNew:old==null){Owner=this}.ShowDialog();
        await vm.Refresh();
    }
    private async Task EditProject()
    {
        if (!vm.CanEdit || projects.SelectedItem is not ProjectRow row || vm.Snapshot is not { } s) return; var p = row.Item;
        var f = new BusinessForm(this, p.Code + " — autorizzazione ore");
        var clients = new[] { new Customer("", "Nessun cliente associato") }.Concat(s.Customers).ToArray();
        var client = f.Choice("Cliente", clients, clients.FirstOrDefault(c => c.Id == p.CustomerId), "Name");
        var modes = new[] { "Blocked", "Quote", "Contract", "Override" }.Select(v => new KeyValuePair<string, string>(v, Mode(v))).ToArray();
        var mode = f.Choice("Condizione per inserire ore", modes, modes.First(x => x.Key == p.HoursMode), "Value");
        var evidence = f.Text("Riferimento al preventivo accettato / contratto", p.Evidence); var reason = f.Text("Motivazione della deroga", p.Reason); var until = f.Date("Autorizzato fino al (vuoto = nessuna scadenza)", p.Until?.ToDateTime(TimeOnly.MinValue));
        if (f.ShowDialog() == true) await vm.Save(new("SaveProject", Project: p with { CustomerId = (client.SelectedItem as Customer)?.Id ?? "", HoursMode = ((KeyValuePair<string, string>)mode.SelectedItem).Key, Evidence = evidence.Text, Reason = reason.Text, Until = until.SelectedDate is { } d ? DateOnly.FromDateTime(d) : null }));
    }
    private Task OpenProject()
    {
        if(projects.SelectedItem is not ProjectRow row)return Task.CompletedTask;
        var detail=new ProjectDetailWindow(new ProjectDetailViewModel(vm,row.Item.Id)){Owner=this};
        detail.Closed+=async(_,_)=>await vm.Refresh();detail.Show();return Task.CompletedTask;
    }
    private async Task EditActivity(WorkActivity? a)
    {
        if (!vm.CanEdit || vm.Snapshot is not { } s) return; var f = new BusinessForm(this, a == null ? "Nuova attività" : "Modifica attività");
        var project = f.Choice("Commessa", s.Projects, s.Projects.FirstOrDefault(p => p.Id == a?.ProjectId), "Code");
        var phase = f.Text("Fase", a?.Phase ?? ""); var title = f.Text("Attività", a?.Title ?? ""); var person = f.Choice("Collaboratore · gruppo aziendale", s.Collaborators, s.Collaborators.FirstOrDefault(x => x.Equals(a?.Assignee ?? s.User, StringComparison.OrdinalIgnoreCase)), "");
        var start = f.Date("Inizio", a?.Start.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today); var end = f.Date("Fine", a?.End.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today);
        var hours = f.Text("Ore previste", ((a?.PlannedMinutes ?? 0) / 60m).ToString()); var progress = f.Text("Avanzamento %", (a?.Progress ?? 0).ToString());
        var states = new[] { new KeyValuePair<string, string>("Planned", "Pianificata"), new("Active", "In corso"), new("Done", "Completata"), new("Suspended", "Sospesa") };
        var state = f.Choice("Stato", states, states.First(x => x.Key == (a?.State ?? "Planned")), "Value");
        if (f.ShowDialog() != true) return;
        if (project.SelectedItem is not WorkProject p || start.SelectedDate == null || end.SelectedDate == null) throw new ArgumentException("Selezionare commessa e date.");
        await vm.Save(new("SaveActivity", Activity: new(a?.Id ?? "", p.Id, phase.Text, title.Text, person.SelectedItem as string ?? "", DateOnly.FromDateTime(start.SelectedDate.Value), DateOnly.FromDateTime(end.SelectedDate.Value), Minutes(hours.Text), ((KeyValuePair<string, string>)state.SelectedItem).Key, int.Parse(progress.Text), a?.Revision ?? 0)));
    }
    private async Task EditTime(WorkTime? t)
    {
        if (vm.Snapshot is not { } s) return; var f = new BusinessForm(this, "Ore lavorate");
        var choices = s.Activities.Where(a => a.Assignee.Equals(t?.User ?? s.User, StringComparison.OrdinalIgnoreCase)).Select(a => new KeyValuePair<WorkActivity, string>(a, Code(a.ProjectId) + " — " + a.Title)).ToArray();
        if (choices.Length == 0) { MessageBox.Show(this, "Nessuna attività assegnata nel periodo. Ampliare le date o rivolgersi ad Antonio/Giuseppe."); return; }
        var activity = f.Choice("Attività assegnata", choices, choices.FirstOrDefault(a => a.Key.Id == t?.ActivityId), "Value");
        var day = f.Date("Giorno", t?.Day.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today); var hours = f.Text("Ore lavorate (es. 1,5)", ((t?.Minutes ?? 60) / 60m).ToString()); var description = f.Text("Lavoro svolto", t?.Description ?? "");
        if (f.ShowDialog() != true) return;
        if (activity.SelectedItem is not KeyValuePair<WorkActivity, string> selected || selected.Key == null || day.SelectedDate == null) throw new ArgumentException("Selezionare attività e giorno.");
        await vm.Save(new("SaveTime", Time: new(t?.Id ?? "", selected.Key.ProjectId, selected.Key.Id, DateOnly.FromDateTime(day.SelectedDate.Value), Minutes(hours.Text), description.Text, Revision: t?.Revision ?? 0)));
    }
    private static int Minutes(string text)
    {
        if (!decimal.TryParse(text, out var hours) || hours < 0 || hours > 166666 || hours * 60 != decimal.Truncate(hours * 60)) throw new ArgumentException("Indicare ore valide, con precisione al minuto.");
        return checked((int)(hours * 60));
    }
    private async Task DeleteTime()
    {
        if (times.SelectedItem is TimeRow row && MessageBox.Show(this, "Eliminare questa registrazione ore?", "Conferma", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            await vm.Save(new("DeleteTime", Id: row.Item.Id, Revision: row.Item.Revision));
    }
    private async Task Import(string provider)
    {
        if (!vm.CanEdit) return;
        var pick = new Microsoft.Win32.OpenFileDialog { Title = "Esportazione clienti " + provider, Filter = provider == "Fatture" ? "Esportazione Fatture in Cloud|*.xlsx;*.csv|Excel|*.xlsx|CSV|*.csv" : "Esportazione CSV|*.csv" };
        if (pick.ShowDialog(this) != true) return;
        var excel = provider == "Fatture" && Path.GetExtension(pick.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
        if (new FileInfo(pick.FileName).Length > (excel ? 8 * 1024 * 1024 : 2_000_000)) throw new ArgumentException("File troppo grande: limite Excel 8 MiB, CSV 2 MB.");
        // Nessuna copia locale o log: il contenuto selezionato resta in memoria fino all'importazione.
        var data = excel ? await File.ReadAllBytesAsync(pick.FileName) : null;
        var csv = excel ? null : await File.ReadAllTextAsync(pick.FileName, new UTF8Encoding(false, true));
        await vm.Run(async () =>
        {
            var preview = await vm.Send<CustomerImportResult>(new("Preview" + provider, Csv: csv, Excel: data));
            var f = new BusinessForm(this, "Anteprima importazione clienti");
            var detail = f.Text("Riepilogo", $"Letti: {preview.Read}\nNuovi: {preview.Added}\nCollegamenti: {preview.Linked}\nEsclusi, da verificare: {preview.Skipped}\n\n" + string.Join("\n", preview.Issues));
            detail.IsReadOnly = true; detail.AcceptsReturn = true; detail.Height = 390; detail.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            detail.TextWrapping = TextWrapping.Wrap;
            if (f.ShowDialog() != true) return;
            var result = await vm.Send<CustomerImportResult>(new("Import" + provider, Csv: csv, Excel: data));
            var report = new BusinessForm(this, "Esito importazione clienti", "Chiudi", false);
            var outcome = report.Text("Riepilogo", $"Importazione completata: {result.Added} nuovi, {result.Linked} collegati, {result.Skipped} esclusi da verificare.\n\n" + string.Join("\n", result.Issues));
            outcome.IsReadOnly = true; outcome.AcceptsReturn = true; outcome.Height = 420; outcome.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; outcome.TextWrapping = TextWrapping.Wrap;
            report.ShowDialog();
        });
        await vm.Refresh();
    }
}
