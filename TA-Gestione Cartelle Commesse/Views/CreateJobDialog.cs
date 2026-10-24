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
        Title = "Nuova commessa da modello"; Width = 630; Height = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        TextBox Field(string title, string initial = "")
        {
            panel.Children.Add(new TextBlock { Text = title });
            var box = new TextBox { Text = initial, Margin = new Thickness(0, 4, 0, 12) }; panel.Children.Add(box); return box;
        }
        var code = Field("Codice CXXYYY", "C" + DateTime.Now.ToString("yy"));
        var client = Field("Cliente"); var place = Field("Luogo (facoltativo)"); var subject = Field("Argomento / cosa");
        panel.Children.Add(new TextBlock { Text = "Vengono create solo le sottocartelle del modello, senza copiare file. Nessuna cartella esistente viene sovrascritta.", TextWrapping = TextWrapping.Wrap });
        var detail = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 10) }; panel.Children.Add(detail);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var preview = new Button { Content = "Anteprima", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(5) };
        var create = new Button { Content = "Crea commessa", IsEnabled = false, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(5) };
        var cancel = new Button { Content = "Chiudi", IsCancel = true, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(5) };
        buttons.Children.Add(preview); buttons.Children.Add(create); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        CreateJobRequest? reviewed = null; bool working = false;
        void Busy(bool value) { working = value; preview.IsEnabled = !value; cancel.IsEnabled = !value; foreach (var field in new[] { code, client, place, subject }) field.IsEnabled = !value; }
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
                detail.Text = "Destinazione:\n" + plan.Destination + "\n\nModello:\n" + plan.Template + $"\n\n{plan.Directories.Length} sottocartelle, nessun file.\n" + string.Join("\n", plan.Directories);
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
