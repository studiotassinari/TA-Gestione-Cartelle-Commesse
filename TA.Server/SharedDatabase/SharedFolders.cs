using GeCo.Dati;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
namespace TA.Server.SharedDatabase;

// Elenco delle cartelle rilevate scritto nel database comune con GeCo (tabella tblCartelle della libreria GeCo.Dati).
// Solo nomi, percorsi e metadati già presenti negli elenchi: nessuna lettura aggiuntiva sulle cartelle originali.
public static class SharedFolders
{
    // Anno a due cifre nel nome della cartella: 24 diventa 2024.
    public static int? Year(string text) => int.TryParse(text, out var year) && year >= 0 ? year < 100 ? 2000 + year : year : null;
    private static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static CartellaRilevata[] From(ArchiveSnapshot snapshot) => snapshot.Records.Where(r => r.Path.Trim().Length > 0 && r.Code.Trim().Length > 0)
        .Select(r => new CartellaRilevata(r.Path.Trim(), r.Code.Trim(), Year(r.Year), Text(r.Client), Text(r.Subject), Text(r.Status),
            r.ArchiveBytes ?? r.Inspection?.TotalBytes, r.Inspection?.FileCount, (r.ArchiveModifiedUtc ?? r.Inspection?.LastFileWriteUtc)?.ToLocalTime(), Text(r.Inspection?.QuoteStatus)))
        .ToArray();

    // Porta prima il database alla versione prevista dalla libreria (chi parte per primo tra GeCo e questo servizio lo aggiorna),
    // poi riscrive l'elenco di ogni categoria che ha un elenco caricato. Una categoria senza elenco non viene toccata:
    // scriverla vuota farebbe risultare sparite tutte le sue cartelle.
    public static async Task<string> PushAsync(string connectionString, IEnumerable<CategoryState> states, CancellationToken token)
    {
        await AggiornamentoDatabase.EseguiAsync(connectionString, null, token);
        var done = new List<string>();
        foreach (var state in states)
        {
            if (state.Snapshot is not { } snapshot) continue;
            int written = await SincronizzazioneCartelle.AggiornaAsync(connectionString, state.Prefix.ToString(), From(snapshot), "TA-Gestione su " + Environment.MachineName, token);
            done.Add(state.Prefix + ": " + written + " cartelle");
        }
        return done.Count == 0 ? "nessun elenco da inviare" : string.Join(", ", done);
    }
}
