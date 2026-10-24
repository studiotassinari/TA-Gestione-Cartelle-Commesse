using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;
public static class CommunicationLog
{
    private static readonly object gate = new();
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tassinari", "CommesseServer", "Logs");
    public static string FilePath => Path.Combine(DirectoryPath, "communications.jsonl");
    private static string Safe(string? value, int limit = 512) => new((value ?? "").Where(c => !char.IsControl(c)).Take(limit).ToArray());
    public static void Write(string? computer, string address, string user, string action, string outcome, string detail, string? correlation)
    {
        try
        {
            var entry = new CommunicationRecord(DateTimeOffset.Now, Safe(computer, 128), Safe(address), Safe(user), Safe(action, 80), Safe(outcome, 80), Safe(detail, 2048), Safe(correlation, 80));
            lock (gate)
            {
                if (!Directory.Exists(DirectoryPath)) return; // ACL predisposte dall'installatore.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2 * 1024 * 1024) File.Move(FilePath, FilePath + ".previous", true);
                using var file = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var output = new StreamWriter(file);
                output.WriteLine(JsonSerializer.Serialize(entry));
            }
        }
        catch (Exception ex) { StartupLog.Write("Log comunicazioni non disponibile: " + ex.Message); }
    }
    public static string Tail(string path)
    {
        if (!File.Exists(path)) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(Math.Max(0, stream.Length - 262144), SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        if (stream.Position > 0) reader.ReadLine();
        return reader.ReadToEnd();
    }
}
