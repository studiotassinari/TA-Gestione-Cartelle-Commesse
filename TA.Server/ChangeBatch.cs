namespace TA.Server;
// Raggruppa raffiche di eventi senza perdere modifiche arrivate durante una scansione.
public sealed class ChangeBatch
{
    private readonly object gate = new();
    private long revision;
    private bool pending;
    private DateTime first, last;
    public bool Mark(DateTime now)
    {
        lock (gate) { bool isNew = !pending; if (isNew) first = now; pending = true; last = now; revision++; return isNew; }
    }
    public bool TryGetDue(DateTime now, out long version)
    {
        lock (gate)
        {
            version = revision;
            return pending && (now - last >= TimeSpan.FromSeconds(3) || now - first >= TimeSpan.FromSeconds(30));
        }
    }
    public void Acknowledge(long version) { lock (gate) { if (revision == version) pending = false; } }
}
