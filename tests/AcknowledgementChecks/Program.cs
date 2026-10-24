using System.Net;
using System.Net.Sockets;
using TA.Server;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;

static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
var token = timeout.Token;
var logsRead = false;
ServerLogs Logs() { logsRead = true; return new("servizio", "comunicazione"); }
Check(AdministrativeCommands.Handle(new("AdminLogs", ClientMachine: "Domain Admin"), false, Logs)?.Success == false && !logsRead,
    "Utente ordinario: log amministrativi negati dal server senza leggerli");
Check(AdministrativeCommands.Handle(new("AdminAudit", RelativePath: "Restart"), false, Logs)?.Success == false, "Utente ordinario: audit amministrativo negato");
Check(AdministrativeCommands.Handle(new("AdminLogs"), true, Logs)?.Success == true && logsRead, "Amministratore verificato: lettura log consentita");
Check(AdministrativeCommands.Handle(new("AdminAudit", RelativePath: "Other"), true, Logs)?.Success == false, "Azione amministrativa sconosciuta rifiutata");
Check(AdministrativeCommands.Handle(new("Ping", Version: 999), true, Logs)?.Success == false, "Versione protocollo non supportata rifiutata anche sui nuovi comandi");

foreach (var wrong in new[] { false, true })
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
    var confirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var serving = Task.Run(async () =>
    {
        using var accepted = await listener.AcceptTcpClientAsync(token);
        try
        {
            await PushSession.RunAsync(accepted.GetStream(), new ChangeHub(), stop.Token, true,
                (notice, received) => { if (received) confirmed.TrySetResult(); });
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    });
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, token);
    var stream = client.GetStream();
    Check((await WireProtocol.ReadAsync<Reply>(stream, token)).Success, "Sottoscrizione con verifica bidirezionale accettata");
    var change = await WireProtocol.ReadAsync<PushNotice>(stream, token);
    Check(change.ProbeId?.Length == 32, "Ogni avviso richiede una prova specifica");
    await WireProtocol.WriteAsync(stream, new PushAcknowledgement(wrong ? "non-corrisponde" : change.ProbeId!, change.Kind, change.Revision, true), token);
    if (wrong)
    {
        try { await serving.WaitAsync(token); throw new Exception("Riscontro errato accettato"); }
        catch (InvalidDataException) { Check(!confirmed.Task.IsCompleted, "Riscontro di altra notifica rifiutato senza registrare viste aggiornate"); }
    }
    else
    {
        await confirmed.Task.WaitAsync(token);
        Check(true, "Server conferma aggiornamento viste solo dopo il riscontro");
        var heartbeat = await WireProtocol.ReadAsync<PushNotice>(stream, token);
        Check(heartbeat.Kind == "Heartbeat" && heartbeat.ProbeId != change.ProbeId, "Heartbeat verifica il canale con una nuova prova");
        await WireProtocol.WriteAsync(stream, new PushAcknowledgement(heartbeat.ProbeId!, heartbeat.Kind, heartbeat.Revision, false), token);
        stop.Cancel(); await serving.WaitAsync(token);
    }
}
