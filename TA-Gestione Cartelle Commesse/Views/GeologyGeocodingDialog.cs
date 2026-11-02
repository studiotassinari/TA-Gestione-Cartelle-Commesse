using System.Windows;
using System.Windows.Controls;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Dialogo esplicito: nessuna ricerca alla digitazione e nessuna scelta automatica del primo risultato.
public sealed class GeologyGeocodingDialog : Window
{
    public GeologicalPositionUpdate? Change { get; private set; }
    public GeologyGeocodingDialog(GeologyViewModel model)
    {
        var report = model.Selected ?? throw new InvalidOperationException("Selezionare una relazione."); var version = model.Version;
        Title = "Localizza indirizzo con Nominatim"; Width = 780; Height = 590; MinWidth = 650; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var cancellation = new CancellationTokenSource(); Closed += (_, _) => cancellation.Cancel();
        var panel = new DockPanel { Margin = new Thickness(20) }; Content = panel;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(new TextBlock { Text = report.Title, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, FontSize = 17 });
        top.Children.Add(new TextBlock { Text = "Indirizzo geografico da cercare (via, civico, comune, provincia, paese)", Margin = new Thickness(0,12,0,5) });
        var address = new TextBox { Text = string.Join(", ", new[] { report.Address, report.Municipality }.Where(s => !string.IsNullOrWhiteSpace(s))), Padding = new Thickness(8), MaxLength = 400 }; top.Children.Add(address);
        top.Children.Add(new TextBlock { Text = "Verrà inviato solo questo indirizzo al servizio Nominatim configurato sul server. Non inserire dati personali o riservati. Nessun documento o nome del committente viene inviato automaticamente.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,8) });
        var search = new Button { Content = "Cerca indirizzo", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14,8,14,8) }; top.Children.Add(search);
        var status = new TextBlock { Text = "Scegli un risultato e controlla località e precisione prima di salvarlo.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) }; top.Children.Add(status);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        bottom.Children.Add(new TextBlock { Text = "Dati © OpenStreetMap contributors · ODbL. Nominatim: massimo 1 richiesta/s condivisa, cache sul server, nessun autocomplete o ricerca periodica. Condizioni: https://operations.osmfoundation.org/policies/nominatim/", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,8), FontSize = 12 });
        var precision = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) }; bottom.Children.Add(precision);
        var save = new Button { Content = report.HasPosition ? "Sostituisci con la posizione indicativa scelta" : "Salva posizione indicativa", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(14,8,14,8) }; bottom.Children.Add(save);
        var list = new ListBox { DisplayMemberPath = nameof(GeocodingCandidate.Label), HorizontalContentAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(list);
        var template = new DataTemplate(); var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(GeocodingCandidate.Label))); label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); label.SetValue(FrameworkElement.MarginProperty, new Thickness(8)); template.VisualTree = label; list.DisplayMemberPath = ""; list.ItemTemplate = template;
        list.SelectionChanged += (_, _) => { save.IsEnabled = list.SelectedItem is GeocodingCandidate; precision.Text = (list.SelectedItem as GeocodingCandidate)?.Precision ?? ""; };
        address.TextChanged += (_, _) => { list.ItemsSource = null; save.IsEnabled = false; };
        search.Click += async (_, _) => {
            search.IsEnabled = false; address.IsEnabled = false; list.ItemsSource = null; save.IsEnabled = false; status.Text = "Ricerca in corso tramite il server...";
            try { var result = await model.FindPositionAsync(new(report.Id, address.Text, version), cancellation.Token); if(cancellation.IsCancellationRequested) return; list.ItemsSource = result.Candidates; status.Text = result.Candidates.Count == 0 ? "Nessun risultato. Specifica meglio via, comune e provincia." : result.Candidates.Count + " risultati · " + (result.FromCache ? "dalla cache condivisa" : "da Nominatim") + " · " + result.RetrievedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { if(!cancellation.IsCancellationRequested) status.Text = "Ricerca non riuscita: " + ex.Message; }
            finally { search.IsEnabled = true; address.IsEnabled = true; }
        };
        save.Click += (_, _) => { if(list.SelectedItem is not GeocodingCandidate selected) return; Change = new(report.Id, selected.Latitude, selected.Longitude, selected.Municipality, selected.Label, "Nominatim / OpenStreetMap · " + selected.Source, version, "Geocodificata, da verificare · " + selected.Precision); DialogResult = true; };
    }
}
