using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;

// Classificazione prudente dalle prime pagine: conferma o smentisce il candidato individuato dal nome.
// Autore, coordinate e raggruppamento in fascicoli non vengono dedotti.
public static class GeologyLocalAnalyzer
{
    public const string Notice = "Classificazione automatica locale dalle prime pagine, senza ChatGPT: proposta da verificare sul documento.";
    private static readonly Regex Geology = new(@"\bgeologi\w*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Geotechnics = new(@"\bgeotecnic\w*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Municipality = new(@"\bComune\s+di\s+(?<name>[A-ZÀ-Ý][\p{L}'’]+(?:[ \-][A-ZÀ-Ý][\p{L}'’]+){0,3})", RegexOptions.CultureInvariant);

    // null = testo insufficiente (per esempio scansione senza OCR): nessuna analisi viene salvata.
    public static GeologicalAnalysisDraft? Analyze(GeologicalReport report, string version, string text, IEnumerable<string> readWarnings, GeologicalAnalysisRecord? previous)
    {
        if (LocalQuote.Content(text) < 200) return null;
        var geology = Geology.Matches(text); var geotechnics = Geotechnics.Matches(text);
        bool pertinent = geology.Count + geotechnics.Count >= 3;
        var kind = !pertinent ? "Non pertinente" : geology.Count > 0 && geotechnics.Count > 0 ? "Geologica e geotecnica" : geology.Count > 0 ? "Geologica" : "Geotecnica";
        var first = pertinent ? (geology.Count > 0 ? geology[0] : geotechnics[0]).Index : LocalQuote.FirstLetter(text);
        var basis = LocalQuote.Line(text, first, 1, 8);
        if (basis.Quote.Length < 8) return null;
        var evidence = new List<GeologicalEvidence> { new GeologicalEvidence("classificazione", basis.Offset, basis.Quote), new GeologicalEvidence("fascicolo", basis.Offset, basis.Quote) };
        var warnings = readWarnings.Where(w => w.Length <= 500).Distinct().Take(10).Append(Notice).ToList();
        string municipality = "";
        if (pertinent && Municipality.Match(text) is { Success: true } place)
        {
            var quote = LocalQuote.Line(text, place.Index, place.Length, 8);
            municipality = place.Groups["name"].Value.Trim(); evidence.Add(new GeologicalEvidence("localita", quote.Offset, quote.Quote));
            warnings.Add("Comune ricavato dalla prima citazione nel testo: potrebbe non essere il sito dell'intervento.");
        }
        var title = string.IsNullOrWhiteSpace(report.Title) ? report.FileName : report.Title;
        var dossier = (string.IsNullOrWhiteSpace(report.ProjectCode) ? "" : report.ProjectCode + " — ") + title;
        return new GeologicalAnalysisDraft(report.Id, report.SourceFingerprint, version, title, kind,
            pertinent ? $"Nelle prime pagine: {geology.Count} riferimenti geologici e {geotechnics.Count} geotecnici." : "Nelle prime pagine non risultano riferimenti geologici o geotecnici sufficienti: tipologia da verificare.",
            "", municipality, "", null, null, "", "",
            // Una nuova analisi dello stesso documento resta nel fascicolo già assegnato.
            previous?.Analysis.DossierId ?? "", dossier.Length > 300 ? dossier[..300] : dossier,
            "Fascicolo proposto automaticamente per il singolo documento; raggruppamento da verificare.", evidence, warnings, previous?.Revision ?? "");
    }
    public static bool QuotesMatch(GeologicalAnalysisDraft draft, string text) => draft.Evidence.All(e =>
        e.Offset >= 0 && e.Offset + e.Quote.Length <= text.Length && text.AsSpan(e.Offset, e.Quote.Length).SequenceEqual(e.Quote));
}
