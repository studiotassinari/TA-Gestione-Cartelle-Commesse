using TA.Transport;
namespace TA.Server;
public static class PushSession
{
    public static async Task RunAsync(Stream stream, ChangeHub hub, CancellationToken shutdown,
        bool requireAcknowledgement = false, Action<PushNotice, bool>? trace = null)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        using var subscription = hub.Subscribe();
        await WireProtocol.WriteAsync(stream, Reply.Ok(true), lifetime.Token);
        while (!lifetime.IsCancellationRequested)
        {
            PushNotice notice;
            using (var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                heartbeat.CancelAfter(TimeSpan.FromSeconds(20));
                try { notice = new("Changed", await subscription.ReadAsync(heartbeat.Token), hub.Summary); }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { notice = new("Heartbeat", 0); }
            }
            if (requireAcknowledgement) notice = notice with { ProbeId = Guid.NewGuid().ToString("N") };
            using var write = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            write.CancelAfter(TimeSpan.FromSeconds(30));
            await WireProtocol.WriteAsync(stream, notice, write.Token);
            trace?.Invoke(notice, false);
            if (!requireAcknowledgement) continue; // Compatibilita con i client precedenti.
            using var receipt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            receipt.CancelAfter(TimeSpan.FromSeconds(90)); // Due richieste State possono richiedere fino a 60 secondi.
            PushAcknowledgement ack;
            try { ack = await WireProtocol.ReadAsync<PushAcknowledgement>(stream, receipt.Token); }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            { throw new TimeoutException("Riscontro client mancante entro 90 secondi; aggiornamento viste non confermato."); }
            if (ack.ProbeId != notice.ProbeId || ack.Revision != notice.Revision || ack.Kind != notice.Kind
                || (notice.Kind == "Changed" && !ack.ViewsUpdated))
                throw new InvalidDataException("Riscontro push non valido o viste non aggiornate.");
            trace?.Invoke(notice, true);
        }
    }
}
