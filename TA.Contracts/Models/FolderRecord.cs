// Raggruppa questo tipo nel livello Models dellʼapplicazione.
namespace TA_Gestione_Cartelle_Commesse.Models;

// Rappresenta una cartella con codice, anno a due cifre, progressivo, cliente, argomento, percorso ed esito.
public sealed record FolderRecord(string Code, string Year, string Number, string Client, string Subject, string Path, string Status)
{
    // Dati dell'ultima verifica: null indica una cartella non ancora analizzata.
    public FolderInspection? Inspection { get; init; }
    public long? ArchiveBytes { get; init; }
    public DateTime? ArchiveModifiedUtc { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string SizeText => ArchiveBytes is { } size ? FolderInspection.FormatBytes(size) + " (compresso)" : Inspection?.SizeText ?? "In verifica...";
    // Identificatore stabile: il server risolve il percorso esclusivamente dal proprio archivio.
    public string Id => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.ToUpperInvariant())));
}
