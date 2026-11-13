using System.Windows.Controls;
namespace TA_Gestione_Cartelle_Commesse.Views;
public partial class WorkProjectListView : UserControl
{
    private readonly Dictionary<Button, string> labels = new();
    private ViewModels.CategoryViewModel? shown;
    public WorkProjectListView()
    {
        InitializeComponent();
        foreach (var button in Header.Children.OfType<Button>()) labels[button] = button.Content?.ToString() ?? "";
        DataContextChanged += (_, _) =>
        {
            if (shown != null) shown.SortChanged -= ShowSort;
            shown = DataContext as ViewModels.CategoryViewModel;
            if (shown != null) shown.SortChanged += ShowSort;
            ShowSort();
        };
        Unloaded += (_, _) => { if (shown != null) shown.SortChanged -= ShowSort; };
        Loaded += (_, _) => { if (shown != null) { shown.SortChanged -= ShowSort; shown.SortChanged += ShowSort; } ShowSort(); };
    }
    private void Sort(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string column } && DataContext is ViewModels.CategoryViewModel category) category.SortBy(column);
    }
    // La freccia indica la colonna e il verso dell'ordinamento attivo.
    private void ShowSort()
    {
        foreach (var (button, label) in labels)
            button.Content = label + (shown != null && shown.SortColumn == (button.Tag as string) ? (shown.SortDescending ? " ▼" : " ▲") : "");
    }
}
