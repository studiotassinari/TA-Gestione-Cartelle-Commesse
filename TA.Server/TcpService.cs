using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;
// Ogni comando richiede cifratura, firma, autenticazione Windows mutua e appartenenza al gruppo configurato.
public sealed class TcpService(ServerSettings settings, ScanCoordinator scans, ChangeHub changes, TemplateCreationService templates, ILogger<TcpService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var listener = new TcpListener(IPAddress.Any, settings.Port);
        using var slots = new SemaphoreSlim(16);
        using var pushSlots = new SemaphoreSlim(64);
        var sessions = new List<Task>();
        listener.Start();
        logger.LogInformation("TCP pronto: servizio TCP/Kerberos in ascolto sulla porta {Port}", settings.Port);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await slots.WaitAsync(stoppingToken);
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(stoppingToken); }
                catch { slots.Release(); throw; }
                sessions.RemoveAll(t => t.IsCompleted);
                sessions.Add(Handle(client, slots, pushSlots, stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { listener.Stop(); await Task.WhenAll(sessions); }
    }
    private async Task Handle(TcpClient tcp, SemaphoreSlim slots, SemaphoreSlim pushSlots, CancellationToken shutdown)
    {
        using (tcp)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var ct = timeout.Token;
            bool commandSlot = true, pushSlot = false;
            var remote = tcp.Client.RemoteEndPoint?.ToString() ?? "Sconosciuto";
            string user = "Non autenticato";
            Request? received = null;
            void Trace(string action, string outcome, string detail) => CommunicationLog.Write(received?.ClientMachine, remote, user, action, outcome, detail, received?.CorrelationId);
            try
            {
                using var authenticated = new NegotiateStream(tcp.GetStream(), false);
                await authenticated.AuthenticateAsServerAsync(CredentialCache.DefaultNetworkCredentials, ProtectionLevel.EncryptAndSign, TokenImpersonationLevel.Identification).WaitAsync(ct);
                if (!authenticated.IsEncrypted || !authenticated.IsSigned || !authenticated.IsMutuallyAuthenticated)
                    throw new AuthenticationException($"Protezione Windows insufficiente: cifrata={authenticated.IsEncrypted}, firmata={authenticated.IsSigned}, mutua={authenticated.IsMutuallyAuthenticated}. Verificare SPN e account del servizio.");
                if (authenticated.RemoteIdentity is not WindowsIdentity identity)
                    throw new AuthenticationException("Identita remota non Windows: " + authenticated.RemoteIdentity?.GetType().Name);
                user = identity.Name;
                using var serviceIdentity = WindowsIdentity.GetCurrent();
                var domainSid = serviceIdentity.User?.AccountDomainSid;
                bool domainAdmin = domainSid != null && new WindowsPrincipal(identity).IsInRole(new SecurityIdentifier(domainSid.Value + "-512"));
                // Risolve esplicitamente il gruppo per distinguere configurazione errata da utente escluso.
                SecurityIdentifier groupSid;
                try { groupSid = (SecurityIdentifier)new NTAccount(settings.AllowedGroup).Translate(typeof(SecurityIdentifier)); }
                catch (IdentityNotMappedException)
                {
                    logger.LogWarning("Gruppo configurato non risolto: {Group}; utente {User}", settings.AllowedGroup, identity.Name);
                    received = await WireProtocol.ReadAsync<Request>(authenticated, ct);
                    await WireProtocol.WriteAsync(authenticated, Reply.Fail("Gruppo autorizzato non risolvibile sul server. Contattare l'amministratore."), ct);
                    return;
                }
                if (!new WindowsPrincipal(identity).IsInRole(groupSid) && !domainAdmin)
                {
                    // Il canale e gia autenticato e cifrato: risponde senza eseguire il comando.
                    received = await WireProtocol.ReadAsync<Request>(authenticated, ct);
                    Trace("Autorizzazione", "Negata", "Account escluso dal gruppo autorizzato"); await WireProtocol.WriteAsync(authenticated, Reply.Fail("Il tuo account Windows non risulta nel gruppo autorizzato dal server. Verificare il gruppo e rinnovare l'accesso Windows."), ct);
                    return;
                }
                var request = received = await WireProtocol.ReadAsync<Request>(authenticated, ct); Trace(request.Operation, "Ricevuta", "Categoria=" + request.Category + "; client=" + request.ClientVersion + (request.Operation == "AdminAudit" ? "; azione dichiarata=" + request.RelativePath : ""));
                if (request.Operation == "Subscribe" && request.Version == 2)
                {
                    if (!await pushSlots.WaitAsync(0, ct))
                    { await WireProtocol.WriteAsync(authenticated, Reply.Fail("Limite di 64 client push raggiunto."), ct); return; }
                    pushSlot = true;
                    slots.Release(); commandSlot = false;
                    timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                    Trace("Subscribe", "Accettata", request.SupportsAcknowledgements ? "Riscontro viste e heartbeat richiesto" : "Client precedente: riscontro non disponibile");
                    await PushSession.RunAsync(authenticated, changes, shutdown, request.SupportsAcknowledgements,
                        (notice, acknowledged) => Trace(notice.Kind, acknowledged ? "Riscontro ricevuto" : "Inviata",
                            "Revisione=" + notice.Revision + "; prova=" + notice.ProbeId + (acknowledged && notice.Kind == "Changed" ? "; viste applicate dal client" : "")));
                    return;
                }
                Reply reply;
                try
                {
                    reply = AdministrativeCommands.Handle(request, domainAdmin, () => new(
                        CommunicationLog.Tail(Path.Combine(CommunicationLog.DirectoryPath, "service.log")),
                        CommunicationLog.Tail(CommunicationLog.FilePath))) ?? Dispatch(request);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or KeyNotFoundException)
                { reply = Reply.Fail(ex.Message); }
                await WireProtocol.WriteAsync(authenticated, reply, ct); Trace(request.Operation, reply.Success ? "Risposta inviata" : "Rifiutata", reply.Error ?? "Comando completato");
            }
            catch (OperationCanceledException) { Trace(received?.Operation ?? "Connessione", "Terminata", shutdown.IsCancellationRequested ? "Arresto servizio" : "Timeout o rinnovo sessione push (5 minuti)"); }
            catch (Exception ex) { Trace(received?.Operation ?? "Connessione", "Errore", ex.Message); }
            finally { if (commandSlot) slots.Release(); if (pushSlot) pushSlots.Release(); }
        }
    }
    private Reply Dispatch(Request request)
    {
        if (request.Version != 2) return Reply.Fail("Versione del protocollo non supportata.");
        switch (request.Operation)
        {
            case "Info": return Reply.Ok(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Sconosciuta");
            case "TemplatePreview":
                if (request.Category != "C") return Reply.Fail("Il modello e disponibile solo per le commesse.");
                return Reply.Ok(templates.Preview(request.Creation ?? throw new ArgumentException("Dati commessa mancanti.")));
            case "CreateJob":
                if (request.Category != "C") return Reply.Fail("Il modello e disponibile solo per le commesse.");
                var created = templates.Create(request.Creation ?? throw new ArgumentException("Dati commessa mancanti."));
                logger.LogInformation("Commessa creata da modello: {Path}", created);
                scans.Get("C").NotifyChange();
                scans.Refresh("C");
                return Reply.Ok(created);
            case "State":
                var state = scans.Get(request.Category).State;
                return Reply.Ok(state with { Snapshot = state.Snapshot == null ? null : state.Snapshot with { Records = state.Snapshot.Records.Select(r => r.Inspection == null ? r : r with { Inspection = r.Inspection with { ArchiveDirectories = [] } }).ToList() } });
            case "Refresh": return Reply.Ok(scans.Refresh(request.Category));
            case "Cancel": scans.Get(request.Category).Cancel(); return Reply.Ok(true);
            case "Children": return Reply.Ok(HierarchyService.Children(scans.Get(request.Category).State, request.FolderId ?? "", request.RelativePath));
            case "DailyReport": return Reply.Ok(scans.DailyReport());
            case "Report":
                var jobs = scans.Get("C").State.Snapshot;
                var bids = scans.Get("G").State.Snapshot;
                if (jobs == null || bids == null) return Reply.Fail("Attendere una prima scansione completa di entrambe le categorie.");
                var archived = scans.Get("A").State.Snapshot;
                var html = ReportWriter.Create(jobs.Root, new ScanResult(jobs.Records, jobs.Warnings), bids.Root, new ScanResult(bids.Records, bids.Warnings), archived?.Root, archived == null ? null : new ScanResult(archived.Records, archived.Warnings));
                return Reply.Ok(html.Replace("<main>", $"<main><p>Verifiche server: commesse {jobs.SavedUtc:O}; gare {bids.SavedUtc:O}. Date UTC.</p>"));
            default: return Reply.Fail("Comando non supportato.");
        }
    }
}
