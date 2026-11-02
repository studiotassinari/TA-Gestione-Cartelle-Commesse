using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using TA.Server;
using TA.Server.Documents;
using TA.Mcp;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using TA_Gestione_Cartelle_Commesse.Views;

internal static class Program
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    static void Reject<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception("Atteso "+typeof(T).Name);}
    [STAThread] static void Main(string[] args)
    {
        if(args.Contains("--map-browser")){MapBrowserChecks.Run();return;}
        var root=Path.Combine(Path.GetTempPath(),"TA-Geology-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var sources=Path.Combine(root,"lavori");Directory.CreateDirectory(sources);
            var file=Path.Combine(sources,"relazione-geologica.txt");File.WriteAllText(file,"Documento sintetico di prova geologica.");
            var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));var stamp=File.GetLastWriteTimeUtc(file);
            var reports=new List<GeologicalReport>{
                new(){Id="geo-1",Title="Relazione geologica — terreno industriale",Municipality="Rimini",Address="Via di prova",Latitude=44.06,Longitude=12.56,PositionSource="Esempio di collaudo",PositionAccuracy="Indicativa",ProjectCode="C26001",Client="Committente di prova",FileName="relazione-geologica.txt",PhysicalPath=file},
                new(){Id="geo-2",Title="Relazione geotecnica — ampliamento",Kind="Geotecnica",Municipality="Cesena",FileName="geotecnica.pdf"},
                new(){Id="geo-3",Title="Indagine del sottosuolo",Municipality="Rimini",Latitude=44.06,Longitude=12.56,ProjectCode="C26002",PositionSource="Esempio di collaudo"}};
            var catalog=new GeologyCatalog(1,DateTime.UtcNow,reports,[]);
            var catalogFile=Path.Combine(root,"catalogo.json");File.WriteAllText(catalogFile,JsonSerializer.Serialize(catalog));
            var settings=new ServerSettings{JobsRoot=sources,BidsRoot=Path.Combine(root,"gare"),ArchivedJobsRoot=Path.Combine(root,"archivio"),ArchiveFolder=Path.Combine(root,"json"),GeologyCatalogPath=catalogFile};
            var changes=new ChangeHub(false);var analysis=new AnalysisService(settings,NullLogger<AnalysisService>.Instance);var scans=new ScanCoordinator(settings,NullLogger<ScanCoordinator>.Instance,changes,analysis);
            var service=new GeologyService(settings,new DocumentService(settings,scans));
            var first=service.Query(new(Limit:1),default);var second=service.Query(new(Offset:1,Limit:1,Version:first.Version),default);
            Check(first.Total==3&&first.NextOffset==1&&first.Reports[0].Id!=second.Reports[0].Id,"Catalogo completo, totale e paginazione stabile");
            Check(service.Query(new(Search:"Rimini industriale"),default).Reports.Single().Id=="geo-1","Ricerca combinata per titolo e località");
            Check(service.Query(new(OnlyLocated:true),default).Total==2,"Coordinate assenti distinte dalle posizioni note");
            Reject<IOException>(()=>service.Query(new(Offset:1,Version:"vecchia"),default));
            var read=GeologyService.ResolveDocument(settings,reports[0],new("geo-1"));
            var document=DocumentWorker.Execute(read).Document!;
            Check(document.Text.Contains("prova geologica"),"Relazione senza dipendenza dalla commessa leggibile sul server solo su richiesta");
            var isolatedDocument=service.ReadAsync(new("geo-1"),default).GetAwaiter().GetResult();
            Check(isolatedDocument.Text==document.Text,"Lettura della relazione per ID tramite il processo isolato del server");
            Reject<UnauthorizedAccessException>(()=>GeologyService.ResolveDocument(settings,reports[0] with{PhysicalPath=Path.Combine(root,"fuori.txt")},new("geo-1")));
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))==hash&&File.GetLastWriteTimeUtc(file)==stamp,"Documento originale invariato dopo la lettura");
            File.WriteAllText(catalogFile,JsonSerializer.Serialize(catalog with{Reports=[reports[0] with{Latitude=95}]}));
            Reject<InvalidDataException>(()=>service.Query(new(),default));
            File.WriteAllText(catalogFile,JsonSerializer.Serialize(catalog));
            LocalReadOnlyPolicy.Validate(new("Geology",McpTool:"cerca_relazioni_geologiche",CorrelationId:Guid.NewGuid().ToString("N")));
            Reject<UnauthorizedAccessException>(()=>LocalReadOnlyPolicy.Validate(new("ReadGeologicalDocument",McpTool:"cerca_relazioni_geologiche",CorrelationId:Guid.NewGuid().ToString("N"))));
            var session=JsonSerializer.SerializeToElement(ConsultationSessions.Shared.Open(false)).GetProperty("consultazione").GetString()!;
            Reject<ModelContextProtocol.McpException>(()=>McpInvocation.Begin("leggi_relazione_geologica",session));
            Check(true,"MCP distingue consultazione dei metadati da lettura dei contenuti");

            GeologyAnalysisChecks.Run(root).GetAwaiter().GetResult();
            GeocodingChecks.Run(root).GetAwaiter().GetResult();
            GeologyOpenChecks.Run(root).GetAwaiter().GetResult();
            GeologyIndexChecks.Run(root);
            AppContext.SetSwitch("TA.Tests.DisableMapNetwork",true);
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var model=new GeologyViewModel();model.Apply(catalog);model.Search="rimini industriale";
            Check(model.Rows.Count==1&&model.Rows[0].Id=="geo-1","Ricerca dinamica locale senza interrogare il server");
            model.Search="";model.Location="Da localizzare";Check(model.Rows.Single().Id=="geo-2","Relazioni prive di coordinate restano consultabili");
            model.Location="Tutte";model.Select("geo-3");Check(model.Selected?.Id=="geo-3","Selezione mappa restituita all'elenco per identificativo");
            model.Search="Cesena";Check(model.Selected==null,"Il filtro rimuove selezioni non più visibili");model.Search="";model.Select("geo-1");
            var window=new GeologyWindow(model);var content=(FrameworkElement)window.Content;window.Content=null;content.DataContext=model;content.Resources=window.Resources;content.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty,window.FontFamily);content.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,window.Foreground);if(content is Panel panel)panel.Background=window.Background;
            Render(content,"geology-window.png",1400,820);Check(((DataGrid)window.FindName("ReportsGrid")).Items.Count==3,"Finestra WPF con elenco, dettaglio e pannello mappa");
            window.Close();
            var main=new MainViewModel();main.GeologyCommand.Execute(null);
            var shown=app.Windows.OfType<GeologyWindow>().Single();shown.WindowState=WindowState.Minimized;main.GeologyCommand.Execute(null);
            Check(app.Windows.OfType<GeologyWindow>().Count()==1&&shown.WindowState==WindowState.Normal&&shown.Owner==null,"Finestra indipendente, unica istanza e ripristino da minimizzata");
            shown.Close();main.GeologyCommand.Execute(null);Check(app.Windows.OfType<GeologyWindow>().Count()==1,"Riapertura dopo chiusura senza riferimenti alla vecchia finestra");
            main.Stop();Check(!app.Windows.OfType<GeologyWindow>().Any(),"Chiusura client libera la finestra geologica");app.Shutdown();
        }
        finally{if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("TA-Geology-"))Directory.Delete(root,true);}
    }
    static void Render(FrameworkElement root,string name,int width,int height)
    {
        RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
        using var source=new HwndSource(new HwndSourceParameters("TA Geology test"){Width=width,Height=height,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x90000000)});
        source.RootVisual=root;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();root.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);Directory.CreateDirectory("artifacts/checks");using var output=File.Create("artifacts/checks/"+name);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(output);source.RootVisual=null;
    }
}
