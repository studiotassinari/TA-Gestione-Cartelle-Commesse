using System.Windows;
using System.Windows.Controls;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Views;
// La creazione richiede un'anteprima esplicita; ogni controllo viene ripetuto anche dal server.
public sealed class CreateJobDialog : Window
{
    public string? CreatedPath { get; private set; }
    public CreateJobDialog(ServerConnection server)
    {
        Title = "Nuova commessa da modello"; Width = 820; Height = 850; MinHeight = 650;
        var main = Application.Current.MainWindow;
        Resources = main.Resources; Background = main.Background; Foreground = main.Foreground; FontFamily = main.FontFamily; Icon = main.Icon;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Nuova commessa", FontSize = 26, Margin = new Thickness(0, 0, 0, 16) });
        TextBox Field(string title, string initial = "")
        {
            panel.Children.Add(new TextBlock { Text = title });
            var box = new TextBox { Text = initial, Margin = new Thickness(0, 4, 0, 12) }; panel.Children.Add(box); return box;
        }
        var code = Field("Codice CXXYYY", "C" + DateTime.Now.ToString("yy"));
        panel.Children.Add(new TextBlock { Text = "Scegli dal catalogo clienti (facoltativo)" });
        var customer = new ComboBox { DisplayMemberPath = "Name", IsTextSearchEnabled = true, MinHeight = 32, Margin = new Thickness(0, 4, 0, 12) }; panel.Children.Add(customer);
        var customerStatus = new TextBlock { Text = "Caricamento anagrafiche…", TextWrapping = TextWrapping.Wrap }; panel.Children.Add(customerStatus);
        var client = Field("Denominazione da usare nella cartella"); var place = Field("Luogo (facoltativo)"); var subject = Field("Argomento / cosa");
        customer.SelectionChanged += (_, _) => { if (customer.SelectedItem is Customer selected) client.Text = selected.Name; };
        Loaded += async (_, _) =>
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var data = await server.SendAsync<BusinessSnapshot>(new("Business", Business: new("Snapshot", new(today, today))));
                customer.ItemsSource = data.Customers.OrderBy(c => c.Name).ToArray();
                customerStatus.Text = "La denominazione resta modificabile. L'associazione gestionale si imposta nella scheda Commesse e autorizzazioni.";
                var prefix = "C" + DateTime.Today.ToString("yy");
                var next = data.Projects.Where(p => p.Code.StartsWith(prefix) && p.Code.Length == 6).Select(p => int.TryParse(p.Code[3..], out var n) ? n : 0).DefaultIfEmpty().Max() + 1;
                if (next <= 999 && code.Text == prefix) code.Text = prefix + next.ToString("D3");
            }
            catch (Exception ex) { customerStatus.Text = "Catalogo non disponibile; puoi inserire il cliente manualmente. " + ex.Message; }
        };
        panel.Children.Add(new TextBlock { Text = "Vengono create solo le sottocartelle del modello, senza copiare file. Nessuna cartella esistente viene sovrascritta.", TextWrapping = TextWrapping.Wrap });
        var detail = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 10) }; panel.Children.Add(detail);
        var hierarchy = new TreeView { Height = 160, Margin = new Thickness(0, 0, 0, 10) }; panel.Children.Add(hierarchy);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var preview = new Button { Content = "Anteprima", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(5) };
        var create = new Button { Content = "Crea commessa", IsEnabled = false, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(5) };
        var cancel = new Button { Content = "Chiudi", IsCancel = true, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(5) };
        buttons.Children.Add(preview); buttons.Children.Add(create); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        CreateJobRequest? reviewed = null; bool working = false;
        void Busy(bool value) { working = value; customer.IsEnabled = !value; preview.IsEnabled = !value; cancel.IsEnabled = !value; foreach (var field in new[] { code, client, place, subject }) field.IsEnabled = !value; }
        foreach (var field in new[] { code, client, place, subject }) field.TextChanged += (_, _) => { reviewed = null; create.IsEnabled = false; };
        Closing += (_, args) => { if (working) args.Cancel = true; };
        preview.Click += async (_, _) =>
        {
            Busy(true); create.IsEnabled = false; reviewed = null;
            try
            {
                var input = new CreateJobRequest(code.Text, client.Text, place.Text, subject.Text);
                var plan = await server.SendAsync<TemplatePreview>(new("TemplatePreview", Creation: input));
                reviewed = input with { TemplateStamp = plan.Stamp };
                detail.Text = "Destinazione:\n" + plan.Destination + "\nModello: " + plan.Template + $"\n{plan.Directories.Length} sottocartelle, nessun file.";
                hierarchy.Items.Clear();
                foreach (var directory in plan.Directories.Order(StringComparer.OrdinalIgnoreCase))
                {
                    ItemCollection children = hierarchy.Items;
                    foreach (var part in directory.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var node = children.OfType<TreeViewItem>().FirstOrDefault(n => n.Header?.ToString() == part);
                        if (node == null) { node = new TreeViewItem { Header = part }; children.Add(node); }
                        children = node.Items;
                    }
                }
                create.IsEnabled = true;
            }
            catch (Exception ex) { detail.Text = "Anteprima non disponibile: " + ex.Message; }
            finally { Busy(false); }
        };
        create.Click += async (_, _) =>
        {
            if (reviewed == null) return;
            Busy(true); create.IsEnabled = false;
            try
            {
                CreatedPath = await server.SendAsync<string>(new("CreateJob", Creation: reviewed));
                working = false; DialogResult = true;
            }
            catch (Exception ex)
            {
                reviewed = null;
                detail.Text = "Creazione non confermata: " + ex.Message + "\nSe la connessione e caduta, verificare la cartella prima di riprovare. Non viene eseguito alcun tentativo automatico.";
            }
            finally { Busy(false); }
        };
    }
}
