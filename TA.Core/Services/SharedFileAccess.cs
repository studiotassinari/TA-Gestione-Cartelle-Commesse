using System.Diagnostics;

namespace TA_Gestione_Cartelle_Commesse.Services;

// I lock applicativi restano obbligatori; i blocchi Windows temporanei vengono attesi senza forzarli.
internal static class SharedFileAccess
{
    public static T Retry<T>(Func<T> operation, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var pause = 100;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return operation(); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
            {
                if (watch.Elapsed >= TimeSpan.FromSeconds(10))
                    throw new IOException("Archivio JSON ancora occupato dopo i tentativi di accesso; i dati precedenti restano conservati. " + ex.Message, ex);
                if (token.WaitHandle.WaitOne(pause)) token.ThrowIfCancellationRequested();
                pause = Math.Min(500, pause * 2);
            }
        }
    }
    public static void Retry(Action operation, CancellationToken token) => Retry(() => { operation(); return true; }, token);
    public static byte[] ReadBytes(string path, CancellationToken token) => Retry(() =>
    {
        // Consente lettori e sostituzione atomica. Il file .lock protegge tutte le generazioni annuali.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        var block = new byte[65536];
        int count;
        while ((count = file.Read(block)) > 0) { token.ThrowIfCancellationRequested(); buffer.Write(block, 0, count); }
        return buffer.ToArray();
    }, token);
}
