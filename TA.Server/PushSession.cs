using TA.Transport;
namespace TA.Server;
public static class PushSession
{
    // Scadenza per rinnovare periodicamente autenticazione e autorizzazione del gruppo.
    public static async Task RunAsync(Stream stream, ChangeHub hub, CancellationToken shutdown)
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
                try { notice = new("Changed", await subscription.ReadAsync(heartbeat.Token)); }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { notice = new("Heartbeat", 0); }
            }
            using var write = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            write.CancelAfter(TimeSpan.FromSeconds(30));
            await WireProtocol.WriteAsync(stream, notice, write.Token);
        }
    }
}
