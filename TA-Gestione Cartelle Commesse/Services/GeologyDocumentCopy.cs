using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SharpCompress.Archives;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA.Server.Documents;

namespace TA_Gestione_Cartelle_Commesse.Services;

public sealed record GeologicalCopyRequest(string Source, string? ArchiveEntry);
public sealed record GeologicalCopyReply(string? Path = null, string? Error = null);

// Ogni doppio clic prepara una copia locale non modificabile; il parser ZIP/7z gira in un processo limitato.
public static class GeologyDocumentCopy
{
    private const int MaximumBytes = 50 * 1024 * 1024;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".rtf", ".odt" };
    public static string CacheRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tassinari", "CommesseClient", "DocumentiConsultazione");

    public static async Task<string> PrepareAsync(GeologicalReport report, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(report.PhysicalPath)) throw new IOException("Percorso originale non disponibile nel catalogo.");
        var executable = System.IO.Path.ChangeExtension(typeof(GeologyDocumentCopy).Assembly.Location, ".exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--geological-copy-worker");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromSeconds(25));
        using var process = Process.Start(start) ?? throw new IOException("Impossibile preparare il documento.");
        var output = ReadBounded(process.StandardOutput, limit.Token); var errors = ReadBounded(process.StandardError, limit.Token);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new GeologicalCopyRequest(report.PhysicalPath, report.ArchiveEntry)).AsMemory(), limit.Token);
            process.StandardInput.Close();
            while (!process.HasExited)
            {
                limit.Token.ThrowIfCancellationRequested(); process.Refresh();
                if (process.PrivateMemorySize64 > 512L * 1024 * 1024) throw new IOException("Documento troppo complesso: limite di memoria raggiunto.");
                if (output.IsFaulted) await output; if (errors.IsFaulted) await errors;
                await Task.Delay(100, limit.Token);
            }
            var json = await output;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json)) throw new IOException("Preparazione documento interrotta (codice " + process.ExitCode + ").");
            var reply = JsonSerializer.Deserialize<GeologicalCopyReply>(json) ?? throw new IOException("Risposta apertura vuota.");
            await errors;
            if (reply.Error != null || process.ExitCode != 0) throw new IOException(reply.Error ?? "Preparazione interrotta.");
            var path = System.IO.Path.GetFullPath(reply.Path ?? "");
            if (!path.StartsWith(System.IO.Path.GetFullPath(CacheRoot).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || !Extensions.Contains(System.IO.Path.GetExtension(path)))
                throw new IOException("Copia del documento non valida.");
            return path;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("Preparazione interrotta dopo 25 secondi."); }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
            try { await Task.WhenAll(output, errors); } catch { }
        }
    }

    public static async Task RunWorkerAsync()
    {
        try
        {
            var input = await Console.In.ReadLineAsync(); if (input == null || input.Length > 16000) throw new IOException("Richiesta apertura non valida.");
            var request = JsonSerializer.Deserialize<GeologicalCopyRequest>(input) ?? throw new IOException("Richiesta vuota.");
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new GeologicalCopyReply(Create(request))));
        }
        catch (Exception ex) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new GeologicalCopyReply(Error: ex.Message))); }
    }

    public static string Create(GeologicalCopyRequest request)
    {

        // Le radici sono quelle aziendali già autorizzate, mai percorsi eseguibili o URL forniti da un documento.
        var roots = new[] { @"\\ingtassinari.it\data\lavori", @"\\ingtassinari.it\data\gare", @"\\ingtassinari.it\data\archivio", @"Z:\lavori", @"Z:\gare", @"Z:\archivio" };
        return CreateForRoots(request, roots, CacheRoot);
    }
    internal static string CreateForRoots(GeologicalCopyRequest request, IReadOnlyList<string> roots, string cacheRoot)
    {
        var path = System.IO.Path.GetFullPath(request.Source);
        var root = roots.FirstOrDefault(r => path.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)) ?? throw new UnauthorizedAccessException("Documento fuori dalle sorgenti aziendali.");
        path = SafeDocumentSource.Resolve(root, root, System.IO.Path.GetRelativePath(root, path));
        var name = request.ArchiveEntry == null ? System.IO.Path.GetFileName(path) : request.ArchiveEntry.Replace('\\', '/').Split('/').Last();
        if (!Extensions.Contains(System.IO.Path.GetExtension(name))) throw new IOException("Formato non ammesso per l'apertura dal catalogo.");
        using var source = SafeDocumentSource.Open(root, path);
        byte[] content;
        if (request.ArchiveEntry == null)
        {
            if (source.Length > MaximumBytes) throw new IOException("Documento oltre 50 MiB.");
            content = CopyBounded(source);
        }
        else
        {
            DocumentValidation.Relative(request.ArchiveEntry);
            if (System.IO.Path.GetExtension(path).ToLowerInvariant() is not (".zip" or ".7z")) throw new IOException("Contenitore non supportato.");
            using var archive = ArchiveFactory.OpenArchive(source); var wanted = request.ArchiveEntry.Replace('\\', '/');
            IArchiveEntry? found = null; int count = 0;
            foreach (var entry in archive.Entries)
            {
                if (++count > 200000) throw new IOException("Archivio troppo grande.");
                if (!entry.IsDirectory && entry.Key?.Replace('\\', '/') == wanted)
                { if (found != null) throw new IOException("Nome duplicato nell'archivio."); found = entry; }
            }
            if (found == null || found.IsEncrypted || found.Size > MaximumBytes) throw new IOException("Documento assente, cifrato o oltre 50 MiB.");
            using var entryStream = found.OpenEntryStream(); content = CopyBounded(entryStream);
        }
        Directory.CreateDirectory(cacheRoot);
        var check = System.IO.Path.GetFullPath(cacheRoot);
        while (check != System.IO.Path.GetPathRoot(check))
        {
            if ((File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) throw new IOException("Cartella delle copie reindirizzata: apertura interrotta.");
            check = System.IO.Path.GetDirectoryName(check)!;
        }
        var directory = System.IO.Path.Combine(cacheRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var target = System.IO.Path.Combine(directory, name);
        File.WriteAllBytes(target, content);
        // Office apre in visualizzazione protetta; l'originale, anche se in archivio, non viene mai modificato.
        File.WriteAllText(target + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
        return target;
    }

    private static byte[] CopyBounded(Stream input)
    {
        using var output = new MemoryStream(); var buffer = new byte[65536]; int count;
        while ((count = input.Read(buffer)) > 0) { if (output.Length + count > MaximumBytes) throw new IOException("Documento oltre 50 MiB."); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
    private static async Task<string> ReadBounded(StreamReader input, CancellationToken token)
    {
        var buffer = new char[2048]; var output = new System.Text.StringBuilder(); int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0) { if (output.Length + count > 16000) throw new IOException("Risposta apertura oltre il limite."); output.Append(buffer, 0, count); }
        return output.ToString();
    }
}
