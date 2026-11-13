using System.Globalization;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Services;

public sealed record CurriculumImage(byte[] Data, string Type, int Width, int Height);
// Formato: Complete (una scheda per lavoro), Short (più lavori per pagina) oppure List (elenco orizzontale per i requisiti di gara).
public sealed record CurriculumExportOptions(string Format = "Complete", bool Cover = true, string Title = "Lavori svolti", string Subtitle = "",
    bool Photos = true, bool Fees = true, bool References = true, bool Keywords = true, int MaxPhotos = 5);
// Grafica dello studio letta dal server. Senza materiale restano colori e caratteri predefiniti e nessuna immagine.
public sealed class CurriculumKit
{
    public string Main { get; init; } = "135F6E";
    public string Dark { get; init; } = "192845";
    public string TitleFont { get; init; } = "Segoe UI";
    public string TextFont { get; init; } = "Segoe UI";
    public string Footer { get; init; } = "Curriculum dello studio";
    public CurriculumImage? Logo { get; init; }
    public CurriculumImage? LogoLight { get; init; }
    public CurriculumImage? Cover { get; init; }
    // Icone delle macro categorie nell'ordine di CurriculumSectors.All: tutte e quattro oppure nessuna.
    public CurriculumImage[] Icons { get; init; } = [];
    // Simbolo a quadranti con accese le sole macro categorie indicate; null se le icone non sono disponibili.
    public Func<string[], CurriculumImage?> Quadrant { get; init; } = _ => null;
}

// Impaginazione dinamica del curriculum: ogni blocco compare solo se ha contenuto, le fotografie vengono ritagliate
// al centro senza deformarle e la scheda si allunga o si accorcia da sola. Il documento è scritto direttamente in
// WordprocessingML, così la stessa struttura è verificabile con il validatore dello schema.
public sealed class CurriculumLayout
{
    private const string Namespaces = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";
    private const string Grey = "6B7480", Light = "E7EFF1", Line = "C9D3D8", White = "FFFFFF", Tab = "<w:r><w:tab/></w:r>";
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private readonly CurriculumKit kit; private readonly CurriculumExportOptions options;
    private readonly Func<CurriculumWork, IReadOnlyList<CurriculumExportPhoto>> photos;
    private Func<object, byte[], string, string> image = (_, _, _) => "";
    private int drawing;
    private CurriculumLayout(CurriculumKit kit, CurriculumExportOptions options, Func<CurriculumWork, IReadOnlyList<CurriculumExportPhoto>> photos) { this.kit = kit; this.options = options; this.photos = photos; }

