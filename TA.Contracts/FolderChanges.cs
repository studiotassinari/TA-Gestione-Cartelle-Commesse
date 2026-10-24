using TA_Gestione_Cartelle_Commesse.Models;
namespace TA_Gestione_Cartelle_Commesse.Contracts;
// Confronta dati effettivi; l'ora di scansione non costituisce una modifica.
public static class FolderChanges
{
    public static List<string> Compare(IEnumerable<FolderRecord> before, IEnumerable<FolderRecord> after)
    {
        var old = before.GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var current = after.GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var item in current.Values.OrderBy(r => r.Path))
        {
            if (!old.TryGetValue(item.Path, out var prior)) { result.Add("Aggiunta: " + item.Path); continue; }
            var fields = new List<string>(); if (prior.ArchiveBytes != item.ArchiveBytes || prior.ArchiveModifiedUtc != item.ArchiveModifiedUtc) fields.Add("dimensione/data archivio compresso");
            if (prior.Code != item.Code || prior.Client != item.Client || prior.Subject != item.Subject) fields.Add("codice/cliente/argomento");
            if (prior.Status != item.Status) fields.Add($"conformita: {prior.Status} → {item.Status}");
            var a = prior.Inspection; var b = item.Inspection;
            if (a != null && b != null)
            {
                if (a.FileCount != b.FileCount) fields.Add($"file: {a.FileCount} → {b.FileCount}");
                if (a.TotalBytes != b.TotalBytes) fields.Add($"dimensione: {a.TotalBytes} → {b.TotalBytes} byte");
                if (a.LastFileWriteUtc != b.LastFileWriteUtc) fields.Add("ultima modifica dei file");
                if (a.QuoteStatus != b.QuoteStatus || !Same(a.QuoteFiles, b.QuoteFiles)) fields.Add("documentazione preventivo");
                if (a.Complete != b.Complete || !Same(a.Issues, b.Issues)) fields.Add("esito lettura / anomalie");
                // Una firma assente appartiene a un archivio precedente: non inventa modifiche alla prima migrazione.
                if (a.ContentSignature != null && b.ContentSignature != null && a.ContentSignature != b.ContentSignature)
                    fields.Add("struttura cartelle o metadati dei file");
            }
            else if (a != null || b != null) fields.Add("metadati disponibili");
            if (fields.Count > 0) result.Add("Modificata: " + item.Path + " — " + string.Join("; ", fields));
        }
        foreach (var item in old.Values.Where(r => !current.ContainsKey(r.Path)).OrderBy(r => r.Path)) result.Add("Rimossa dall'elenco: " + item.Path);
        return result;
    }
    public static string Summary(IReadOnlyList<string> changes) => changes.Count == 0 ? "Nessuna modifica" : changes[0] + (changes.Count > 1 ? $" (+{changes.Count - 1} altre modifiche)" : "");
    private static bool Same(IEnumerable<string> a, IEnumerable<string> b) => a.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(b.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
}
