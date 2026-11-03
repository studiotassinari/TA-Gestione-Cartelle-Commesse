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
        const string text = "MUR A.1/D.1. Intervento B.4.4.a: demolizione di divisori interni non strutturali.";
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
        var scans = new ScanCoordinator(settings,NullLogger<ScanCoordinator>.Instance,new ChangeHub(false),new AnalysisService(settings,NullLogger<AnalysisService>.Instance));
        var service = new IpripiService(settings,new DocumentService(settings,scans));
        var catalog = service.Query(new(Code:"C26001"),default);
        Check(catalog.Total == 2 && service.Query(new(Code:"C26001",AllPdfs:true),default).Total == 3, "PDF candidati e rinominati, inclusi ZIP");
        var row = catalog.Documents.Single(r => r.ArchiveEntry == null);
        var page = await service.ReadAsync(new(row.Id,"C26001"),default);
        var quote = "B.4.4.a"; var form = "MUR A.1/D.1";
        var item = new IpripiIntervention("Demolizione di divisori interni non strutturali","B.4.4.a","L0","Dichiarato","Codice riportato nella descrizione dell'opera compilata.",[],[new(page.Text.IndexOf(quote,StringComparison.Ordinal),quote)]);
        var draft = new IpripiAnalysisDraft(row.Id,"C26001",row.SourceFingerprint,page.Version,"MUR A.1/D.1","IPRiPI individuati","Demolizione di divisori dichiarata nel modulo.",[item],[new(page.Text.IndexOf(form,StringComparison.Ordinal),form)],[]);
        await Denied<UnauthorizedAccessException>(() => service.SaveAsync(draft,"test",default));
        settings.EnableMcpIpripiWrites = true;
        await Denied<ArgumentException>(() => service.SaveAsync(draft with {Interventions=[item with {RuleCode="B.99.1"}]},"test",default));
        await Denied<ArgumentException>(() => service.SaveAsync(draft with {Interventions=[item with {Level="L2"}]},"test",default));
        await Denied<ArgumentException>(() => service.SaveAsync(draft with {Interventions=[item with {Basis="Dedotto",MissingData=["Dimensioni"]}]},"test",default));
        await Denied<ArgumentException>(() => service.SaveAsync(draft with {FormEvidence=[new(0,"Citazione inventata")]},"test",default));
        var saved = await service.SaveAsync(draft,"utente-test",default);
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
        return row with { Saved=saved };
    }
}
