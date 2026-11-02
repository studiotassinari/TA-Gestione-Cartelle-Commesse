using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;

namespace TA.Server;

// Isola gli errori della singola voce; la categoria prosegue con le commesse/archivi successivi.
public static class RecordInspection
{
    public static FolderRecord Read(FolderRecord record, char prefix, CancellationToken token,
        Func<string, CancellationToken, FolderInspection> inspect, ILogger logger)
    {
        FolderInspection inspection;
        try { inspection = inspect(record.Path, token); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException or InvalidDataException)
        {
            logger.LogWarning(ex, "Metadati parziali {Category}: {Path}; la scansione prosegue", prefix, record.Path);
            var previous = record.Inspection ?? new FolderInspection();
            inspection = previous with { Complete = false, CheckedUtc = DateTime.UtcNow,
                QuoteStatus = "Da verificare (lettura parziale)",
                Issues = previous.Issues.Append(record.Path + ": lettura non completata; eventuali metadati precedenti conservati. " + ex.Message).Distinct().ToList() };
        }
        if (prefix != 'A') return record with { Inspection = inspection };
        long? size = record.ArchiveBytes;
        DateTime? modified = record.ArchiveModifiedUtc;
        var issues = inspection.Issues.ToList();
        try
        {
            var file = new FileInfo(record.Path);
            size = file.Length;
            modified = MetadataDates.Read(() => file.LastWriteTimeUtc, record.Path, issues);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { issues.Add("Metadati del contenitore non leggibili, eventuali valori precedenti conservati: " + record.Path + ": " + ex.Message); }
        if (issues.Count > inspection.Issues.Count)
            inspection = inspection with { Complete = false, QuoteStatus = "Da verificare (lettura archivio parziale)", Issues = issues };
        return record with { Inspection = inspection, ArchiveBytes = size, ArchiveModifiedUtc = modified };
    }
}
