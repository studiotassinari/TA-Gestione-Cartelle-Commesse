using GeCo.Dati;
using GeCo.Dati.Regole;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.SharedDatabase;

// Cartelle chieste a GeCa (2.40.0, migrazione 14): un amministratore di GeCo2027 (o di GeCa, dal modulo Allineamento) chiede la
// cartella di una commessa o di una gara con un nome già validato; questo servizio la crea con la creazione da modello, l'unica
// via ammessa per creare cartelle (TemplateCreationService: struttura vuota resa visibile con una rinomina, mai sovrascritture),
// e scrive l'esito nel database comune. La scansione successiva trova la cartella e la libreria la collega da sola.
// Il canale è solo il database: le richieste si leggono quando arriva l'evento «CartellaRichiesta» (GeCoEventsService chiama
// Wake) e comunque ogni 30 secondi, perché un evento può arrivare con il servizio spento. Una richiesta alla volta; due servizi
// che prendono la stessa richiesta non si sovrappongono (PrendiInCaricoAsync: il secondo riceve 409 e la salta).
// Prima della migrazione 14 non c'è nulla da fare (DaEseguireAsync restituisce un elenco vuoto).
public sealed class GeCoFolderRequestsService : BackgroundService
{
    public sealed record Outcome(int Id, string Nome, string Stato, string Messaggio, string? Percorso);

