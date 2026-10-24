using System.IO;
using System.Threading.Channels;
namespace TA_Gestione_Cartelle_Commesse.Services;

// Scrittura fuori dal thread grafico, senza credenziali e senza dipendere dalla mappatura Z:.
public static class ClientLog
{
    public const string NetworkFolder = @"\\ingtassinari.it\data\programmi\_originali\TA\Client\log";
    public static string LocalFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tassinari", "CommesseClient", "Logs");
    private static readonly Channel<string> queue = Channel.CreateBounded<string>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
    private static readonly Task writer = Task.Run(WriteLoop);
    public static void Write(string message, Exception? error = null)
        => queue.Writer.TryWrite($"{DateTimeOffset.Now:O} PID {Environment.ProcessId} {message}" + (error == null ? "" : " | " + error));
    private static async Task WriteLoop()
    {
        DateTime retry = DateTime.MinValue;
        await foreach (var entry in queue.Reader.ReadAllAsync())
        {
            if (DateTime.UtcNow >= retry)
            {
                try { Append(NetworkFolder, entry); continue; }
                catch (Exception ex)
                {
                    retry = DateTime.UtcNow.AddMinutes(1);
                    try { Append(LocalFolder, $"{DateTimeOffset.Now:O} Log rete non disponibile: {ex.Message}"); } catch { }
                }
            }
            try { Append(LocalFolder, entry); } catch { /* Il log non deve arrestare il client. */ }
        }
    }
    private static void Append(string folder, string entry)
    {
        Directory.CreateDirectory(folder);
        var name = $"Client-{Environment.MachineName}-{Environment.ProcessId}-{DateTime.Now:yyyyMMdd}";
        var path = Path.Combine(folder, name + ".log");
        // File separati per PC/processo e rotazione a 10 MiB senza cancellare registri esistenti.
        for (int part = 1; File.Exists(path) && new FileInfo(path).Length >= 10 * 1024 * 1024; part++)
            path = Path.Combine(folder, name + "-" + part + ".log");
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var output = new StreamWriter(stream, System.Text.Encoding.UTF8);
        output.WriteLine(entry);
    }
}
