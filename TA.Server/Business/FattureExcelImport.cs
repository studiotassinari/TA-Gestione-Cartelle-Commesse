using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.Business;

// Legge esclusivamente i valori dell'esportazione; non apre Excel e non esegue formule o collegamenti.
public static class FattureExcelImport
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public sealed record Parsed(CustomerCandidate[] Customers, int Read, string[] Issues);
    public static CustomerImportResult Import(BusinessService service, byte[] bytes, string actor, bool preview)
    {
        Parsed parsed;
        try { parsed = Parse(bytes); }
        catch (InvalidDataException ex) { throw new ArgumentException(ex.Message); }
        var result = CustomerImport.Apply(service, parsed.Customers, actor, preview);
        return result with { Read = parsed.Read, Skipped = result.Skipped + parsed.Read - parsed.Customers.Length, Issues = parsed.Issues.Concat(result.Issues).ToArray() };
    }
    public static Parsed Parse(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumBytes) throw new ArgumentException("Excel vuoto o oltre 8 MiB.");
        using var input = new MemoryStream(bytes, false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > 256 || zip.Entries.Any(e => e.Length > 32L * 1024 * 1024) || zip.Entries.Sum(e => e.Length) > 32L * 1024 * 1024) throw new InvalidDataException("Esportazione Excel troppo grande.");
        if (zip.Entries.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidDataException("Excel con parti duplicate.");
        XDocument Xml(string path)
        {
            var entry = zip.GetEntry(path) ?? throw new InvalidDataException("Struttura Excel incompleta.");
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 24 * 1024 * 1024 });
            try { return XDocument.Load(reader); } catch (XmlException) { throw new InvalidDataException("XML Excel non valido."); }
        }
        var workbook = Xml("xl/workbook.xml");
        var exports = workbook.Descendants(Sheet + "sheet").Where(s => (string?)s.Attribute("name") == "Export").ToArray();
        if (exports.Length != 1) throw new InvalidDataException("Manca il foglio Export di Fatture in Cloud.");
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var id = (string?)exports[0].Attribute(rel + "id");
        var links = Xml("xl/_rels/workbook.xml.rels").Root!.Elements().Where(e => (string?)e.Attribute("Id") == id).ToArray();
        if (links.Length != 1 || (string?)links[0].Attribute("TargetMode") == "External") throw new InvalidDataException("Riferimento al foglio non valido.");
        var target = (string?)links[0].Attribute("Target") ?? "";
        var part = target.StartsWith("/xl/", StringComparison.Ordinal) ? target[1..] : "xl/" + target;
        if (!System.Text.RegularExpressions.Regex.IsMatch(part, @"^xl/worksheets/[A-Za-z0-9_.-]+\.xml$")) throw new InvalidDataException("Percorso foglio non ammesso.");
        string Text(XElement element) => string.Concat(element.Elements().Select(e => e.Name == Sheet + "t" ? e.Value : e.Name == Sheet + "r" ? e.Element(Sheet + "t")?.Value ?? "" : ""));
        var shared = zip.GetEntry("xl/sharedStrings.xml") == null ? [] : Xml("xl/sharedStrings.xml").Root!.Elements(Sheet + "si").Select(Text).ToArray();
        string Value(XElement cell)
        {
            if (cell.Element(Sheet + "f") != null) throw new InvalidDataException("L'esportazione contiene formule: esportare nuovamente i valori dal portale.");
            var kind = (string?)cell.Attribute("t"); var v = cell.Element(Sheet + "v")?.Value ?? "";
            if (kind == "inlineStr") return cell.Element(Sheet + "is") is { } inline ? Text(inline).Trim() : "";
            if (kind == "s") return int.TryParse(v, out var index) && index >= 0 && index < shared.Length ? shared[index].Trim() : throw new InvalidDataException("Indice di testo Excel non valido.");
            if (kind == "e") throw new InvalidDataException("L'esportazione contiene celle con errore.");
            return v.Trim();
        }
        var rows = Xml(part).Descendants(Sheet + "sheetData").Elements(Sheet + "row").ToArray();
        if (rows.Length is < 1 or > 5001) throw new InvalidDataException("Excel vuoto o oltre 5000 clienti.");
        Dictionary<string, XElement> Cells(XElement row)
        {
            var result = new Dictionary<string, XElement>();
            foreach (var c in row.Elements(Sheet + "c"))
            {
                var reference = (string?)c.Attribute("r") ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(reference, "^[A-Z]{1,3}[1-9][0-9]{0,6}$")) throw new InvalidDataException("Riferimento cella non valido.");
                var col = new string(reference.TakeWhile(char.IsLetter).ToArray());
                if (!result.TryAdd(col, c)) throw new InvalidDataException("Cella Excel duplicata.");
            }
            return result;
        }
        string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var heads = Cells(rows[0]).ToDictionary(x => x.Key, x => Normalize(Value(x.Value)));
        if (heads.Values.Distinct().Count() != heads.Count || !new[] { "denominazione", "codiceinterno", "pivataxid", "codicefiscale" }.All(heads.ContainsValue))
            throw new InvalidDataException("Intestazioni non riconosciute come esportazione clienti Fatture in Cloud.");
        var clients = new List<CustomerCandidate>(); var issues = new List<string>(); int count = 0;
        var sourceRows = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Skip(1))
        {
            var cells = Cells(row); var values = cells.ToDictionary(x => x.Key, x => Value(x.Value));
            if (values.Values.All(string.IsNullOrWhiteSpace)) continue;
            count++;
            string Get(string heading) { var col = heads.FirstOrDefault(h => h.Value == heading).Key; return col != null && values.TryGetValue(col, out var v) ? v : ""; }
            var number = (string?)row.Attribute("r") ?? (count + 1).ToString();
            bool numericKey = new[] { "codiceinterno", "pivataxid", "codicefiscale", "cap" }.Any(h =>
            { var col = heads.FirstOrDefault(x => x.Value == h).Key; return col != null && cells.TryGetValue(col, out var c) && Get(h).Length > 0 && (string?)c.Attribute("t") is not ("s" or "inlineStr" or "str"); });
            if (numericKey) { issues.Add($"Riga {number}: identificativo o CAP numerico; verificare gli zeri iniziali. Riga esclusa."); continue; }
            var vat = BusinessService.Tax(Get("pivataxid")); var tax = BusinessService.Tax(Get("codicefiscale")); var code = Get("codiceinterno");
            // I prefissi distinguono il codice interno dagli identificativi fiscali.
            var external = code.Length > 0 ? "ID:" + code : vat.Length > 0 ? "VAT:" + vat : tax.Length > 0 ? "CF:" + tax : "";
            if (external.Length == 0) { issues.Add($"Riga {number}: mancano codice interno, partita IVA e codice fiscale. Riga esclusa, da verificare manualmente."); continue; }
            if (!sourceRows.TryGetValue(external, out var positions)) sourceRows[external] = positions = [];
            positions.Add(number);
            var locality = string.Join(" ", new[] { Get("cap"), Get("comune"), Get("provincia") }.Where(s => s.Length > 0));
            var address = string.Join(", ", new[] { Get("indirizzo"), locality, Get("paese"), Get("noteindirizzo") }.Where(s => s.Length > 0));
            clients.Add(new(new("FattureInCloud", external, Get("denominazione"), vat, tax, DateTime.UtcNow), address, Get("indirizzoemail")));
        }
        var duplicates = clients.GroupBy(c => c.Source.ExternalId, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (duplicates.Count > 0)
        {
            foreach (var duplicate in duplicates) issues.Add($"Righe {string.Join(", ", sourceRows[duplicate])}: identificativo sorgente duplicato; righe escluse, da verificare nel portale.");
            clients.RemoveAll(c => duplicates.Contains(c.Source.ExternalId));
        }
        return new(clients.ToArray(), count, issues.ToArray());
    }
}
