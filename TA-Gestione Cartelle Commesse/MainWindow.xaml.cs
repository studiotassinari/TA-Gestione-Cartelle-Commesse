using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TA_Gestione_Cartelle_Commesse.ViewModels;

namespace TA_Gestione_Cartelle_Commesse;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // These handlers translate view gestures into the shared ViewModel command.
    private void FolderRow_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void FolderRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            sender is not DataGridRow row ||
            row.DataContext is not FolderRecord record ||
            row.Tag is not CategoryViewModel category) return;

        category.Selected = record;
        if (category.OpenFolderCommand.CanExecute(null))
            category.OpenFolderCommand.Execute(null);
        e.Handled = true;
    }
}
