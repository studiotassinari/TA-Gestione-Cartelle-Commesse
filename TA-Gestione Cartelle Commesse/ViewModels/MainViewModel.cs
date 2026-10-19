// Importa le raccolte che notificano alla vista aggiunte e rimozioni.
using System.Collections.ObjectModel;
// Importa il contratto e gli eventi di notifica delle proprietà.
using System.ComponentModel;
// Importa le operazioni su file, cartelle e flussi e le relative eccezioni.
using System.IO;
// Importa CallerMemberName per ricavare automaticamente il nome della proprietà.
using System.Runtime.CompilerServices;
// Importa ICommand, il contratto WPF dei comandi associati ai controlli.
using System.Windows.Input;
// Importa le espressioni regolari usate per interpretare anni e codici.
using System.Text.RegularExpressions;

// Raggruppa i tipi usati dal modello di presentazione dell'applicazione.
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Fornisce alle classi derivate la notifica dei cambiamenti richiesta dai binding WPF.
public abstract class Observable : INotifyPropertyChanged
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Espone l'evento ascoltato dalla vista quando cambia una proprietà.
    public event PropertyChangedEventHandler? PropertyChanged;
    // Notifica gli ascoltatori, usando il nome del chiamante se non specificato esplicitamente.
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
// Fine del blocco di istruzioni o della definizione corrente.
}
// Adatta un'azione e una condizione facoltativa al contratto ICommand.
public sealed class Command(Action execute, Func<bool>? canExecute = null) : ICommand
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Segnala alla vista quando deve rivalutare l'abilitazione del comando.
    public event EventHandler? CanExecuteChanged;
    // Consente l'esecuzione se la condizione è vera; senza condizione il comando è abilitato.
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    // Ricontrolla l'abilitazione prima di invocare l'azione associata.
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    // Richiede ai controlli collegati di aggiornare il proprio stato abilitato/disabilitato.
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
// Fine del blocco di istruzioni o della definizione corrente.
}
// Rappresenta una cartella con codice, anno a due cifre, progressivo, cliente, argomento, percorso ed esito.
public sealed record FolderRecord(string Code, string Year, string Number, string Client, string Subject, string Path, string Status);
// Rappresenta un nodo della gerarchia, caricando i figli soltanto quando viene espanso.
public sealed class FolderNode : Observable
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Espone il nome visualizzato oppure il testo di un messaggio informativo.
    public string Name { get; }
    // Conserva il percorso del nodo, utilizzato anche come suggerimento nella vista.
    public string Path { get; }
    // Mantiene i nodi figli e notifica automaticamente le modifiche al TreeView.
    public ObservableCollection<FolderNode> Children { get; } = [];
    // Memorizza lo stato di espansione e se il caricamento è già stato effettuato.
    private bool expanded, loaded;
    // Notifica l'espansione e avvia il caricamento asincrono solo per nodi non ancora caricati.
    public bool IsExpanded { get => expanded; set { expanded = value; Notify(); if (value && !loaded) _ = Load(); } }
    // Costruisce un nodo di cartella oppure, con message, un nodo puramente informativo.
    public FolderNode(string path, bool message = false)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Inizializza percorso e nome; considera già caricati i messaggi per evitare accessi al filesystem.
        Path = path; Name = message ? path : System.IO.Path.GetFileName(path); loaded = message;
        // Inserisce un figlio provvisorio affinché WPF mostri il controllo di espansione.
        if (!message) Children.Add(new("Caricamento...", true));
    // Fine del blocco di istruzioni o della definizione corrente.
    }
    // Carica le sottocartelle di un nodo esplorabile senza bloccare il thread della vista.
    private async Task Load()
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Segna il caricamento come avviato per evitare richieste duplicate durante l'attesa.
        loaded = true;
        // Protegge la lettura dagli errori di rete, filesystem e autorizzazione gestiti sotto.
        try
        // Inizio del blocco di istruzioni o della definizione precedente.
        {
            // Legge in background i figli diretti, esclude cartelle speciali e collegamenti, quindi ordina i percorsi.
            var paths = await Task.Run(() => Directory.GetDirectories(Path).Where(p => !FolderReader.IsExcluded(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).OrderBy(p => p).ToArray());
            // Sostituisce il segnaposto con i nodi effettivi dopo il ritorno al thread della vista.
            Children.Clear(); foreach (var path in paths) Children.Add(new(path));
        // Fine del blocco di istruzioni o della definizione corrente.
        }
        // Intercetta soltanto gli errori previsti di accesso alle cartelle.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        // Mostra l'errore come nodo e permette un nuovo tentativo alla successiva espansione.
        { Children.Clear(); Children.Add(new("Errore: " + ex.Message, true)); loaded = false; }
    // Fine del blocco di istruzioni o della definizione corrente.
    }
