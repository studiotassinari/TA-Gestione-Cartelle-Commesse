using Microsoft.Extensions.Logging;
namespace TA.Server;
// Registro locale limitato a due file da circa 2 MB, leggibile durante l'avvio.
internal static class StartupLog
{
    private static readonly object Gate = new();
    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tassinari", "CommesseServer", "Logs");
                var path = Path.Combine(directory, "service.log");
                // La cartella con ACL dedicate viene predisposta dall'installatore.
                if (!Directory.Exists(directory)) { Console.Error.WriteLine(message); return; }
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024) File.Move(path, path + ".previous", true);
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(file);
                writer.WriteLine($"{DateTimeOffset.Now:O} PID {Environment.ProcessId} {message}");
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("Registro non disponibile: " + ex.Message); }
    }
}
internal sealed class StartupLogProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName);
    public void Dispose() { }
    private sealed class FileLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (IsEnabled(level)) StartupLog.Write($"{level} {category}: {formatter(state, exception)} {exception}"); }
    }
}
