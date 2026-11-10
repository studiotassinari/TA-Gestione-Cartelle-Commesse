using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Authentication;
using System.Security.Principal;
using TA.Server;
using TA.Transport;

void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
try
{
LocalReadOnlyPolicy.Validate(new Request("Info"));
AnalysisStatusChecks.Run();
FolderArchiveChecks.Run();
DeepInspectionChecks.Run();
foreach(var (operation,tool) in new[]{("DeepInspection","approfondisci_commessa"),("State","cerca_cartelle"),("Children","sottocartelle"),("DailyReport","report_giornaliero"),("Analysis","analisi_file"),("Files","cerca_file"),("AnalysisStatus","stato_analisi"),("Progress","stato_scansione"),("ReadDocument","leggi_documento"),("ListDocuments","elenca_documenti")})
{
    LocalReadOnlyPolicy.Validate(new Request(operation,CorrelationId:Guid.NewGuid().ToString("N"),McpTool:tool));
    try{LocalReadOnlyPolicy.Validate(new Request(operation));throw new Exception("MCP precedente ammesso");}catch(UnauthorizedAccessException){}
    try{LocalReadOnlyPolicy.Validate(new Request(operation,CorrelationId:Guid.NewGuid().ToString("N"),McpTool:"inesistente"));throw new Exception("Strumento non corrispondente ammesso");}catch(UnauthorizedAccessException){}
}
foreach(var operation in new[] { "Refresh", "Cancel", "CreateJob", "TemplatePreview", "AdminAudit", "Report", "Subscribe", "Unknown" })
{
    try { LocalReadOnlyPolicy.Validate(new Request(operation)); throw new Exception("Operazione ammessa: " + operation); }
    catch(UnauthorizedAccessException) { }
}
try { LocalReadOnlyPolicy.Validate(new Request("State", Version: 1)); throw new Exception("Versione ammessa"); } catch(UnauthorizedAccessException) { }
using var identity = WindowsIdentity.GetCurrent();
var group = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
var security = LocalMcpService.Security(identity.User!, group, null);
var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();
Check(rules.Any(r => r.IdentityReference.Value == "S-1-5-2" && r.AccessControlType == AccessControlType.Deny), "Manca divieto accesso rete");
Check(!rules.Any(r => r.IdentityReference.Equals(group) && (r.PipeAccessRights & PipeAccessRights.CreateNewInstance) != 0), "Gruppo può creare un falso server");
var name = "TA.Mcp.Test." + Guid.NewGuid().ToString("N");
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
using var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096, security);
using var client = new NamedPipeClientStream(".", name, PipeAccessRights.ReadWrite, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation, HandleInheritability.None);
var accepted = server.WaitForConnectionAsync(timeout.Token);
await client.ConnectAsync(timeout.Token);
await accepted;
LocalServiceIdentity.VerifyProcess(client.SafePipeHandle, (uint)Environment.ProcessId);
try { LocalServiceIdentity.VerifyProcess(client.SafePipeHandle, 0); throw new Exception("Falso proprietario non rifiutato"); } catch(AuthenticationException) { }
await WireProtocol.WriteAsync(client, new Request("Info"), timeout.Token);
var request = await WireProtocol.ReadAsync<Request>(server, timeout.Token);
string? caller = null;
server.RunAsClient(() => { using var impersonated = WindowsIdentity.GetCurrent(true); caller = impersonated?.Name; });
Check(caller == identity.Name, "Identità chiamante non corrispondente");
var send = LocalPipeProtocol.WriteReplyAsync(server, Reply.Ok("test"), timeout.Token);
await Task.Delay(100);
Check(!send.IsCompleted, "Il server deve attendere la lettura del client");
var reply = await LocalPipeProtocol.ReadReplyAsync(client, timeout.Token);
await send;
server.Disconnect();
Check(reply.Success && reply.Data.GetString() == "test", "Risposta non ricevuta prima della disconnessione");
Console.WriteLine("PASS: allowlist sola lettura, ACL con rete negata, divieto creazione istanze al gruppo, PID del server, identità Windows e scambio messaggi.");

}
catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
