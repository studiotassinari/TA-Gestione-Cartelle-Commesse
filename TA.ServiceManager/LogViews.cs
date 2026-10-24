using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.ServiceManager;

public sealed class ServiceLogView : UserControl
{
    public TextBox Output { get; } = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas") };
    public ServiceLogView() => Content = new GroupBox { Header = "Debug del servizio: avvio, scansioni ed errori", Content = Output };
}
public sealed class CommunicationLogView : UserControl
{
    private readonly ObservableCollection<CommunicationRecord> rows = [];
    private readonly TextBlock status = new() { Margin = new Thickness(5), TextWrapping = TextWrapping.Wrap };
    private string previous = "";
    public CommunicationLogView()
    {
        var panel = new DockPanel();
        DockPanel.SetDock(status, Dock.Top); panel.Children.Add(status);
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, ItemsSource = rows,
            CanUserAddRows = false, EnableRowVirtualization = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        foreach (var (title, field, width) in new[] { ("Ora", "Time", 155), ("PC dichiarato", "Computer", 130), ("IP", "Address", 120),
            ("Utente Windows", "User", 190), ("Azione", "Action", 140), ("Esito", "Outcome", 170), ("Dettaglio", "Detail", 380) })
            grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(field) { StringFormat = field == "Time" ? "dd/MM HH:mm:ss" : null }, Width = width });
        panel.Children.Add(grid);
        Content = new GroupBox { Header = "Comunicazioni con i client", Content = panel };
    }
    public void Update(string text)
    {
        if (text != previous)
        {
            previous = text; rows.Clear();
            foreach (var line in text.Split('\n').Reverse())
            {
                try { var row = JsonSerializer.Deserialize<CommunicationRecord>(line); if (row != null) rows.Add(row); }
                catch (JsonException) { }
                if (rows.Count >= 500) break;
            }
        }
        var confirmed = rows.FirstOrDefault(r => r.Outcome == "Riscontro ricevuto");
        status.Text = confirmed == null ? "Nessun riscontro recente disponibile. I client precedenti non confermano l'applicazione alle viste."
            : $"Ultimo riscontro: {confirmed.Computer}, {confirmed.User}, {confirmed.Time.LocalDateTime:HH:mm:ss} ({(DateTimeOffset.Now - confirmed.Time).TotalSeconds:F0} secondi fa). Vedere le righe per ciascun PC.";
    }
}
