using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;

// Prove transitorie in memoria, mai testo integrale nel catalogo.
public sealed class IpripiEvidenceStore
{
    private sealed record Receipt(DateTime Expires, List<DocumentPage> Pages);
    private readonly Dictionary<string, Receipt> receipts = new();
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
            if (receipt.Pages.Sum(p => p.Text.Length) + page.Text.Length > 400000) throw new IOException("Troppe porzioni lette per il documento.");
            receipt.Pages.Add(page);
        }
    }
    public List<string> Validate(IpripiAnalysisDraft draft)
    {
        lock (gate)
        {
            if (!receipts.TryGetValue(draft.DocumentId + ":" + draft.DocumentVersion, out var receipt) || receipt.Expires <= DateTime.UtcNow)
                throw new IOException("Leggere prima il PDF con leggi_mur_ipripi nella versione indicata.");
            foreach (var item in draft.FormEvidence.Concat(draft.Interventions.SelectMany(i => i.Evidence)))
                if (!receipt.Pages.Any(p => item.Offset >= p.Offset && item.Offset + item.Quote.Length <= p.Offset + p.Text.Length &&
                    p.Text.AsSpan(item.Offset - p.Offset, item.Quote.Length).SequenceEqual(item.Quote)))
                    throw new ArgumentException("Citazione non riscontrata nel PDF letto.");
            var pages = receipt.Pages.OrderBy(p => p.Offset).ToList(); int covered = 0;
            foreach (var page in pages) { if (page.Offset > covered) break; covered = Math.Max(covered, page.Offset + page.Text.Length); }
            var warnings = pages.SelectMany(p => p.Warnings).Distinct().ToList();
            bool partial = covered < pages[0].TotalExtractedCharacters || pages.Any(p => p.Truncated);
            if (partial) warnings.Add("Lettura parziale: potrebbero mancare altri interventi.");
            bool extractionIssues = warnings.Any(w => w != "Testo PDF: ordine di lettura, tabelle e disegni possono richiedere verifica visiva. Nessun OCR.");
            if (draft.Outcome == "Nessun IPRiPI dichiarato" && (partial || extractionIssues || pages[0].TotalExtractedCharacters < 50))
                throw new ArgumentException("Lettura incompleta o con avvisi: usare Da verificare, non un esito negativo.");
            warnings.Add("Estrazione testuale senza OCR: caselle, firme e annotazioni grafiche richiedono verifica sul PDF. Classificazione proposta da ChatGPT, non attestazione del progettista.");
            return warnings;
        }
    }
}
