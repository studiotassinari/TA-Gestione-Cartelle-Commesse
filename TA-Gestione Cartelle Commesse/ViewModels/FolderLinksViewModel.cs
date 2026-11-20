using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Collegamento cartelle (pagina Collegamento cartelle di GeCo2027): commesse e gare con la cartella collegata o proposta, e le
// notifiche arrivate da GeCo2027. Il servizio decide chi è amministratore, propone e scrive con le regole della libreria
// GeCo.Dati; qui si tengono soltanto filtro, ricerca, ultimo esito e notifiche ricevute. Una proposta resta una proposta finché
// l'amministratore non la conferma: i testi la indicano sempre come tale, con il motivo e il punteggio.
public sealed class FolderLinksViewModel(IGeCoGateway gateway) : IDisposable
{
    public const string All = "Tutte", ToLink = "Da collegare", Safe = "Proposte sicure", Warnings = "Con segnalazioni", Linked = "Collegate";
    public static readonly string[] Views = [All, ToLink, Safe, Warnings, Linked];
    public const int MaxEvents = 100;
    private readonly CancellationTokenSource lifetime = new();
    public GeCoAccess? Access { get; private set; }
    public GeCoLinks? Links { get; private set; }
    public string Error { get; private set; } = "";
    public string Done { get; private set; } = "";
    public bool Busy { get; private set; }
    public string View { get; set; } = All;
    public string Filter { get; set; } = "";
    public List<GeCoSyncEvent> Events { get; } = [];
    public string EventsMessage { get; private set; } = "";
    // Notifiche arrivate dopo l'apertura della finestra (le prime caricate non contano come nuove).
    public int Arrived { get; private set; }
    private long lastEvent, seenRevision;
    private bool eventsLoaded;
    public event Action? Changed;
    // Domanda di conferma all'utente (finestra di dialogo nella vista; nelle verifiche risponde sempre sì).
    public Func<string, bool> Ask { get; set; } = _ => true;
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }

    // Amministratore di GeCo su un database con la migrazione 12: solo allora si vede e si modifica la situazione completa.
    public bool Allowed => Access is { Recognized: true, Administrator: true, Links: true };
    public string Denied => Access switch
    {
        null => "",
        { Recognized: false } a => a.Message,
        { Links: false } => "Collegamento delle cartelle non disponibile: il database comune non è ancora aggiornato. Aggiornare prima GeCo2027 o il servizio.",
        { Administrator: false } => "Il collegamento delle cartelle è riservato agli amministratori di GeCo (tipo «Amministratore» o «Tutte le funzioni» nella pagina Utenti).",
        _ => "",
    };

    public async Task StartAsync()
    {
        try { Access = await gateway.Send<GeCoAccess>(new("Access"), lifetime.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Access = new(false, Message(ex), ""); }
        if (Access.Recognized && Access.Links) await LoadEventsAsync();
        if (Allowed) await LoadAsync(); else Changed?.Invoke();
    }
    // Un servizio precedente alla 2.38.0 non conosce i comandi: si chiede di aggiornarlo invece di mostrare un errore generico.
    public static string Message(Exception ex) => ex.Message is "Comando non supportato." or "Comando GeCo sconosciuto."
        ? "Il server non gestisce ancora il collegamento delle cartelle: aggiornare il servizio alla versione 2.38.0 o successiva." : ex.Message;

    public async Task LoadAsync()
    {
        try
        {
            Busy = true; Changed?.Invoke();
            Links = await gateway.Send<GeCoLinks>(new("Links"), lifetime.Token);
            Error = "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = Message(ex); }
        finally { Busy = false; Changed?.Invoke(); }
    }

    // Notifiche: la prima volta le ultime, poi solo quelle dopo l'ultima ricevuta. La più recente in cima.
    public async Task LoadEventsAsync()
    {
        try
        {
            var result = await gateway.Send<GeCoSyncEvents>(new("SyncEvents", DopoId: eventsLoaded ? lastEvent : null), lifetime.Token);
            if (!result.Disponibile) { EventsMessage = result.Message; return; }
            var fresh = result.Eventi.Where(e => e.Id > lastEvent || !eventsLoaded).Where(e => Events.All(x => x.Id != e.Id)).ToList();
            if (eventsLoaded) Arrived += fresh.Count;
            Events.InsertRange(0, fresh.OrderByDescending(e => e.Id));
            Events.Sort((a, b) => b.Id.CompareTo(a.Id));
            if (Events.Count > MaxEvents) Events.RemoveRange(MaxEvents, Events.Count - MaxEvents);
            lastEvent = Math.Max(lastEvent, result.UltimoId);
            eventsLoaded = true; EventsMessage = "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { EventsMessage = "Notifiche non aggiornate: " + Message(ex); }
        finally { Changed?.Invoke(); }
    }
    // Notifica push «GeCo»: Revision è l'ultimo evento di GeCo2027 nel registro. Ogni riconnessione la ripete: si chiede solo
    // se è nuova. Una commessa o una gara creata, modificata o collegata in GeCo2027 cambia anche l'elenco: si ricarica.
    public async Task PushedAsync(long revision)
    {
        if (revision <= seenRevision) return;
        seenRevision = revision;
        if (Access is not { Recognized: true, Links: true }) return;
        var before = Events.Count == 0 ? 0 : Events[0].Id;
        await LoadEventsAsync();
        if (Allowed && Events.Count > 0 && Events[0].Id > before) await LoadAsync();
    }
    public void Seen() { Arrived = 0; Changed?.Invoke(); }

    // ---------------------------------------------------------------- elenco

    // Riga della tabella: testi già pronti; Stato dice sempre se è un collegamento confermato o soltanto una proposta.
    public sealed record Item(GeCoLinkRow Row, string Elemento, string Codice, string Anno, string Descrizione, string Cartella, string Stato, string Motivo, string Segnalazioni);
    public static Item Describe(GeCoLinkRow r)
    {
        string kind = r.Tipo == "gara" ? "Gara" : "Commessa";
        string description = string.Join(" – ", new[] { r.Descrizione, r.Cliente }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()));
        string folder, state, reason = "";
        if (r.Collegamento != null)
        {
            folder = r.Cartella?.Nome ?? "#" + r.Collegamento.IdCartella;
            state = r.Cartella?.Presente == true ? "Collegata" : "Collegata, cartella non più sul disco";
            reason = $"Confermata da {r.Collegamento.Utente ?? "?"} in {r.Collegamento.Origine} il {r.Collegamento.Data}";
            if (r.Cartella?.Presente != true && r.Proposta is { } moved)
                reason += $"; proposta da verificare: {moved.Cartella?.Nome ?? "#" + moved.IdCartella} ({moved.Punteggio}, {moved.Motivo})";
        }
        else if (r.Proposta is { } p)
        {
            folder = p.Cartella?.Nome ?? "#" + p.IdCartella;
            state = r.Livello == "sicura" ? "Proposta sicura (da confermare)" : "Proposta da verificare";
            reason = $"Punteggio {p.Punteggio}: {p.Motivo}" + (r.Candidati.Length > 1 ? $"; {r.Candidati.Length} cartelle candidate" : "");
        }
        else { folder = ""; state = "Senza cartella"; }
        return new(r, kind, r.Codice, r.Anno?.ToString() ?? "", description, folder, state, reason, string.Join(" ", r.Conflitti.Select(c => c.Testo)));
    }
    public IEnumerable<Item> Items()
    {
        IEnumerable<GeCoLinkRow> rows = Links?.Elementi ?? [];
        rows = View switch
        {
            ToLink => rows.Where(r => r.Collegamento == null || r.Cartella?.Presente != true),
            Safe => rows.Where(r => r.Collegamento == null && r.Livello == "sicura"),
            Warnings => rows.Where(r => r.Conflitti.Length > 0),
            Linked => rows.Where(r => r.Collegamento != null),
            _ => rows,
        };
        var items = rows.Select(Describe);
        foreach (var word in Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            items = items.Where(i => new[] { i.Codice, i.Anno, i.Descrizione, i.Cartella }.Any(t => t.Contains(word, StringComparison.CurrentCultureIgnoreCase)));
        return items;
    }
    public int SafeCount => Links?.Sicure.Length ?? 0;
    public string Summary
    {
        get
        {
            if (Links is not { } l) return "";
            int linked = l.Elementi.Count(r => r.Collegamento != null), safe = l.Sicure.Length, check = l.Elementi.Count(r => r.Collegamento == null && r.Livello == "daverificare");
            int orphans = l.CartelleLibere.Count(f => f.CandidataPer.Length == 0 && f.Cartella.Categoria != "A");
            var syncs = string.Join(" · ", l.Sincronizzazioni.Select(s => $"{s.Categoria}: {s.Cartelle} cartelle il {s.Data}"));
            return $"{l.Elementi.Length} commesse e gare: {linked} collegate, {safe} proposte sicure da confermare, {check} proposte da verificare; {orphans} cartelle senza commessa né gara."
                + (syncs.Length > 0 ? " Ultime scansioni: " + syncs + "." : "");
        }
    }

    // ---------------------------------------------------------------- azioni (sempre ricalcolate dal servizio)

    private async Task<bool> Run(Func<Task<string>> action)
    {
        try { Busy = true; Changed?.Invoke(); Done = await action(); Error = ""; return true; }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = Message(ex); Done = ""; return false; }
        finally { Busy = false; await LoadAsync(); }
    }
    private static string Of(GeCoLinkRow r) => (r.Tipo == "gara" ? "gara " : "commessa ") + r.Codice;
    // Conferma la proposta; se l'elemento ha già una cartella (sparita o diversa) la sostituzione va confermata a parte.
    public Task<bool> ConfirmAsync(GeCoLinkRow r) => r.Proposta is { } p ? LinkAsync(r, p.IdCartella, p.Cartella?.Nome) : Task.FromResult(false);
    public async Task<bool> LinkAsync(GeCoLinkRow r, int folder, string? name)
    {
        bool replace = r.Collegamento != null;
        if (replace && !Ask($"La {Of(r)} è già collegata alla cartella {r.Cartella?.Nome ?? "#" + r.Collegamento!.IdCartella}. Sostituire il collegamento con {name ?? "la cartella scelta"}?")) return false;
        return await Run(async () =>
        {
            await gateway.Send<int>(new("Link", Tipo: r.Tipo, Id: r.Id, IdCartella: folder, Valore: replace), lifetime.Token);
            return $"Cartella {name} collegata alla {Of(r)}.";
        });
    }
    public async Task<bool> UnlinkAsync(GeCoLinkRow r)
    {
        if (r.Collegamento == null || !Ask($"Scollegare la cartella {r.Cartella?.Nome} dalla {Of(r)}? La cartella sul disco non viene toccata.")) return false;
        return await Run(async () => await gateway.Send<bool>(new("Unlink", Tipo: r.Tipo, Id: r.Id), lifetime.Token)
            ? $"Cartella scollegata dalla {Of(r)}." : "Il collegamento era già stato tolto.");
    }
    // Si manda l'anteprima vista: se nel frattempo è cambiato qualcosa il servizio non scrive nulla e lo dice.
    public async Task<bool> ConfirmSafeAsync()
    {
        var safe = Links?.Sicure ?? [];
        if (safe.Length == 0 || !Ask($"Confermare {safe.Length} proposte sicure (stesso codice, nessun'altra cartella o commessa in concorrenza)?")) return false;
        return await Run(async () => $"{await gateway.Send<int>(new("ConfirmSafe", Collegamenti: safe), lifetime.Token)} collegamenti confermati.");
    }
    public async Task<GeCoFolderChoice[]> FoldersAsync(string text, string type)
    {
        try { return await gateway.Send<GeCoFolderChoice[]>(new("Folders", Testo: text, Tipo: type), lifetime.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = Message(ex); Changed?.Invoke(); return []; }
    }
}
