using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TA_Gestione_Cartelle_Commesse;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
internal static class Program
{
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    [STAThread] static void Main()
    {
        var app=new Application();
        var window=new MainWindow(); // Non viene mostrata: nessun evento Loaded né avvio connessione/updater.
        var vm=(MainViewModel)window.DataContext;
        vm.DiagnosticsOpen=false;
        vm.ApplyProgress(new(true,"Preparazione",0,null,"Censimento unità"));
        Check(vm.ProgressIndeterminate,"Fase senza totale mostra attività indeterminata");
        vm.ApplyProgress(new(true,"Inventario nomi",25,100,"C26198 - Cliente - Progetto",DateTime.UtcNow));
        Check(!vm.ProgressIndeterminate && vm.ProgressValue==25 && vm.ProgressCaption.Contains("25%"),"Percentuale e contatori aggiornati insieme");
        var root=(FrameworkElement)window.Content;
        var diagnostics=(Border)window.FindName("DiagnosticsPanel");
        Render(root,"client-status-closed.png");
        Check(diagnostics.Visibility==Visibility.Collapsed,"Dettagli nascosti quando non richiesti");
        vm.ToggleDiagnosticsCommand.Execute(null);
        Render(root,"client-status-open.png");
        Check(diagnostics.Visibility==Visibility.Visible && Walk(root).OfType<TA_Gestione_Cartelle_Commesse.Views.ConnectionStatusView>().Any(),"Comando apre il controllo WPF di stato e connessione");
        vm.ToggleDiagnosticsCommand.Execute(null);
        Render(root,"client-status-closed.png");
        Check(diagnostics.Visibility==Visibility.Collapsed,"Stesso comando richiude i dettagli");
        vm.ApplyProgress(new(false,"Errore",0,null,"Rete non raggiungibile",HasErrors:true));
        Check(!vm.ProgressIndeterminate && vm.ProgressCaption=="Errore","Errore non mostra attività o completamento fittizi");
        app.Shutdown();
    }
    static IEnumerable<DependencyObject> Walk(DependencyObject parent)
    {yield return parent;for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(parent,i)))yield return child;}
    static void Render(FrameworkElement root,string name)
    {
        foreach(var element in Walk(root).OfType<UIElement>())element.InvalidateVisual();
        root.Measure(new Size(1264,826));root.Arrange(new Rect(0,0,1264,826));root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1264,826,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        Directory.CreateDirectory("artifacts/checks");using var output=File.Create("artifacts/checks/"+name);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(output);
    }
}
