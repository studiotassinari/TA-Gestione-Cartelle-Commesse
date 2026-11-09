namespace TA_Gestione_Cartelle_Commesse.Contracts;
public sealed record CatalogAnalysisStatus(string Catalog,ScanProgress? Indexing,DateTime? CatalogUpdatedUtc,
    int? Candidates,int? SavedAnalyses,int? CurrentAnalyses,int? PendingAnalyses,int? OutdatedAnalyses,
    DateTime? LastAnalysisSavedUtc,string? NextProjectCode,string[] Warnings);
public sealed record DocumentAnalysisChannel(string Category,bool IsReading,string LastDocumentId,DateTime? NextNewDocumentUtc);
public sealed record AnalysisStatus(DateTime ObservedUtc,ScanProgress FileInventory,CatalogAnalysisStatus Geology,
    CatalogAnalysisStatus MurIpripi,DocumentAnalysisChannel[]? Channels=null,string AnalysisMode="Su richiesta ChatGPT; nessuna analisi cloud automatica",
    string Order="Anno decrescente, numero commessa decrescente; un documento alla volta per ciascun canale geologia/MUR, almeno 60 secondi tra nuovi documenti dello stesso canale");
