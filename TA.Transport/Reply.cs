using System.Text.Json;
namespace TA.Transport;
// Esito del protocollo dedicato, senza intestazioni o stato HTTP.
public sealed record Reply(bool Success, JsonElement Data, string? Error = null)
{
    public static Reply Ok<T>(T data) => new(true, JsonSerializer.SerializeToElement(data));
    public static Reply Fail(string error) => new(false, JsonSerializer.SerializeToElement<object?>(null), error);
}
