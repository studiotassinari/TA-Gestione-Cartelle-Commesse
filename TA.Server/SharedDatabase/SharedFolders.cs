using GeCo.Dati;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
namespace TA.Server.SharedDatabase;

// Elenco delle cartelle rilevate scritto nel database comune con GeCo (tabella tblCartelle della libreria GeCo.Dati).
// Solo nomi, percorsi e metadati già presenti negli elenchi; dalla 2.40.0 anche i nomi e i metadati delle copie ZIP/7z
// accanto alle cartelle, senza aprirle: nessuna lettura dei contenuti delle cartelle originali.
public static class SharedFolders
{
    // Anno a due cifre nel nome della cartella: 24 diventa 2024.
    public static int? Year(string text) => int.TryParse(text, out var year) && year >= 0 ? year < 100 ? 2000 + year : year : null;
    private static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    // Archiviata (2.40.0, migrazione 14): le righe dell'archivio (A) e le copie ZIP/7z sono riscontri archiviati della commessa;
    // il valore si scrive sempre esplicito, così la libreria non deve dedurlo.
    public static CartellaRilevata[] From(ArchiveSnapshot snapshot) => From(snapshot.Records, snapshot.Prefix);
    public static CartellaRilevata[] From(IEnumerable<FolderRecord> records, char prefix) => records.Where(r => r.Path.Trim().Length > 0 && r.Code.Trim().Length > 0)
        .Select(r => new CartellaRilevata(r.Path.Trim(), r.Code.Trim(), Year(r.Year), Text(r.Client), Text(r.Subject), Text(r.Status),
            r.ArchiveBytes ?? r.Inspection?.TotalBytes, r.Inspection?.FileCount, (r.ArchiveModifiedUtc ?? r.Inspection?.LastFileWriteUtc)?.ToLocalTime(), Text(r.Inspection?.QuoteStatus),
            Archiviata: char.ToUpperInvariant(prefix) == 'A' || TA_Gestione_Cartelle_Commesse.Services.ArchivedCopiesReader.IsArchive(r.Path.Trim())))
        .ToArray();

    // Elenco completo di una categoria per il database comune: le cartelle dell'ultima scansione e, per commesse e gare, le copie
    // ZIP/7z rimaste accanto alle cartelle (2.40.0), lette ora soltanto per nome e metadati. Null se le copie non si leggono per
    // intero: l'invio di quella categoria si rimanda, perché un elenco parziale farebbe risultare sparite le copie non lette.
    public static CartellaRilevata[]? Complete(CategoryState state, CancellationToken token, out string? problem)
    {
        problem = null;
        var snapshot = state.Snapshot!;
        var rows = From(snapshot).ToList();
        var root = string.IsNullOrWhiteSpace(state.Root) ? snapshot.Root : state.Root;
        if (state.Prefix is not ('C' or 'G') || string.IsNullOrWhiteSpace(root)) return rows.ToArray();
        var copies = TA_Gestione_Cartelle_Commesse.Services.ArchivedCopiesReader.Scan(root, state.Prefix, token);
        token.ThrowIfCancellationRequested();
        if (copies.Cancelled) throw new OperationCanceledException(token);
        if (copies.Error != null || copies.Incomplete) { problem = copies.Error ?? string.Join("; ", copies.Warnings); return null; }
        var known = rows.Select(r => r.Percorso).ToHashSet(StringComparer.OrdinalIgnoreCase);
        rows.AddRange(From(copies.Records, state.Prefix).Where(r => known.Add(r.Percorso)));
        return rows.ToArray();
    }

    // Porta prima il database alla versione prevista dalla libreria (chi parte per primo tra GeCo e questo servizio lo aggiorna),
    // poi riscrive l'elenco di ogni categoria che ha un elenco caricato. Una categoria senza elenco non viene toccata:
    // scriverla vuota farebbe risultare sparite tutte le sue cartelle.
    // Dalla migrazione 12 la libreria confronta l'elenco con quello precedente e registra, nella stessa transazione, gli eventi
    // per GeCo2027 (cartelle nuove, modificate, rinominate o spostate, sparite, cartelle collegate da ricollegare): la regola è
    // EventiSincronizzazione.DaSincronizzazione, qui non si decide nulla. Prima della migrazione 12 gli eventi sono zero.
    public static async Task<string> PushAsync(string connectionString, IEnumerable<CategoryState> states, CancellationToken token)
    {
        await AggiornamentoDatabase.EseguiAsync(connectionString, null, token);
        var done = new List<string>();
        foreach (var state in states)
        {
            if (state.Snapshot is not { } snapshot) continue;
            var rows = Complete(state, token, out var problem);
            if (rows == null) { done.Add(state.Prefix + ": copie ZIP non lette per intero, invio rimandato (" + problem + ")"); continue; }
            var result = await SincronizzazioneCartelle.AggiornaConEventiAsync(connectionString, state.Prefix.ToString(), rows, "TA-Gestione su " + Environment.MachineName, token);
            int archived = rows.Count(r => r.Archiviata == true && state.Prefix != 'A');
            done.Add(state.Prefix + ": " + result.Scritte + " cartelle" + (archived > 0 ? $" (di cui {archived} copie ZIP)" : "") + (result.Eventi > 0 ? $" ({result.Eventi} eventi per GeCo2027)" : "")
                + (result.Collegate > 0 ? $", {result.Collegate} cartelle chieste collegate" : ""));
        }
        return done.Count == 0 ? "nessun elenco da inviare" : string.Join(", ", done);
    }
}