// Fine del blocco di istruzioni o della definizione corrente.
}
// Gestisce percorso, elenco, filtro, selezione e comandi di una categoria di cartelle.
public sealed class CategoryViewModel : Observable
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Conserva il prefisso C per le commesse oppure G per le gare.
    private readonly char prefix;
    // Conserva il percorso radice, il filtro inizialmente vuoto e il messaggio di stato.
    private string path, filter = "", status;
    // Mantiene tutti i risultati dell'ultima scansione, indipendentemente dal filtro visibile.
    private List<FolderRecord> all = [];
    // Indica se una lettura è in corso per impedire scansioni concorrenti nella stessa categoria.
    private bool busy;
    // Consente al comando Annulla di richiedere l'interruzione della lettura corrente.
    private CancellationTokenSource? cancellation;
    // Conserva la cartella selezionata nella tabella.
    private FolderRecord? selected;
    // Espone il titolo della categoria inizializzato dal costruttore.
    public string Title { get; }
    // Aggiorna e notifica il percorso radice digitato dall'utente.
    public string RootPath { get => path; set { path = value; Notify(); } }
    // Aggiorna il testo cercato e applica subito il filtro ai risultati già letti.
    public string Filter { get => filter; set { filter = value; Notify(); ApplyFilter(); } }
    // Espone lo stato della lettura, modificabile soltanto da questo ViewModel.
    public string Status { get => status; private set { status = value; Notify(); } }
    // Abilita la modifica del percorso quando non è in corso una scansione.
    public bool IsIdle => !busy;
    // Espone alla tabella la raccolta dei risultati che soddisfano il filtro.
    public ObservableCollection<FolderRecord> Records { get; } = [];
    // Espone le segnalazioni di accesso e i nomi non conformi.
    public ObservableCollection<string> Warnings { get; } = [];
    // Espone il nodo radice della gerarchia associata alla selezione corrente.
    public ObservableCollection<FolderNode> Hierarchy { get; } = [];
    // Espande la cartella selezionata per entrambe le categorie; i figli sono solo nodi esplorabili, non nuove commesse.
    public FolderRecord? Selected { get => selected; set { selected = value; Notify(); OpenFolderCommand.Refresh(); Hierarchy.Clear(); if (value != null) Hierarchy.Add(new(value.Path) { IsExpanded = true }); } }
    // Espone il comando di aggiornamento dell'elenco.
    public Command RefreshCommand { get; }
    // Espone il comando di annullamento della scansione.
    public Command CancelCommand { get; }
    // Espone il comando condiviso da menu Apri, menu contestuale e doppio clic.
    public Command OpenFolderCommand { get; }
    // Inizializza la categoria con titolo, percorso iniziale e prefisso atteso.
    public CategoryViewModel(string title, string root, char code)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Memorizza i parametri ricevuti dal ViewModel principale.
        Title = title; path = root; prefix = code;
        // Abilita l'apertura della cartella soltanto quando esiste una selezione.
        OpenFolderCommand = new(OpenFolder, () => Selected != null);
        // Descrive all'utente l'ambito della scansione previsto per questa categoria.
        status = code == 'C' ? "Premere Aggiorna elenco. Identificazione e verifica solo al primo livello dentro ogni anno (es. 2026). Selezionare una commessa per esplorarne le sottocartelle." : "Premere Aggiorna elenco. Cestini e copie storiche esclusi.";
        // Avvia l'aggiornamento asincrono solo quando non è già in corso una lettura.
        RefreshCommand = new(() => _ = Refresh(), () => !busy);
        // Richiede l'annullamento cooperativo e aggiorna lo stato; il comando è attivo solo durante la lettura.
        CancelCommand = new(() => { cancellation?.Cancel(); Status = "Annullamento richiesto..."; }, () => busy);
    // Fine del blocco di istruzioni o della definizione corrente.
    }
    // Apre la cartella selezionata tramite la shell di Windows.
    private void OpenFolder()
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Ricava il percorso dalla selezione, che potrebbe essere assente.
        var folder = Selected?.Path;
        // Termina senza operazioni se non è selezionata alcuna cartella.
        if (folder == null) return;
        // Protegge la verifica e l'apertura del percorso dagli errori gestiti sotto.
        try
        // Inizio del blocco di istruzioni o della definizione precedente.
        {
            // Verifica che il percorso esista e prepara un messaggio utile se la rete o la cartella non sono disponibili.
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Cartella non disponibile. Verificare il percorso e la connessione alla rete.");
            // Delega alla shell Windows l'apertura della cartella in Esplora file.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        // Fine del blocco di istruzioni o della definizione corrente.
        }
        // Gestisce gli errori di accesso, filesystem o avvio della shell previsti da questa operazione.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        // Inizio del blocco di istruzioni o della definizione precedente.
        {
            // Mostra l'errore e il percorso interessato in una finestra di avviso.
            System.Windows.MessageBox.Show(ex.Message + "\n\n" + folder, "Impossibile aprire la cartella", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        // Fine del blocco di istruzioni o della definizione corrente.
        }
    // Fine del blocco di istruzioni o della definizione corrente.
    }
    // Ricostruisce l'elenco visibile partendo dai risultati completi già in memoria.
    private void ApplyFilter()
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Azzera la selezione prima di modificare le righe della tabella.
        Selected = null;
        // Rimuove le righe del filtro precedente dalla raccolta visibile.
        Records.Clear();
        // Cerca senza distinguere maiuscole e minuscole in codice, cliente e argomento e inserisce le corrispondenze.
        foreach (var row in all.Where(r => (r.Code + " " + r.Client + " " + r.Subject).Contains(filter, StringComparison.OrdinalIgnoreCase))) Records.Add(row);
    // Fine del blocco di istruzioni o della definizione corrente.
    }
    // Esegue una nuova scansione asincrona preservando i risultati precedenti in caso di errore o annullamento.
    private async Task Refresh()
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Ignora richieste aggiuntive mentre una scansione è già in corso.
        if (busy) return;
        // Segna la categoria occupata e aggiorna proprietà e comandi collegati alla vista.
        busy = true; Notify(nameof(IsIdle)); RefreshCommand.Refresh(); CancelCommand.Refresh();
        // Crea una sorgente di annullamento limitata a questa scansione e la rende disponibile al comando Annulla.
        using var source = new CancellationTokenSource(); cancellation = source;
        // Mostra l'avanzamento e acquisisce una copia del percorso da usare nel lavoro in background.
        Status = "Lettura in corso..."; var root = path;
        // Protegge l'attesa e l'applicazione dei risultati per ripristinare sempre lo stato della vista.
        try
        // Inizio del blocco di istruzioni o della definizione precedente.
        {
            // Esegue l'accesso al filesystem su un thread di lavoro, passando il token di annullamento.
            var result = await Task.Run(() => FolderReader.Scan(root, prefix, source.Token));
            // Scarta una scansione annullata e mantiene l'elenco precedente.
            if (result.Cancelled || source.IsCancellationRequested) { Status = "Lettura annullata. Elenco precedente conservato."; return; }
            // Scarta una scansione con errore principale e mantiene i risultati precedenti.
            if (result.Error != null) { Status = "Lettura non riuscita: " + result.Error + ". Elenco precedente conservato."; return; }
            // Sostituisce i dati completi, riapplica il filtro e svuota le vecchie segnalazioni.
            Selected = null; all = result.Records; ApplyFilter(); Warnings.Clear();
            // Trasferisce le segnalazioni della nuova scansione alla raccolta osservabile.
            foreach (var warning in result.Warnings) Warnings.Add(warning);
            // Riepiloga quantità, segnalazioni, percorso e profondità della scansione eseguita.
            Status = all.Count + " " + Title.ToLowerInvariant() + " | " + Warnings.Count + " segnalazioni | " + root + (prefix == 'C' ? " | Verifica al primo livello dentro ogni anno; sottocartelle esplorabili nella gerarchia" : " | Copie storiche e cestini esclusi");
        // Fine del blocco di istruzioni o della definizione corrente.
        }
        // Comunica eventuali eccezioni della lettura senza propagare l'errore alla vista.
        catch (Exception ex) { Status = "Lettura non riuscita: " + ex.Message + ". Elenco precedente conservato."; }
        // Libera sempre lo stato occupato, rimuove la sorgente corrente e aggiorna i comandi.
        finally { busy = false; cancellation = null; Notify(nameof(IsIdle)); RefreshCommand.Refresh(); CancelCommand.Refresh(); }
    // Fine del blocco di istruzioni o della definizione corrente.
    }
