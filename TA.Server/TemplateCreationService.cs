using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;
// Costruisce una struttura vuota e la rende visibile con una rinomina finale, senza sovrascritture.
public sealed class TemplateCreationService(ServerSettings settings)
{
    private readonly object gate = new();
    public TemplatePreview Preview(CreateJobRequest request)
    {
        var code = (request.Code ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(code, "^C[0-9]{5}$")) throw new ArgumentException("Codice atteso: CXXYYY, ad esempio C26199.");
        var client = Segment(request.Client, "Cliente");
        var place = string.IsNullOrWhiteSpace(request.Place) ? "" : Segment(request.Place, "Luogo");
        var subject = Segment(request.Subject, "Argomento");
        var year = "20" + code.Substring(1, 2);
        var root = Path.GetFullPath(settings.JobsRoot);
        var yearRoot = Path.Combine(root, year);
        CheckAncestors(root); CheckAncestors(yearRoot);
        if (Directory.EnumerateDirectories(yearRoot).Any(p => Path.GetFileName(p).StartsWith(code, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Esiste gia una cartella con il codice " + code + ". Nessuna cartella e stata modificata.");
        var name = code + " - " + client + (place.Length > 0 ? " - " + place : "") + " - " + subject;
        if (name.Length > 200) throw new ArgumentException("Nome cartella troppo lungo (massimo 200 caratteri).");
        var destination = Path.Combine(yearRoot, name);
        var template = Path.GetFullPath(settings.JobTemplate);
        CheckAncestors(template);
        var directories = new List<string>();
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((template, 0));
        while (pending.TryPop(out var item))
        {
            if (item.Depth >= 32) throw new IOException("Modello troppo profondo (limite 32 livelli).");
            foreach (var directory in Directory.EnumerateDirectories(item.Path))
            {
                CheckDirectory(directory);
                var relative = Path.GetRelativePath(template, directory);
                directories.Add(relative);
                if (directories.Count > 10000) throw new IOException("Modello troppo grande (limite 10000 cartelle).");
                pending.Push((directory, item.Depth + 1));
            }
        }
        var ordered = directories.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(template + "\n" + string.Join("\n", ordered))));
        return new(destination, template, ordered, stamp);
    }
    public string Create(CreateJobRequest request)
    {
        lock (gate)
        {
            // Lock condiviso tra istanze del server: riusa la cartella archivio gia abilitata in scrittura.
            CheckAncestors(settings.ArchiveFolder);
            using var lease = new FileStream(Path.Combine(settings.ArchiveFolder, ".create-job.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var plan = Preview(request);
            if (request.TemplateStamp != plan.Stamp) throw new IOException("Il modello e cambiato o manca l'anteprima. Aprire nuovamente la creazione.");
            var year = Path.GetDirectoryName(plan.Destination)!;
            var staging = Path.Combine(year, ".TA-new-" + Guid.NewGuid().ToString("N"));
            var created = new List<string>();
            try
            {
                Directory.CreateDirectory(staging); created.Add(staging);
                foreach (var relative in plan.Directories.OrderBy(p => p.Split(Path.DirectorySeparatorChar).Length))
                {
                    var target = Path.GetFullPath(Path.Combine(staging, relative));
                    if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Percorso modello non valido.");
                    CheckAncestors(Path.GetDirectoryName(target)!);
                    Directory.CreateDirectory(target); created.Add(target);
                }
                CheckAncestors(year);
                // Directory.Move fallisce se la destinazione esiste, anche in caso di creazione esterna concorrente.
                Directory.Move(staging, plan.Destination);
                return plan.Destination;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Rimuove solo le cartelle vuote create da questa operazione, mai documenti aggiunti da altri.
                foreach (var directory in created.AsEnumerable().Reverse())
                    try { if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0) Directory.Delete(directory, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                var residue = Directory.Exists(staging) ? " Struttura temporanea rimasta: " + staging : "";
                throw new IOException("Creazione non completata. Verificare i permessi dell'account del servizio sulla cartella annuale." + residue, ex);
            }
        }
    }
    private static string Segment(string? value, string label)
    {
        value = value?.Trim() ?? "";
        if (value.Length is 0 or > 100 || value.EndsWith('.') || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value is "." or "..")
            throw new ArgumentException(label + " non valido: usare testo senza separatori di percorso o caratteri riservati.");
        return value;
    }
    private static void CheckAncestors(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        CheckDirectory(root);
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        { current = Path.Combine(current, part); CheckDirectory(current); }
    }
    private static void CheckDirectory(string path)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Cartella non disponibile: " + path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Collegamento escluso dal modello o destinazione: " + path);
    }
}
