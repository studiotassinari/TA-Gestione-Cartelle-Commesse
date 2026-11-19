using System.Threading.Channels;
using GeCo.Dati;
using GeCo.Dati.Regole;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace TA.Server.SharedDatabase;

// Ultimo evento di GeCo2027 letto dal registro comune, per le sessioni push dei client che lo chiedono (SupportsGeCoEvents).
// Come ChangeHub ogni client ha una coda di un solo elemento: basta l'ultimo Id, gli eventi il client li chiede poi con il
// comando GeCo «SyncEvents», che li filtra secondo ciò che il suo utente di GeCo può vedere. Per questo la notifica non porta
// i testi degli eventi: il canale push è di tutti gli utenti autorizzati, non solo di chi vede quelle commesse.
// Dalla 2.39.0 c'è una seconda corsia, per i client che dichiarano SupportsGeCoViews: l'ultimo evento tecnico (pianificazione
// o ore salvate in GeCo2027 o in GeCa), che il client chiede con «ViewEvents». È separata perché le due notifiche non devono
// scavalcarsi nella coda di un elemento e i client 2.38.0 non conoscono la seconda.
public sealed class GeCoEventHub
{
    private readonly object gate = new();
    private readonly HashSet<Subscription> subscribers = [];
    private readonly SemaphoreSlim wake = new(0, 1);
    private long last, lastView;
    private string? summary;
    public long Last { get { lock (gate) return last; } }
    public long LastView { get { lock (gate) return lastView; } }
    public string? Summary { get { lock (gate) return summary; } }
    // Chi si collega riceve subito l'ultimo Id noto: gli eventi arrivati mentre il canale era chiuso non si perdono.
    public Subscription Subscribe(bool views = false)
    {
        lock (gate)
        {
            var subscription = new Subscription(this, views);
            subscribers.Add(subscription);
            if (last > 0) subscription.Queue.Writer.TryWrite(last);
            if (views && lastView > 0) subscription.Views.Writer.TryWrite(lastView);
            return subscription;
        }
    }
    // La posizione non torna indietro: una lettura ripetuta non rimanda una notifica già inviata. Il riepilogo è quello
    // dell'ultima notifica (nessuno = nulla da annunciare a tutti, per esempio solo notifiche rivolte a utenti precisi).
    public void Publish(long lastId, string? message = null)
    {
        lock (gate)
        {
            if (lastId <= last) return;
            last = lastId; summary = message;
            foreach (var subscriber in subscribers) subscriber.Queue.Writer.TryWrite(last);
        }
    }
    // Ultimo evento tecnico: i client con le finestre di pianificazione o ore aperte chiedono che cosa è cambiato.
    public void PublishViews(long lastId)
    {
        lock (gate)
        {
            if (lastId <= lastView) return;
            lastView = lastId;
            foreach (var subscriber in subscribers) if (subscriber.WantsViews) subscriber.Views.Writer.TryWrite(lastView);
        }
    }
    // Un client di GeCa ha appena salvato pianificazione o ore: il lettore rilegge subito il registro invece di aspettare il
    // giro successivo, così gli altri client si aggiornano quasi all'istante. Più richieste ravvicinate valgono una.
    public void Wake() { lock (gate) { if (wake.CurrentCount == 0) wake.Release(); } }
    // Attesa del lettore: finisce allo scadere o alla prima richiesta di Wake; true se è stata una richiesta.
    public Task<bool> WaitAsync(TimeSpan delay, CancellationToken token) => wake.WaitAsync(delay, token);
    public sealed class Subscription(GeCoEventHub owner, bool views) : IDisposable
    {
        internal bool WantsViews { get; } = views;
        internal Channel<long> Queue { get; } = Channel.CreateBounded<long>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        internal Channel<long> Views { get; } = Channel.CreateBounded<long>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        public ValueTask<bool> WaitToReadAsync(CancellationToken token) => Queue.Reader.WaitToReadAsync(token);
        public bool TryRead(out long lastId) => Queue.Reader.TryRead(out lastId);
        public ValueTask<bool> WaitToReadViewsAsync(CancellationToken token) => Views.Reader.WaitToReadAsync(token);
        public bool TryReadViews(out long lastId) => Views.Reader.TryRead(out lastId);
        public void Dispose()
        {
            lock (owner.gate) { owner.subscribers.Remove(this); Queue.Writer.TryComplete(); Views.Writer.TryComplete(); }
        }
    }
}