// Fine del blocco di istruzioni o della definizione corrente.
}
// Coordina le due categorie, la scheda attiva e la creazione del report complessivo.
public sealed class MainViewModel : Observable
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Inizializza le commesse sulla radice aziendale Z:\lavori con prefisso C.
    public CategoryViewModel Commesse { get; } = new("Commesse", @"Z:\lavori", 'C');
    // Inizializza le gare sulla radice aziendale Z:\gare con prefisso G.
    public CategoryViewModel Gare { get; } = new("Gare", @"Z:\gare", 'G');
    // Memorizza l'indice della scheda selezionata, inizialmente zero per le commesse.
    private int activeTabIndex;
    // Notifica il cambio di scheda e aggiorna anche il binding della categoria attiva.
    public int ActiveTabIndex { get => activeTabIndex; set { activeTabIndex = value; Notify(); Notify(nameof(ActiveCategory)); } }
    // Fornisce al menu Apri la categoria della scheda corrente.
    public CategoryViewModel ActiveCategory => ActiveTabIndex == 1 ? Gare : Commesse;
    // Impedisce l'avvio contemporaneo di più esportazioni.
    private bool reporting;
    // Imposta il messaggio iniziale che descrive il contenuto del report.
    private string reportStatus = "Report completo: commesse, gare e non conformita.";
    // Espone e notifica lo stato dell'esportazione.
    public string ReportStatus { get => reportStatus; private set { reportStatus = value; Notify(); } }
    // Espone il comando associato alla voce Crea report del menu Export.
    public Command ReportCommand { get; }
    // Collega il comando alla creazione asincrona e lo disabilita durante un'esportazione.
    public MainViewModel() { ReportCommand = new(() => _ = CreateReport(), () => !reporting); }
    // Chiede la destinazione, rilegge le cartelle e salva il report HTML.
    private async Task CreateReport()
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Configura la finestra Salva con filtro HTML, estensione automatica e nome contenente data e ora.
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Salva report cartelle e non conformita", Filter = "Report HTML (*.html)|*.html", FileName = "Report-cartelle-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".html", AddExtension = true, DefaultExt = ".html" };
        // Interrompe l'esportazione se l'utente non conferma il salvataggio.
        if (dialog.ShowDialog() != true) return;
        // Disabilita ulteriori esportazioni e comunica l'inizio della nuova lettura.
        reporting = true; ReportCommand.Refresh(); ReportStatus = "Creazione report: lettura aggiornata delle cartelle...";
        // Acquisisce i percorsi delle due categorie prima di avviare il lavoro in background.
        var jobsRoot = Commesse.RootPath; var bidsRoot = Gare.RootPath;
        // Gestisce come un'unica operazione la scansione, la generazione e il salvataggio.
        try
        // Inizio del blocco di istruzioni o della definizione precedente.
        {
            // Esegue le operazioni di lettura e scrittura fuori dal thread grafico e ne attende il riepilogo.
            var summary = await Task.Run(() => {
                // Rilegge le commesse senza token di annullamento, rispettando il limite sotto le cartelle annuali.
                var jobs = FolderReader.Scan(jobsRoot, 'C', CancellationToken.None);
                // Rilegge le gare senza token di annullamento usando le regole della categoria G.
                var bids = FolderReader.Scan(bidsRoot, 'G', CancellationToken.None);
                // Impedisce la creazione del report se almeno una radice ha prodotto un errore principale.
                if (jobs.Error != null || bids.Error != null) throw new IOException("Report non creato: " + jobs.Error + " " + bids.Error);
                // Genera il documento HTML completo a partire dalle due scansioni.
                var html = ReportWriter.Create(jobsRoot, jobs, bidsRoot, bids);
                // Salva il documento nella destinazione scelta usando UTF-8 senza BOM.
                File.WriteAllText(dialog.FileName, html, new System.Text.UTF8Encoding(false));
                // Restituisce i conteggi di commesse, gare e segnalazioni per il messaggio conclusivo.
                return jobs.Records.Count + " commesse, " + bids.Records.Count + " gare; " + (jobs.Warnings.Count + bids.Warnings.Count) + " segnalazioni";
            // Chiude il lavoro in background e attende il risultato prima di aggiornare la vista.
            });
            // Mostra nella finestra principale il percorso del report salvato.
            ReportStatus = "Report salvato: " + dialog.FileName;
            // Conferma il salvataggio mostrando riepilogo e destinazione.
            System.Windows.MessageBox.Show(summary + "\n\n" + dialog.FileName, "Report creato", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        // Fine del blocco di istruzioni o della definizione corrente.
        }
        // Comunica un errore di esportazione sia nello stato sia in una finestra dedicata.
        catch (Exception ex) { ReportStatus = "Report non creato: " + ex.Message; System.Windows.MessageBox.Show(ReportStatus, "Errore report", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); }
        // Riabilita sempre il comando di esportazione al termine dell'operazione.
        finally { reporting = false; ReportCommand.Refresh(); }
    // Fine del blocco di istruzioni o della definizione corrente.
    }
