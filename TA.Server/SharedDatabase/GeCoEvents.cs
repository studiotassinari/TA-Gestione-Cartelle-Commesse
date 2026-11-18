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
public sealed class GeCoEventHub
{
    private readonly object gate = new();
    private readonly HashSet<Subscription> subscribers = [];
    private long last;
    private string? summary;
    public long Last { get { lock (gate) return last; } }
    public string? Summary { get { lock (gate) return summary; } }
    // Chi si collega riceve subito l'ultimo Id noto: gli eventi arrivati mentre il canale era chiuso non si perdono.
    public Subscription Subscribe()
    {
        lock (gate)
        {
            var subscription = new Subscription(this);
            subscribers.Add(subscription);
            if (last > 0) subscription.Queue.Writer.TryWrite(last);
            return subscription;
        }
    }
    // La posizione non torna indietro: una lettura ripetuta non rimanda una notifica già inviata.
    public void Publish(long lastId, string? message = null)
    {
        lock (gate)
        {
            if (lastId <= last) return;
            last = lastId;
            if (message != null) summary = message;
            foreach (var subscriber in subscribers) subscriber.Queue.Writer.TryWrite(last);
        }
    }
    public sealed class Subscription(GeCoEventHub owner) : IDisposable
    {
        internal Channel<long> Queue { get; } = Channel.CreateBounded<long>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        public ValueTask<bool> WaitToReadAsync(CancellationToken token) => Queue.Reader.WaitToReadAsync(token);
        public bool TryRead(out long lastId) => Queue.Reader.TryRead(out lastId);
        public void Dispose()
        {
            lock (owner.gate) { owner.subscribers.Remove(this); Queue.Writer.TryComplete(); }
        }
    }
}

// Lettore degli eventi scritti da GeCo2027 nel registro comune (tblSincronizzazioneEventi), come consumatore «GeCa»: legge
// quelli successivi alla propria posizione, avvisa i client e salva la posizione (tblSincronizzazionePosizioni), così dopo un
// riavvio riprende dall'ultimo evento elaborato e la pulizia di GeCo2027 non elimina eventi non ancora letti. Il database è
// l'unico canale con GeCo2027: nessuna chiamata tra i due server. Senza la migrazione 12 aspetta senza errori (il database si
// aggiorna con GeCo2027 o con l'invio delle cartelle); un database irraggiungibile non ferma il servizio.
public sealed class GeCoEventsService(ServerSettings settings, GeCoEventHub hub, ILogger<GeCoEventsService> logger) : BackgroundService
{
    public const string Consumer = EventiSincronizzazione.GeCa;
    public const int Batch = 500;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10), Waiting = TimeSpan.FromMinutes(5), Retry = TimeSpan.FromMinutes(1);
    private long? position;
    public long? Position => position;

    // Una lettura: eventi di GeCo2027 dopo la posizione, a blocchi finché ne arrivano. Restituisce quanti; -1 se il database
    // non ha ancora la migrazione 12. Al primo giro senza posizione salvata si parte dall'ultimo evento (la cronologia
    // precedente non è una novità) e la posizione si salva subito, così la pulizia sa che GeCa legge gli eventi di GeCo2027.
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
        }
        int total = 0;
        while (true)
        {
            var events = await EventiSincronizzazione.LeggiDopoAsync(db, current, soloOrigine: EventiSincronizzazione.GeCo2027, massimo: Batch, stop: token);
            if (events.Count == 0) break;
            current = events[^1].Id; total += events.Count;
            if (events.Count < Batch) break;
        }
        if (total > 0 || save) await EventiSincronizzazione.SalvaPosizioneAsync(db, Consumer, current, EventiSincronizzazione.GeCo2027, token);
        position = current;
        if (total > 0) hub.Publish(current, total == 1 ? "1 novità da GeCo2027" : total + " novità da GeCo2027");
        return total;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var connection = settings.SharedDatabase.Trim();
        if (connection.Length == 0) return;
        var open = GeCoWorkService.Open(connection)!;
        bool warned = false, waiting = false;
        while (!stop.IsCancellationRequested)
        {
            var delay = Interval;
            try
            {
                var version = await AggiornamentoDatabase.VersioneAttualeAsync(connection, stop);
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
                warned = true; delay = Retry;
            }
            try { await Task.Delay(delay, stop); }
            catch (OperationCanceledException) { break; }
        }
    }
}
