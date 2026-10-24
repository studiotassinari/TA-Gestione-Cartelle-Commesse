using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace TA.Updater;
public static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window { Title = "TA — Aggiornatore", Width = 620, Height = 320, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var panel = new StackPanel { Margin = new Thickness(24) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Aggiornamento diretto dei file", FontSize = 24, Margin = new Thickness(0, 0, 0, 14) });
        var status = new TextBlock { Text = "Scegli il componente installato su questo PC. Il server richiede privilegi amministrativi.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(status);
        var bar = new ProgressBar { Height = 6, Visibility = Visibility.Collapsed }; panel.Children.Add(bar);
        var choices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) }; panel.Children.Add(choices);
        bool working = false; bool automatic = args.Contains("--automatic");
        string? Value(string key) { var at = Array.IndexOf(args, key); return at >= 0 && at + 1 < args.Length ? args[at + 1] : null; }
        async Task Run(string component)
        {
            if (working) return;
            working = true; choices.IsEnabled = false;
            try
            {
                var root = Value("--root") ?? UpdateEngine.FindRoot(component);
                if (component == "Server" && !UpdateEngine.IsAdministrator())
                {
                    var elevated = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
                    elevated.ArgumentList.Add("--component"); elevated.ArgumentList.Add(component); elevated.ArgumentList.Add("--root"); elevated.ArgumentList.Add(root);
                    Process.Start(elevated); app.Shutdown(); return;
                }
                bar.Visibility = Visibility.Visible; bar.IsIndeterminate = true;
                var changed = await Task.Run(() => new UpdateEngine(component, root, (text, show) => window.Dispatcher.Invoke(() => { status.Text = text; if (show) window.Show(); })).Run());
                bar.IsIndeterminate = false;
                if (automatic && !changed) app.Shutdown();
                else { window.Show(); status.Text += "\nPuoi chiudere questa finestra."; }
            }
            catch (Exception ex) { window.Show(); status.Text = "Operazione non completata: " + ex.Message; bar.IsIndeterminate = false; }
            finally { working = false; choices.IsEnabled = true; }
        }
        foreach (var component in new[] { "Client", "Server" })
        { var button = new Button { Content = "Aggiorna " + component, Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(15, 8, 15, 8) }; button.Click += async (_, _) => await Run(component); choices.Children.Add(button); }
        window.Closing += (_, e) => { if (working) { e.Cancel = true; status.Text += "\nAttendere il completamento per non interrompere la sostituzione."; } }; window.Closed += (_, _) => app.Shutdown();
        app.Startup += async (_, _) => { if (!automatic) window.Show(); if (Value("--component") is { } component) await Run(component); };
        app.Run();
    }
}
