// Raccoglie una versione completa dell'archivio di una categoria e radice.
namespace TA_Gestione_Cartelle_Commesse.Models;

public sealed record ArchiveSnapshot
{
    // Consente di rifiutare formati futuri invece di interpretarli in modo errato.
    public int SchemaVersion { get; init; } = 1;
    // Radice normalizzata da cui provengono i dati.
    public string Root { get; init; } = "";
    // Categoria C o G, distinta anche nel nome del file JSON.
    public char Prefix { get; init; }
    // Inizio della scansione: impedisce a una scansione vecchia di sovrascriverne una più recente.
    public DateTime StartedUtc { get; init; }
    // Istante di completamento della verifica.
    public DateTime SavedUtc { get; init; }
    // Cartelle principali, arricchite con dimensioni, date ed esito preventivi.
    public List<FolderRecord> Records { get; init; } = [];
    // Segnalazioni della scansione principale, indipendenti dal controllo dei documenti.
    public List<string> Warnings { get; init; } = [];
}