    private readonly Func<GeCoDbContext>? open;
    private readonly Func<CancellationToken, Task<int>> version;
    private readonly Func<CreateJobRequest, string, TemplatePreview> preview;
    private readonly Func<CreateJobRequest, string, string> create;
    private readonly Action<string> created;
    private readonly ILogger? logger;
    private readonly SemaphoreSlim gate = new(1, 1), wake = new(0, 1);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30), Retry = TimeSpan.FromMinutes(1);
    public static string Executor => "TA-Gestione su " + Environment.MachineName;

    public GeCoFolderRequestsService(ServerSettings settings, TemplateCreationService templates, ScanCoordinator scans, ILogger<GeCoFolderRequestsService> logger)
        : this(GeCoWorkService.Open(settings.SharedDatabase), token => AggiornamentoDatabase.VersioneAttualeAsync(settings.SharedDatabase, token),
            templates.Preview, templates.Create, category => { scans.Get(category).NotifyChange(); scans.Refresh(category); }, logger) { }
    // created: la categoria in cui è comparsa una cartella; il servizio rilegge l'elenco e lo invia al database comune.
    public GeCoFolderRequestsService(Func<GeCoDbContext>? open, Func<CancellationToken, Task<int>> version, Func<CreateJobRequest, string, TemplatePreview> preview,
        Func<CreateJobRequest, string, string> create, Action<string> created, ILogger? logger = null)
    { this.open = open; this.version = version; this.preview = preview; this.create = create; this.created = created; this.logger = logger; }

    // Evento «CartellaRichiesta» o richiesta fatta da un client di GeCa: il giro successivo parte subito. Più richiami valgono uno.
    public void Wake() { lock (wake) { if (wake.CurrentCount == 0) wake.Release(); } }
    public bool WakePending => wake.CurrentCount > 0;

    // Tutte le richieste da eseguire, una alla volta e le più vecchie prima.
    public async Task<List<Outcome>> RunAsync(CancellationToken token)
    {
        var done = new List<Outcome>();
        if (open == null) return done;
        await gate.WaitAsync(token);
        try
        {
            var current = await version(token);
            await using var db = open();
            foreach (var r in await ServizioRichiesteCartelle.DaEseguireAsync(db, current, token))
            {
                token.ThrowIfCancellationRequested();
                db.ChangeTracker.Clear();
                if (await ExecuteAsync(db, current, r, token) is { } outcome) done.Add(outcome);
            }
        }
        finally { gate.Release(); }
        return done;
    }

    // Una richiesta precisa, subito (creazione chiesta dal modulo Allineamento): null se non è più da eseguire.
    public async Task<Outcome?> RunOneAsync(int id, CancellationToken token)
    {
        if (open == null) return null;
        await gate.WaitAsync(token);
        try
        {
            var current = await version(token);
            await using var db = open();
            var r = (await ServizioRichiesteCartelle.DaEseguireAsync(db, current, token)).FirstOrDefault(x => x.Id == id);
            db.ChangeTracker.Clear();
            return r == null ? null : await ExecuteAsync(db, current, r, token);
        }
        finally { gate.Release(); }
    }

    private async Task<Outcome?> ExecuteAsync(GeCoDbContext db, int current, RichiestaCartella r, CancellationToken token)
    {
        RichiestaCartella taken;
        try { taken = await ServizioRichiesteCartelle.PrendiInCaricoAsync(db, current, r.Id, r.Versione, Executor, token); }
        // Presa da un altro servizio, annullata o eliminata nel frattempo: non tocca a questo.
        catch (RegolaException ex) when (ex.Stato is 404 or 409) { db.ChangeTracker.Clear(); return null; }
        db.ChangeTracker.Clear();
        var (path, refused, message) = await Task.Run(() => Create(r), token);
        if (path != null)
        {
            await ServizioRichiesteCartelle.SegnaCreataAsync(db, current, r.Id, taken.Versione, path, Executor, token);
            logger?.LogInformation("Cartella chiesta da {Origin} creata: {Path}", r.Origine, path);
            try { created(r.Categoria); } catch (Exception ex) when (ex is not OperationCanceledException) { logger?.LogWarning(ex, "Elenco {Category} non riletto dopo la creazione", r.Categoria); }
            return new(r.Id, r.Nome, RichiesteCartelle.Creata, message.Length > 0 ? message : "Cartella creata.", path);
        }
        await ServizioRichiesteCartelle.SegnaNonCreataAsync(db, current, r.Id, taken.Versione, refused, message, Executor, token);
        logger?.LogWarning("Cartella «{Name}» chiesta da {Origin} non creata: {Message}", r.Nome, r.Origine, message);
        return new(r.Id, r.Nome, refused ? RichiesteCartelle.Rifiutata : RichiesteCartelle.Errore, message, null);
    }

    // Creazione con le stesse regole e la stessa anteprima del client: rifiutata (non va ripetuta così com'è) per un nome non
    // valido, per un nome diverso da quello chiesto o per una cartella con lo stesso codice; errore (si può chiedere di nuovo)
    // per modello, permessi o disco. Una cartella con il nome esatto della richiesta è quella creata da un tentativo precedente
    // rimasto senza esito (servizio fermato dopo la creazione): vale come creata, senza crearne un'altra.
    private (string? Path, bool Refused, string Message) Create(RichiestaCartella r)
    {
        try
        {
            var request = new CreateJobRequest(r.Codice, r.Cliente, r.Luogo ?? "", r.Oggetto);
            var plan = preview(request, r.Categoria);
            var name = Path.GetFileName(plan.Destination);
            if (!string.Equals(name, r.Nome, StringComparison.Ordinal))
                return (null, true, $"GeCa creerebbe la cartella «{name}», diversa da quella chiesta («{r.Nome}»): nessuna cartella creata. Ripetere la richiesta.");
            return (create(request with { TemplateStamp = plan.Stamp }, r.Categoria), false, "");
        }
        catch (TemplateCreationService.FolderExistsException ex)
        {
            if (ex.Existing.FirstOrDefault(p => Directory.Exists(p) && string.Equals(Path.GetFileName(p), r.Nome, StringComparison.Ordinal)) is { } same)
                return (same, false, "Cartella già presente con il nome chiesto: non ne è stata creata un'altra.");
            return (null, true, "Esiste già " + string.Join(", ", ex.Existing.Select(p => "«" + Path.GetFileName(p) + "»")) + " con lo stesso codice: collegarla invece di crearne una nuova. Nessuna cartella creata.");
        }
        catch (ArgumentException ex) { return (null, true, ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return (null, false, ex.Message + (ex.InnerException is { } inner ? " (" + inner.Message + ")" : "")); }
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (open == null) return;
        bool warned = false;
        while (!stop.IsCancellationRequested)
        {
            var delay = Interval;
            try { await RunAsync(stop); warned = false; }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Un solo messaggio nel registro finché il database non torna raggiungibile.
                if (!warned) logger?.LogWarning(ex, "Cartelle chieste da GeCo2027 non lette; nuovo tentativo tra un minuto");
                warned = true; delay = Retry;
            }
            try { await wake.WaitAsync(delay, stop); }
            catch (OperationCanceledException) { break; }
        }
    }
}
