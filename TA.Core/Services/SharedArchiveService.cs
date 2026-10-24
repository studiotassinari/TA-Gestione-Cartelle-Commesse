using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Models;

// Mantiene un file per categoria/radice e coordina i processi tramite un lock sulla condivisione.
namespace TA_Gestione_Cartelle_Commesse.Services;

public static class SharedArchiveService
{
    // Percorso condiviso esplicitamente indicato dall'utente.
    public const string DefaultFolder = @"\\ingtassinari.it\data\amministrazione\_GESTIONE COMMESSE";
    // JSON leggibile senza dipendenze o database da installare.
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    // Normalizza i percorsi senza risolvere collegamenti o accedere alla rete.
    public static string Normalize(string root) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();
    // L'hash separa radici diverse anche quando la categoria è la stessa.
    public static string FilePath(string folder, string root, char prefix) => Path.Combine(folder,
        $"{prefix}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(root))))}.json");

    // Legge una fotografia completa; FileShare.Delete permette la sostituzione atomica da un altro PC.
    public static ArchiveSnapshot? Load(string folder, string root, char prefix)
    {
        try
        {
            using var stream = new FileStream(FilePath(folder, root, prefix), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var data = JsonSerializer.Deserialize<ArchiveSnapshot>(stream, Options) ?? throw new InvalidDataException("Archivio JSON vuoto.");
            if (data.SchemaVersion != 1 || data.Root != Normalize(root) || data.Prefix != prefix || data.Records == null || data.Warnings == null)
                throw new InvalidDataException("Formato o radice dell'archivio non compatibili.");
            // Rifiuta anche strutture JSON sintatticamente valide ma prive dei campi indispensabili.
            if (data.Records.Any(r => r == null || string.IsNullOrWhiteSpace(r.Path) || r.Code == null || r.Client == null || r.Subject == null
                || (r.Inspection is { } inspection && (inspection.QuoteFiles == null || inspection.Issues == null || inspection.TotalBytes < 0 || inspection.FileCount < 0))))
                throw new InvalidDataException("Record incompleti nell'archivio JSON.");
            return data;
        }
        catch (FileNotFoundException) { return null; }
    }

    // Scrive solo scansioni complete; annullamento o errori principali non sostituiscono i dati salvati.
    public static bool Save(string folder, ArchiveSnapshot data, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(folder);
        var target = FilePath(folder, data.Root, data.Prefix);
        // Il lock viene rilasciato anche se il processo fallisce; un file .lock vuoto può restare ed è innocuo.
        using var lease = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = Load(folder, data.Root, data.Prefix);
        if (previous != null && previous.StartedUtc > data.StartedUtc) return false;
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, Options);
                stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            // Il file temporaneo risiede nella stessa cartella; i lettori non vedono un JSON scritto a metà.
            File.Move(temporary, target, true);
            return true;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
