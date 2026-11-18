namespace TA.Transport;
// Una notifica invalida la fotografia locale; il client recupera lo stato completo.
// Kind «GeCo» (2.38.0): nuovi eventi di GeCo2027 nel registro comune; Revision è l'ultimo evento, il client li chiede con il comando GeCo «SyncEvents».
public sealed record PushNotice(string Kind, long Revision, string? Summary = null, string? ProbeId = null)
{
    public const string GeCo = "GeCo";
}
