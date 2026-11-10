namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Il client indica solo ID censiti, mai percorsi da eliminare.
public sealed record FolderArchiveQuery(string FolderId = "", string JobId = "", string Confirmation = "");
public sealed record FolderArchiveStatus(string JobId, string Category, string Source, string Zip,
    string State, string Message, int Files, long Bytes, DateTime StartedUtc, DateTime? FinishedUtc,
    string? Confirmation, DateTime? ConfirmationExpiresUtc);
