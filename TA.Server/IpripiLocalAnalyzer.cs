using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;

// Citazioni esatte dal testo letto: riga che contiene il riscontro, con offset assoluto.
public static class LocalQuote
{
    public static (int Offset, string Quote) Line(string text, int index, int length, int minimum, int before = 120, int after = 300)
    {
        length = Math.Min(length, 150);
        int start = index, end = index + length;
        while (start > 0 && index - start < before && text[start - 1] is not ('\n' or '\r')) start--;
        while (end < text.Length && end - (index + length) < after && text[end] is not ('\n' or '\r')) end++;
        // Riga più corta del minimo richiesto: estende oltre il fine riga, senza uscire dal testo.
        while (end - start < minimum && end < text.Length) end++;
        while (end - start < minimum && start > 0) start--;
        return (start, text[start..end]);
    }
    public static int FirstLetter(string text)
    {
        // I marcatori di pagina aggiunti dall'estrazione non sono contenuto del documento.
        var marker = Regex.Match(text, @"\A(?:\s|\[Pagina \d+\])*");
        for (int i = marker.Length; i < text.Length; i++) if (char.IsLetterOrDigit(text[i])) return i;
        return -1;
    }
    public static int Content(string text) => Regex.Replace(text, @"\[Pagina \d+\]", "").Count(char.IsLetterOrDigit);
}

// Riconoscimento prudente da regole testuali: propone solo ciò che il modulo cita esplicitamente.
// Nessun esito negativo viene dedotto; ogni dubbio resta «Da verificare».
public static class IpripiLocalAnalyzer
{
    public const string Notice = "Analisi automatica locale da regole testuali, senza ChatGPT: caselle, firme e annotazioni grafiche non sono valutate. Proposta da verificare sul documento, non attestazione del progettista.";
    private static readonly Regex Form = new(@"\bA\s*\.?\s*1\s*/\s*D\s*\.?\s*1\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Subject = new(@"privi\s+di\s+rilevanza|\bIPRiPI\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Mur = new(@"\bMUR\b", RegexOptions.CultureInvariant);
    private static readonly Regex Level = new(@"\bL\s?([012])\b", RegexOptions.CultureInvariant);
    // Il punto tra le parti del codice è obbligatorio: sigle come «A13» non sono codici dell'Allegato 1.
    private static readonly Lazy<(IpripiRule Rule, Regex Pattern)[]> Codes = new(() => IpripiRegulation.Get().Rules
        .Select(rule => (Rule: rule, Pattern: new Regex(@"(?<![\p{L}\d.])" + string.Join(@"\s*\.\s*", rule.Code.Split('.').Select(Regex.Escape)) + @"(?![\p{L}\d])", RegexOptions.CultureInvariant)))
        .ToArray());

    public static IpripiAnalysisDraft Analyze(IpripiDocument source, string version, string text, bool partial, IEnumerable<string> readWarnings, string expectedRevision)
    {
        var warnings = readWarnings.Where(w => w.Length <= 500).Distinct().Take(10).Append(Notice).ToList();
        if (partial) warnings.Add("Lettura limitata alla parte iniziale del testo: potrebbero mancare altri interventi.");
        IpripiAnalysisDraft Draft(string formType, string outcome, string summary, List<IpripiIntervention> interventions, List<IpripiEvidence> evidence) =>
            new IpripiAnalysisDraft(source.Id, "", source.SourceFingerprint, version, formType, outcome, summary, interventions, evidence, warnings, expectedRevision);
        var none = new List<IpripiIntervention>();

        if (LocalQuote.Content(text) < 40)
            return Draft("Non leggibile", "Non leggibile", "Testo non estraibile: probabile documento acquisito come immagine. Serve OCR o verifica visiva.", none, new List<IpripiEvidence>());

        var anchor = Form.Match(text);
        if (!anchor.Success && Mur.IsMatch(text)) anchor = Subject.Match(text);
        if (!anchor.Success)
        {
            var first = LocalQuote.Line(text, LocalQuote.FirstLetter(text), 1, 3);
            return Draft("Altro", "Da verificare", "Il nome del file indica un modulo MUR A.1/D.1 ma il testo estratto non lo conferma: verificare il documento.", none, new List<IpripiEvidence> { new IpripiEvidence(first.Offset, first.Quote) });
        }
        var header = LocalQuote.Line(text, anchor.Index, anchor.Length, 3);
        var formEvidence = new List<IpripiEvidence> { new IpripiEvidence(header.Offset, header.Quote) };

        var found = Codes.Value.Select(code => (code.Rule, Match: code.Pattern.Match(text))).Where(item => item.Match.Success).OrderBy(item => item.Match.Index).ToList();
        if (found.Count == 0)
            return Draft("MUR A.1/D.1", "Da verificare", "Modulo riconosciuto, ma nel testo estratto non compare alcun codice dell'Allegato 1 della DGR 2272/2016: verificare il documento.", none, formEvidence);
        // Molti codici diversi indicano l'elenco prestampato delle voci, non la scelta del progettista.
        if (found.Count > 4)
            return Draft("MUR A.1/D.1", "Da verificare", $"Modulo riconosciuto con {found.Count} codici diversi dell'Allegato 1: probabile elenco prestampato; la voce dichiarata va verificata sul documento.", none, formEvidence);

        var items = new List<IpripiIntervention>();
        foreach (var (rule, match) in found)
        {
            var level = rule.Levels.Count == 1 ? rule.Levels[0] : "";
            if (level.Length == 0)
            {
                var near = Level.Match(text, match.Index + match.Length, Math.Min(80, text.Length - match.Index - match.Length));
                if (near.Success && rule.Levels.Contains("L" + near.Groups[1].Value)) level = "L" + near.Groups[1].Value;
            }
            var quote = LocalQuote.Line(text, match.Index, match.Length, 3);
            var missing = new List<string>();
            if (level.Length == 0) missing.Add("Livello L0/L1/L2 non leggibile accanto al codice");
            items.Add(new IpripiIntervention(rule.Text.Length > 1000 ? rule.Text[..1000] : rule.Text, rule.Code, level, "Dichiarato",
                "Codice citato nel testo del modulo; descrizione dall'Allegato 1 della DGR 2272/2016. Riconoscimento automatico locale.",
                missing, new List<IpripiEvidence> { new IpripiEvidence(quote.Offset, quote.Quote) }));
        }
        bool complete = items.All(i => i.Level.Length > 0);
        return Draft("MUR A.1/D.1", complete ? "IPRiPI individuati" : "Da verificare",
            "Codici citati nel modulo: " + string.Join(", ", items.Select(i => i.RuleCode)) + (complete ? "." : ". Livello non leggibile per almeno un codice."), items, formEvidence);
    }
    // Ogni citazione deve coincidere con il testo letto, come per le analisi salvate su richiesta.
    public static bool QuotesMatch(IpripiAnalysisDraft draft, string text) =>
        draft.FormEvidence.Concat(draft.Interventions.SelectMany(i => i.Evidence)).All(e =>
            e.Offset >= 0 && e.Offset + e.Quote.Length <= text.Length && text.AsSpan(e.Offset, e.Quote.Length).SequenceEqual(e.Quote));
}
