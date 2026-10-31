using System.IO;

namespace TA.Transport;

// Disconnect scarta i byte non ancora letti: il server attende un riscontro esplicito.
public static class LocalPipeProtocol
{
    public static async Task WriteReplyAsync(Stream pipe, Reply reply, CancellationToken token)
    {
        await WireProtocol.WriteAsync(pipe, reply, token);
        var acknowledgement = new byte[1];
        await pipe.ReadExactlyAsync(acknowledgement, token);
        if (acknowledgement[0] != 1) throw new InvalidDataException("Riscontro locale non valido.");
    }
    public static async Task<Reply> ReadReplyAsync(Stream pipe, CancellationToken token)
    {
        var reply = await WireProtocol.ReadAsync<Reply>(pipe, token);
        await pipe.WriteAsync(new byte[] { 1 }, token);
        await pipe.FlushAsync(token);
        return reply;
    }
}
