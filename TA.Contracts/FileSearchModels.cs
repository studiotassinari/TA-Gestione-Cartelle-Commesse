namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Identifica un file senza aprirne il contenuto. Per gli archivi separa contenitore e percorso interno.
public sealed record FileLocation(string Id, string Name, string Extension, string PhysicalPath,
    string? ArchivePath, string? InternalPath, string ParentRelativePath);
public sealed record FileSearchQuery(string Name = "", string Extension = "", string Category = "",
    string Year = "", string Code = "", int Offset = 0, int Limit = 50, string? Version = null);
public sealed record FileSearchItem(string Code, string Category, string Year, string UnitId,
    string RelativePath, string? ContainerRelativePath, FileLocation Location, long? Bytes,
    DateTime? ModifiedUtc, DateTime CheckedUtc, bool Complete);
public sealed record FileSearchPage(string Version, DateTime LastScanUtc, DateTime ScanStartedUtc,
    int Total, int Offset, int? NextOffset, bool Complete, List<FileSearchItem> Files, List<string> Warnings);

public static class FileSearchValidation
{
    public static void Validate(FileSearchQuery q)
    {
        if (q.Name == null || q.Extension == null || q.Year == null || q.Code == null ||
            q.Name.Length > 200 || q.Extension.Length > 30 || q.Offset < 0 || q.Limit is < 1 or > 100 || q.Offset > int.MaxValue-q.Limit ||
            q.Category is not ("" or "C" or "G" or "A") ||
            (q.Year.Length != 0 && !System.Text.RegularExpressions.Regex.IsMatch(q.Year, @"^(19|20)\d{2}$")) ||
            (q.Code.Length != 0 && !System.Text.RegularExpressions.Regex.IsMatch(q.Code, @"^[CG]\d{0,5}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ||
            q.Extension.TrimStart('.').Any(c => !char.IsLetterOrDigit(c) && c is not ('_' or '-')) ||
            (q.Offset > 0 && string.IsNullOrWhiteSpace(q.Version)))
            throw new ArgumentException("Filtri non validi. Limite 1..100; anno a quattro cifre; estensione singola senza wildcard. Per le pagine successive passare version.");
    }
}
