using System.Collections.ObjectModel;
using System.Text;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

public sealed class IpripiViewModel : Observable, IDisposable
{
    private readonly Func<IpripiQuery, CancellationToken, Task<IpripiPage>>? load;
    private readonly CancellationTokenSource lifetime = new();
    private bool busy, allPdfs, opening, disposed, recognizedOnly;
    private string code = "", search = "", status = "Cerca una commessa o consulta i candidati MUR censiti.";
    private IpripiDocument? selected;
    public IpripiViewModel(Func<IpripiQuery, CancellationToken, Task<IpripiPage>>? load)
    {
        this.load = load;
        RefreshCommand = new(async () => await LoadAsync(), () => !busy && load != null);
        OpenCommand = new(async () => await OpenAsync(), () => Selected?.OriginalAvailable == true && !opening);
        if (load == null) status = "Server non collegato. Riaprire questa finestra dopo la connessione.";
    }
    public ObservableCollection<IpripiDocument> Rows { get; } = [];
    public Command RefreshCommand { get; }
    public Command OpenCommand { get; }
    public string Code { get => code; set { code = value; Notify(); } }
    public string Search { get => search; set { search = value; Notify(); } }
    public bool AllPdfs { get => allPdfs; set { allPdfs = value; Notify(); } }
    public bool RecognizedOnly { get => recognizedOnly; set { recognizedOnly = value; Notify(); } }
    public string Status { get => status; private set { status = value; Notify(); } }
    public string CountText => $"{Rows.Count} documenti · {Rows.Count(r => r.AnalysisCurrent)} analisi aggiornate ai metadati censiti";
    public IpripiDocument? Selected
    {
        get => selected;
        set { selected = value; Notify(); Notify(nameof(Details)); OpenCommand.Refresh(); }
    }
    public string Details
    {
        get
        {
            if (Selected == null) return "Seleziona un documento per vedere interventi, motivazioni e citazioni.";
            var row = Selected; var text = new StringBuilder();
            text.AppendLine(row.FileName).AppendLine("Commessa: " + (row.ProjectCode.Length == 0 ? "Non attribuita" : row.ProjectCode))
                .AppendLine("Progettisti: " + (row.Designers.Length == 0 ? "Non ancora identificati" : row.Designers))
                .AppendLine("Indirizzo: " + row.Address + " · " + row.Municipality)
                .AppendLine("Fonte posizione: " + row.Saved?.Analysis.Position?.Source)
                .AppendLine(row.Status).AppendLine(row.Location).AppendLine();
            if (row.Saved is not { } saved) return text.AppendLine("Nessuna analisi salvata. Copia la richiesta e inviala a ChatGPT con il plugin TA – Gestione Commesse.").ToString();
            text.AppendLine($"Revisione {saved.Revision} · {saved.SavedUtc.ToLocalTime():dd/MM/yyyy HH:mm} · {saved.SavedBy}")
                .AppendLine("Modulo: " + saved.Analysis.FormType).AppendLine(saved.Analysis.Summary);
            if (!row.AnalysisCurrent) text.AppendLine("ATTENZIONE: risultati storici; il documento non è più censito oppure i metadati sono cambiati.");
            foreach (var intervention in saved.Analysis.Interventions)
            {
                text.AppendLine().AppendLine($"{intervention.RuleCode} {intervention.Level} · {intervention.Basis}")
                    .AppendLine(intervention.Description).AppendLine(intervention.Reason);
                foreach (var missing in intervention.MissingData) text.AppendLine("Da verificare: " + missing);
                foreach (var evidence in intervention.Evidence) text.AppendLine($"Citazione (offset {evidence.Offset}): «{evidence.Quote}»");
            }
            text.AppendLine().AppendLine("Prove di riconoscimento del modulo / esito:");
            foreach (var designer in saved.Analysis.Designers ?? [])
                foreach (var evidence in designer.Evidence) text.AppendLine($"{designer.Name} · {designer.Role} · offset {evidence.Offset}: «{evidence.Quote}»");
            foreach (var evidence in saved.Analysis.Position?.Evidence ?? []) text.AppendLine($"Ubicazione · offset {evidence.Offset}: «{evidence.Quote}»");
            foreach (var evidence in saved.Analysis.FormEvidence) text.AppendLine($"Offset {evidence.Offset}: «{evidence.Quote}»");
            foreach (var warning in saved.Analysis.Warnings) text.AppendLine("• " + warning);
            return text.ToString();
        }
    }
    public string ChatRequest()
    {
        if (Selected == null) throw new InvalidOperationException("Seleziona prima un PDF da analizzare.");
        var row = Selected;
        return $"Analizza e salva nel catalogo IPRiPI il PDF MUR A.1/D.1 selezionato della commessa {row.ProjectCode}: {row.FileName}. " +
            $"ID documento: {row.Id}. Usa il plugin TA – Gestione Commesse: inizia_consultazione con contenuti=true e analisiIpripi=true, " +
            "cerca_mur_ipripi, regole_ipripi, leggi_mur_ipripi e salva_analisi_ipripi. " +
            "Individua gli interventi secondo la DGR Emilia-Romagna 2272/2016, Allegato 1. " +
            "Distingui codici dichiarati, dedotti e da verificare; cita il testo con offset, specifica livelli L0/L1/L2, requisiti mancanti, spunte non leggibili e limiti. " +
            "Non trattare opzioni prestampate come interventi selezionati. Non leggere altri documenti automaticamente. Non modificare gli originali.";
    }
    public async Task LoadAsync()
    {
        if (load == null || busy || lifetime.IsCancellationRequested) return;
        busy = true; RefreshCommand.Refresh(); Status = "Consultazione dell'inventario e delle analisi salvate...";
        try
        {
            var query = new IpripiQuery(Code.Trim(), Search.Trim(), AllPdfs, RecognizedOnly: RecognizedOnly);
            var rows = new List<IpripiDocument>(); IpripiPage? page = null;
            do
            {
                page = await load(query with { Offset = page?.NextOffset ?? 0, Version = page?.Version }, lifetime.Token);
                rows.AddRange(page.Documents);
                if (rows.Count > 50000 || page.NextOffset.HasValue && page.NextOffset <= page.Offset) throw new System.IO.InvalidDataException("Paginazione IPRiPI non valida.");
            } while (page.NextOffset.HasValue);
            lifetime.Token.ThrowIfCancellationRequested();
            var id = Selected?.Id; Rows.Clear(); foreach (var row in rows) Rows.Add(row);
            Selected = Rows.FirstOrDefault(r => r.Id == id); Notify(nameof(CountText));
            Status = string.Join(" · ", page.Warnings);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Status = "Aggiornamento non riuscito: " + ex.Message + " Dati precedenti conservati."; }
        finally { busy = false; RefreshCommand.Refresh(); }
    }
    private async Task OpenAsync()
    {
        if (Selected is not { OriginalAvailable: true } row || opening) return;
        opening = true; OpenCommand.Refresh();
        try
        {
            var source = new GeologicalReport { Id = row.Id, FileName = row.FileName, PhysicalPath = row.PhysicalPath, ArchiveEntry = row.ArchiveEntry };
            var path = await Services.GeologyDocumentCopy.PrepareAsync(source, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            Status = "Copia locale in sola lettura aperta per verificare anche caselle e annotazioni.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Status = "Documento non aperto: " + ex.Message; }
        finally { opening = false; OpenCommand.Refresh(); }
    }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); lifetime.Dispose(); }
}
