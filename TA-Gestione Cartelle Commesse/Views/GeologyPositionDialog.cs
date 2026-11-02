using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Modifica soltanto una scheda del catalogo; il file originale non viene toccato.
public sealed class GeologyPositionDialog : Window
{
    public GeologicalPositionUpdate? Change { get; private set; }
    public GeologyPositionDialog(GeologicalReport report, string version, double? latitude = null, double? longitude = null)
    {
        Title = "Posizione della relazione"; Width = 530; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(22) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = report.Title, FontSize = 18, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        TextBox Field(string label, string value) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,12,0,4) }); var field = new TextBox { Text = value, Padding = new Thickness(8) }; panel.Children.Add(field); return field; }
        var lat = Field("Latitudine WGS84", (latitude ?? report.Latitude)?.ToString("F6", CultureInfo.InvariantCulture) ?? "");
        var lon = Field("Longitudine WGS84", (longitude ?? report.Longitude)?.ToString("F6", CultureInfo.InvariantCulture) ?? "");
        var city = Field("Comune / località", report.Municipality); var address = Field("Indirizzo", report.Address);
        var source = Field("Fonte e precisione della posizione", latitude.HasValue ? "Posizione indicata manualmente sulla mappa" : report.PositionSource);
        panel.Children.Add(new TextBlock { Text = "Salva solo nel catalogo condiviso, con il tuo account Windows. Nessuna modifica alla relazione originale.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) });
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.DarkRed, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var save = new Button { Content = "Salva posizione", Padding = new Thickness(14,8,14,8), HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(save);
        save.Click += (_, _) => {
            if (!double.TryParse(lat.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) || !double.TryParse(lon.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var b) || a is not (>= -90 and <= 90) || b is not (>= -180 and <= 180) || string.IsNullOrWhiteSpace(source.Text)) { error.Text = "Indicare coordinate valide e fonte della posizione."; return; }
            Change = new(report.Id, a, b, city.Text, address.Text, source.Text, version); DialogResult = true;
        };
    }
}
