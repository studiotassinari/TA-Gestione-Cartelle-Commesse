using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Principal;

using System.Text.Json;
namespace TA.Transport;
// Connessione Kerberos cifrata e firmata: credenziali esplicite facoltative, mai salvate su disco.
public sealed class ServerConnection(string host, int port, NetworkCredential? credentials = null)
{
    public async Task<T> SendAsync<T>(Request request, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Contains('/') || port is < 1024 or > 65535 or 8080 or 8443)
            throw new InvalidOperationException("Configurare il nome DNS del server e una porta TCP dedicata.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct);
        using var authenticated = new NegotiateStream(tcp.GetStream(), false);
        await authenticated.AuthenticateAsClientAsync(credentials ?? CredentialCache.DefaultNetworkCredentials,
            "TACommesse/" + host, ProtectionLevel.EncryptAndSign, TokenImpersonationLevel.Identification).WaitAsync(ct);
        // L'SPN deve essere registrato sull'account di servizio: un ripiego non mutuo viene rifiutato.
        if (!authenticated.IsMutuallyAuthenticated || !authenticated.IsEncrypted || !authenticated.IsSigned)
            throw new AuthenticationException("Autenticazione mutua non riuscita. Verificare SPN e account del servizio.");
        await WireProtocol.WriteAsync(authenticated, request, ct);
        var reply = await WireProtocol.ReadAsync<Reply>(authenticated, ct);
        if (!reply.Success) throw new ServerRejectedException(reply.Error ?? "Richiesta rifiutata dal server.");
        return reply.Data.Deserialize<T>() ?? throw new InvalidDataException("Risposta del server vuota.");
    }
    // Connessione persistente separata dai comandi, sulla stessa porta e con gli stessi controlli AD.
    public async Task WatchAsync(Func<Task> changed, Action ready, CancellationToken token)
    {
        using var tcp = new TcpClient();
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
        startup.CancelAfter(TimeSpan.FromSeconds(30));
        await tcp.ConnectAsync(host, port, startup.Token);
        using var authenticated = new NegotiateStream(tcp.GetStream(), false);
        try
        {
            await authenticated.AuthenticateAsClientAsync(credentials ?? CredentialCache.DefaultNetworkCredentials,
                "TACommesse/" + host, ProtectionLevel.EncryptAndSign, TokenImpersonationLevel.Identification).WaitAsync(startup.Token);
            if (!authenticated.IsMutuallyAuthenticated || !authenticated.IsEncrypted || !authenticated.IsSigned)
                throw new AuthenticationException("Autenticazione mutua push non riuscita. Verificare SPN e account.");
            await WireProtocol.WriteAsync(authenticated, new Request("Subscribe"), startup.Token);
            var reply = await WireProtocol.ReadAsync<Reply>(authenticated, startup.Token);
            if (!reply.Success) throw new AuthenticationException(reply.Error ?? "Sottoscrizione rifiutata.");
        }
        catch (IOException ex) { throw new AuthenticationException("Sottoscrizione non accettata; verificare registro server e versione installata.", ex); }
        ready();
        while (true)
        {
            using var read = CancellationTokenSource.CreateLinkedTokenSource(token);
            read.CancelAfter(TimeSpan.FromSeconds(60));
            var notice = await WireProtocol.ReadAsync<PushNotice>(authenticated, read.Token);
            if (notice.Kind == "Changed") await changed();
            else if (notice.Kind != "Heartbeat") throw new InvalidDataException("Notifica push non riconosciuta.");
        }
    }

}
