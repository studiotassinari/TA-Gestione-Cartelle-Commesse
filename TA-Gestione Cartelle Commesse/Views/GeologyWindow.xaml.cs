using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;
public partial class GeologyWindow : System.Windows.Window
{
    public GeologyWindow(GeologyViewModel model)
    {
        InitializeComponent(); DataContext = model;
        void RowsChanged() => Map.SetReports(model.Rows);
        void SelectionChanged() { Map.Select(model.Selected?.Id); if (model.Selected != null) ReportsGrid.ScrollIntoView(model.Selected); }
        model.RowsChanged += RowsChanged; model.SelectionChanged += SelectionChanged; Map.ReportSelected += model.Select;
        Loaded += async (_, _) => { RowsChanged(); await model.LoadAsync(); SearchBox.Focus(); };
        Closed += (_, _) => { model.RowsChanged -= RowsChanged; model.SelectionChanged -= SelectionChanged; Map.ReportSelected -= model.Select; Map.Dispose(); model.Dispose(); };
    }
}
