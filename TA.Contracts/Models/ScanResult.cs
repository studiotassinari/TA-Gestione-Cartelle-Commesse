// Raggruppa questo tipo nel livello Models dellʼapplicazione.
namespace TA_Gestione_Cartelle_Commesse.Models;

// Raccoglie risultati, segnalazioni e gli eventuali stati di annullamento o errore principale.
public sealed record ScanResult(List<FolderRecord> Records, List<string> Warnings, bool Cancelled = false, string? Error = null, bool Incomplete = false);
