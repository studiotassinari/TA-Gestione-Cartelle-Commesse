using System.IO;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.Views;

internal static partial class Program
{
    private sealed class FakeCurriculum(CurriculumSnapshot snapshot) : ICurriculumGateway
    {
        public CurriculumWork? Saved;
        public Task<T> Send<T>(CurriculumCommand command) => Task.FromResult((T)(object)(command.Action switch
        {
            "Snapshot" => (object)(Saved == null ? snapshot : snapshot with { Works = snapshot.Works.Append(Saved).ToArray() }),
            "Save" => (object)(Saved = command.Work! with { Id = "0123456789abcdef0123456789abcdef", Revision = 1 }),
            _ => throw new InvalidOperationException(command.Action)
        }));
    }
    // Lavori, committenti e immagini sintetici.
    static void CheckCurriculum()
    {
        var school = new CurriculumWork("a", "Adeguamento sismico di una scuola", "Comune di Esempio", "Esempio (XX)", "Primo paragrafo.\n\nSecondo paragrafo.", "Progettazione strutturale", "Incaricato",
            new DateOnly(2023, 2, 1), new DateOnly(2025, 6, 30), 1250000m, 85000m, [new("S.03", 900000m, "Strutture")], "RUP di prova", ["scuola", "sismica"], ["C24031"],
            [new("0123456789abcdef0123456789abcdef", "facciata.png", "png", "Facciata dopo l'intervento")]);
        var bridge = new CurriculumWork("b", "Verifica di un ponte", "Società Beta S.r.l.", Keywords: ["ponte"], ProjectCodes: ["G22007"], Start: new DateOnly(2022, 5, 1));
        var works = new[] { school, bridge };
        Check(CurriculumText.Match(works, " ").Length == 2 && CurriculumText.Match(works, "sismica 2025").Single().Id == "a" && CurriculumText.Match(works, "societa").Single().Id == "b", "Curriculum: ricerca per parole chiave, anno e senza accenti");
        Check(CurriculumText.Match(works, "c24031").Single().Id == "a" && CurriculumText.Match(works, "s.03").Single().Id == "a" && CurriculumText.Match(works, "facciata").Single().Id == "a" && CurriculumText.Match(works, "g22007 ponte").Single().Id == "b", "Curriculum: ricerca per codice commessa, categoria e didascalia");
        Check(CurriculumText.Period(school) == "2023–2025" && CurriculumText.Period(bridge) == "dal 2022", "Curriculum: periodo dalle date");
        var parsed = CurriculumText.Categories("S.03; 900.000,00; Strutture in cemento armato\r\n\r\ne.08 ; 350.000");
        Check(parsed.Length == 2 && parsed[0] is { Code: "S.03", Amount: 900000m, Description: "Strutture in cemento armato" } && parsed[1].Amount == 350000m && CurriculumText.Categories(CurriculumText.Categories(parsed)).Length == 2, "Curriculum: categorie delle opere lette dal testo e riscritte");
        Check(CurriculumText.Amount("", "Importo") == null && CurriculumText.Amount("€ 1.250.000,50", "Importo") == 1250000.50m, "Curriculum: importi in formato italiano");
        try { CurriculumText.Amount("molto", "Importo"); Check(false, "Curriculum: importo non numerico accettato"); } catch (FormatException) { }

        // Fotografia grande: ridotta e salvata in JPEG prima dell'invio.
        var large = new RenderTargetBitmap(3000, 2000, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(large)); using var pngStream = new MemoryStream(); png.Save(pngStream);
        var prepared = CurriculumWindow.PreparePhoto(pngStream.ToArray());
        var decoded = BitmapDecoder.Create(new MemoryStream(prepared), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Check(prepared[0] == 0xFF && prepared[1] == 0xD8 && decoded.PixelWidth is >= 1799 and <= 1801 && decoded.PixelHeight is >= 1199 and <= 1201, "Curriculum: fotografia ridotta a 1800 punti e convertita in JPEG");

        var folder = Path.Combine(Path.GetTempPath(), "TA-CurriculumChecks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            IReadOnlyList<CurriculumExportPhoto> Photos(CurriculumWork work) => work.Id == "a" ? new[] { new CurriculumExportPhoto(prepared, "jpg", "Facciata dopo l'intervento", 1800, 1200) } : Array.Empty<CurriculumExportPhoto>();
            var neutral = Path.Combine(folder, "neutro.docx");
            CurriculumWordExport.Write(neutral, works, Photos, null);
            using (var document = WordprocessingDocument.Open(neutral, false))
            {
                var text = document.MainDocumentPart!.Document.Body!.InnerText;
                Check(text.Contains("Adeguamento sismico di una scuola") && text.Contains("Comune di Esempio") && text.Contains("1.250.000,00") && text.Contains("S.03") && text.Contains("Secondo paragrafo.") && text.Contains("Verifica di un ponte") && text.Contains("Facciata dopo l'intervento"),
                    "Curriculum: esportazione Word senza modello con dati, categorie, descrizione e didascalie");
                Check(document.MainDocumentPart.ImageParts.Count() == 1 && document.MainDocumentPart.Document.Body.Descendants<Drawing>().Count() == 1 && document.MainDocumentPart.Document.Body.Descendants<Table>().Count() == 1, "Curriculum: fotografia e tabella delle categorie nel documento");
            }
            // Modello sintetico dello studio: intestazione, stile dei titoli con identificativo italiano e testo di esempio da sostituire.
            var template = Path.Combine(folder, "modello.dotx");
            using (var model = WordprocessingDocument.Create(template, WordprocessingDocumentType.Template))
            {
                var main = model.AddMainDocumentPart();
                var styles = main.AddNewPart<StyleDefinitionsPart>();
                styles.Styles = new Styles(new Style(new StyleName { Val = "heading 1" }) { Type = StyleValues.Paragraph, StyleId = "Titolo1" });
                var header = main.AddNewPart<HeaderPart>(); header.Header = new Header(new Paragraph(new Run(new Text("INTESTAZIONE DI PROVA"))));
                main.Document = new Document(new Body(new Paragraph(new Run(new Text("Testo di esempio del modello"))),
                    new SectionProperties(new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(header) })));
                styles.Styles.Save(); header.Header.Save(); main.Document.Save();
            }
            var styled = Path.Combine(folder, "con-modello.docx");
            CurriculumWordExport.Write(styled, works, Photos, File.ReadAllBytes(template));
            using (var document = WordprocessingDocument.Open(styled, false))
            {
                var body = document.MainDocumentPart!.Document.Body!;
                Check(document.DocumentType == WordprocessingDocumentType.Document && !body.InnerText.Contains("Testo di esempio") && body.InnerText.Contains("Verifica di un ponte"), "Curriculum: modello convertito in documento e testo di esempio sostituito");
                Check(document.MainDocumentPart.HeaderParts.Single().Header.InnerText == "INTESTAZIONE DI PROVA" && body.LastChild is SectionProperties section && section.Descendants<HeaderReference>().Any(), "Curriculum: intestazione del modello conservata");
                Check(body.Descendants<ParagraphStyleId>().Count(s => s.Val?.Value == "Titolo1") == 2, "Curriculum: titoli dei lavori con lo stile del modello");
            }
        }
        finally { Directory.Delete(folder, true); }

        // Finestra: caricamento, ricerca iniziale e salvataggio tramite un server simulato.
        var gateway = new FakeCurriculum(new(works, [new("p1", "C24031", "C", "C24031 — Comune di Esempio — Scuola"), new("p2", "C24031", "A", "C24031 — archivio")], false));
        var window = new CurriculumWindow(gateway, "ponte", new CurriculumWork("", "Nuovo lavoro proposto", ProjectCodes: ["C24031"]));
        window.LoadAsync().GetAwaiter().GetResult();
        Check(window.VisibleCount == 1, "Curriculum: la finestra applica la ricerca iniziale");
        window.SaveAsync().GetAwaiter().GetResult();
        Check(gateway.Saved is { Title: "" } && window.VisibleCount == 1, "Curriculum: la scheda vuota viene inviata al server, che ne verifica i campi");
    }
}
