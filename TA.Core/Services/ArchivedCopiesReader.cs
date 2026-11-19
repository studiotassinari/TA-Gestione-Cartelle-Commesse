using System.IO;
using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Models;
namespace TA_Gestione_Cartelle_Commesse.Services;

// Copie ZIP/7z delle cartelle di commesse e gare rimaste accanto alle altre (create da «Archivia cartella in ZIP», che scrive
// «C26012 - Cliente - Oggetto.zip» nella stessa cartella dell'originale). Servono soltanto al database comune con GeCo: per
// l'allineamento una copia archiviata è un riscontro valido della commessa, anche dopo l'eliminazione dell'originale.
// Solo nomi e metadati del file system (dimensione e data dell'ultima modifica): gli archivi non vengono mai aperti.
// Percorso dei contenitori come FolderReader: per le commesse le sole cartelle annuali (19xx/20xx) della radice, per le gare la
// radice e i contenitori non riconosciuti come cartelle di gara. Le cartelle riconosciute non si visitano: dentro una commessa
// uno ZIP è un documento della commessa, non la sua copia archiviata.
public static class ArchivedCopiesReader
{
    private static readonly string[] Extensions = [".zip", ".7z"];
    private static readonly Regex Pattern = new(@"^(?<code>[CG](?<year>\d{2})(?<number>\d{3}))\s*-\s*(?<client>.+?)\s+-\s+(?<subject>.+)$", RegexOptions.IgnoreCase);
    private static readonly Regex Code = new(@"^[CG](?<year>\d{2})(?<number>\d{3})", RegexOptions.IgnoreCase);

    public static bool IsArchive(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // Copie della categoria (C o G) sotto la radice. Un contenitore non leggibile rende l'elenco incompleto: chi lo usa non deve
    // scriverlo come elenco completo, altrimenti le copie non lette risulterebbero sparite.
    public static ScanResult Scan(string root, char prefix, CancellationToken token)
    {
        prefix = char.ToUpperInvariant(prefix);
        if (prefix is not ('C' or 'G')) throw new ArgumentException("Copie archiviate cercate solo tra commesse e gare.");
        var records = new List<FolderRecord>(); var warnings = new List<string>(); bool incomplete = false;
        var pending = new Stack<string>();
        try
        {
            if (prefix == 'C')
            {
                foreach (var year in Directory.EnumerateDirectories(root))
                    if (Regex.IsMatch(Path.GetFileName(year), @"^(19|20)[0-9]{2}$") && (File.GetAttributes(year) & FileAttributes.ReparsePoint) == 0) pending.Push(year);
            }
            else pending.Push(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new([], [], Error: ex.Message); }
        while (pending.TryPop(out var current))
        {
            if (token.IsCancellationRequested) return new([], [], true);
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(current))
                {
                    if (token.IsCancellationRequested) return new([], [], true);
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0 || FolderReader.IsExcluded(path)) continue;
                    var name = Path.GetFileName(path);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        // Solo per le gare si scende nei contenitori, come nella lettura dell'elenco; mai dentro una cartella con codice.
                        if (prefix == 'G' && !Code.IsMatch(name)) pending.Push(path);
                        continue;
                    }
                    if (!IsArchive(path)) continue;
                    var bare = Path.GetFileNameWithoutExtension(path);
                    if (!Code.IsMatch(bare) || char.ToUpperInvariant(bare[0]) != prefix) continue;
                    var info = new FileInfo(path);
                    var match = Pattern.Match(bare);
                    var row = match.Success
                        ? new FolderRecord(match.Groups["code"].Value.ToUpperInvariant(), match.Groups["year"].Value, match.Groups["number"].Value, match.Groups["client"].Value.Trim(), match.Groups["subject"].Value.Trim(), path, "Archiviata")
                        : new FolderRecord(bare[..6].ToUpperInvariant(), bare.Substring(1, 2), bare.Substring(3, 3), "Da verificare", bare, path, "Archiviata");
                    DateTime? modified = null;
                    try { modified = info.LastWriteTimeUtc; } catch (ArgumentOutOfRangeException) { warnings.Add("Data archivio non valida: " + path); }
                    records.Add(row with { ArchiveBytes = info.Length, ArchiveModifiedUtc = modified });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (current == root) return new([], [], Error: ex.Message);
                incomplete = true; warnings.Add(current + ": " + ex.Message);
            }
        }
        return new(records.OrderByDescending(r => r.Year).ThenByDescending(r => r.Number).ToList(), warnings, Incomplete: incomplete);
    }
}
