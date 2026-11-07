using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;
public partial class WorkProjectListItem : UserControl
{
    public WorkProjectListItem() => InitializeComponent();
    private void SelectRow(object sender, MouseButtonEventArgs e)
    {
        if (this.FindAncestor<ListBox>() is {} list && ItemsControl.ContainerFromElement(list, this) is ListBoxItem row) row.IsSelected = true;
    }
    private void OpenFolder(object sender, MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node != null && node != this; node = VisualTreeHelper.GetParent(node))
            if (node is ButtonBase) return;
        if (DataContext is WorkProjectViewModel vm && vm.OpenFolderCommand.CanExecute(null))
        { vm.OpenFolderCommand.Execute(null); e.Handled = true; }
    }
}
internal static class ProjectVisualTree
{
    public static T? FindAncestor<T>(this DependencyObject child) where T : DependencyObject
    {
        for (var node = VisualTreeHelper.GetParent(child); node != null; node = VisualTreeHelper.GetParent(node))
            if (node is T found) return found;
        return null;
    }
}
