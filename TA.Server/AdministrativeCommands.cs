using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server;
// Il booleano deriva dal token Windows verificato dal server, mai da un campo della richiesta.
public static class AdministrativeCommands
{
    public static Reply? Handle(Request request, bool domainAdmin, Func<ServerLogs> readLogs)
    {
        if (request.Version != 2) return Reply.Fail("Versione del protocollo non supportata.");
        return request.Operation switch
        {
            "Ping" => Reply.Ok(request.CorrelationId ?? ""),
            "Permissions" => Reply.Ok(new ServerPermissions(domainAdmin)),
            "AdminAudit" when domainAdmin && request.RelativePath is "Start" or "Stop" or "Restart" => Reply.Ok(true),
            "AdminAudit" => Reply.Fail("Operazione riservata agli amministratori di dominio o azione non valida."),
            "AdminLogs" when domainAdmin => Reply.Ok(readLogs()),
            "AdminLogs" => Reply.Fail("Operazione riservata agli amministratori di dominio."),
            _ => null
        };
    }
}
