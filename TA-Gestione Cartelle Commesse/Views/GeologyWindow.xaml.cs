using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;
public partial class GeologyWindow : System.Windows.Window
{
    private void Report_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.DataGridRow row && row.Item is Contracts.GeologicalReport report && DataContext is GeologyViewModel model) model.Selected = report;
    }
    private async void Geocode_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not GeologyViewModel model || !model.CanGeocode) return;
        var dialog = new GeologyGeocodingDialog(model) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Change != null) await model.SavePositionAsync(dialog.Change);
    }
    // Intestazioni, barre di scorrimento e spazio vuoto non attivano l'apertura.
    private async void Reports_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left || DataContext is not GeologyViewModel model ||
            e.OriginalSource is not System.Windows.DependencyObject source ||
            System.Windows.Controls.ItemsControl.ContainerFromElement(ReportsGrid, source) is not System.Windows.Controls.DataGridRow row ||
            row.Item is not Contracts.GeologicalReport report) return;
        e.Handled = true; model.Selected = report; await model.OpenSelectedAsync();
    }
    private void Position_Click(object sender, System.Windows.RoutedEventArgs e) => EditPosition();
    private void PositionPicked(double lat, double lon) => EditPosition(lat, lon);
    private async void EditPosition(double? lat = null, double? lon = null)
    {
        if (DataContext is not GeologyViewModel model || !model.CanLocate || model.Selected == null) return;
        var dialog = new GeologyPositionDialog(model.Selected, model.Version, lat, lon) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Change != null) await model.SavePositionAsync(dialog.Change);
    }
    public GeologyWindow(GeologyViewModel model)
    {
        InitializeComponent(); DataContext = model;
        void RowsChanged() => Map.SetReports(model.Rows);
        void SelectionChanged() { Map.Select(model.Selected?.Id); if (model.Selected != null) ReportsGrid.ScrollIntoView(model.Selected); }
        model.RowsChanged += RowsChanged; model.SelectionChanged += SelectionChanged; Map.ReportSelected += model.Select; Map.PositionPicked += PositionPicked;
        Loaded += async (_, _) => { RowsChanged(); await model.LoadAsync(); SearchBox.Focus(); };
        Closed += (_, _) => { model.RowsChanged -= RowsChanged; model.SelectionChanged -= SelectionChanged; Map.ReportSelected -= model.Select; Map.PositionPicked -= PositionPicked; Map.Dispose(); model.Dispose(); };
    }
}
