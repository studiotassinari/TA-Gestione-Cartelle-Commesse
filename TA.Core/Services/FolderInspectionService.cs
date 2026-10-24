using System.IO;
using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Models;

// Legge esclusivamente metadati e nomi: nessun documento è aperto o inviato a servizi esterni.
namespace TA_Gestione_Cartelle_Commesse.Services;

public static class FolderInspectionService
{
    // Include le varianti indicate dall'utente e la grafia ordinaria.
    private static readonly HashSet<string> AdministrationNames = new(StringComparer.OrdinalIgnoreCase)
        { "amministraione", "amministrazione", "99_AMMINISTRAZIONE" };
    // Limita i candidati a documenti e messaggi, escludendo file temporanei ed eseguibili.
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".odt", ".xls", ".xlsx", ".ods", ".rtf", ".txt", ".msg", ".eml", ".p7m" };

    // Arricchisce solo le cartelle già riconosciute: non aggiunge commesse dai livelli inferiori.
    public static ScanResult Enrich(ScanResult scan, CancellationToken token, IProgress<string>? progress = null)
    {
        if (scan.Error != null || scan.Cancelled) return scan;
        var records = new List<FolderRecord>();
        foreach (var record in scan.Records)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"Verifica {records.Count + 1}/{scan.Records.Count}: {record.Code} {record.Client}");
            records.Add(record with { Inspection = Inspect(record.Path, token) });
        }
        return scan with { Records = records };
    }

    // Visita l'albero una volta per calcolare dimensioni, ultima modifica e candidati preventivi.
    public static FolderInspection Inspect(string root, CancellationToken token)
        => Inspect(root, token, file => file.LastWriteTimeUtc);

    // Lettore separato per riprodurre nei test date Win32 non rappresentabili in .NET.
    internal static FolderInspection Inspect(string root, CancellationToken token, Func<FileInfo, DateTime> readModified)
    {
        long bytes = 0, count = 0;
        DateTime? latest = null;
        var issues = new List<string>();
        var candidates = new List<string>();
        bool administrationFound = false, strongCandidate = false;
        var pending = new Stack<(string Path, bool Administration)>();
        pending.Push((root, false));
        while (pending.TryPop(out var item))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                // Non segue collegamenti, evitando cicli e letture fuori dalla cartella di lavoro.
                if ((File.GetAttributes(item.Path) & FileAttributes.ReparsePoint) != 0)
                {
                    issues.Add("Collegamento escluso: " + item.Path);
                    continue;
                }
                foreach (var entry in Directory.EnumerateFileSystemEntries(item.Path))
                {
                    token.ThrowIfCancellationRequested();
                    if (FolderReader.IsExcluded(entry)) continue;
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            issues.Add("Collegamento escluso: " + entry);
                            continue;
                        }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            bool isAdmin = AdministrationNames.Contains(Path.GetFileName(entry));
                            administrationFound |= isAdmin;
                            pending.Push((entry, item.Administration || isAdmin));
                            continue;
                        }
                        var file = new FileInfo(entry);
                        var length = file.Length;

                        bytes = checked(bytes + length);
                        count++;
                        try
                        {
                            var modified = readModified(file);
                            if (latest == null || modified > latest) latest = modified;
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            // Conserva dimensione e nome del file; la data sconosciuta rende i metadati parziali.
                            issues.Add("Data ultima modifica non valida: " + entry);
                        }
                        // Un preventivo fuori dall'amministrazione non soddisfa il controllo richiesto.
                        if (!item.Administration || !DocumentExtensions.Contains(file.Extension) || file.Name.StartsWith("~$")) continue;
                        var name = Path.GetFileNameWithoutExtension(file.Name);
                        bool quote = Regex.IsMatch(name, @"(?i)(^|[\W_])(preventiv\w*|offert\w*)(?=$|[\W_])");
                        if (!quote) continue;
                        candidates.Add(entry);
                        // Richieste, facsimili e modelli restano ambigui anche quando contengono la parola preventivo.
                        bool ambiguous = Regex.IsMatch(name, @"(?i)(richiest|modell|fac[ _-]?simile|bozza)");
                        strongCandidate |= !ambiguous;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        issues.Add(entry + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(item.Path + ": " + ex.Message);
            }
        }
        // Un accesso incompleto non può dimostrare l'assenza del documento.
        var status = issues.Count > 0 ? "Da verificare (lettura parziale)"
            : strongCandidate ? "Presente (da nome file)"
            : candidates.Count > 0 ? "Da verificare (nome ambiguo)"
            : administrationFound ? "Non trovato nei nomi" : "Cartella amministrazione non trovata";
        return new FolderInspection
        {
            TotalBytes = bytes, FileCount = count, LastFileWriteUtc = latest, CheckedUtc = DateTime.UtcNow,
            Complete = issues.Count == 0, QuoteStatus = status, QuoteFiles = candidates.OrderBy(p => p).ToList(), Issues = issues
        };
    }
}
