using ModelContextProtocol;
using TA.Transport;
namespace TA.Mcp;

// Lo SDK nasconde le eccezioni generiche: comunica solo errori applicativi controllati, senza stack trace.
public static class McpErrors
{
    public static async Task<T> Run<T>(Func<Task<T>> request)
    {
        try { return await request(); }
        catch (ServerRejectedException ex) { throw new McpException("Il servizio ha rifiutato questa operazione: " + ex.Message[..Math.Min(ex.Message.Length, 1600)]); }
        catch (System.Security.Authentication.AuthenticationException) { throw new McpException("Autenticazione Windows non riuscita tra MCP e servizio. Verificare l'account del processo, il gruppo autorizzato e il canale configurato."); }
        catch (UnauthorizedAccessException) { throw new McpException("Il processo MCP non è autorizzato al canale Windows del servizio."); }
        catch (OperationCanceledException) { throw new McpException("Operazione annullata o scaduta. Nessuna elaborazione in background è stata accodata."); }
        catch (IOException) { throw new McpException("Comunicazione con il servizio interrotta. Non equivale a zero documenti; verificare il servizio e riprovare su richiesta."); }
        catch (System.Net.Sockets.SocketException) { throw new McpException("Servizio non raggiungibile. Verificare DNS, porta dedicata e stato del servizio."); }
    }
}
