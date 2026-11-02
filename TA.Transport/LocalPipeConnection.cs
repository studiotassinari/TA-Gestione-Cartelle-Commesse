using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace TA.Transport;

// Canale riservato a processi sulla stessa macchina; nessun ripiego da Kerberos a NTLM.
public sealed class LocalPipeConnection
{
    public const string PipeName = "TACommesseServer.Mcp.ReadOnly.v1";
    public async Task<T> SendAsync<T>(Request request, CancellationToken token = default)
    {
        LocalCatalogPolicy.Validate(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeAccessRights.ReadWrite,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation, HandleInheritability.None);
        await pipe.ConnectAsync(timeout.Token);
        // Verifica Windows del processo proprietario, prima di inviare la richiesta.
        LocalServiceIdentity.Verify(pipe.SafePipeHandle);
        await WireProtocol.WriteAsync(pipe, request with { ClientMachine = Environment.MachineName,
            ClientVersion = "MCP-local/"+System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version, CorrelationId = request.CorrelationId ?? Guid.NewGuid().ToString("N") }, timeout.Token);
        var reply = await LocalPipeProtocol.ReadReplyAsync(pipe, timeout.Token);
        if (!reply.Success) throw new ServerRejectedException(reply.Error ?? "Richiesta locale rifiutata.");
        return reply.Data.Deserialize<T>() ?? throw new InvalidDataException("Risposta locale vuota.");
    }
}
