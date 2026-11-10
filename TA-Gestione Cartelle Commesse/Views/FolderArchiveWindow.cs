using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;

public sealed class FolderArchiveWindow : Window
{
    public FolderArchiveWindow(FolderArchiveViewModel vm)
    {
        DataContext = vm; Title = "Archivia cartella in ZIP — TA"; Width = 800; Height = 480;
        MinWidth = 650; MinHeight = 400;
        var main = Application.Current.MainWindow;
        Resources = main.Resources; Background = main.Background; Foreground = main.Foreground;
        FontFamily = main.FontFamily; Icon = main.Icon;
        var panel = new StackPanel { Margin = new Thickness(22) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Archiviazione sul server", FontSize = 26, Margin = new Thickness(0, 0, 0, 18) });
        Label("Cartella originale"); Label(vm.Source); Label("ZIP verificato da conservare"); Label(vm.Zip);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 18) };
        panel.Children.Add(actions);
        AddButton("1. Prepara ZIP", nameof(vm.CanPrepare), async () => await vm.PrepareAsync());
        AddButton("Aggiorna stato", nameof(vm.CanRefresh), async () => await vm.RefreshAsync());
        AddButton("2. Elimina originale…", nameof(vm.CanDelete), async () =>
        {
            if (!vm.CanDelete) return;
            var answer = MessageBox.Show(this, "Eliminare definitivamente questa cartella e il suo contenuto?\n\n" + vm.Source +
                "\n\nResterà lo ZIP verificato:\n" + vm.Zip + "\n\nIl server ricontrollerà ZIP e originali prima di procedere.",
                "Conferma eliminazione originale", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            await vm.DeleteAsync(answer == MessageBoxResult.Yes);
        });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        status.SetBinding(TextBlock.TextProperty, nameof(vm.Message)); panel.Children.Add(status);
        Label("Lo ZIP conserva file e sottocartelle, non le autorizzazioni NTFS e tutti gli attributi del filesystem. Chiudere la finestra non annulla un lavoro già avviato sul server.");
        // Solo consultazione dello stato di un lavoro esplicitamente avviato dal client.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += async (_, _) => { if (vm.Result is { FinishedUtc: null } && vm.CanRefresh) await vm.RefreshAsync(); };
        Loaded += (_, _) => timer.Start(); Closed += (_, _) => timer.Stop();
        void Label(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) });
        void AddButton(string text, string enabled, Func<Task> action)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(12, 9, 12, 9) };
            button.SetBinding(IsEnabledProperty, new Binding(enabled)); button.Click += async (_, _) => await action(); actions.Children.Add(button);
        }
    }
}
