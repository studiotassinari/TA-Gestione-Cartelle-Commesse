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
    private readonly TextBox search = new();
    public BusinessWindow(BusinessViewModel model)
    {
        vm = model; DataContext = vm;
        // Attività e ore non sono più qui: dalla 2.37.0 sono quelle di GeCo sul database comune (finestre Pianificazione e Ore lavorate).
        Title = "Clienti e commesse — TA"; Width = 1300; Height = 850; MinWidth = 900; MinHeight = 600;
        var main = Application.Current.MainWindow;
        Resources = main.Resources; Background = main.Background; Foreground = main.Foreground; FontFamily = main.FontFamily; Icon = main.Icon;
        var dock = new DockPanel { Margin = new Thickness(18), Background = Background }; Content = dock;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) }; status.SetBinding(TextBlock.TextProperty, new Binding(nameof(vm.Status)));
        DockPanel.SetDock(status, Dock.Bottom); dock.Children.Add(status);
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock { Text = "Clienti e commesse", FontSize = 26, Margin = new Thickness(0, 0, 12, 0) }); heading.Children.Add(Button("Aggiorna", vm.Refresh)); header.Children.Add(heading);
        header.Children.Add(new TextBlock { Text = "Cerca cliente o commessa", Margin = new Thickness(0, 10, 0, 3) });
        search.TextChanged += (_, _) => Render(); header.Children.Add(search);
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        var tabs = new TabControl(); dock.Children.Add(tabs);
        tabs.Items.Add(Tab("Clienti", customers, Button("Nuovo cliente", () => EditCustomer(null), true), Button("Modifica", () => EditCustomer(customers.SelectedItem as Customer), true), Button("Importa Magoot…", () => Import("Magoot"), true), Button("Importa Fatture in Cloud…", () => Import("Fatture"), true)));
        tabs.Items.Add(Tab("Commesse e autorizzazioni", projects, Button("Apri commessa…", OpenProject), Button("Cliente e autorizzazione ore…", EditProject, true)));
        projects.MouseDoubleClick += async (_, _) => await OpenProject();
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
        foreach (var c in columns) grid.Columns.Add(new DataGridTextColumn { Header = c.Label, Binding = new Binding(c.Path) { StringFormat = c.Path == "Item.Number" ? "D3" : null }, MinWidth = 100, Width = DataGridLength.SizeToCells }); return grid;
    }
    private bool Matches(params string[] text) => text.Any(t => t.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
    private static string Mode(string mode) => mode switch { "Quote" => "Preventivo accettato", "Contract" => "Contratto", "Override" => "Deroga", _ => "Bloccato" };
    private sealed record ProjectRow(WorkProject Item, string Code, string Title, string Mode, string Evidence, string Reason,string Customer);
    private void Render()
    {
        if (vm.Snapshot is not { } s) return;
        customers.ItemsSource = s.Customers.Where(c => Matches(c.Name, c.Vat, c.TaxCode, c.Address)).OrderBy(c => c.Name).ToArray();
        projects.ItemsSource = s.Projects.Select(p => new ProjectRow(p, p.Code, p.Title, Mode(p.HoursMode), p.Evidence, p.Reason,s.Customers.FirstOrDefault(c=>c.Id==p.CustomerId)?.Name??"Da associare")).Where(p=>Matches(p.Code,p.Title,p.Customer,p.Item.Subject,p.Item.Location)).ToArray();
    }
    private Task EditCustomer(Customer? old)
    {
        if(vm.CanEdit&&(Application.Current.MainWindow.DataContext is MainViewModel main))main.Workspace?.ShowCustomers(old?.Id??"",startNew:old==null);
        return Task.CompletedTask;
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
        if(Application.Current.MainWindow.DataContext is MainViewModel main)main.Workspace?.ShowProject(row.Item.Id);
        return Task.CompletedTask;
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
