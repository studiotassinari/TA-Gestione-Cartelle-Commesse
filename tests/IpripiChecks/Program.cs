using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using TA.Mcp;
using TA.Server;
using TA.Server.Documents;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using TA_Gestione_Cartelle_Commesse.Views;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

internal static class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    static async Task Denied<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static byte[] Pdf(string line)
    {
        var b = new PdfDocumentBuilder(); var font = b.AddStandard14Font(Standard14Font.Helvetica);
        var p = b.AddPage(UglyToad.PdfPig.Content.PageSize.A4); p.AddText(line, 11, new PdfPoint(30, 700), font); return b.Build();
    }
    [STAThread]
    static void Main(string[] args)
    {
        try { RunMain(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }
    static void RunMain(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "TA-IpripiChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // Lascia in temp solo fixture sintetiche in caso di errore, per la diagnosi.
        var row = Run(root).GetAwaiter().GetResult();
        using var model = new IpripiViewModel((q,t) => Task.FromResult(new IpripiPage("v",1,0,null,[row],[])));
        model.LoadAsync().GetAwaiter().GetResult(); model.Selected = row;
        Check(model.ChatRequest().Contains("analisiIpripi=true") && model.Details.Contains("B.4.4.a"), "Richiesta ChatGPT e dettagli con citazioni");
        var window = new IpripiWindow(model); window.Width = 1280; window.Height = 820;
        window.Measure(new Size(1280,820)); window.Arrange(new Rect(0,0,1280,820)); window.UpdateLayout();
        if (args.Length > 0)
        {
            var visual = (FrameworkElement)window.Content; visual.Margin = new Thickness(0); visual.Measure(new Size(1232,772)); visual.Arrange(new Rect(0,0,1232,772)); visual.UpdateLayout();
            var image = new RenderTargetBitmap(1232,772,96,96,PixelFormats.Pbgra32); image.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(args[0]); encoder.Save(file);
        }
        Check(window.Title.Contains("IPRiPI"), "Finestra WPF costruita e impaginata");
        window.Close();
    }
    static async Task<IpripiDocument> Run(string root)
    {
        var rules = IpripiRegulation.Get();
        Check(rules.Rules.Count == 83 && rules.Rules.Select(r => r.Code.Split('.').Take(3).Aggregate((a,b) => a+"."+b)).Distinct().Count() == 57, "Catalogo normativo: 57 voci, 83 varianti");
        Check(rules.Rules.Single(r => r.Code == "B.4.4.a").Levels.SequenceEqual(["L0"]), "Livello B.4.4.a verificato dalla fonte");
        Check(IpripiService.IsCandidate("MUR_A1_D1.pdf") && IpripiService.IsCandidate("MUR A.1 D.1 firmato.pdf") && !IpripiService.IsCandidate("muro.pdf"), "Ricerca nomi MUR senza falso positivo muro");
        var settings = new ServerSettings { JobsRoot = Path.Combine(root,"lavori"), BidsRoot = Path.Combine(root,"gare"), ArchivedJobsRoot = Path.Combine(root,"archivio"), ArchiveFolder = Path.Combine(root,"json") };
        var folder = Directory.CreateDirectory(Path.Combine(settings.JobsRoot,"2026","C26001 - Prova")).FullName;
        const string text = "MUR A.1/D.1. Intervento B.4.4.a: demolizione di divisori interni non strutturali. Progettista Mario Rossi. Via Roma 10, Bologna.";
        var path = Path.Combine(folder,"MUR_A1_D1.pdf"); File.WriteAllBytes(path,Pdf(text));
        File.WriteAllBytes(Path.Combine(folder,"rinominato.pdf"),Pdf(text));
        var zipPath = Path.Combine(folder,"pratica.zip");
        using (var zip = ZipFile.Open(zipPath,ZipArchiveMode.Create)) { using var file = zip.CreateEntry("MUR_A1_D1.pdf").Open(); file.Write(Pdf(text)); }
        var original = SHA256.HashData(File.ReadAllBytes(path)); var stamp = File.GetLastWriteTimeUtc(path);
        var store = new InventoryStore(settings.ArchiveFolder);
        var unit = new InventoryUnit(new string('A',64),"C","2026","C26001","Prova","Prova",folder,"censita");
        InventoryEntry Entry(string name) => new(name,"file",Bytes:new FileInfo(Path.Combine(folder,name)).Length,ModifiedUtc:File.GetLastWriteTimeUtc(Path.Combine(folder,name)));
        void Inventory(bool complete = true) => store.Save(unit,new([Entry("MUR_A1_D1.pdf"),Entry("rinominato.pdf"),new("pratica.zip","archive",Bytes:new FileInfo(zipPath).Length,ModifiedUtc:File.GetLastWriteTimeUtc(zipPath)),new("MUR_A1_D1.pdf","file","pratica.zip",100,stamp)],[],complete),default);
        Inventory();
        var bidUnit = unit with { Id = new string('B',64), Category = "G", Code = "G26001" };
        store.Save(bidUnit,new([Entry("MUR_A1_D1.pdf")],[],true),default);
        var archiveRoot=Directory.CreateDirectory(ArchiveScope.Root(settings.ArchivedJobsRoot)).FullName;
        var archiveFile=Path.Combine(archiveRoot,"C25001 - Cliente - Progetto.zip");File.Copy(zipPath,archiveFile);
        var archiveUnit = unit with { Id = new string('C',64), Category = "A", Code = "C25001", Year = "2025", Path=archiveFile };
        store.Save(archiveUnit,new([new("MUR_A1_D1.pdf","file","pratica.zip",100,stamp)],[],true),default);
        var scans = new ScanCoordinator(settings,NullLogger<ScanCoordinator>.Instance,new ChangeHub(false),new AnalysisService(settings,NullLogger<AnalysisService>.Instance));
        var service = new IpripiService(settings,new DocumentService(settings,scans));
        var catalog = service.Query(new(Code:"C26001"),default);
        Check(service.Query(new(Code:"G26001",AllPdfs:true),default).Total == 0 && service.Query(new(),default).Documents.All(d => d.Category is "C" or "A"),
            "Gare escluse dalla ricerca MUR anche con Tutti i PDF");
        Check(catalog.Total == 2 && service.Query(new(Code:"C26001",AllPdfs:true),default).Total == 3, "PDF candidati e rinominati, inclusi ZIP");
        var archiveRow = service.Query(new(Code:"C25001"),default).Documents.Single();
        Check(archiveRow.Category == "A" && (await service.ReadAsync(new(archiveRow.Id,"C25001"),default)).Text.Contains("MUR"), "Ricerca e lettura MUR anche nell'Archivio");
        var row = catalog.Documents.Single(r => r.ArchiveEntry == null);
        var page = await service.ReadAsync(new(row.Id,"C26001"),default);
        var quote = "B.4.4.a"; var form = "MUR A.1/D.1";
        var item = new IpripiIntervention("Demolizione di divisori interni non strutturali","B.4.4.a","L0","Dichiarato","Codice riportato nella descrizione dell'opera compilata.",[],[new(page.Text.IndexOf(quote,StringComparison.Ordinal),quote)]);
        var draft = new IpripiAnalysisDraft(row.Id,"C26001",row.SourceFingerprint,page.Version,"MUR A.1/D.1","IPRiPI individuati","Demolizione di divisori dichiarata nel modulo.",[item],[new(page.Text.IndexOf(form,StringComparison.Ordinal),form)],[]);
        await Denied<UnauthorizedAccessException>(() => service.SaveAsync(draft,"test",default));
        settings.EnableMcpIpripiWrites = true;
        var designerQuote = "Progettista Mario Rossi";
        var addressQuote = "Via Roma 10, Bologna";
        draft = draft with { Designers = [new("Mario Rossi", "Progettista", [new(page.Text.IndexOf(designerQuote, StringComparison.Ordinal), designerQuote)])],
            Position = new("Via Roma 10", "Bologna", 44.49, 11.34, "Coordinate sintetiche di test", [new(page.Text.IndexOf(addressQuote, StringComparison.Ordinal), addressQuote)]) };
        await Denied<ArgumentException>(() => service.SaveAsync(draft with { Designers = [new("Persona inventata", "Progettista", draft.Designers[0].Evidence)] }, "test", default));
        await Denied<ArgumentException>(() => service.SaveAsync(draft with { Position = draft.Position with { Latitude = 100 } }, "test", default));
        await Denied<ArgumentException>(() => service.SaveAsync(draft with { Position = draft.Position with { Longitude = null } }, "test", default));
        await Denied<ArgumentException>(() => service.SaveAsync(draft with {Interventions=[item with {RuleCode="B.99.1"}]},"test",default));
        await Denied<ArgumentException>(() => service.SaveAsync(draft with {Interventions=[item with {Level="L2"}]},"test",default));
        await Denied<ArgumentException>(() => service.SaveAsync(draft with {Interventions=[item with {Basis="Dedotto",MissingData=["Dimensioni"]}]},"test",default));
        await Denied<ArgumentException>(() => service.SaveAsync(draft with {FormEvidence=[new(0,"Citazione inventata")]},"test",default));
        var saved = await service.SaveAsync(draft,"utente-test",default);
        Check(service.Query(new(Search: "Mario Rossi", RecognizedOnly: true), default).Total == 1 &&
            service.Query(new(Search: "divisori"), default).Total == 1 && service.Query(new(Search: "Bologna"), default).Total == 1,
            "Catalogo ricercabile per progettista, tipologia e indirizzo");
        Check(new IpripiService(settings,new DocumentService(settings,scans)).Query(new(RecognizedOnly:true),default).Documents.Single().Latitude == 44.49,
            "Posizione e progettisti conservati nei JSON dopo riavvio");
        await Denied<IOException>(() => service.SaveAsync(draft,"altro-utente",default));
        Check(service.Query(new(),default).Documents.Single(r => r.Id==row.Id).AnalysisCurrent && saved.SavedBy=="utente-test", "Salvataggio persistente, identità e concorrenza");
        await Denied<IOException>(() => Task.Run(() => service.Query(new(Version:catalog.Version),default)));
        Check(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(original) && File.GetLastWriteTimeUtc(path)==stamp,"Originale invariato dopo lettura e salvataggio");
        var zipRow = catalog.Documents.Single(r => r.ArchiveEntry != null);
        Check((await service.ReadAsync(new(zipRow.Id,"C26001"),default)).Text.Contains(quote), "Lettura MUR dentro ZIP tramite worker confinato");
        var evidence = new IpripiEvidenceStore(); evidence.Remember(row.Id,page with {Text=page.Text[..20],NextOffset=20});
        await Denied<ArgumentException>(() => Task.Run(() => evidence.Validate(draft with {Outcome="Nessun IPRiPI dichiarato",Interventions=[]})));
        Check(true,"Esito negativo rifiutato su lettura incompleta");
        File.WriteAllBytes(path,Pdf(text+" Revisione."));
        await Denied<IOException>(() => service.SaveAsync(draft with {ExpectedRevision=saved.Revision},"test",default));
        Inventory();
        Check(!service.Query(new(),default).Documents.Single(r=>r.Id==row.Id).AnalysisCurrent,"Modifica della fonte invalida analisi, checksum protegge anche prima del ricensimento");
        var restarted = new IpripiService(settings,new DocumentService(settings,scans));
        Check(restarted.Query(new(),default).Documents.Single(r=>r.Id==row.Id).Saved?.Revision == saved.Revision,"Analisi conservata dopo riavvio");
        store.Save(unit,new([Entry("rinominato.pdf")],[],true),default);
        var retained = restarted.Query(new(),default).Documents.Single(r=>r.Id==row.Id);
        Check(!retained.OriginalAvailable && !retained.AnalysisCurrent && retained.Saved?.Revision == saved.Revision,"Fonte non più censita: storico conservato e segnalato");
        var session = new ConsultationSessions();
        string Open(bool geology, bool ipripi) => JsonSerializer.SerializeToElement(session.Open(true,geology,ipripi)).GetProperty("consultazione").GetString()!;
        await Denied<InvalidOperationException>(()=>Task.Run(()=>session.Use(Open(true,false),true,ipripi:true)));
        session.Use(Open(false,true),true,ipripi:true);
        await Denied<UnauthorizedAccessException>(()=>Task.Run(()=>LocalCatalogPolicy.Validate(new("SaveIpripi",McpTool:"cerca_mur_ipripi",CorrelationId:Guid.NewGuid().ToString("N")))));
        LocalCatalogPolicy.Validate(new("SaveIpripi",McpTool:"salva_analisi_ipripi",CorrelationId:Guid.NewGuid().ToString("N")));
        Check(true,"Sessioni IPRiPI separate dalla geologia e policy strumento verificata");
        using (var docx = ZipFile.Open(Path.Combine(folder,"MUR_A1_D1.docx"),ZipArchiveMode.Create))
        using (var writer = new StreamWriter(docx.CreateEntry("word/document.xml").Open()))
            writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>MUR A.1/D.1 Progettista di prova</w:t></w:r></w:p></w:body></w:document>");
        store.Save(unit,new([Entry("MUR_A1_D1.pdf"),Entry("MUR_A1_D1.docx")],[],true),default);
        var wordRow = service.Query(new(Code:"C26001"),default).Documents.Single(d => d.FileName.EndsWith(".docx"));
        Check((await service.ReadAsync(new(wordRow.Id,"C26001"),default)).Text.Contains("Progettista di prova"), "Lettura contenuto MUR DOCX attraverso il servizio");
        var indexer = new IpripiIndexer(settings, service, NullLogger<IpripiIndexer>.Instance);
        var indexed = indexer.Refresh(default);
        Check(indexed.Documents.Any(d => d.FileName.EndsWith(".docx")) && indexed.Documents.All(d => d.Category is "C" or "A"), "Indice giornaliero include DOCX ed esclude Gare");
        Check(CatalogStorage.Exists(settings,Path.Combine(service.Root,"candidati.json")) && service.Query(new(),default).Documents.Any(d => d.FileName.EndsWith(".docx")), "Catalogo candidati JSON consultabile senza rileggere tutti i file");
        Check((await service.ReadAsync(new(wordRow.Id,"C26001"),default)).Text.Contains("Progettista di prova"), "Indice individua direttamente l'unità del documento senza ricerca globale");
        Check(IpripiIndexer.NextRun(new DateTime(2026,11,3,10,0,0,DateTimeKind.Utc)) == new DateTime(2026,11,3,23,0,0,DateTimeKind.Utc), "Scansione giornaliera alle 00:00 italiane");
        Check(IpripiIndexer.NextRun(new DateTime(2026,7,3,10,0,0,DateTimeKind.Utc)) == new DateTime(2026,7,3,22,0,0,DateTimeKind.Utc), "Scansione giornaliera rispetta ora legale italiana");
        var first=service.Query(new(Limit:1),default);
        var target=Path.Combine(service.Root,"candidati.json");
        CatalogStorage.Write(settings,target,indexed with{CheckedUtc=indexed.CheckedUtc.AddMinutes(1),Running=true});
        Check(service.Query(new(Limit:1),default).Version==first.Version,"Progresso e data scansione non invalidano la versione delle righe");
        CatalogStorage.Write(settings,target,indexed with{Documents=indexed.Documents.Select(d=>d with{FileName="MUR modificato "+d.FileName}).ToList()});
        var continuation=service.Query(new(Offset:1,Limit:1,Version:first.Version),default);
        Check(continuation.Version==first.Version&&continuation.Total==first.Total&&!continuation.Documents[0].FileName.StartsWith("MUR modificato"),"Pagine coerenti durante pubblicazione concorrente del catalogo");
        Check(service.Query(new(),default).Version!=first.Version,"Nuova ricerca vede il catalogo aggiornato");
        await Denied<IOException>(()=>Task.Run(()=>service.Query(new(Code:"C26001",Offset:1,Version:first.Version),default)));
        return row with { Saved=saved };
    }
}
