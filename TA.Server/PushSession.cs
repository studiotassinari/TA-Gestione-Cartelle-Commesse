using TA.Transport;
namespace TA.Server;
public static class PushSession
{
    // geCo: solo per i client che dichiarano SupportsGeCoEvents (2.38.0); gli altri non conoscono le notifiche «GeCo».
    public static async Task RunAsync(Stream stream, ChangeHub hub, CancellationToken shutdown,
        bool requireAcknowledgement = false, Action<PushNotice, bool>? trace = null, SharedDatabase.GeCoEventHub? geCo = null)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        using var subscription = hub.Subscribe();
        using var events = geCo?.Subscribe();
        await WireProtocol.WriteAsync(stream, Reply.Ok(true), lifetime.Token);
        while (!lifetime.IsCancellationRequested)
        {
            PushNotice notice;
            using (var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                heartbeat.CancelAfter(TimeSpan.FromSeconds(20));
                try { notice = events == null ? new("Changed", await subscription.ReadAsync(heartbeat.Token), hub.Summary) : await NextAsync(subscription, hub, events, geCo!, heartbeat.Token); }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { notice = new("Heartbeat", 0); }
            }
            if (requireAcknowledgement) notice = notice with { ProbeId = Guid.NewGuid().ToString("N") };
            using var write = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            write.CancelAfter(TimeSpan.FromSeconds(30));
            await WireProtocol.WriteAsync(stream, notice, write.Token);
            trace?.Invoke(notice, false);
            if (!requireAcknowledgement) continue; // Compatibilita con i client precedenti.
            using var receipt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            receipt.CancelAfter(TimeSpan.FromSeconds(120)); // Due richieste State possono richiedere fino a 60 secondi.
            PushAcknowledgement ack;
            try { ack = await WireProtocol.ReadAsync<PushAcknowledgement>(stream, receipt.Token); }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            { throw new TimeoutException("Riscontro client mancante entro 120 secondi; aggiornamento viste non confermato."); }
            if (ack.ProbeId != notice.ProbeId || ack.Revision != notice.Revision || ack.Kind != notice.Kind
                || (notice.Kind == "Changed" && !ack.ViewsUpdated))
                throw new InvalidDataException("Riscontro push non valido o viste non aggiornate.");
            trace?.Invoke(notice, true);
        }
    }
    // Prima notifica disponibile tra le modifiche delle cartelle e gli eventi di GeCo. Le attese non consumano nulla (si legge
    // solo con TryRead), quindi quella che non vince non perde la sua notifica; all'uscita si annullano entrambe.
    private static async Task<PushNotice> NextAsync(ChangeHub.Subscription changes, ChangeHub hub, SharedDatabase.GeCoEventHub.Subscription events,
        SharedDatabase.GeCoEventHub geCo, CancellationToken token)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            while (true)
            {
                if (changes.TryRead(out var revision)) return new("Changed", revision, hub.Summary);
                if (events.TryRead(out var last)) return new(PushNotice.GeCo, last, geCo.Summary);
                var first = await Task.WhenAny(changes.WaitToReadAsync(waiting.Token).AsTask(), events.WaitToReadAsync(waiting.Token).AsTask());
                token.ThrowIfCancellationRequested();
                if (!await first) throw new InvalidOperationException("Sottoscrizione push chiusa.");
            }
        }
        finally { waiting.Cancel(); }
    }
}
