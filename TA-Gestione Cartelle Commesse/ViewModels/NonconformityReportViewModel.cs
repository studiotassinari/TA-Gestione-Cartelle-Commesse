using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Text;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Fotografia dei dati già censiti dal server: aprire il report non legge le sorgenti.
public sealed class NonconformityReportViewModel : Observable, IDisposable
{
    private readonly ServerConnection? connection;
    private readonly CancellationTokenSource closing=new();
    private bool busy;
    private string coverage="";
    private List<NonconformityRow> all=[];
    private string status="Caricamento dal server...", summary="", filter="", category="Tutte";
    public NonconformityReportViewModel(ServerConnection? server=null)
    {
        connection=server;
        RefreshCommand=new(()=>_=LoadAsync(),()=>!busy && connection!=null);
        ExportCommand=new(Export,()=>!busy && HasSnapshot);
    }
    public ObservableCollection<NonconformityRow> Rows { get; }=[];
    public string[] Categories { get; }=["Tutte","Commesse","Gare","Archivio"];
    public string Category {get=>category;set{category=value;Notify();ApplyFilter();}}
    public string Filter {get=>filter;set{filter=value;Notify();ApplyFilter();}}
    public string Status {get=>status;private set{status=value;Notify();}}
    public string Summary {get=>summary;private set{summary=value;Notify();}}
    public bool HasSnapshot {get;private set;}
    public string CountText=>$"{Rows.Count} segnalazioni visualizzate su {all.Count}";
    public Command RefreshCommand {get;}
    public Command ExportCommand {get;}
    public async Task LoadAsync()
    {
        if(busy || connection==null || closing.IsCancellationRequested)return;
        busy=true;Commands();Status="Recupero delle fotografie dal server...";
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(closing.Token);timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var states=new List<CategoryState>();
            foreach(var code in new[]{"C","G","A"})states.Add(await connection.SendAsync<CategoryState>(new("State",code),timeout.Token));
            if(!closing.IsCancellationRequested)Apply(states);
        }
        catch(OperationCanceledException) when(closing.IsCancellationRequested) { }
        catch(Exception ex){Status="Report non aggiornato: "+ex.Message+(HasSnapshot?" Dati precedenti conservati.":" Nessuna verifica disponibile.");}
        finally{busy=false;Commands();}
    }
    public void Apply(IEnumerable<CategoryState> states)
    {
        var rows=new List<NonconformityRow>();var dates=new List<string>();int pending=0;
        foreach(var state in states)
        {
            var title=state.Prefix=='C'?"Commesse":state.Prefix=='G'?"Gare":"Archivio";
            if(state.Snapshot is not {} snapshot){dates.Add(title+": non disponibile");rows.Add(new(title,"","","Disponibilità","Nessun censimento disponibile",state.Root));continue;}
            dates.Add(title+": "+snapshot.SavedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm")+(state.Busy?" (scansione in corso)":""));
            foreach(var record in snapshot.Records)
            {
                if(record.Status!="Valido")rows.Add(new(title,record.Code,record.Client,"Denominazione","Nome non conforme: "+System.IO.Path.GetFileName(record.Path),record.Path));
                if(record.Inspection is not {} inspection){pending++;continue;}
                foreach(var issue in inspection.Issues)rows.Add(new(title,record.Code,record.Client,"Lettura",issue,record.Path));
                if(!inspection.Complete && inspection.Issues.Count==0)rows.Add(new(title,record.Code,record.Client,"Lettura","Verifica parziale",record.Path));
                if(inspection.QuoteStatus is "Non trovato nei nomi" or "Cartella amministrazione non trovata" or "Da verificare (nome ambiguo)")
                    rows.Add(new(title,record.Code,record.Client,"Preventivo da verificare",inspection.QuoteStatus+" — controllo basato solo sui nomi",record.Path));
            }
            foreach(var warning in snapshot.Warnings)
            {
                // Il dettaglio del nome è già presente nella riga della cartella, senza duplicarlo.
                if(warning.StartsWith("Nome ") && snapshot.Records.Any(r=>r.Status!="Valido" && warning.EndsWith(r.Path,StringComparison.OrdinalIgnoreCase)))continue;
                rows.Add(new(title,"","","Segnalazione",warning,state.Root));
            }
        }
        all=rows.Distinct().OrderBy(r=>r.Category).ThenBy(r=>r.Code).ThenBy(r=>r.Path).ThenBy(r=>r.Type).ToList();
        HasSnapshot=true;Summary=string.Join(" • ",dates);
        coverage=Status=$"{pending} cartelle ancora senza verifica dei file. I risultati descrivono nomi e metadati; non certificano contenuti o approvazioni.";
        ApplyFilter();Commands();
    }
    private void ApplyFilter()
    {
        Rows.Clear();foreach(var row in all.Where(r=>(category=="Tutte"||r.Category==category) &&
            string.Join(' ',r.Code,r.Client,r.Type,r.Detail,r.Path).Contains(filter,StringComparison.OrdinalIgnoreCase)))Rows.Add(row);
        Notify(nameof(CountText));
    }
    public string CreateHtml()
    {
        static string H(string s)=>WebUtility.HtmlEncode(s);
        var html=new StringBuilder("<!doctype html><html lang='it'><meta charset='utf-8'><title>Non conformità</title><style>body{font:14px Segoe UI;color:#192845;margin:28px}table{border-collapse:collapse;width:100%}td,th{padding:8px;border-bottom:1px solid #ccc;text-align:left;overflow-wrap:anywhere}</style><h1>Non conformità e verifiche</h1>");
        html.Append($"<p>{H(Summary)}</p><p>{H(coverage)}</p><p>{H(Status==coverage?"":Status)}</p><p>{H(CountText)}. Categoria: {H(Category)}; ricerca: {H(Filter)}</p><table><tr><th>Categoria</th><th>Codice</th><th>Committente</th><th>Tipo</th><th>Dettaglio</th><th>Percorso</th></tr>");
        foreach(var row in Rows)html.Append($"<tr><td>{H(row.Category)}</td><td>{H(row.Code)}</td><td>{H(row.Client)}</td><td>{H(row.Type)}</td><td>{H(row.Detail)}</td><td>{H(row.Path)}</td></tr>");
        return html.Append("</table></html>").ToString();
    }
    private void Export()
    {
        var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Report HTML (*.html)|*.html",FileName="Non-conformita-"+DateTime.Now.ToString("yyyy-MM-dd-HHmm")+".html"};
        if(dialog.ShowDialog()!=true)return;
        try{File.WriteAllText(dialog.FileName,CreateHtml(),new UTF8Encoding(false));Status="Report esportato: "+dialog.FileName;}
        catch(Exception ex){Status="Esportazione non riuscita: "+ex.Message;}
    }
    private void Commands(){RefreshCommand.Refresh();ExportCommand.Refresh();}
    public void Dispose(){closing.Cancel();closing.Dispose();}
}
