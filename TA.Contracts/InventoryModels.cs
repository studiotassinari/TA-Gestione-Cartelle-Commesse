namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Solo nomi e metadati: nessun contenuto dei documenti.
public sealed record InventoryEntry(string RelativePath, string Kind, string? Container = null, long? Bytes = null, DateTime? ModifiedUtc = null, FileLocation? Location = null);
public sealed record InventoryUnit(string Id, string Category, string Year, string Code, string Client, string Subject, string Path, string Classification);
public sealed record ProjectCandidate(string Path, string Client, string Subject, string? SuggestedCode, string Evidence, string Confidence);
public sealed record InventoryResult(List<InventoryEntry> Entries, List<string> Issues, bool Complete);
public sealed record InventoryManifest(InventoryUnit Unit, string Generation, DateTime CheckedUtc, bool Complete, int EntryCount, int PageCount, List<string> Issues, int Added, int Removed, int Changed, bool Baseline);
public sealed record InventoryRun(DateTime StartedUtc, DateTime CompletedUtc, List<InventoryManifest> Inventories, List<ProjectCandidate> Candidates, List<string> Issues);
public sealed record AnalysisQuery(string Kind = "catalog", string? Id = null, string Text = "", int Offset = 0, int Limit = 50);
public sealed record AnalysisPage(DateTime? CheckedUtc, int Total, int? NextOffset, object Items, string Note);
