namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Metadati del catalogo: la posizione non viene inventata se manca una fonte verificabile.
public sealed record GeologicalReport
{
    public string Id { get; init; } = "";
    public string InventoryUnitId { get; init; } = "";
    public string Year { get; init; } = "";
    public string SuggestedProjectCode { get; init; } = "";
    public string DetectionStatus { get; init; } = "Catalogata";
    public string DetectionEvidence { get; init; } = "";
    public DateTime? SourceCheckedUtc { get; init; }
    public bool SourceComplete { get; init; } = true;
    // La firma dei metadati permette di conservare l'analisi finché il documento non cambia.
    public bool SourceMetadataComplete { get; init; }
    public bool OriginalAvailable { get; init; } = true;
    public string SourceFingerprint { get; init; } = "";
    public DateTime? SourceModifiedUtc { get; init; }
    public long? SourceBytes { get; init; }
    public string AnalysisRevision { get; init; } = "";
    public DateTime? AnalysisSavedUtc { get; init; }
    public bool AnalysisCurrent { get; init; }
    public string AnalysisStatus { get; init; } = "Da analizzare con ChatGPT";
    public string DossierId { get; init; } = "";
    public string DossierTitle { get; init; } = "";
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
    public string PositionLabel => string.Join(" · ", new[] { Municipality, Address, HasPosition ? PositionAccuracy : "Coordinate da individuare" }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string ServerLocation => PhysicalPath + (string.IsNullOrWhiteSpace(ArchiveEntry) ? "" : " > " + ArchiveEntry);
    public string ProjectLabel => string.IsNullOrWhiteSpace(ProjectCode) ? "Non associata" : ProjectCode;
    public bool CanRead => OriginalAvailable && (!string.IsNullOrWhiteSpace(PhysicalPath) || (FolderId.Length == 64 && FolderId.All(Uri.IsHexDigit) && Category is "C" or "G" or "A"));
}

public sealed record GeologyCatalog(int SchemaVersion, DateTime UpdatedUtc, List<GeologicalReport> Reports, List<string> Warnings, string? Version = null);
public sealed record GeologyQuery(string Search = "", string Code = "", bool OnlyLocated = false, int Offset = 0, int Limit = 100, string? Version = null);
public sealed record GeologyPage(string Version, DateTime UpdatedUtc, int Total, int Offset, int? NextOffset, List<GeologicalReport> Reports, List<string> Warnings);
public sealed record GeologicalReadQuery(string ReportId, int Offset = 0, int Length = 12000, string? Version = null);

public sealed record GeologicalPositionUpdate(string ReportId, double Latitude, double Longitude, string Municipality, string Address, string Source, string Version, string Accuracy = "Posizione indicata manualmente");
public sealed record GeologicalPosition(string ReportId, double Latitude, double Longitude, string Municipality, string Address, string Source, DateTime UpdatedUtc, string UpdatedBy, string Accuracy = "Posizione indicata manualmente");
