using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TA_Gestione_Cartelle_Commesse.Contracts;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
namespace TA_Gestione_Cartelle_Commesse.Services;

public sealed record CurriculumExportPhoto(byte[] Data, string Type, string Caption, int PixelWidth, int PixelHeight);

// Esportazione Word del curriculum. Con un modello dello studio (.docx o .dotx) restano intestazione, piè di pagina,
// margini e stili del modello e viene sostituito solo il testo; senza modello l'impaginazione è neutra.
public static class CurriculumWordExport
{
    public static void Write(string path, IReadOnlyList<CurriculumWork> works, Func<CurriculumWork, IReadOnlyList<CurriculumExportPhoto>> photos, byte[]? template, string heading = "Curriculum — lavori svolti")
    {
        if (template != null) File.WriteAllBytes(path, template);
        using var document = template != null ? WordprocessingDocument.Open(path, true) : WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        MainDocumentPart main;
        if (template == null) { main = document.AddMainDocumentPart(); main.Document = new Document(new Body()); }
        else
        {
            if (document.DocumentType != WordprocessingDocumentType.Document) document.ChangeDocumentType(WordprocessingDocumentType.Document);
            main = document.MainDocumentPart ?? throw new InvalidDataException("Il modello Word non contiene un documento.");
        }
        var body = main.Document.Body ?? main.Document.AppendChild(new Body());
        // L'ultima sezione porta margini, intestazione e piè di pagina del modello: va conservata in fondo.
        var section = body.Elements<SectionProperties>().LastOrDefault();
        body.RemoveAllChildren();

        var styles = main.StyleDefinitionsPart?.Styles?.Elements<Style>().ToList() ?? new List<Style>();
        // I nomi degli stili predefiniti sono gli stessi in ogni lingua di Word; cambia solo l'identificativo.
        string? StyleFor(string name) => styles.FirstOrDefault(s => string.Equals(s.StyleName?.Val?.Value, name, StringComparison.OrdinalIgnoreCase))?.StyleId?.Value;
        string? titleStyle = StyleFor("Title"), workStyle = StyleFor("heading 1"), partStyle = StyleFor("heading 2"), captionStyle = StyleFor("caption");

        Run Piece(string text, bool bold = false, int halfPoints = 0, string? color = null)
        {
            var run = new Run();
            if (bold || halfPoints > 0 || color != null)
            {
                var properties = new RunProperties();
                if (bold) properties.Append(new Bold());
                if (color != null) properties.Append(new Color { Val = color });
                if (halfPoints > 0) properties.Append(new FontSize { Val = halfPoints.ToString() });
                run.Append(properties);
            }
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) run.Append(new Break());
                run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            }
            return run;
        }
        // Con uno stile del modello non si aggiunge formattazione diretta: l'aspetto è quello dello studio.
        Paragraph Styled(string text, string? style, int halfPoints, string? color)
        {
            var paragraph = new Paragraph();
            if (style != null) { paragraph.Append(new ParagraphProperties(new ParagraphStyleId { Val = style })); paragraph.Append(Piece(text)); }
            else paragraph.Append(Piece(text, true, halfPoints, color));
            return paragraph;
        }
        void Field(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            body.Append(new Paragraph(Piece(label + ": ", true), Piece(value.Trim())));
        }

        body.Append(Styled(heading, titleStyle, 40, "192845"));
        uint drawing = 1;
        for (int index = 0; index < works.Count; index++)
        {
            var work = works[index];
            if (index > 0) body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
            body.Append(Styled(work.Title, workStyle, 30, "135F6E"));
            Field("Committente", work.Customer); Field("Luogo", work.Place); Field("Periodo", CurriculumText.Period(work));
            Field("Prestazioni svolte", work.Services); Field("Ruolo dello studio", work.Role);
            Field("Importo dei lavori", work.WorksAmount is { } amount ? "€ " + CurriculumText.Amount(amount) : null);
            Field("Importo del servizio", work.FeeAmount is { } fee ? "€ " + CurriculumText.Amount(fee) : null);
            if (work.Categories is { Length: > 0 } categories)
            {
                body.Append(Styled("Categorie delle opere", partStyle, 24, "192845"));
                var table = new Table(new TableProperties(new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                    new TableBorders(new TopBorder { Val = BorderValues.Single, Size = 4U }, new LeftBorder { Val = BorderValues.Single, Size = 4U }, new BottomBorder { Val = BorderValues.Single, Size = 4U },
                        new RightBorder { Val = BorderValues.Single, Size = 4U }, new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4U })));
                TableRow Row(bool header, params string[] cells) => new(cells.Select(cell => new TableCell(new Paragraph(Piece(cell, header)))));
                table.Append(Row(true, "ID opere", "Importo (€)", "Descrizione"));
                foreach (var category in categories) table.Append(Row(false, category.Code, CurriculumText.Amount(category.Amount), category.Description));
                body.Append(table); body.Append(new Paragraph());
            }
            Field("Riferimenti del committente", work.Reference);
            if (!string.IsNullOrWhiteSpace(work.Description))
            {
                body.Append(Styled("Descrizione", partStyle, 24, "192845"));
                foreach (var block in work.Description.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)) body.Append(new Paragraph(Piece(block.Trim())));
            }
            foreach (var photo in photos(work))
            {
                if (photo.PixelWidth <= 0 || photo.PixelHeight <= 0) continue;
                var part = main.AddImagePart(photo.Type == "png" ? ImagePartType.Png : ImagePartType.Jpeg);
                using (var stream = new MemoryStream(photo.Data)) part.FeedData(stream);
                // Larghezza naturale a 96 punti per pollice, mai oltre 16 × 20 cm.
                long width = Math.Min(photo.PixelWidth * 9525L, 16 * 360000L), height = width * photo.PixelHeight / photo.PixelWidth;
                if (height > 20 * 360000L) { height = 20 * 360000L; width = height * photo.PixelWidth / photo.PixelHeight; }
                body.Append(new Paragraph(new Run(Picture(main.GetIdOfPart(part), width, height, drawing++))));
                if (!string.IsNullOrWhiteSpace(photo.Caption))
                    body.Append(captionStyle != null ? Styled(photo.Caption, captionStyle, 0, null) : new Paragraph(Piece(photo.Caption, false, 18, "53717B")));
            }
        }
        if (section != null) body.Append(section);
        main.Document.Save();
    }
    private static Drawing Picture(string relationship, long width, long height, uint id) => new(new DW.Inline(
        new DW.Extent { Cx = width, Cy = height },
        new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
        new DW.DocProperties { Id = id, Name = "Fotografia " + id },
        new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
        new A.Graphic(new A.GraphicData(new PIC.Picture(
            new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = 0U, Name = "Fotografia " + id }, new PIC.NonVisualPictureDrawingProperties()),
            new PIC.BlipFill(new A.Blip { Embed = relationship }, new A.Stretch(new A.FillRectangle())),
            new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
    { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
}
