using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using TA.Transport;

namespace TA.Server;

// Endpoint locale: ACL Windows, consultazioni e sola scrittura tipizzata del catalogo, se abilitata.
public sealed class LocalMcpService(ServerSettings settings, ScanCoordinator scans, AnalysisService analysis, FileSearchService files, ILogger<LocalMcpService> logger, TA.Server.Documents.DocumentService? documents = null, GeologyService? geology = null) : BackgroundService
{
    public static PipeSecurity Security(SecurityIdentifier owner, SecurityIdentifier group, SecurityIdentifier? admins)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(group, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        if (admins != null) security.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Un errore del solo endpoint MCP non deve interrompere il servizio TCP esistente.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var serviceIdentity = WindowsIdentity.GetCurrent();
                var group = (SecurityIdentifier)new NTAccount(settings.AllowedGroup).Translate(typeof(SecurityIdentifier));
                var domainSid = serviceIdentity.User?.AccountDomainSid;
                var admins = domainSid == null ? null : new SecurityIdentifier(domainSid.Value + "-512");
                using var pipe = NamedPipeServerStreamAcl.Create(LocalPipeConnection.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096,
                    Security(serviceIdentity.User!, group, admins));
                logger.LogInformation("MCP locale pronto: originali in sola lettura, scrittura catalogo={CatalogWrites}, accesso rete negato, gruppo {Group}", settings.EnableMcpGeologyWrites, settings.AllowedGroup);
                while (!stoppingToken.IsCancellationRequested)
                {
                    await pipe.WaitForConnectionAsync(stoppingToken);
                    await Handle(pipe, group, admins, stoppingToken);
                    if (pipe.IsConnected) pipe.Disconnect();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "MCP locale non disponibile; TCP/Kerberos resta indipendente");
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
    private async Task Handle(NamedPipeServerStream pipe, SecurityIdentifier group, SecurityIdentifier? admins, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        Request? request = null;
        string user = "Non autenticato";
        try
        {
            // Lettura necessaria prima di impersonare; nessuna azione viene ancora eseguita.
            request = await WireProtocol.ReadAsync<Request>(pipe, timeout.Token);
            bool allowed = false;
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent(true) ?? throw new UnauthorizedAccessException("Identità Windows assente.");
                user = identity.Name;
                var principal = new WindowsPrincipal(identity);
                allowed = principal.IsInRole(group) || (admins != null && principal.IsInRole(admins));
            });
            if (!allowed) throw new UnauthorizedAccessException("Account non autorizzato al canale MCP locale.");
            LocalCatalogPolicy.Validate(request);
            var reply = await Dispatch(request, timeout.Token, user);
            await LocalPipeProtocol.WriteReplyAsync(pipe, reply, timeout.Token);
            CommunicationLog.Write(Environment.MachineName, "pipe-locale", user, request.Operation, "Risposta inviata", "MCP su richiesta; strumento="+request.McpTool+"; client="+request.ClientVersion, request.CorrelationId);
        }
        catch (Exception ex)
        {
            CommunicationLog.Write(Environment.MachineName, "pipe-locale", user, request?.Operation ?? "Connessione", "Errore", ex.Message, request?.CorrelationId);
            try { await LocalPipeProtocol.WriteReplyAsync(pipe, Reply.Fail(ex.Message), timeout.Token); } catch { /* Il chiamante può essersi disconnesso. */ }
        }
    }
    private async Task<Reply> Dispatch(Request request,CancellationToken token,string user)
    {
        switch (request.Operation)
        {
            case "PendingGeologicalAnalyses": return Reply.Ok((geology ?? throw new IOException("Catalogo geologico non disponibile.")).Pending(request.GeologicalPending ?? new(), token));
            case "GeologicalDossiers": return Reply.Ok((geology ?? throw new IOException("Catalogo geologico non disponibile.")).Dossiers(request.GeologicalDossiers ?? new(), token));
            case "SaveGeologicalAnalysis": return Reply.Ok(await (geology ?? throw new IOException("Catalogo geologico non disponibile.")).SaveAnalysisAsync(request.GeologicalAnalysis ?? throw new ArgumentException("Analisi mancante."), user, token));
            case "Geology": return Reply.Ok((geology ?? throw new IOException("Catalogo geologico non disponibile sul server.")).Query(request.Geology ?? new(), token));
            case "ReadGeologicalDocument": return Reply.Ok(await (geology ?? throw new IOException("Catalogo geologico non disponibile sul server.")).ReadAsync(request.GeologicalRead ?? throw new ArgumentException("Relazione mancante."), token));
            case "ReadDocument": return Reply.Ok((await (documents ?? throw new IOException("Lettore documenti non configurato.")).ExecuteAsync(request.Category,request.Document,null,token)).Document);
            case "ListDocuments": return Reply.Ok((await (documents ?? throw new IOException("Lettore documenti non configurato.")).ExecuteAsync(request.Category,null,request.Documents,token)).Listing);
            case "Info": return Reply.Ok(typeof(LocalMcpService).Assembly.GetName().Version?.ToString(3) ?? "Sconosciuta");
            case "State":
                var state = scans.Get(request.Category).State;
                return Reply.Ok(state with { Snapshot = state.Snapshot == null ? null : state.Snapshot with {
                    Records = state.Snapshot.Records.Select(r => r.Inspection == null ? r : r with { Inspection = r.Inspection with { ArchiveDirectories = [] } }).ToList() } });
            case "Children": return Reply.Ok(HierarchyService.Children(scans.Get(request.Category).State, request.FolderId ?? "", request.RelativePath));
            case "Progress": return Reply.Ok(scans.Progress);
            case "DailyReport": return Reply.Ok(scans.DailyReport());
            case "Files": return Reply.Ok(files.Search(request.Files ?? new(), token));
            case "Analysis": return Reply.Ok(analysis.Query(request.Analysis ?? new()));
            default: throw new UnauthorizedAccessException("Comando non ammesso.");
        }
    }
}
