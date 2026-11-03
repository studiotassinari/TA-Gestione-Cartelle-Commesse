namespace TA_Gestione_Cartelle_Commesse.Contracts;

public sealed record IpripiQuery(string Code = "", string Search = "", bool AllPdfs = false, int Offset = 0, int Limit = 100, string? Version = null, bool RecognizedOnly = false);
public sealed record IpripiReadQuery(string DocumentId, string Code = "", int Offset = 0, int Length = 12000, string? Version = null);
public sealed record IpripiEvidence(int Offset, string Quote);
public sealed record IpripiDesigner(string Name, string Role, List<IpripiEvidence> Evidence);
public sealed record IpripiLocation(string Address, string Municipality, double? Latitude, double? Longitude,
    string Source, List<IpripiEvidence> Evidence);
public sealed record IpripiIntervention(string Description, string RuleCode, string Level, string Basis,
    string Reason, List<string> MissingData, List<IpripiEvidence> Evidence);
public sealed record IpripiAnalysisDraft(string DocumentId, string Code, string SourceFingerprint, string DocumentVersion,
    string FormType, string Outcome, string Summary, List<IpripiIntervention> Interventions,
    List<IpripiEvidence> FormEvidence, List<string> Warnings, string ExpectedRevision = "", List<IpripiDesigner>? Designers = null, IpripiLocation? Position = null);
public sealed record IpripiAnalysisRecord(string Revision, DateTime SavedUtc, string SavedBy, IpripiDocument Source, IpripiAnalysisDraft Analysis);
public sealed record IpripiDocument(string Id, string Category, string FolderId, string ProjectCode, string Year,
    string FileName, string PhysicalPath, string? ArchiveEntry, string SourceFingerprint, bool SourceComplete,
    IpripiAnalysisRecord? Saved = null, bool OriginalAvailable = true)
{
    public bool AnalysisCurrent => OriginalAvailable && Saved?.Analysis.SourceFingerprint == SourceFingerprint;
    public string Status => Saved == null ? "Candidato da analizzare" : !OriginalAvailable ? "Originale non censito — analisi conservata" :
        !AnalysisCurrent ? "Documento modificato — da rianalizzare" : Saved.Analysis.Outcome + " — proposta da verificare";
    public string Codes => Saved == null ? "" : string.Join(", ", Saved.Analysis.Interventions.Select(i => i.RuleCode).Where(c => c.Length > 0).Distinct());
    public string Designers => string.Join("; ", (Saved?.Analysis.Designers ?? []).Select(d => d.Name + " (" + d.Role + ")"));
    public string Typologies => string.Join("; ", (Saved?.Analysis.Interventions ?? []).Select(i => i.Description).Distinct());
    public string FormType => Saved?.Analysis.FormType ?? "Da riconoscere";
    public string Address => Saved?.Analysis.Position?.Address ?? "";
    public string Municipality => Saved?.Analysis.Position?.Municipality ?? "";
    public double? Latitude => Saved?.Analysis.Position?.Latitude;
    public double? Longitude => Saved?.Analysis.Position?.Longitude;
    public string Location => PhysicalPath + (ArchiveEntry == null ? "" : " > " + ArchiveEntry);
}
public sealed record IpripiPage(string Version, int Total, int Offset, int? NextOffset, List<IpripiDocument> Documents, List<string> Warnings);
public sealed record IpripiRule(string Code, string Text, List<string> Levels, int PdfPage);
public sealed record IpripiRules(string Reference, string SourceUrl, string SourceSha256, string GeneralRequirements, List<IpripiRule> Rules);
public sealed record IpripiAnalysisIndex(int SchemaVersion, List<IpripiAnalysisRecord> Records);
