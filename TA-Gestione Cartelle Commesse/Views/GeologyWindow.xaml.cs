using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;
public partial class GeologyWindow : System.Windows.Window
{
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
