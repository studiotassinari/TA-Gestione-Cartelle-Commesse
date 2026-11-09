using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TA.Desktop;

internal static class SessionInstanceChecks
{
    public static void Run()
    {
        var component = "Test." + Guid.NewGuid().ToString("N");
        var window = new Window { Width = 200, Height = 100, ShowInTaskbar = false };
        using (var first = new SessionInstance(component))
        {
            Check(first.IsPrimary, "Prima istanza ammessa");
            Probe(component, false);
            first.Listen(window);
            PumpUntil(() => window.IsVisible);
            Check(window.IsVisible, "Secondo avvio durante startup conservato e finestra aperta");
            window.WindowState = WindowState.Minimized; window.Hide();
            Probe(component, false);
            PumpUntil(() => window.IsVisible && window.WindowState != WindowState.Minimized);
            Check(window.IsVisible && window.WindowState != WindowState.Minimized, "Secondo processo ripristina finestra nascosta e minimizzata");
            window.WindowState = WindowState.Maximized; window.Hide();
            Probe(component, false);
            PumpUntil(() => window.IsVisible);
            Check(window.WindowState == WindowState.Maximized, "Ripristino conserva finestra massimizzata");
            Probe(component + ".Manager", true);
            Check(true, "Componenti diversi possono coesistere");
        }
        Probe(component, true);
        Check(true, "Uscita rilascia blocco e consente riavvio dopo aggiornamento");
        window.Close();
    }

    private static void Probe(string component, bool expected)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(SessionInstanceChecks).Assembly.Location);
        start.ArgumentList.Add("--instance-probe"); start.ArgumentList.Add(component); start.ArgumentList.Add(expected.ToString());
        using var child = Process.Start(start)!;
        if (!child.WaitForExit(10000)) { child.Kill(); throw new Exception("Seconda istanza non terminata"); }
        Check(child.ExitCode == 0, "Controllo istanza fra processi separati");
    }

    private static void PumpUntil(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < end)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }
    private static void Check(bool success, string message)
    { if (!success) throw new Exception(message); Console.WriteLine("PASS: " + message); }
}
