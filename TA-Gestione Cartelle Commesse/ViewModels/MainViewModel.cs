using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace TA_Gestione_Cartelle_Commesse.ViewModels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class Command(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
public sealed record FolderRecord(string Code, string Year, string Number, string Client, string Subject, string Path, string Status);
public sealed class FolderNode : Observable
{
    public string Name { get; }
    public string Path { get; }
    public ObservableCollection<FolderNode> Children { get; } = [];
    private bool expanded, loaded;
    public bool IsExpanded { get => expanded; set { expanded = value; Notify(); if (value && !loaded) _ = Load(); } }
    public FolderNode(string path, bool message = false)
    {
        Path = path; Name = message ? path : System.IO.Path.GetFileName(path); loaded = message;
        if (!message) Children.Add(new("Caricamento...", true));
    }
    private async Task Load()
    {
        loaded = true;
        try
        {
            var paths = await Task.Run(() => Directory.GetDirectories(Path).Where(p => !FolderReader.IsExcluded(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).OrderBy(p => p).ToArray());
            Children.Clear(); foreach (var path in paths) Children.Add(new(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Children.Clear(); Children.Add(new("Errore: " + ex.Message, true)); loaded = false; }
    }
}
public sealed class CategoryViewModel : Observable
{
    private readonly char prefix;
    private string path, filter = "", status;
    private List<FolderRecord> all = [];
    private bool busy;
    private CancellationTokenSource? cancellation;
    private FolderRecord? selected;
    public string Title { get; }
    public string RootPath { get => path; set { path = value; Notify(); } }
    public string Filter { get => filter; set { filter = value; Notify(); ApplyFilter(); } }
    public string Status { get => status; private set { status = value; Notify(); } }
    public bool IsIdle => !busy;
    public ObservableCollection<FolderRecord> Records { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<FolderNode> Hierarchy { get; } = [];
    public FolderRecord? Selected { get => selected; set { selected = value; Notify(); Hierarchy.Clear(); if (value != null) Hierarchy.Add(new(value.Path, prefix == 'C') { IsExpanded = prefix != 'C' }); } }
    public Command RefreshCommand { get; }
    public Command CancelCommand { get; }
    public CategoryViewModel(string title, string root, char code)
    {
        Title = title; path = root; prefix = code;
        status = code == 'C' ? "Premere Aggiorna elenco. Verranno analizzate solo le cartelle di primo livello dentro ogni anno (es. 2026), senza sottocartelle." : "Premere Aggiorna elenco. Cestini e copie storiche esclusi.";
        RefreshCommand = new(() => _ = Refresh(), () => !busy);
        CancelCommand = new(() => { cancellation?.Cancel(); Status = "Annullamento richiesto..."; }, () => busy);
    }
    private void ApplyFilter()
    {
        Records.Clear();
        foreach (var row in all.Where(r => (r.Code + " " + r.Client + " " + r.Subject).Contains(filter, StringComparison.OrdinalIgnoreCase))) Records.Add(row);
    }
    private async Task Refresh()
    {
        if (busy) return;
        busy = true; Notify(nameof(IsIdle)); RefreshCommand.Refresh(); CancelCommand.Refresh();
        using var source = new CancellationTokenSource(); cancellation = source;
        Status = "Lettura in corso..."; var root = path;
        try
        {
            var result = await Task.Run(() => FolderReader.Scan(root, prefix, source.Token));
            if (result.Cancelled || source.IsCancellationRequested) { Status = "Lettura annullata. Elenco precedente conservato."; return; }
            if (result.Error != null) { Status = "Lettura non riuscita: " + result.Error + ". Elenco precedente conservato."; return; }
            Selected = null; all = result.Records; ApplyFilter(); Warnings.Clear();
            foreach (var warning in result.Warnings) Warnings.Add(warning);
            Status = all.Count + " " + Title.ToLowerInvariant() + " | " + Warnings.Count + " segnalazioni | " + root + (prefix == 'C' ? " | Primo livello dentro ogni anno, senza sottocartelle" : " | Copie storiche e cestini esclusi");
        }
        catch (Exception ex) { Status = "Lettura non riuscita: " + ex.Message + ". Elenco precedente conservato."; }
        finally { busy = false; cancellation = null; Notify(nameof(IsIdle)); RefreshCommand.Refresh(); CancelCommand.Refresh(); }
    }
}
public sealed class MainViewModel : Observable
{
    public CategoryViewModel Commesse { get; } = new("Commesse", @"Z:\lavori", 'C');
    public CategoryViewModel Gare { get; } = new("Gare", @"Z:\gare", 'G');
    private bool reporting;
    private string reportStatus = "Report completo: commesse, gare e non conformita.";
    public string ReportStatus { get => reportStatus; private set { reportStatus = value; Notify(); } }
    public Command ReportCommand { get; }
    public MainViewModel() { ReportCommand = new(() => _ = CreateReport(), () => !reporting); }
    private async Task CreateReport()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Salva report cartelle e non conformita", Filter = "Report HTML (*.html)|*.html", FileName = "Report-cartelle-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".html", AddExtension = true, DefaultExt = ".html" };
        if (dialog.ShowDialog() != true) return;
        reporting = true; ReportCommand.Refresh(); ReportStatus = "Creazione report: lettura aggiornata delle cartelle...";
        var jobsRoot = Commesse.RootPath; var bidsRoot = Gare.RootPath;
        try
        {
            var summary = await Task.Run(() => {
                var jobs = FolderReader.Scan(jobsRoot, 'C', CancellationToken.None);
                var bids = FolderReader.Scan(bidsRoot, 'G', CancellationToken.None);
                if (jobs.Error != null || bids.Error != null) throw new IOException("Report non creato: " + jobs.Error + " " + bids.Error);
                var html = ReportWriter.Create(jobsRoot, jobs, bidsRoot, bids);
                File.WriteAllText(dialog.FileName, html, new System.Text.UTF8Encoding(false));
                return jobs.Records.Count + " commesse, " + bids.Records.Count + " gare; " + (jobs.Warnings.Count + bids.Warnings.Count) + " segnalazioni";
            });
            ReportStatus = "Report salvato: " + dialog.FileName;
            System.Windows.MessageBox.Show(summary + "\n\n" + dialog.FileName, "Report creato", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex) { ReportStatus = "Report non creato: " + ex.Message; System.Windows.MessageBox.Show(ReportStatus, "Errore report", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); }
        finally { reporting = false; ReportCommand.Refresh(); }
    }
}
public sealed record ScanResult(List<FolderRecord> Records, List<string> Warnings, bool Cancelled = false, string? Error = null);
public static class FolderReader
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "@Recycle", "@Recently-Snapshot", "#recycle", "$RECYCLE.BIN", "System Volume Information", ".snapshot" };
    public static bool IsExcluded(string path) => Excluded.Contains(System.IO.Path.GetFileName(path));
    public static ScanResult Scan(string root, char prefix, CancellationToken token)
    {
        var records = new List<FolderRecord>(); var warnings = new List<string>();
        var pending = new Stack<string>();
        if (token.IsCancellationRequested) return new([], [], true);
        if (prefix == 'C')
        {
            try
            {
                foreach (var yearFolder in Directory.EnumerateDirectories(root))
                {
                    if (token.IsCancellationRequested) return new([], [], true);
                    if (!Regex.IsMatch(System.IO.Path.GetFileName(yearFolder), @"^(19|20)[0-9]{2}$")) continue;
                    if ((File.GetAttributes(yearFolder) & FileAttributes.ReparsePoint) == 0) pending.Push(yearFolder);
                }
                if (pending.Count == 0) warnings.Add("Nessuna cartella annuale principale trovata (nome atteso: quattro cifre, es. 2026).");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new([], [], Error: ex.Message); }
        }
        else pending.Push(root);
        var pattern = new Regex(@"^(?<code>[CG](?<year>\d{2})(?<number>\d{3}))\s*-\s*(?<client>.+?)\s+-\s+(?<subject>.+)$", RegexOptions.IgnoreCase);
        while (pending.TryPop(out var current))
        {
            if (token.IsCancellationRequested) return new([], [], true);
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    if (token.IsCancellationRequested) return new([], [], true);
                    if (IsExcluded(directory)) continue;
                    try
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { warnings.Add("Collegamento escluso: " + directory); continue; }
                        var name = System.IO.Path.GetFileName(directory); var match = pattern.Match(name);
                        if (match.Success && char.ToUpperInvariant(name[0]) == prefix)
                        {
                            records.Add(new(match.Groups["code"].Value.ToUpperInvariant(), match.Groups["year"].Value, match.Groups["number"].Value, match.Groups["client"].Value.Trim(), match.Groups["subject"].Value.Trim(), directory, "Valido"));
                            continue;
                        }
                        if (Regex.IsMatch(name, "^[CG][0-9]{5}", RegexOptions.IgnoreCase))
                        {
                            if (char.ToUpperInvariant(name[0]) != prefix) { warnings.Add("Codice di altra categoria escluso: " + directory); continue; }
                            records.Add(new(name[..6].ToUpperInvariant(), name.Substring(1, 2), name.Substring(3, 3), "Da verificare", name, directory, "Non conforme"));
                            warnings.Add("Nome non conforme: " + directory); continue;
                        }
                        if (prefix == 'C')
                        {
                            records.Add(new("", System.IO.Path.GetFileName(current)[2..], "", "Da verificare", name, directory, "Non conforme"));
                            warnings.Add("Nome non conforme (codice commessa non riconosciuto): " + directory);
                        }
                        else pending.Push(directory);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(directory + ": " + ex.Message); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { if (current == root) return new([], [], Error: ex.Message); warnings.Add(current + ": " + ex.Message); }
        }
        return new(records.OrderByDescending(r => r.Year).ThenByDescending(r => r.Number).ToList(), warnings);
    }
}

public static class ReportWriter
{
    private static string H(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
    public static string Create(string jobsRoot, ScanResult jobs, string bidsRoot, ScanResult bids)
    {
        var html = new System.Text.StringBuilder();
        html.Append("<!doctype html><html lang='it'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title>TA - Report cartelle e non conformita</title><style>body{font:15px/1.5 Segoe UI,Arial,sans-serif;color:#203247;background:#f2f5f9;margin:0}main{max-width:1400px;margin:auto;background:white;padding:40px}h1{font-size:30px;margin:0}h2{border-bottom:2px solid #224c70;padding-bottom:8px;margin-top:36px}h3{margin-top:28px}p{max-width:1000px}.meta{color:#53677a}.summary{padding:18px;background:#eaf1f7;border-left:5px solid #224c70}.warning{background:#fff1d6}.path{font-size:12px;color:#526477;overflow-wrap:anywhere}table{border-collapse:collapse;width:100%;margin:14px 0 28px;table-layout:fixed}th,td{text-align:left;vertical-align:top;border:1px solid #d5dde6;padding:9px;overflow-wrap:anywhere}th{background:#224c70;color:white}tr:nth-child(even){background:#f6f8fa}tr.bad{background:#fff1d6}a{color:#224c70}thead{display:table-header-group}@media print{body{background:white}main{max-width:none;padding:0;font-size:10pt}tr{break-inside:avoid}h2,h3{break-after:avoid}@page{size:A4 landscape;margin:12mm}</style></head><body><main>");
        html.Append("<h1>TA - Gestione Cartelle Commesse</h1><p class='meta'>Report delle cartelle e delle non conformita | " + H(DateTime.Now.ToString("dd/MM/yyyy HH:mm")) + "</p>");
        html.Append("<div class='summary'><strong>" + jobs.Records.Count + " commesse · " + bids.Records.Count + " gare</strong><br>" + jobs.Records.Concat(bids.Records).Count(r => r.Status != "Valido") + " nomi non conformi. " + (jobs.Warnings.Count + bids.Warnings.Count) + " segnalazioni complessive.</div>");
        html.Append("<p>Commesse: analisi delle sole cartelle di primo livello dentro ogni cartella annuale principale (1900–2099). Nessuna scansione dei livelli successivi. Gare: ricerca nella radice selezionata. Cestini, copie storiche e collegamenti esclusi. Nessuna cartella e stata modificata. Ordine: anno e codice crescenti, quindi cliente.</p><p>Formato atteso: <strong>CXXYYY - Cliente - Argomento</strong>, oppure <strong>GXXYYY - Cliente - Argomento</strong>. XX indica l'anno a due cifre; YYY il progressivo. Il controllo riguarda il formato del nome, non la correttezza amministrativa dei dati. Per i nomi non conformi, cliente e argomento non vengono dedotti.</p><nav><a href='#commesse'>Commesse</a> | <a href='#gare'>Gare</a></nav>");
        Section(html, "commesse", "Commesse", jobsRoot, jobs); Section(html, "gare", "Gare", bidsRoot, bids);
        html.Append("<p class='meta'>Fine report. Le segnalazioni di accesso indicano possibili letture parziali. Le sottocartelle delle commesse non vengono lette. Le sottocartelle delle gare sono esplorabili nell'applicazione e non sono soggette a questo controllo di denominazione.</p></main></body></html>");
        return html.ToString();
    }
    private static void Section(System.Text.StringBuilder html, string id, string title, string root, ScanResult data)
    {
        html.Append("<h2 id='" + id + "'>" + H(title) + "</h2><p class='meta'>Radice: " + H(root) + "</p>");
        var ordered = data.Records.OrderBy(r => r.Year).ThenBy(r => r.Code).ThenBy(r => r.Client).ThenBy(r => r.Path).ToList();
        html.Append("<h3>Non conformita e segnalazioni (" + data.Warnings.Count + ")</h3>");
        if (data.Warnings.Count == 0) html.Append("<p>Nessuna segnalazione.</p>");
        else { html.Append("<table><thead><tr><th style='width:50px'>N.</th><th>Segnalazione e cartella interessata</th></tr></thead><tbody>"); int n = 0; foreach (var warning in data.Warnings.OrderBy(w => w, StringComparer.CurrentCultureIgnoreCase)) html.Append("<tr class='bad'><td>" + (++n) + "</td><td>" + H(warning) + "</td></tr>"); html.Append("</tbody></table>"); }
        html.Append("<h3>Elenco ordinato delle cartelle (" + ordered.Count + ")</h3><table><thead><tr><th style='width:85px'>Codice</th><th style='width:45px'>Anno XX</th><th style='width:20%'>Cliente</th><th>Argomento e percorso</th><th style='width:100px'>Esito</th></tr></thead><tbody>");
        foreach (var row in ordered)
            html.Append("<tr" + (row.Status == "Valido" ? "" : " class='bad'") + "><td>" + H(row.Code) + "</td><td>" + H(row.Year) + "</td><td>" + H(row.Client) + "</td><td>" + H(row.Subject) + "<div class='path'>" + H(row.Path) + "</div></td><td>" + H(row.Status) + "</td></tr>");
        html.Append("</tbody></table>");
    }
}
