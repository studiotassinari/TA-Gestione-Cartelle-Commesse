using System.Windows;
using System.Windows.Controls;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Modulo riutilizzabile per editor piccoli; le regole sono sempre ripetute dal server.
internal sealed class BusinessForm : Window
{
    private readonly StackPanel fields = new() { Margin = new Thickness(22) };
    public BusinessForm(Window owner, string title)
    {
        Owner = owner; Title = title; Width = 620; Height = 680; MinWidth = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = owner.Background; Foreground = owner.Foreground; FontFamily = owner.FontFamily;
        Resources = owner.Resources;
        var dock = new DockPanel(); Content = dock;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16) };
        var save = new Button { Content = "Conferma", IsDefault = true }; save.Click += (_, _) => DialogResult = true;
        footer.Children.Add(save); footer.Children.Add(new Button { Content = "Annulla", IsCancel = true });
        DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
    private T Add<T>(string label, T control) where T : FrameworkElement
    {
        fields.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) }); fields.Children.Add(control); return control;
    }
    public TextBox Text(string label, string value = "") => Add(label, new TextBox { Text = value, MaxLength = 2000 });
    public DatePicker Date(string label, DateTime? value) => Add(label, new DatePicker { SelectedDate = value });
    public ComboBox Choice<T>(string label, IEnumerable<T> items, T? selected, string display = "") => Add(label, new ComboBox
    { ItemsSource = items, SelectedItem = selected, DisplayMemberPath = display, MinHeight = 32, IsTextSearchEnabled = true });
}
