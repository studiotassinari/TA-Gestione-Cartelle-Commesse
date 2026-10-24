using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
namespace TA.ServiceManager;
// Legge solo dati diagnostici: nessuna password viene richiesta o esportata.
internal static class ServiceDiagnostics
{
    public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tassinari", "CommesseServer");
    public static string LogFile => Path.Combine(Data, "Logs", "service.log");
    public static string Tail(string? path = null)
    {
        path ??= LogFile; if (!File.Exists(path)) return "Il servizio non ha ancora scritto messaggi di avvio.";
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        file.Seek(Math.Max(0, file.Length - 65536), SeekOrigin.Begin);
        using var reader = new StreamReader(file);
        return reader.ReadToEnd();
    }
    public static string Inspect()
    {
        var text = new StringBuilder();
        text.AppendLine($"Diagnosi {DateTimeOffset.Now:O} - {Environment.MachineName}");
        text.AppendLine("Controlli eseguiti con l'account amministratore dell'utilita, non con l'account del servizio.");
        var config = Path.Combine(Data, "server-settings.json");
        if (!File.Exists(config)) text.AppendLine("ERRORE: configurazione assente. Premere Configura e indicare il gruppo AD.");
        else try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(config));
            var server = document.RootElement.GetProperty("Server");
            foreach (var key in new[] { "AllowedGroup", "Port", "JobsRoot", "BidsRoot", "ArchiveFolder" })
                text.AppendLine(key + ": " + (server.TryGetProperty(key, out var value) ? value.ToString() : "MANCANTE"));
            if (!server.TryGetProperty("AllowedGroup", out var group) || string.IsNullOrWhiteSpace(group.GetString()))
                text.AppendLine("ERRORE: gruppo AD vuoto; il servizio non puo avviarsi.");
        }
        catch (Exception ex) { text.AppendLine("ERRORE configurazione: " + ex.Message); }
        foreach (var name in new[] { "Application", "System" })
        {
            text.AppendLine("--- Eventi " + name + " ---");
            try
            {
                using var log = new EventLog(name);
                var matches = 0;
                for (int i = log.Entries.Count - 1, count = 0; i >= 0 && count < 1500 && matches < 15; i--, count++)
                {
                    var entry = log.Entries[i];
                    if (entry.TimeGenerated < DateTime.Now.AddDays(-1)) break;
                    var message = entry.Message;
                    if (!message.Contains("TA.Server", StringComparison.OrdinalIgnoreCase) && !message.Contains("TACommesseServer", StringComparison.OrdinalIgnoreCase) && !message.Contains("TA Gestione Commesse", StringComparison.OrdinalIgnoreCase)) continue;
                    text.AppendLine($"{entry.TimeGenerated:O} {entry.Source} {entry.InstanceId}: {message}"); matches++;
                }
                if (matches == 0) text.AppendLine("Nessun evento pertinente nelle ultime 24 ore tra gli eventi esaminati.");
            }
            catch (Exception ex) { text.AppendLine("Lettura eventi: " + ex.Message); }
        }
        return text.ToString();
    }
}