// Fine del blocco di istruzioni o della definizione corrente.
}
// Raccoglie risultati, segnalazioni e gli eventuali stati di annullamento o errore principale.
public sealed record ScanResult(List<FolderRecord> Records, List<string> Warnings, bool Cancelled = false, string? Error = null);
// Implementa la lettura delle cartelle e la verifica delle convenzioni di denominazione.
public static class FolderReader
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Definisce cestini, snapshot e directory di sistema da escludere senza distinguere maiuscole e minuscole.
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "@Recycle", "@Recently-Snapshot", "#recycle", "$RECYCLE.BIN", "System Volume Information", ".snapshot" };
    // Verifica l'esclusione confrontando soltanto l'ultimo componente del percorso.
    public static bool IsExcluded(string path) => Excluded.Contains(System.IO.Path.GetFileName(path));
    // Scansiona una radice per categoria e controlla periodicamente il token di annullamento.
    public static ScanResult Scan(string root, char prefix, CancellationToken token)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Prepara gli elenchi separati delle cartelle trovate e delle segnalazioni.
        var records = new List<FolderRecord>(); var warnings = new List<string>();
        // Usa una pila per tenere traccia delle directory ancora da esaminare.
        var pending = new Stack<string>();
        // Restituisce subito un risultato annullato se la richiesta era già stata interrotta.
        if (token.IsCancellationRequested) return new([], [], true);
        // Applica alle sole commesse la selezione preliminare delle cartelle annuali.
        if (prefix == 'C')
        // Inizio del blocco di istruzioni o della definizione precedente.
        {
            // Gestisce gli errori durante l'accesso iniziale alla radice delle commesse.
            try
            // Inizio del blocco di istruzioni o della definizione precedente.
            {
                // Esamina esclusivamente le directory figlie dirette della radice per individuare gli anni.
                foreach (var yearFolder in Directory.EnumerateDirectories(root))
                // Inizio del blocco di istruzioni o della definizione precedente.
                {
                    // Consente l'interruzione anche durante la ricerca delle cartelle annuali.
                    if (token.IsCancellationRequested) return new([], [], true);
                    // Accetta solo nomi annuali di quattro cifre compresi tra 1900 e 2099.
                    if (!Regex.IsMatch(System.IO.Path.GetFileName(yearFolder), @"^(19|20)[0-9]{2}$")) continue;
                    // Aggiunge alla pila le cartelle annuali reali, senza seguire reparse point o collegamenti.
                    if ((File.GetAttributes(yearFolder) & FileAttributes.ReparsePoint) == 0) pending.Push(yearFolder);
                // Fine del blocco di istruzioni o della definizione corrente.
                }
                // Segnala quando la radice non contiene cartelle annuali utilizzabili.
                if (pending.Count == 0) warnings.Add("Nessuna cartella annuale principale trovata (nome atteso: quattro cifre, es. 2026).");
            // Fine del blocco di istruzioni o della definizione corrente.
            }
            // Restituisce un errore principale se la selezione delle cartelle annuali non riesce.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new([], [], Error: ex.Message); }
        // Fine del blocco di istruzioni o della definizione corrente.
        }
        // Per le gare avvia la ricerca direttamente dalla radice configurata.
        else pending.Push(root);
        // Estrae codice, anno, progressivo, cliente e argomento dal formato C/GXXYYY - Cliente - Argomento.
        var pattern = new Regex(@"^(?<code>[CG](?<year>\d{2})(?<number>\d{3}))\s*-\s*(?<client>.+?)\s+-\s+(?<subject>.+)$", RegexOptions.IgnoreCase);
        // Preleva una directory alla volta fino a esaurire i percorsi da esaminare.
        while (pending.TryPop(out var current))
        // Inizio del blocco di istruzioni o della definizione precedente.
        {
            // Interrompe la scansione tra una directory e la successiva se richiesto.
            if (token.IsCancellationRequested) return new([], [], true);
            // Isola gli errori relativi all'enumerazione della directory corrente.
            try
            // Inizio del blocco di istruzioni o della definizione precedente.
            {
                // Enumera soltanto le cartelle immediatamente contenute nel percorso corrente.
                foreach (var directory in Directory.EnumerateDirectories(current))
                // Inizio del blocco di istruzioni o della definizione precedente.
                {
                    // Controlla l'annullamento prima di analizzare ciascuna cartella.
                    if (token.IsCancellationRequested) return new([], [], true);
                    // Salta cestini, snapshot e altre directory escluse per nome.
                    if (IsExcluded(directory)) continue;
                    // Isola gli errori relativi alla singola cartella senza interrompere le altre letture.
                    try
                    // Inizio del blocco di istruzioni o della definizione precedente.
                    {
                        // Segnala e ignora i reparse point, evitando di seguire collegamenti ad altri percorsi.
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { warnings.Add("Collegamento escluso: " + directory); continue; }
                        // Ricava il nome della cartella e tenta di interpretarlo con il formato atteso.
                        var name = System.IO.Path.GetFileName(directory); var match = pattern.Match(name);
                        // Considera valido soltanto un nome completo il cui prefisso corrisponde alla categoria richiesta.
                        if (match.Success && char.ToUpperInvariant(name[0]) == prefix)
                        // Inizio del blocco di istruzioni o della definizione precedente.
                        {
                            // Registra i campi estratti, normalizzando il codice e togliendo spazi esterni a cliente e argomento.
                            records.Add(new(match.Groups["code"].Value.ToUpperInvariant(), match.Groups["year"].Value, match.Groups["number"].Value, match.Groups["client"].Value.Trim(), match.Groups["subject"].Value.Trim(), directory, "Valido"));
                            // Passa alla cartella successiva senza esplorare l'interno di quella riconosciuta.
                            continue;
                        // Fine del blocco di istruzioni o della definizione corrente.
                        }
                        // Riconosce anche codici iniziali plausibili quando il resto del nome non rispetta il formato completo.
                        if (Regex.IsMatch(name, "^[CG][0-9]{5}", RegexOptions.IgnoreCase))
                        // Inizio del blocco di istruzioni o della definizione precedente.
                        {
                            // Esclude una cartella con codice appartenente all'altra categoria e aggiunge una segnalazione.
                            if (char.ToUpperInvariant(name[0]) != prefix) { warnings.Add("Codice di altra categoria escluso: " + directory); continue; }
                            // Conserva codice e anno riconoscibili, marcando il nome come non conforme senza dedurre il cliente.
                            records.Add(new(name[..6].ToUpperInvariant(), name.Substring(1, 2), name.Substring(3, 3), "Da verificare", name, directory, "Non conforme"));
                            // Registra l'anomalia e passa oltre senza entrare nella cartella.
                            warnings.Add("Nome non conforme: " + directory); continue;
                        // Fine del blocco di istruzioni o della definizione corrente.
                        }
                        // Per le commesse gestisce come anomalia anche una cartella priva di codice riconoscibile.
                        if (prefix == 'C')
                        // Inizio del blocco di istruzioni o della definizione precedente.
                        {
                            // Registra la cartella non conforme ricavando l'anno dalla directory annuale che la contiene.
                            records.Add(new("", System.IO.Path.GetFileName(current)[2..], "", "Da verificare", name, directory, "Non conforme"));
                            // Spiega nella segnalazione che il codice della commessa non è riconoscibile.
                            warnings.Add("Nome non conforme (codice commessa non riconosciuto): " + directory);
                        // Fine del blocco di istruzioni o della definizione corrente.
                        }
                        // Solo per le gare prosegue nei contenitori non riconosciuti; le commesse non vengono mai accodate qui.
                        else pending.Push(directory);
                    // Fine del blocco di istruzioni o della definizione corrente.
                    }
                    // Aggiunge l'errore della singola cartella alle segnalazioni e prosegue la scansione.
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(directory + ": " + ex.Message); }
                // Fine del blocco di istruzioni o della definizione corrente.
                }
            // Fine del blocco di istruzioni o della definizione corrente.
            }
            // Gestisce gli errori di enumerazione della directory corrente.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            // Un errore sulla radice interrompe la lettura; un errore su un contenitore viene segnalato come lettura parziale.
            { if (current == root) return new([], [], Error: ex.Message); warnings.Add(current + ": " + ex.Message); }
        // Fine del blocco di istruzioni o della definizione corrente.
        }
        // Restituisce le cartelle ordinate per anno e progressivo decrescenti, insieme alle segnalazioni.
        return new(records.OrderByDescending(r => r.Year).ThenByDescending(r => r.Number).ToList(), warnings);
    // Fine del blocco di istruzioni o della definizione corrente.
    }
