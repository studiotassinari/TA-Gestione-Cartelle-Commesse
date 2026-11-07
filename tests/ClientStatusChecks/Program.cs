using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TA_Gestione_Cartelle_Commesse;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using TA_Gestione_Cartelle_Commesse.Views;
internal static class Program
{
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    [STAThread] static int Main()
    {
        try { Run(); return 0; } catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void Run()
    {
        System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly; var app=new Application();var window=new MainWindow();
        var vm=(MainViewModel)window.DataContext;
        vm.StatusTabIndex=0;
        var active=new ScanProgress(true,"Inventario nomi",25,100,"Lettura nomi",DateTime.UtcNow,
            Locations:[new("C","Inventario nomi",@"\\server\lavori\2026\C26198 - Cliente - Progetto\Impianti")]);
        vm.ApplyProgress(active);vm.ApplyProgress(active);
        Check(vm.ScanLog.Count==1 && vm.ScanLog[0].Path.EndsWith("Impianti"),"Log indica sottocartella corrente e non duplica rilevazioni identiche");
        for(int i=0;i<20;i++)vm.ApplyProgress(active with{Locations=[new("C","Inventario nomi",@"\\server\lavori\2026\C26198 - Cliente - Progetto\Documenti\Cartella "+i)]});
        Check(vm.ScanLog.Count==12 && vm.ScanLog[0].Path.EndsWith("19"),"Riepilogo limitato a dodici righe con ultima cartella in alto");
        vm.ApplyProgress(active with {Locations=[new("C","Dimensioni e gerarchie",@"\\server\lavori\2026\C26198 - Cliente - Progetto\Impianti"),new("A","Dimensioni e gerarchie",@"\\server\archivio\C25123.zip > Disegni/Strutture")]});
        Check(vm.ScanLog.Take(2).Any(r=>r.Category=="Archivio" && r.Path.Contains(" > ")),"Percorso fisico e interno archivio distinti, categorie simultanee");
        vm.Commesse.Records.Add(new("C26198","26","198","Cliente dimostrativo","Progetto impianti",@"Z:\lavori\2026\C26198","Valido"));
        var folder=vm.Commesse.Records[0];
        vm.Commesse.Projects.Add(new(new(folder.Id,folder.Code,folder.Subject,Subject:folder.Subject,SourceCustomerName:folder.Client,Year:2026,Number:198),folder:folder));
        var root=Detach(window);
        Render(root,"client-bottom-scan.png",1264,826);
        Check(!Walk(root).OfType<ProgressBar>().Any(),"Nessuna barra animata nella finestra principale");
        var statusView=Walk(root).OfType<StatusTabsView>().Single();
        var tabs=(TabControl)statusView.FindName("StatusTabs");
        Check(tabs.Items.Count==4 && tabs.ActualHeight>100,"Schede informative inferiori presenti e utilizzabili");
        vm.ShowConnectionCommand.Execute(null);Render(root,"client-bottom-connection.png",1264,826);
        Check(tabs.SelectedIndex==1 && Walk(root).OfType<ConnectionStatusView>().Any(),"Comando connessione seleziona la scheda inferiore");
        vm.ShowScanCommand.Execute(null);Render(root,"client-bottom-small.png",(int)window.MinWidth-36,(int)window.MinHeight-74);
        Check(tabs.ActualHeight>=140,"Schede utilizzabili anche alla dimensione minima");
        var projectList=Walk(root).OfType<ListBox>().Single(g=>ReferenceEquals(g.ItemsSource,vm.Commesse.Projects));
        Console.WriteLine($"Dimensione elenco: {projectList.ActualWidth} x {projectList.ActualHeight}");
        Check(projectList.ActualHeight>=90&&projectList.ActualWidth>=1000,"Elenco compatto mantiene altezza e colonne leggibili alla dimensione minima");
        Check(Walk(projectList).OfType<TextBlock>().Any(t=>t.Text=="Progetto impianti"&&t.ActualWidth>100),"Argomento della commessa visibile nel nuovo componente");
        var invalid=new FolderRecord("C26198","26","198","Cliente","Progetto",@"Z:\lavori\2026\C26198 - <script>","Non conforme");
        var checkedRow=invalid with {Code="C26199",Path=@"Z:\lavori\2026\C26199",Status="Valido",Inspection=new(){Complete=false,Issues=["Accesso negato"],QuoteStatus="Non trovato nei nomi"}};
        var report=new NonconformityReportViewModel();
        report.Apply([new('C',@"Z:\lavori",true,"","",new(){SavedUtc=DateTime.UtcNow,Records=[invalid,checkedRow],Warnings=["Nome non conforme: "+invalid.Path]}),new('A',@"Z:\archivio",false,"","",null)]);
        Check(report.Rows.Count(r=>r.Type=="Denominazione")==1,"Denominazione non duplicata dalla segnalazione di elenco");
        Check(report.Rows.Any(r=>r.Type=="Lettura") && report.Rows.Any(r=>r.Type=="Disponibilità") && report.Status.StartsWith("1 cartelle"),"Report distingue lettura parziale, dati mancanti e verifiche non eseguite");
        Check(report.CreateHtml().Contains("&lt;script&gt;") && !report.CreateHtml().Contains("<script>"),"Esportazione HTML codifica i nomi come dati");
        report.Category="Commesse";report.Filter="C26199";
        Check(report.Rows.Count==2 && report.Rows.All(r=>r.Code=="C26199"),"Filtri report applicati anche alla vista esportata");
        report.Category="Tutte";report.Filter="";
        var child=new NonconformityReportWindow(report);
        Render(Detach(child),"nonconformity-child.png",1120,650);
        Check(!child.ShowInTaskbar && child.DataContext==report,"Finestra report separata, senza icona aggiuntiva sulla barra applicazioni");
        app.Shutdown();
    }
    static IEnumerable<DependencyObject> Walk(DependencyObject parent)
    {yield return parent;for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(parent,i)))yield return child;}
    static FrameworkElement Detach(Window window)
    {
        var root=(FrameworkElement)window.Content;root.DataContext=window.DataContext;
        root.Resources=window.Resources;window.Content=null;
        TextElement.SetFontSize(root,window.FontSize);TextElement.SetFontFamily(root,window.FontFamily);
        TextElement.SetForeground(root,window.Foreground);
        if(root is Panel panel)panel.Background=window.Background;
        return root;
    }
    static void Render(FrameworkElement root,string name,int width,int height)
    {
        using var source=new HwndSource(new HwndSourceParameters("TA test rendering"){Width=width,Height=height,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x90000000)});
        source.RootVisual=root;
        foreach(var element in Walk(root).OfType<UIElement>())element.InvalidateVisual();
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout(); root.Dispatcher.Invoke(() => {}, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual(); using(var dc=drawing.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,width,height));dc.DrawRectangle(new VisualBrush(root),null,new Rect(18,18,width-36,height-36));} bitmap.Render(drawing);
        var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
        if(!pixels.Any(p=>p!=0)){Console.WriteLine("Rendering non disponibile nella sessione Windows; verifiche del layout mantenute.");source.RootVisual=null;return;}
        Directory.CreateDirectory("artifacts/checks");using var output=File.Create("artifacts/checks/"+name);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(output);source.RootVisual=null;
    }
}
