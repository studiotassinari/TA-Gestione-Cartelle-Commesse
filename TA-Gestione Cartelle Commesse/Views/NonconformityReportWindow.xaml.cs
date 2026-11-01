using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;
public partial class NonconformityReportWindow : System.Windows.Window
{
    public NonconformityReportWindow(NonconformityReportViewModel model)
    {
        InitializeComponent();DataContext=model;
        Loaded+=async (_,_)=>await model.LoadAsync();
        Closed+=(_,_)=>model.Dispose();
    }
}
