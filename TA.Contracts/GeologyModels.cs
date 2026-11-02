namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Metadati del catalogo: la posizione non viene inventata se manca una fonte verificabile.
public sealed record GeologicalReport
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Kind { get; init; } = "Geologica";
    public string FileName { get; init; } = "";
    public string PhysicalPath { get; init; } = "";
    public string Municipality { get; init; } = "";
    public string Address { get; init; } = "";
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string PositionSource { get; init; } = "";
    public string PositionAccuracy { get; init; } = "";
    public string ProjectCode { get; init; } = "";
    public string Client { get; init; } = "";
    public string Author { get; init; } = "";
    public string Notes { get; init; } = "";
    public DateTime? DocumentDate { get; init; }
    public string Category { get; init; } = "C";
    public string FolderId { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string? ArchiveEntry { get; init; }
    public bool HasPosition => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
    public string PositionLabel => HasPosition ? string.Join(" · ", new[] { Municipality, Address, PositionAccuracy }.Where(s => !string.IsNullOrWhiteSpace(s))) : "Posizione da individuare";
    public string ProjectLabel => string.IsNullOrWhiteSpace(ProjectCode) ? "Non associata" : ProjectCode;
    public bool CanRead => !string.IsNullOrWhiteSpace(PhysicalPath) || (FolderId.Length == 64 && FolderId.All(Uri.IsHexDigit) && Category is "C" or "G" or "A");
}

public sealed record GeologyCatalog(int SchemaVersion, DateTime UpdatedUtc, List<GeologicalReport> Reports, List<string> Warnings);
public sealed record GeologyQuery(string Search = "", string Code = "", bool OnlyLocated = false, int Offset = 0, int Limit = 100, string? Version = null);
public sealed record GeologyPage(string Version, DateTime UpdatedUtc, int Total, int Offset, int? NextOffset, List<GeologicalReport> Reports, List<string> Warnings);
public sealed record GeologicalReadQuery(string ReportId, int Offset = 0, int Length = 12000, string? Version = null);