// Fine del blocco di istruzioni o della definizione corrente.
}

// Compone il report HTML autonomo con dati, segnalazioni e risorse grafiche incorporate.
public static class ReportWriter
// Inizio del blocco di istruzioni o della definizione precedente.
{
    // Legge una risorsa aziendale incorporata nell'applicazione e la converte per l'inclusione nel report.
    private static string BrandData(string name)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Risolve il nome della risorsa tramite un URI pack WPF nella cartella Assets/Brand.
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Brand/" + name));
        // Acquisisce il flusso della risorsa assicurandone la chiusura a fine metodo.
        using var stream = resource.Stream;
        // Prepara un buffer temporaneo in memoria, anch'esso rilasciato automaticamente.
        using var buffer = new MemoryStream();
        // Copia nel buffer i byte della risorsa incorporata.
        stream.CopyTo(buffer);
        // Restituisce i byte codificati Base64 per un URL dati HTML o CSS.
        return Convert.ToBase64String(buffer.ToArray());
    // Fine del blocco di istruzioni o della definizione corrente.
    }
    // Codifica i caratteri speciali HTML e sostituisce i valori nulli con testo vuoto.
    private static string H(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
    // Genera un documento completo a partire da percorsi e risultati delle due categorie.
    public static string Create(string jobsRoot, ScanResult jobs, string bidsRoot, ScanResult bids)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Accumula il contenuto HTML evitando concatenazioni ripetute dell'intero documento.
        var html = new System.Text.StringBuilder();
        // Apre il documento e definisce stile aziendale, tabelle, evidenziazione anomalie e stampa A4 orizzontale.
        html.Append("<!doctype html><html lang='it'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title>TA - Report cartelle e non conformita</title><style>body{font:15px/1.5 Acherus,Segoe UI,Arial,sans-serif;color:#192845;background:#f3f6f6;margin:0}main{max-width:1400px;margin:auto;background:white;padding:40px}h1{font-size:30px;margin:0}h2{border-bottom:2px solid #135f6e;padding-bottom:8px;margin-top:36px}h3{margin-top:28px}p{max-width:1000px}.meta{color:#53677a}.summary{padding:18px;background:#edf5f5;border-left:5px solid #135f6e}.warning{background:#fff1d6}.path{font-size:12px;color:#526477;overflow-wrap:anywhere}table{border-collapse:collapse;width:100%;margin:14px 0 28px;table-layout:fixed}th,td{text-align:left;vertical-align:top;border:1px solid #d5dde6;padding:9px;overflow-wrap:anywhere}th{background:#135f6e;color:white}tr:nth-child(even){background:#f0f6f6}tr.bad{background:#fff1d6}a{color:#135f6e}thead{display:table-header-group}@media print{body{background:white}main{max-width:none;padding:0;font-size:10pt}tr{break-inside:avoid}h2,h3{break-after:avoid}@page{size:A4 landscape;margin:12mm}</style></head><body><main>");
        // Incorpora i font Acherus regolare e grassetto e definisce dimensioni del logo e colore del titolo.
        html.Append("<style>@font-face{font-family:Acherus;src:url(data:font/otf;base64," + BrandData("Acherus-Regular.otf") + ") format('opentype');font-weight:400}@font-face{font-family:Acherus;src:url(data:font/otf;base64," + BrandData("Acherus-Bold.otf") + ") format('opentype');font-weight:700}.brand{display:block;width:260px;height:auto;margin:44px}h1{color:#192845}</style>");
        // Incorpora il logo in Base64 affinché il report sia consultabile senza file grafici esterni.
        html.Append("<img class='brand' alt='Tassinari Associati' src='data:image/png;base64," + BrandData("Logo.png") + "'>");
        // Inserisce titolo e data/ora locale di generazione, codificando il testo per HTML.
        html.Append("<h1>Gestione Cartelle Commesse</h1><p class='meta'>Report delle cartelle e delle non conformita | " + H(DateTime.Now.ToString("dd/MM/yyyy HH:mm")) + "</p>");
        // Mostra i totali delle due categorie, dei nomi non validi e delle segnalazioni.
        html.Append("<div class='summary'><strong>" + jobs.Records.Count + " commesse · " + bids.Records.Count + " gare</strong><br>" + jobs.Records.Concat(bids.Records).Count(r => r.Status != "Valido") + " nomi non conformi. " + (jobs.Warnings.Count + bids.Warnings.Count) + " segnalazioni complessive.</div>");
        // Descrive limiti, formato atteso e ordine del report, aggiungendo i collegamenti alle due sezioni.
        html.Append("<p>Commesse: analisi delle sole cartelle di primo livello dentro ogni cartella annuale principale (1900–2099). Nessuna scansione dei livelli successivi. Gare: ricerca nella radice selezionata. Cestini, copie storiche e collegamenti esclusi. Nessuna cartella e stata modificata. Ordine: anno e codice crescenti, quindi cliente.</p><p>Formato atteso: <strong>CXXYYY - Cliente - Argomento</strong>, oppure <strong>GXXYYY - Cliente - Argomento</strong>. XX indica l'anno a due cifre; YYY il progressivo. Il controllo riguarda il formato del nome, non la correttezza amministrativa dei dati. Per i nomi non conformi, cliente e argomento non vengono dedotti.</p><nav><a href='#commesse'>Commesse</a> | <a href='#gare'>Gare</a></nav>");
        // Genera separatamente le sezioni delle commesse e delle gare.
        Section(html, "commesse", "Commesse", jobsRoot, jobs); Section(html, "gare", "Gare", bidsRoot, bids);
        // Aggiunge le note sulle letture parziali e chiude il documento HTML.
        html.Append("<p class='meta'>Fine report. Le segnalazioni di accesso indicano possibili letture parziali. Le sottocartelle delle commesse e delle gare sono esplorabili nella gerarchia dell'applicazione, ma non sono incluse nel report e non sono soggette a questo controllo di denominazione.</p></main></body></html>");
        // Restituisce il documento completo come stringa da salvare.
        return html.ToString();
    // Fine del blocco di istruzioni o della definizione corrente.
    }
    // Genera titolo, segnalazioni e tabella delle cartelle per una singola categoria.
    private static void Section(System.Text.StringBuilder html, string id, string title, string root, ScanResult data)
    // Inizio del blocco di istruzioni o della definizione precedente.
    {
        // Inserisce l'ancora di navigazione, il titolo e il percorso radice con i testi codificati.
        html.Append("<h2 id='" + id + "'>" + H(title) + "</h2><p class='meta'>Radice: " + H(root) + "</p>");
        // Ordina il report per anno, codice, cliente e percorso crescenti, indipendentemente dall'ordine della vista.
        var ordered = data.Records.OrderBy(r => r.Year).ThenBy(r => r.Code).ThenBy(r => r.Client).ThenBy(r => r.Path).ToList();
        // Introduce l'elenco delle segnalazioni con il relativo conteggio.
        html.Append("<h3>Non conformita e segnalazioni (" + data.Warnings.Count + ")</h3>");
        // Esplicita l'assenza di segnalazioni invece di produrre una tabella vuota.
        if (data.Warnings.Count == 0) html.Append("<p>Nessuna segnalazione.</p>");
        // In presenza di segnalazioni crea una tabella numerata, ordinata alfabeticamente e con testi codificati.
        else { html.Append("<table><thead><tr><th style='width:50px'>N.</th><th>Segnalazione e cartella interessata</th></tr></thead><tbody>"); int n = 0; foreach (var warning in data.Warnings.OrderBy(w => w, StringComparer.CurrentCultureIgnoreCase)) html.Append("<tr class='bad'><td>" + (++n) + "</td><td>" + H(warning) + "</td></tr>"); html.Append("</tbody></table>"); }
        // Apre la tabella delle cartelle e definisce intestazioni e larghezze delle colonne.
        html.Append("<h3>Elenco ordinato delle cartelle (" + ordered.Count + ")</h3><table><thead><tr><th style='width:85px'>Codice</th><th style='width:45px'>Anno XX</th><th style='width:20%'>Cliente</th><th>Argomento e percorso</th><th style='width:100px'>Esito</th></tr></thead><tbody>");
        // Esamina ciascuna cartella nell'ordine di presentazione del report.
        foreach (var row in ordered)
            // Genera una riga con tutti i campi codificati e applica l'evidenziazione ai nomi non validi.
            html.Append("<tr" + (row.Status == "Valido" ? "" : " class='bad'") + "><td>" + H(row.Code) + "</td><td>" + H(row.Year) + "</td><td>" + H(row.Client) + "</td><td>" + H(row.Subject) + "<div class='path'>" + H(row.Path) + "</div></td><td>" + H(row.Status) + "</td></tr>");
        // Chiude il corpo e la tabella delle cartelle della sezione corrente.
        html.Append("</tbody></table>");
    // Fine del blocco di istruzioni o della definizione corrente.
    }
// Fine del blocco di istruzioni o della definizione corrente.
}
