using System.Buffers.Binary;
using System.Text.Json;
namespace TA.Transport;
// JSON UTF-8 preceduto da quattro byte di lunghezza, con limite di memoria esplicito.
public static class WireProtocol
{
    public const int MaxFrameBytes = 32 * 1024 * 1024;
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > MaxFrameBytes) throw new InvalidDataException("Dimensione messaggio non valida.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, token);
        return JsonSerializer.Deserialize<T>(payload) ?? throw new InvalidDataException("Messaggio vuoto.");
    }
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (payload.Length > MaxFrameBytes) throw new InvalidDataException("Risposta troppo grande per il protocollo.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(payload, token);
        await stream.FlushAsync(token);
    }
}