    public static void Write(string path, IReadOnlyList<CurriculumWork> works, Func<CurriculumWork, IReadOnlyList<CurriculumExportPhoto>> photos, CurriculumKit kit, CurriculumExportOptions options)
    {
        if (options.Format is not ("Complete" or "Short" or "List")) throw new ArgumentException("Formato di esportazione non previsto.");
        var layout = new CurriculumLayout(kit, options, photos);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var styles = main.AddNewPart<StyleDefinitionsPart>(); var header = main.AddNewPart<HeaderPart>(); var footer = main.AddNewPart<FooterPart>();
        bool landscape = options.Format == "List"; int width = Cm(landscape ? 25.7 : 17);
        string title = options.Format switch { "Short" => "Schede sintetiche", "List" => "Elenco dei servizi svolti", _ => "Schede lavoro" };
        // Ogni parte del documento ha le proprie immagini: il logo dell'intestazione non è quello della copertina.
        layout.image = Register(data => { var part = header.AddImagePart(Kind(data.Type)); Feed(part, data.Bytes); return header.GetIdOfPart(part); });
        Feed(header, layout.HeaderXml(title, width));
        Feed(footer, layout.FooterXml(width));
        layout.image = Register(data => { var part = main.AddImagePart(Kind(data.Type)); Feed(part, data.Bytes); return main.GetIdOfPart(part); });
        Feed(main, layout.DocumentXml(works, main.GetIdOfPart(header), main.GetIdOfPart(footer), landscape, width));
        Feed(styles, layout.StylesXml());
    }
    private static PartTypeInfo Kind(string type) => type == "png" ? ImagePartType.Png : ImagePartType.Jpeg;
    // La stessa immagine usata più volte entra nel documento una volta sola.
    private static Func<object, byte[], string, string> Register(Func<(byte[] Bytes, string Type), string> add)
    {
        var known = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        return (key, bytes, type) => known.TryGetValue(key, out var id) ? id : (known[key] = add((bytes, type)));
    }
    private static void Feed(OpenXmlPart part, string xml) => Feed(part, Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml));
    private static void Feed(OpenXmlPart part, byte[] bytes) { using var stream = new MemoryStream(bytes); part.FeedData(stream); }

    // ------------------------------------------------------------ mattoni
    private static int Cm(double value) => (int)Math.Round(value * 567);
    private static long Emu(double value) => (long)Math.Round(value * 360000);
    private static string Esc(string? text) => new string((text ?? "").Where(c => c >= ' ' || c is '\t' or '\n').ToArray()).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private static string Money(decimal value) => "€ " + value.ToString("N2", Italian);
    private string Rid(CurriculumImage picture) => image(picture, picture.Data, picture.Type);

    private string R(string? text, double size, bool bold = false, string? color = null, bool caps = false, int track = 0)
    {
        string font = Esc(bold ? kit.TitleFont : kit.TextFont); int half = (int)Math.Round(size * 2);
        var x = new StringBuilder($"""<w:r><w:rPr><w:rFonts w:ascii="{font}" w:hAnsi="{font}" w:cs="{font}"/>""");
        if (bold) x.Append("<w:b/><w:bCs/>");
        if (caps) x.Append("<w:caps/>");
        x.Append($"""<w:color w:val="{color ?? kit.Dark}"/>""");
        if (track != 0) x.Append($"""<w:spacing w:val="{track}"/>""");
        x.Append($"""<w:sz w:val="{half}"/><w:szCs w:val="{half}"/></w:rPr><w:t xml:space="preserve">""");
        // Gli a capo scritti nella scheda restano a capo nel documento.
        x.Append(Esc(text).Replace("\n", "</w:t><w:br/><w:t xml:space=\"preserve\">"));
        return x.Append("</w:t></w:r>").ToString();
    }
    private static string P(string runs = "", int after = 80, int before = 0, string align = "", bool keep = false, string rule = "", int ruleSize = 8, int tab = 0, int line = 252, bool pageBreak = false, string section = "")
    {
        var x = new StringBuilder("<w:p><w:pPr>");
        if (keep) x.Append("<w:keepNext/>");
        if (pageBreak) x.Append("<w:pageBreakBefore/>");
        if (rule.Length > 0) x.Append($"""<w:pBdr><w:bottom w:val="single" w:sz="{ruleSize}" w:space="1" w:color="{rule}"/></w:pBdr>""");
        if (tab > 0) x.Append($"""<w:tabs><w:tab w:val="right" w:pos="{tab}"/></w:tabs>""");
        x.Append($"""<w:spacing w:before="{before}" w:after="{after}" w:line="{line}" w:lineRule="auto"/>""");
        if (align.Length > 0) x.Append($"""<w:jc w:val="{align}"/>""");
        // Paragrafo vuoto: carattere minimo, così non aggiunge altezza oltre alla spaziatura richiesta.
        if (runs.Length == 0) x.Append("<w:rPr><w:sz w:val=\"2\"/><w:szCs w:val=\"2\"/></w:rPr>");
        return x.Append(section).Append("</w:pPr>").Append(runs).Append("</w:p>").ToString();
    }
    private string Label(string text, int before = 0, int after = 20) => P(R(text, 7, true, kit.Main, true, 30), after, before, keep: true);

    private string Graphic(string rid, long cx, long cy, (int Side, int Top)? crop)
    {
        string source = crop is { } c ? $"""<a:srcRect l="{c.Side}" t="{c.Top}" r="{c.Side}" b="{c.Top}"/>""" : "<a:srcRect/>";
        int id = ++drawing;
        return $"""<wp:docPr id="{id}" name="Immagine {id}"/><wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect="1"/></wp:cNvGraphicFramePr>""" +
            $"""<a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="{id}" name="Immagine {id}"/><pic:cNvPicPr/></pic:nvPicPr>""" +
            $"""<pic:blipFill><a:blip r:embed="{rid}"/>{source}<a:stretch><a:fillRect/></a:stretch></pic:blipFill>""" +
            $"""<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic>""";
    }
    // Ritaglio centrato in millesimi di percento per lato: l'immagine riempie il riquadro senza deformarsi.
    public static (int Side, int Top)? Crop(int pixelWidth, int pixelHeight, double width, double height)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 || width <= 0 || height <= 0) return null;
        double target = width / height, actual = pixelWidth / (double)pixelHeight;
        return actual > target ? ((int)Math.Round((1 - target / actual) * 50000), 0) : (0, (int)Math.Round((1 - actual / target) * 50000));
    }
    private string Pic(string rid, double width, double height, (int Side, int Top)? crop = null)
    {
        long cx = Emu(width), cy = Emu(height);
        return $"""<w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="{cx}" cy="{cy}"/><wp:effectExtent l="0" t="0" r="0" b="0"/>{Graphic(rid, cx, cy, crop)}</wp:inline></w:drawing></w:r>""";
    }
    private string Float(string rid, double x, double y, double width, double height, bool behind, (int Side, int Top)? crop = null)
    {
        long cx = Emu(width), cy = Emu(height);
        return $"""<w:r><w:drawing><wp:anchor distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="{(behind ? 1 : 2)}" behindDoc="{(behind ? 1 : 0)}" locked="0" layoutInCell="1" allowOverlap="1">""" +
            $"""<wp:simplePos x="0" y="0"/><wp:positionH relativeFrom="page"><wp:posOffset>{Emu(x)}</wp:posOffset></wp:positionH><wp:positionV relativeFrom="page"><wp:posOffset>{Emu(y)}</wp:posOffset></wp:positionV>""" +
            $"""<wp:extent cx="{cx}" cy="{cy}"/><wp:effectExtent l="0" t="0" r="0" b="0"/><wp:wrapNone/>{Graphic(rid, cx, cy, crop)}</wp:anchor></w:drawing></w:r>""";
    }
    private static string Cell(string content, int width, string fill = "", string bottom = "", int top = 40, int left = 0, int low = 40, int right = 120, string valign = "top")
    {
        var x = new StringBuilder($"""<w:tc><w:tcPr><w:tcW w:w="{width}" w:type="dxa"/>""");
        if (bottom.Length > 0) x.Append($"""<w:tcBorders><w:bottom w:val="single" w:sz="4" w:space="0" w:color="{bottom}"/></w:tcBorders>""");
        if (fill.Length > 0) x.Append($"""<w:shd w:val="clear" w:color="auto" w:fill="{fill}"/>""");
        x.Append($"""<w:tcMar><w:top w:w="{top}" w:type="dxa"/><w:left w:w="{left}" w:type="dxa"/><w:bottom w:w="{low}" w:type="dxa"/><w:right w:w="{right}" w:type="dxa"/></w:tcMar><w:vAlign w:val="{valign}"/></w:tcPr>""");
        // Una cella deve sempre contenere almeno un paragrafo.
        return x.Append(content.Length > 0 ? content : P()).Append("</w:tc>").ToString();
    }
    private static string Row(IEnumerable<string> cells, bool header = false) => "<w:tr><w:trPr><w:cantSplit/>" + (header ? "<w:tblHeader/>" : "") + "</w:trPr>" + string.Concat(cells) + "</w:tr>";
    private static string Tbl(int[] widths, IEnumerable<string> rows) =>
        $"""<w:tbl><w:tblPr><w:tblW w:w="{widths.Sum()}" w:type="dxa"/><w:tblLayout w:type="fixed"/><w:tblCellMar><w:left w:w="0" w:type="dxa"/><w:right w:w="0" w:type="dxa"/></w:tblCellMar></w:tblPr><w:tblGrid>""" +
        string.Concat(widths.Select(w => $"""<w:gridCol w:w="{w}"/>""")) + "</w:tblGrid>" + string.Concat(rows) + "</w:tbl>";
    private static string Section(string header, string footer, bool landscape, double top, double bottom)
    {
        var x = new StringBuilder("<w:sectPr>");
        if (header.Length > 0) x.Append($"""<w:headerReference w:type="default" r:id="{header}"/>""");
        if (footer.Length > 0) x.Append($"""<w:footerReference w:type="default" r:id="{footer}"/>""");
        x.Append(landscape ? "<w:pgSz w:w=\"16838\" w:h=\"11906\" w:orient=\"landscape\"/>" : "<w:pgSz w:w=\"11906\" w:h=\"16838\"/>");
        return x.Append($"""<w:pgMar w:top="{Cm(top)}" w:right="{Cm(2)}" w:bottom="{Cm(bottom)}" w:left="{Cm(2)}" w:header="{Cm(1.1)}" w:footer="{Cm(0.9)}" w:gutter="0"/></w:sectPr>""").ToString();
    }
    private static double Height(CurriculumImage picture, double width) => picture.Width > 0 ? width * picture.Height / picture.Width : 0;
    private static string[] Blocks(string? text) => (text ?? "").Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string Sectors(CurriculumWork work) => work.Sectors is { Length: > 0 } sectors ? "  ·  " + string.Join(" · ", sectors) : "";

    // ------------------------------------------------------------ parti fisse
    private string HeaderXml(string title, int width)
    {
        string left = kit.Logo is { Width: > 0 } logo ? Pic(Rid(logo), 4.2, Height(logo, 4.2)) : R(kit.Footer, 9, true);
        return $"<w:hdr {Namespaces}>" + P(left + Tab + R(title, 8, true, kit.Main, true, 40), 60, tab: width) + P("", 200, rule: kit.Main, ruleSize: 6) + "</w:hdr>";
    }
    private string FooterXml(int width)
    {
        string font = Esc(kit.TitleFont);
        string page = $"""<w:r><w:rPr><w:rFonts w:ascii="{font}" w:hAnsi="{font}" w:cs="{font}"/><w:b/><w:bCs/><w:color w:val="{kit.Dark}"/><w:sz w:val="16"/><w:szCs w:val="16"/></w:rPr>""" +
            "<w:fldChar w:fldCharType=\"begin\"/><w:instrText xml:space=\"preserve\">PAGE</w:instrText><w:fldChar w:fldCharType=\"separate\"/><w:t>1</w:t><w:fldChar w:fldCharType=\"end\"/></w:r>";
        return $"<w:ftr {Namespaces}>" + Tbl([width - Cm(0.9), Cm(0.9)], [Row([Cell(P(R(kit.Footer, 7.5, false, Grey), 0), width - Cm(0.9), valign: "center"),
            Cell(P(page, 0, align: "center"), Cm(0.9), Light, top: 90, low: 90, right: 0, valign: "center")])]) + P("", 0) + "</w:ftr>";
    }
    private string StylesXml()
    {
        string font = Esc(kit.TextFont);
        return $"<w:styles {Namespaces}>" + $"""<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="{font}" w:hAnsi="{font}" w:cs="{font}"/><w:color w:val="{kit.Dark}"/><w:sz w:val="20"/><w:szCs w:val="20"/><w:lang w:val="it-IT"/></w:rPr></w:rPrDefault>""" +
            "<w:pPrDefault><w:pPr><w:spacing w:after=\"0\" w:line=\"252\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normale\"><w:name w:val=\"Normal\"/></w:style></w:styles>";
    }
    private string DocumentXml(IReadOnlyList<CurriculumWork> works, string header, string footer, bool landscape, int width)
    {
        var body = new StringBuilder();
        if (options.Cover)
        {
            double band = Cover(body);
            body.Append(P("", 0, section: Section("", "", false, band > 0 ? band + 1.7 : 3.2, 2)));
        }
        if (options.Format == "Complete") for (int i = 0; i < works.Count; i++) Full(body, works[i], i + 1, i == 0, width);
        else if (options.Format == "Short") { Intro(body, "Schede sintetiche"); for (int i = 0; i < works.Count; i++) Short(body, works[i], i + 1, i == works.Count - 1, width); }
        else { Intro(body, "Elenco dei servizi svolti"); Listing(body, works, width); }
        body.Append(Section(header, footer, landscape, landscape ? 2.9 : 3.2, landscape ? 1.8 : 2));
        return $"<w:document {Namespaces}><w:body>" + body + "</w:body></w:document>";
    }
    private void Intro(StringBuilder body, string title) => body.Append(P(R(kit.Footer, 8, true, kit.Main, true, 40), 40)).Append(P(R(title, 22, true), 100)).Append(P("", 200, rule: kit.Main, ruleSize: 12));

    // Copertina: fascia a tutta pagina con il logo chiaro sopra, se la grafica dello studio la fornisce. Restituisce l'altezza della fascia in centimetri.
    private double Cover(StringBuilder body)
    {
        double band = 0; int width = Cm(17);
        if (kit.Cover is { Width: > 0 } cover)
        {
            band = Math.Min(12.7, Height(cover, 21));
            string first = Float(Rid(cover), 0, 0, 21, band, true, Crop(cover.Width, cover.Height, 21, band));
            if (kit.LogoLight is { Width: > 0 } light) first += Float(Rid(light), 2, 2.2, 8.4, Height(light, 8.4), false);
            body.Append(P(first, 0));
        }
        else if (kit.Logo is { Width: > 0 } logo) body.Append(P(Pic(Rid(logo), 8.4, Height(logo, 8.4)), 1200));
        body.Append(P(R(kit.Footer, 9, true, kit.Main, true, 60), 120)).Append(P(R(options.Title, 44, true), 80, line: 240));
        if (options.Subtitle.Trim().Length > 0) body.Append(P(R(options.Subtitle.Trim(), 13, false, Grey), 80));
        string date = DateTime.Today.ToString("MMMM yyyy", Italian);
        body.Append(P(R(char.ToUpper(date[0], Italian) + date[1..], 11, false, Grey), 500)).Append(P("", 300, rule: kit.Main, ruleSize: 12));
        if (kit.Icons.Length == CurriculumSectors.All.Length)
        {
            int quarter = width / 4;
            body.Append(Tbl([quarter, quarter, quarter, width - 3 * quarter], [Row(CurriculumSectors.All.Select((sector, i) =>
                Cell(P(Pic(Rid(kit.Icons[i]), 1.5, 1.5), 60) + P(R(sector, 8, true, kit.Main, true, 40), 0), i < 3 ? quarter : width - 3 * quarter)))]));
        }
        return band;
    }

    // ------------------------------------------------------------ scheda completa
    private void Full(StringBuilder body, CurriculumWork work, int number, bool first, int width)
    {
        string kicker = $"Scheda lavoro {number:00}" + Sectors(work), meta = string.Join("  ·  ", new[] { work.Place, CurriculumText.Period(work) }.Where(s => s.Length > 0));
        string Head(bool pageBreak) => P(R(kicker, 8, true, kit.Main, true, 40), 60, pageBreak: pageBreak) + P(R(work.Title, 20, true), 60, line: 264) + (meta.Length > 0 ? P(R(meta, 11, false, Grey), 0) : "");
        if (kit.Quadrant(work.Sectors ?? []) is { } quadrant)
        {
            // L'interruzione di pagina non può stare dentro la tabella: la porta un paragrafo vuoto che la precede.
            if (!first) body.Append(P("", 0, pageBreak: true));
            body.Append(Tbl([width - Cm(2.6), Cm(2.6)], [Row([Cell(Head(false), width - Cm(2.6)), Cell(P(Pic(Rid(quadrant), 2.2, 2.2), 0, align: "right"), Cm(2.6), top: 0, low: 0, right: 0)])]));
        }
        else body.Append(Head(!first));
        body.Append(P("", 140, rule: kit.Main, ruleSize: 12));
        var pictures = options.Photos ? photos(work).Where(p => p.PixelWidth > 0 && p.PixelHeight > 0).Take(Math.Max(0, options.MaxPhotos)).ToArray() : Array.Empty<CurriculumExportPhoto>();
        if (pictures.Length > 0)
            body.Append(P(Pic(image(pictures[0], pictures[0].Data, pictures[0].Type), 17, 6.4, Crop(pictures[0].PixelWidth, pictures[0].PixelHeight, 17, 6.4)), 40)).Append(P(R(pictures[0].Caption, 8, false, Grey), 160));
        Facts(body, work, width);
        if (work.Services.Trim().Length > 0) body.Append(Label("Prestazioni svolte")).Append(P(R(work.Services.Trim(), 10), 140));
        var blocks = Blocks(work.Description);
        if (blocks.Length > 0) { body.Append(Label("Descrizione")); foreach (var block in blocks) body.Append(P(R(block, 10), 100, align: "both")); }
        if (work.Categories is { Length: > 0 } categories) Categories(body, categories);
        if (pictures.Length > 1) Gallery(body, pictures[1..], width);
        if (options.Keywords && work.Keywords is { Length: > 0 } keywords) body.Append(Label("Parole chiave", 60)).Append(P(R(string.Join(" · ", keywords), 9, false, Grey)));
    }
    // Griglia dei dati su quattro colonne: le voci vuote non occupano spazio.
    private void Facts(StringBuilder body, CurriculumWork work, int width)
    {
        var items = new (string Label, string Value)[]
        {
            ("Committente", work.Customer), ("Luogo", work.Place), ("Periodo", CurriculumText.Period(work)), ("Ruolo", work.Role), ("Importo lavori", work.WorksAmount is { } amount ? Money(amount) : ""),
            ("Onorario", options.Fees && work.FeeAmount is { } fee ? Money(fee) : ""), ("Referente", options.References ? work.Reference : ""), ("Commesse collegate", string.Join(" · ", work.ProjectCodes ?? []))
        }.Where(item => item.Value.Trim().Length > 0).ToArray();
        if (items.Length == 0) return;
        int quarter = width / 4; int[] widths = [quarter, quarter, quarter, width - 3 * quarter]; var rows = new List<string>();
        for (int i = 0; i < items.Length; i += 4)
        {
            int start = i;
            rows.Add(Row(Enumerable.Range(0, 4).Select(k => start + k < items.Length
                ? Cell(Label(items[start + k].Label) + P(R(items[start + k].Value.Trim(), 9.5), 40), widths[k], top: 50, low: 30, right: 160) : Cell("", widths[k]))));
        }
        body.Append(Tbl(widths, rows)).Append(P("", 60));
    }
    private void Categories(StringBuilder body, CurriculumCategory[] categories)
    {
        // La colonna del grado compare solo se almeno una categoria lo riporta.
        bool grade = categories.Any(c => c.Grade is { Length: > 0 });
        int[] widths = grade ? new[] { Cm(1.8), Cm(9.2), Cm(2.2), Cm(3.8) } : new[] { Cm(1.8), Cm(11.4), Cm(3.8) };
        string[] heads = grade ? new[] { "ID opere", "Categoria", "Grado", "Importo lavori" } : new[] { "ID opere", "Categoria", "Importo lavori" };
        string Align(int i) => i >= heads.Length - (grade ? 2 : 1) ? "right" : "";
        var rows = new List<string> { Row(heads.Select((head, i) => Cell(P(R(head, 8, true, White), 0, align: Align(i)), widths[i], kit.Dark, top: 70, left: 100, low: 70, right: 100)), true) };
        foreach (var category in categories)
        {
            string[] values = grade ? new[] { category.Code, category.Description, category.Grade ?? "", Money(category.Amount) } : new[] { category.Code, category.Description, Money(category.Amount) };
            rows.Add(Row(values.Select((value, i) => Cell(P(R(value, 9, i == 0, i == 0 ? kit.Main : null), 0, align: Align(i)), widths[i], bottom: Line, top: 70, left: 100, low: 70, right: 100))));
        }
        body.Append(Label("Categorie delle opere", 100, 60)).Append(Tbl(widths, rows)).Append(P("", 100));
    }
    // Le fotografie oltre la prima vanno affiancate a due a due.
    private void Gallery(StringBuilder body, CurriculumExportPhoto[] pictures, int width)
    {
        int half = width / 2; var rows = new List<string>();
        for (int i = 0; i < pictures.Length; i += 2)
        {
            int start = i;
            rows.Add(Row(Enumerable.Range(0, 2).Select(k =>
            {
                if (start + k >= pictures.Length) return Cell("", width - half);
                var picture = pictures[start + k];
                return Cell(P(Pic(image(picture, picture.Data, picture.Type), 8.3, 5.53, Crop(picture.PixelWidth, picture.PixelHeight, 8.3, 5.53)), 40) + P(R(picture.Caption, 8, false, Grey), 120),
                    k == 0 ? half : width - half, top: 0, left: 100 * k, low: 0, right: 100 * (1 - k));
            })));
        }
        body.Append(Label("Fotografie", 60, 60)).Append(Tbl([half, width - half], rows));
    }

    // ------------------------------------------------------------ scheda sintetica
    private void Short(StringBuilder body, CurriculumWork work, int number, bool last, int width)
    {
        var picture = options.Photos ? photos(work).FirstOrDefault(p => p.PixelWidth > 0 && p.PixelHeight > 0) : null;
        var quadrant = kit.Quadrant(work.Sectors ?? []);
        int left = picture != null ? Cm(5.4) : 0, symbol = quadrant != null ? Cm(1.6) : 0, right = width - left - symbol;
        string meta = string.Join("  ·  ", new[] { work.Customer, work.Place, CurriculumText.Period(work) }.Where(s => s.Length > 0));
        var text = new StringBuilder(P(R($"{number:00}" + Sectors(work), 7.5, true, kit.Main, true, 40), 30)).Append(P(R(work.Title, 13, true), 30, line: 250));
        if (meta.Length > 0) text.Append(P(R(meta, 9, false, Grey), 80));
        if (Blocks(work.Description).FirstOrDefault() is { } opening) text.Append(P(R(opening, 9.5), 80, align: "both"));
        if (work.Services.Trim().Length > 0) text.Append(P(R("Prestazioni  ", 8, true, kit.Main) + R(work.Services.Trim(), 9), 40));
        string facts = "";
        if (work.WorksAmount is { } amount) facts += R("Importo lavori  ", 8, true, kit.Main) + R(Money(amount) + "     ", 9);
        if (work.Categories is { Length: > 0 } categories) facts += R("Categorie  ", 8, true, kit.Main) + R(string.Join(", ", categories.Select(c => c.Code)) + "     ", 9);
        if (work.ProjectCodes is { Length: > 0 } codes) facts += R("Commesse  ", 8, true, kit.Main) + R(string.Join(" · ", codes), 9);
        if (facts.Length > 0) text.Append(P(facts, 0));
        var cells = new List<string>(); var widths = new List<int>();
        if (picture != null) { cells.Add(Cell(P(Pic(image(picture, picture.Data, picture.Type), 5, 3.33, Crop(picture.PixelWidth, picture.PixelHeight, 5, 3.33)), 0), left, top: 40, low: 0, right: 0)); widths.Add(left); }
        cells.Add(Cell(text.ToString(), right, top: 0, left: picture != null ? 120 : 0, low: 0, right: 0)); widths.Add(right);
        if (quadrant != null) { cells.Add(Cell(P(Pic(Rid(quadrant), 1.3, 1.3), 0, align: "right"), symbol, top: 20, low: 0, right: 0)); widths.Add(symbol); }
        body.Append(Tbl(widths.ToArray(), [Row(cells)])).Append(last ? P("", 0) : P("", 200, rule: Line, ruleSize: 4));
    }

    // ------------------------------------------------------------ elenco per gare
    // Una riga per categoria di opere; in fondo i totali per ID, che sono il dato richiesto dai bandi.
    private void Listing(StringBuilder body, IReadOnlyList<CurriculumWork> works, int width)
    {
        int[] widths = [Cm(0.9), Cm(6.6), Cm(3.2), Cm(2.2), Cm(5.6), Cm(2.2), Cm(3.0), width - Cm(23.7)];
        string[] heads = ["N.", "Lavoro e committente", "Luogo", "Periodo", "Prestazioni", "ID opere", "Importo lavori", "Commessa"];
        string C(string content, int i, string fill = "", string bottom = "") => Cell(content, widths[i], fill, bottom, top: 55, left: 90, low: 55, right: 90);
        var rows = new List<string> { Row(heads.Select((head, i) => C(P(R(head, 8, true, White), 0, align: i == 6 ? "right" : ""), i, kit.Dark)), true) };
        var totals = new Dictionary<string, decimal>(); var order = new List<string>(); decimal overall = 0;
        for (int n = 0; n < works.Count; n++)
        {
            var work = works[n];
            var lines = work.Categories is { Length: > 0 } categories ? categories.Select(c => (Code: c.Code, Amount: (decimal?)c.Amount)).ToArray() : new[] { (Code: "—", Amount: work.WorksAmount) };
            for (int k = 0; k < lines.Length; k++)
            {
                var (code, amount) = lines[k];
                if (amount is { } value)
                {
                    overall += value;
                    if (code != "—") { if (!totals.ContainsKey(code)) { totals[code] = 0; order.Add(code); } totals[code] += value; }
                }
                string bottom = k == lines.Length - 1 ? Line : "", money = P(R(amount is { } shown ? Money(shown) : "—", 9), 0, align: "right"), id = P(R(code, 9, true, kit.Main), 0);
                rows.Add(k == 0
                    ? Row([C(P(R($"{n + 1:00}", 9, true, kit.Main), 0), 0, bottom: bottom), C(P(R(work.Title, 9, true), 20) + P(R(work.Customer, 8.5, false, Grey), 0), 1, bottom: bottom),
                        C(P(R(work.Place, 9), 0), 2, bottom: bottom), C(P(R(CurriculumText.Period(work), 9), 0), 3, bottom: bottom), C(P(R(work.Services.Trim(), 8.5), 0), 4, bottom: bottom),
                        C(id, 5, bottom: bottom), C(money, 6, bottom: bottom), C(P(R(string.Join(" · ", work.ProjectCodes ?? []), 9), 0), 7, bottom: bottom)])
                    : Row(Enumerable.Range(0, 8).Select(i => C(i == 5 ? id : i == 6 ? money : "", i, bottom: bottom))));
            }
        }
        body.Append(Tbl(widths, rows)).Append(P("", 120));
        // Totali in orizzontale, sei per riga: restano sotto l'elenco senza occupare una pagina propria.
        var cells = order.Select(code => (Title: "Totale " + code, Value: Money(totals[code]), Fill: kit.Main, Bold: false)).Append((Title: "Totale complessivo", Value: Money(overall), Fill: kit.Dark, Bold: true)).ToArray();
        int sixth = width / 6; int[] six = [sixth, sixth, sixth, sixth, sixth, width - 5 * sixth];
        for (int i = 0; i < cells.Length; i += 6)
        {
            int start = i;
            body.Append(Tbl(six, [
                Row(Enumerable.Range(0, 6).Select(k => start + k < cells.Length ? Cell(P(R(cells[start + k].Title, 8, true, White), 0, keep: true), six[k], cells[start + k].Fill, top: 55, left: 90, low: 55, right: 90) : Cell("", six[k]))),
                Row(Enumerable.Range(0, 6).Select(k => start + k < cells.Length ? Cell(P(R(cells[start + k].Value, 10, cells[start + k].Bold), 0), six[k], Light, top: 55, left: 90, low: 55, right: 90) : Cell("", six[k])))])).Append(P("", 60));
        }
    }
}
