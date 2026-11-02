namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Le osservazioni IA sono separate dai documenti originali e dalle posizioni confermate manualmente.
public sealed record GeologicalEvidence(string Field, int Offset, string Quote);
public sealed record GeologicalAnalysisDraft(
    string ReportId, string SourceFingerprint, string DocumentVersion,
    string Title, string Kind, string Summary, string Author,
    string Municipality, string Address, double? Latitude, double? Longitude,
    string PositionAccuracy, string PositionSource, string DossierId,
    string DossierTitle, string DossierReason, List<GeologicalEvidence> Evidence,
    List<string> Warnings, string ExpectedRevision = "", string ExternalPositionSource = "");
public sealed record GeologicalAnalysisRecord(
    string Revision, DateTime SavedUtc, string SavedBy, string Year, string ProjectCode,
    GeologicalAnalysisDraft Analysis, GeologicalReport? Source = null);
public sealed record GeologicalAnalysisQueueQuery(string Code = "", string Search = "", int Limit = 5);
public sealed record GeologicalAnalysisQueue(DateTime CatalogUtc, int TotalPending, List<GeologicalReport> Reports, List<string> Warnings);
public sealed record GeologicalDossier(string Id, string Title, List<string> ReportIds, List<string> ProjectCodes,
    string Municipality, string Address, double? Latitude, double? Longitude, string PositionAccuracy,
    int StaleDocuments, DateTime UpdatedUtc, List<string> Warnings);
public sealed record GeologicalDossierQuery(string Search = "", int Offset = 0, int Limit = 50, string? Version = null);
public sealed record GeologicalDossierPage(string Version, int Total, int? NextOffset, List<GeologicalDossier> Dossiers);
public sealed record GeologicalAnalysisIndex(int SchemaVersion, List<GeologicalAnalysisRecord> Records);
