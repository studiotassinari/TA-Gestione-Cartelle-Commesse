using System.Diagnostics;
using System.IO;
namespace TA.Desktop;
public static class UpdateLauncher
{
    private static string Root(string component) => component == "Server" ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")) : AppContext.BaseDirectory;
    public static string ExitMarker(string component)
    {
        var cache = component == "Server" ? Path.Combine(Root(component), "UpdateCache") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tassinari", "CommesseClient", "Updates");
        return Path.Combine(cache, "exit-" + Environment.ProcessId);
    }
    // Nessun timer o task: viene invocato solo all'avvio o dal comando manuale.
    public static void Check(string component, bool manual = false)
    {
        if (!manual && Environment.GetEnvironmentVariable("TA_SKIP_UPDATE_ONCE") == "1")
        { Environment.SetEnvironmentVariable("TA_SKIP_UPDATE_ONCE", null); return; }
        var script = Path.Combine(Root(component), "Update-Application.ps1");
        if (!File.Exists(script)) return; // Esecuzione da Visual Studio, senza installatore.
        try
        {
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Component", component, "-OnOpen", "-ParentId", Environment.ProcessId.ToString() }) start.ArgumentList.Add(argument);
            if (manual) start.ArgumentList.Add("-Interactive");
            Process.Start(start);
        }
        catch (Exception ex) { if (manual) System.Windows.MessageBox.Show(ex.Message, "Verifica aggiornamenti"); }
    }
}
