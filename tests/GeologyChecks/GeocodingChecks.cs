using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using TA.Server;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;

internal static class GeocodingChecks
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Count; public List<DateTime> Sent = []; public bool Throttle;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++; Sent.Add(DateTime.UtcNow);
            Check(request.Headers.UserAgent.ToString().Contains("TA-Commesse/") && !request.RequestUri!.Query.Contains("ReportId"), "Nominatim riceve identificazione app e solo indirizzo");
            var response = new HttpResponseMessage(Throttle ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK) { Content = new StringContent("[{\"display_name\":\"Via di prova, Comune di prova\",\"lat\":\"44.1\",\"lon\":\"12.2\",\"addresstype\":\"road\",\"address\":{\"town\":\"Comune di prova\"}}]") };
            if(Throttle) response.Headers.RetryAfter = new(TimeSpan.FromSeconds(60));
            return Task.FromResult(response);
        }
    }
    private static void Check(bool ok,string message) { if(!ok) throw new Exception(message); Console.WriteLine("PASS: "+message); }
    public static async Task Run(string root)
    {
        var handler=new Handler();using var http=new HttpClient(handler);var path=Path.Combine(root,"geocoding-tests");
        var service=new NominatimGeocoder(path,"https://nominatim.openstreetmap.org/search",http);
        Check(handler.Count==0,"Nessuna richiesta geografica alla creazione del servizio");
        var first=await service.SearchAsync("Via di prova, Comune di prova",default);
        var cached=await new NominatimGeocoder(path,"https://nominatim.openstreetmap.org/search",http).SearchAsync("  VIA DI PROVA, Comune di prova  ",default);
        Check(!first.FromCache&&cached.FromCache&&handler.Count==1,"Cache JSON persistente e normalizzata, riutilizzata dopo riavvio");
        Check(cached.Candidates.Single().Precision.Contains("civico non")&&cached.Candidates.Single().Municipality=="Comune di prova","Strada indicativa distinta da civico e comune restituito dal servizio");
        await service.SearchAsync("Via seconda, Comune di prova",default);
        Check(handler.Sent[1]-handler.Sent[0]>=TimeSpan.FromSeconds(1),"Richieste centralizzate distanti almeno un secondo");
        handler.Throttle=true;try{await service.SearchAsync("Via terza, Comune di prova",default);throw new Exception("Atteso limite 429");}catch(IOException){}
        var calls=handler.Count;try{await service.SearchAsync("Via quarta, Comune di prova",default);throw new Exception("Attesa pausa");}catch(IOException){}
        Check(handler.Count==calls,"Rispetto Retry-After: nessun tentativo automatico");
        var invalid=NominatimGeocoder.Parse(Encoding.UTF8.GetBytes("[{\"lat\":\"NaN\",\"lon\":\"12\",\"display_name\":\"non valido\"},{\"lat\":\"91\",\"lon\":\"12\",\"display_name\":\"fuori intervallo\"}]"));
        Check(invalid.Count==0,"Coordinate non finite e fuori intervallo rifiutate");
        var report=new GeologicalReport{Id="geo",Municipality="Comune",Address="Via prova",PhysicalPath=@"\\ingtassinari.it\data\lavori\2026\esempio.zip",ArchiveEntry="interno/relazione.pdf"};
        Check(report.PositionLabel.Contains("Via prova")&&!report.HasPosition,"Indirizzo visibile anche senza coordinate");
        Check(report.ServerLocation.EndsWith(" > interno/relazione.pdf"),"Percorso server distingue archivio e documento interno");
        var command=GeologyViewModel.FolderStartInfo(report);
        Check(command.FileName=="explorer.exe"&&command.Arguments=="/select,\"Z:\\lavori\\2026\\esempio.zip\"","Apri percorso usa Z e seleziona il contenitore ZIP senza percorso interno");
        try{LocalReadOnlyPolicy.Validate(new Request("GeocodeGeologicalAddress"));throw new Exception("MCP non deve geocodificare automaticamente");}catch(UnauthorizedAccessException){}
        Check(true,"Geocodifica non esposta al canale MCP readonly");
    }
}
