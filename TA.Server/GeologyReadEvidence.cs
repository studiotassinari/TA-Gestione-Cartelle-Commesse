using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Server;

// Le porzioni lette restano in memoria per 20 minuti, solo per verificare le citazioni dell'analisi corrente.
// Nessun testo integrale viene scritto nell'archivio JSON o nei log.
public sealed class GeologyReadEvidence
{
    private sealed record Receipt(DateTime Expires, List<DocumentPage> Pages);
    private readonly Dictionary<string, Receipt> receipts = new(StringComparer.Ordinal);
    private readonly object gate = new();
    public void Remember(string id, DocumentPage page)
    {
        lock (gate)
        {
            foreach (var key in receipts.Where(p => p.Value.Expires <= DateTime.UtcNow).Select(p => p.Key).ToArray()) receipts.Remove(key);
            var keyId = id + ":" + page.Version;
            if (!receipts.TryGetValue(keyId, out var receipt))
            {
                if (receipts.Count >= 32) receipts.Remove(receipts.MinBy(p => p.Value.Expires).Key);
                receipts[keyId] = receipt = new(DateTime.UtcNow.AddMinutes(20), []);
            }
            if (receipt.Pages.Any(p => p.Offset == page.Offset && p.Text.Length >= page.Text.Length)) return;
            receipt.Pages.RemoveAll(p => p.Offset == page.Offset);
            if (receipt.Pages.Sum(p => p.Text.Length) + page.Text.Length > 400000) throw new IOException("Limite delle porzioni di prova raggiunto per il documento.");
            receipt.Pages.Add(page);
        }
    }
    public List<string> Validate(string id, GeologicalAnalysisDraft draft)
    {
        lock (gate)
        {
            if (!receipts.TryGetValue(id + ":" + draft.DocumentVersion, out var receipt) || receipt.Expires <= DateTime.UtcNow)
                throw new IOException("Leggere prima la relazione tramite leggi_relazione_geologica: manca una lettura recente della versione indicata.");
            foreach (var evidence in draft.Evidence)
            {
                if (!receipt.Pages.Any(p => evidence.Offset >= p.Offset && evidence.Offset + evidence.Quote.Length <= p.Offset + p.Text.Length &&
                    p.Text.AsSpan(evidence.Offset - p.Offset, evidence.Quote.Length).SequenceEqual(evidence.Quote)))
                    throw new ArgumentException("Citazione non riscontrata nelle porzioni lette: " + evidence.Field);
            }
            var pages = receipt.Pages.OrderBy(p => p.Offset).ToArray(); int covered = 0;
            foreach (var page in pages) { if (page.Offset > covered) break; covered = Math.Max(covered, page.Offset + page.Text.Length); }
            var warnings = pages.SelectMany(p => p.Warnings).Distinct().Take(20).ToList();
            if (covered < pages[0].TotalExtractedCharacters || pages.Any(p => p.Truncated)) warnings.Add("Analisi di porzioni del testo: non equivale alla lettura completa del documento.");
            return warnings;
        }
    }
}
