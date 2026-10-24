namespace TA.Transport;
// Una notifica invalida la fotografia locale; il client recupera lo stato completo.
public sealed record PushNotice(string Kind, long Revision);