// Lettore del registro comune (tblSincronizzazioneEventi) come consumatore «GeCa»: legge gli eventi successivi alla propria
// posizione, avvisa i client e salva la posizione (tblSincronizzazionePosizioni), così dopo un riavvio riprende dall'ultimo
// evento elaborato e la pulizia di GeCo2027 non elimina eventi non ancora letti. Le notifiche sono gli eventi di GeCo2027
// che non sono tecnici; gli eventi tecnici (pianificazione e ore salvate, 2.39.0) si leggono di entrambe le origini, così
// anche un salvataggio fatto da un client di GeCa aggiorna gli altri client aperti, e non contano tra le novità.
// Il database è l'unico canale con GeCo2027: nessuna chiamata tra i due server. Senza la migrazione 12 aspetta senza errori
// (il database si aggiorna con GeCo2027 o con l'invio delle cartelle); un database irraggiungibile non ferma il servizio.
public sealed class GeCoEventsService(ServerSettings settings, GeCoEventHub hub, ILogger<GeCoEventsService> logger) : BackgroundService
{
    public const string Consumer = EventiSincronizzazione.GeCa;
    public const int Batch = 500;
    // Ogni 3 secondi, come il distributore di GeCo2027: una lettura per chiave primaria (Sev_Id maggiore della posizione) che
    // senza eventi nuovi non restituisce righe. La versione del database si rilegge di rado: cambia solo con un aggiornamento.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3), Waiting = TimeSpan.FromMinutes(5), Retry = TimeSpan.FromMinutes(1), VersionCheck = TimeSpan.FromMinutes(5);
    private long? position;
    public long? Position => position;

    // Una lettura: eventi dopo la posizione, a blocchi finché ne arrivano. Restituisce quante notifiche di GeCo2027 (gli eventi
    // tecnici e quelli di GeCa non contano); -1 se il database non ha ancora la migrazione 12. Al primo giro senza posizione
    // salvata si parte dall'ultimo evento (la cronologia precedente non è una novità) e la posizione si salva subito, così la
    // pulizia sa che GeCa legge gli eventi di GeCo2027.
    public async Task<int> PollAsync(GeCoDbContext db, int version, CancellationToken token)
    {
        if (version < EventiSincronizzazione.VersioneRichiesta) return -1;
        // La posizione in memoria avanza solo dopo il salvataggio: se questo non riesce, il giro successivo rilegge gli stessi eventi.
        bool save = false;
        long current;
        if (position is { } known) current = known;
        else
        {
            var stored = await EventiSincronizzazione.PosizioneAsync(db, Consumer, token);
            save = stored == null;
            current = stored ?? await EventiSincronizzazione.UltimoIdAsync(db, token);
            // I client collegati prima del primo evento chiedono gli eventi fino a qui (con nessuna novità vedono gli ultimi).
            hub.Publish(current);
            hub.PublishViews(current);
        }
        long start = current, lastNotice = 0, lastView = 0;
        var notices = new List<EventoSincronizzazione>();
        while (true)
        {
            var events = await EventiSincronizzazione.LeggiDopoAsync(db, current, massimo: Batch, stop: token);
            if (events.Count == 0) break;
            current = events[^1].Id;
            foreach (var e in events)
                if (EventiSincronizzazione.Tecnico(e)) lastView = e.Id;
                else if (e.Origine == EventiSincronizzazione.GeCo2027) { notices.Add(e); lastNotice = e.Id; }
            if (events.Count < Batch) break;
        }
        if (current > start || save) await EventiSincronizzazione.SalvaPosizioneAsync(db, Consumer, current, EventiSincronizzazione.GeCo2027, token);
        position = current;
        if (lastView > 0) hub.PublishViews(lastView);
        if (notices.Count > 0)
        {
            // Le notifiche rivolte a utenti precisi (richieste di accesso, migrazione 13) non entrano nel riepilogo che il canale
            // mostra a tutti: i destinatari le vedono comunque nell'elenco, che il comando SyncEvents filtra per ciascuno.
            var aimed = await NotificheMirate.DestinatariAsync(db, version, notices.Select(e => e.Id), token);
            int general = notices.Count(e => !aimed.ContainsKey(e.Id));
            hub.Publish(lastNotice, general == 0 ? null : general == 1 ? "1 novità da GeCo2027" : general + " novità da GeCo2027");
        }
        return notices.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var connection = settings.SharedDatabase.Trim();
        if (connection.Length == 0) return;
        var open = GeCoWorkService.Open(connection)!;
        bool warned = false, waiting = false;
        int version = 0; DateTime versionRead = DateTime.MinValue;
        while (!stop.IsCancellationRequested)
        {
            var delay = Interval;
            try
            {
                if (version < EventiSincronizzazione.VersioneRichiesta || DateTime.UtcNow - versionRead > VersionCheck)
                { version = await AggiornamentoDatabase.VersioneAttualeAsync(connection, stop); versionRead = DateTime.UtcNow; }
                await using var db = open();
                if (await PollAsync(db, version, stop) < 0)
                {
                    if (!waiting) logger.LogInformation("Eventi di GeCo2027 non disponibili: il database comune è alla versione {Version}, serve la {Required}", version, EventiSincronizzazione.VersioneRichiesta);
                    waiting = true; delay = Waiting;
                }
                else waiting = false;
                warned = false;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Un solo messaggio nel registro finché il database non torna raggiungibile.
                if (!warned) logger.LogWarning(ex, "Lettura degli eventi di GeCo2027 non riuscita; nuovo tentativo tra un minuto");
                warned = true; delay = Retry; version = 0;
            }
            // Un salvataggio di un client di GeCa anticipa il giro (Wake); in attesa della migrazione o dopo un errore no.
            try { if (delay == Interval) await hub.WaitAsync(delay, stop); else await Task.Delay(delay, stop); }
            catch (OperationCanceledException) { break; }
        }
    }
}
