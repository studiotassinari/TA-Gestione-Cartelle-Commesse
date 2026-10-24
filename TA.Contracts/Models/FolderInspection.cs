// Definisce i dati persistenti della verifica, senza contenuto dei documenti.
namespace TA_Gestione_Cartelle_Commesse.Models;

public sealed record FolderInspection
{
    // Somma delle dimensioni logiche dei file letti, in byte (non spazio occupato sul disco).
    public long TotalBytes { get; init; }
    // Numero dei file inclusi nel calcolo.
    public long FileCount { get; init; }
    // Massima data di modifica dei file, salvata in UTC; null per una cartella senza file leggibili.
    public DateTime? LastFileWriteUtc { get; init; }
    // Momento in cui la verifica della cartella è stata completata.
    public DateTime CheckedUtc { get; init; }
    // Distingue un risultato completo da una lettura parziale.
    public bool Complete { get; init; }
    // Esito basato solo sui nomi; non certifica il contenuto o l'approvazione.
    public string QuoteStatus { get; init; } = "Non verificato";
    // Percorsi dei documenti candidati individuati nell'amministrazione.
    public List<string> QuoteFiles { get; init; } = [];
    // Dettagli degli accessi falliti e dei collegamenti esclusi.
    public List<string> Issues { get; init; } = [];
    // Presenta la dimensione indicando espressamente eventuali dati parziali.
    [System.Text.Json.Serialization.JsonIgnore]
    public string SizeText => $"{TotalBytes / 1048576d:N2} MiB" + (Complete ? "" : " (parziale)");
    // Converte l'istante UTC nell'orario locale solo per la visualizzazione.
    [System.Text.Json.Serialization.JsonIgnore]
    public string LastWriteText => LastFileWriteUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "Nessun file leggibile";
    // Rende consultabili percorsi ed errori nel suggerimento della riga.
    [System.Text.Json.Serialization.JsonIgnore]
    public string Details => string.Join(Environment.NewLine, QuoteFiles.Concat(Issues));
}
