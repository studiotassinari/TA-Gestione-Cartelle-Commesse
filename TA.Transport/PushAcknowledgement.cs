namespace TA.Transport;
// Il riscontro viene inviato sul medesimo canale Windows autenticato, dopo l'applicazione alle viste.
public sealed record PushAcknowledgement(string ProbeId, string Kind, long Revision, bool ViewsUpdated);
