using System.IO;
using System.Windows;
using TA.Desktop;
internal static class Program
{
    [STAThread]
    static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window { Title = "TA tray test", Width = 200, Height = 100, ShowInTaskbar = false };
        using var tray = new TrayLifetime(window, "TA test", "Client");
        bool closed = false; window.Closed += (_, _) => closed = true;
        window.Show(); window.WindowState = WindowState.Minimized;
        Check(!window.IsVisible && !closed, "Minimizza nasconde senza terminare");
        window.Show(); window.WindowState = WindowState.Normal; window.Close();
        Check(!window.IsVisible && !closed, "Pulsante chiudi mantiene la finestra attiva nell'area di notifica");
        var marker = UpdateLauncher.ExitMarker("Client");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        try
        {
            File.WriteAllText(marker, "update");
            window.Show(); window.Close();
            Check(closed && !File.Exists(marker), "Aggiornamento puo chiudere ordinatamente la finestra");
        }
        finally { if (File.Exists(marker)) File.Delete(marker); }
        Environment.SetEnvironmentVariable("TA_SKIP_UPDATE_ONCE", "1");
        UpdateLauncher.Check("Client");
        Check(Environment.GetEnvironmentVariable("TA_SKIP_UPDATE_ONCE") == null, "Riapertura dopo aggiornamento non innesca cicli");
        app.Shutdown();
    }
    static void Check(bool success, string text) { if (!success) throw new Exception(text); Console.WriteLine("PASS: " + text); }
}
