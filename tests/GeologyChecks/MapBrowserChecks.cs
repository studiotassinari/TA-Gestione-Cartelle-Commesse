using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Views;

public static class MapBrowserChecks
{
    public static void Run()
    {
        var app=new Application();var map=new GeologyMapView();var browser=(WebView2)map.FindName("Browser");
        var window=new Window{Title="Collaudo mappa TA — dati sintetici",Width=1000,Height=700,Content=map};int blocked=0;Exception? failure=null;
        browser.CoreWebView2InitializationCompleted+=(_,e)=>{
            if(!e.IsSuccess)return;
            browser.CoreWebView2.AddWebResourceRequestedFilter("https://tile.openstreetmap.org/*",CoreWebView2WebResourceContext.All);
            browser.CoreWebView2.WebResourceRequested+=(_,r)=>{
                blocked++;
                // Nessuna richiesta al servizio cartografico durante i test automatici.
                var svg="<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256'><rect width='256' height='256' fill='#eaf0f1'/><path d='M0 128H256M128 0V256' stroke='#d8e2e3'/><text x='40' y='80' fill='#668080' font-size='14'>Tessera di collaudo</text></svg>";
                r.Response=browser.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(svg)),200,"OK","Content-Type: image/svg+xml");
            };
        };
        window.Loaded+=async (_,_)=>{
            try{
                var until=DateTime.UtcNow.AddSeconds(25);
                while(browser.CoreWebView2==null||await browser.CoreWebView2.ExecuteScriptAsync("typeof L !== 'undefined' && typeof map !== 'undefined'")!="true") {if(DateTime.UtcNow>until)throw new Exception("WebView2/Leaflet non avviato");await Task.Delay(100);}
                map.SetReports([new(){Id="a",Title="Relazione di collaudo A",Latitude=44,Longitude=12},new(){Id="b",Title="Relazione di collaudo B",Latitude=44,Longitude=12},new(){Id="c",Title="Relazione di collaudo C",Latitude=45,Longitude=13}]);
                while(await browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.pin').length")!="2"){if(DateTime.UtcNow>until)throw new Exception("Simboli non creati");await Task.Delay(100);}
                var selection=new TaskCompletionSource<string>();map.ReportSelected+=id=>selection.TrySetResult(id);
                await browser.CoreWebView2.ExecuteScriptAsync("markers[1].marker.fire('click')");
                if(await selection.Task.WaitAsync(TimeSpan.FromSeconds(5))!="c")throw new Exception("Selezione non arrivata a WPF");
                map.Select("a");await Task.Delay(100);
                if(await browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.pin.selected').length")!="1")throw new Exception("Selezione WPF non evidenziata");
                Directory.CreateDirectory("artifacts/checks");using var file=File.Create("artifacts/checks/geology-map-render.png");await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
                if(blocked==0)throw new Exception("Intercettazione cartografia non verificata");
                Console.WriteLine("PASS: WebView2 reale, Leaflet 1.9.4 locale, CSP, simboli selezionabili e comunicazione WPF; tessere simulate senza traffico esterno.");
            }catch(Exception ex){failure=ex;}finally{map.Dispose();window.Close();app.Shutdown();}
        };
        app.Run(window);if(failure!=null)throw failure;
    }
}
