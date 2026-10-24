using System.Diagnostics;
using System.IO;
namespace TA.Desktop;
public static class UpdateLauncher
{
    private static string Root(string component) => component == "Server" ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")) : AppContext.BaseDirectory;
    private static string Cache(string component) => component == "Server" ? Path.Combine(Root(component), "UpdateCache")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tassinari", "CommesseClient", "Updates");
    public static string ExitMarker(string component) => Path.Combine(Cache(component), "exit-" + Environment.ProcessId);
    // Parte soltanto all'apertura o su richiesta; nessun installatore e nessuna attivita pianificata.
    public static async void Check(string component, bool manual = false)
    {
        if (!manual && Environment.GetEnvironmentVariable("TA_SKIP_UPDATE_ONCE") == "1")
        { Environment.SetEnvironmentVariable("TA_SKIP_UPDATE_ONCE", null); return; }
        var installed = Path.Combine(Root(component), "Updater", "TA.Updater.exe");
        if (!File.Exists(installed)) return;
        try
        {
            // Esegue una copia esterna ai file da sostituire: anche l'aggiornatore puo essere aggiornato.
            var runner = await Task.Run(() =>
            {
                using var source = File.OpenRead(installed); var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)); source.Dispose(); var folder = Path.Combine(Cache(component), "runner-" + hash);
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "TA.Updater.exe"); if (!File.Exists(path)) File.Copy(installed, path); using var copy = File.OpenRead(path); if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(copy)) != hash) throw new IOException("Copia aggiornatore non valida."); return path;
            });
            var start = new ProcessStartInfo(runner) { UseShellExecute = false };
            foreach (var arg in new[] { "--component", component, "--root", Root(component) }) start.ArgumentList.Add(arg);
            if (!manual) start.ArgumentList.Add("--automatic");
            Process.Start(start);
        }
        catch (Exception ex) { if (manual) System.Windows.MessageBox.Show(ex.Message, "Aggiornamento diretto"); }
    }
}
