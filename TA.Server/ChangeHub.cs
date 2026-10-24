using System.Threading.Channels;
namespace TA.Server;
// Ogni client ha una coda limitata: basta conservare la revisione piu recente.
public sealed class ChangeHub
{
    private readonly object gate = new();
    private readonly HashSet<Subscription> subscribers = [];
    private long revision;
    private string? summary;
    public string? Summary { get { lock (gate) return summary; } }
    public Subscription Subscribe()
    {
        lock (gate)
        {
            var subscription = new Subscription(this);
            subscribers.Add(subscription);
            subscription.Queue.Writer.TryWrite(revision);
            return subscription;
        }
    }
    public void Publish(string? message = null)
    {
        lock (gate)
        {
            if (message != null) summary = message;
            revision++;
            foreach (var subscriber in subscribers) subscriber.Queue.Writer.TryWrite(revision);
        }
    }
    public sealed class Subscription(ChangeHub owner) : IDisposable
    {
        internal Channel<long> Queue { get; } = Channel.CreateBounded<long>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        public ValueTask<long> ReadAsync(CancellationToken token) => Queue.Reader.ReadAsync(token);
        public void Dispose()
        {
            lock (owner.gate) { owner.subscribers.Remove(this); Queue.Writer.TryComplete(); }
        }
    }
}
