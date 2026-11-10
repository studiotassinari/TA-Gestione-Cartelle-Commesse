using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TA.Mcp;

var local = Environment.GetEnvironmentVariable("TA_MCP_TRANSPORT") == "local";
// Stdio non apre porte: stdout è riservato al protocollo, stderr alla diagnostica.
// Diagnostica diretta: non inizializza MCP, non usa il tunnel e non legge documenti.
if (args.Contains("--check"))
{
    var server = Environment.GetEnvironmentVariable("TA_MCP_SERVER");
    Console.WriteLine("Identita Windows: " + System.Security.Principal.WindowsIdentity.GetCurrent().Name);
    Console.WriteLine("Server: " + server);
    try
    {
        if (local)
        {
            var localVersion = await new TA.Transport.LocalPipeConnection().SendAsync<string>(new TA.Transport.Request("Info"));
            Console.WriteLine("OK: canale locale Windows, identita del servizio verificata e account autorizzato. Versione servizio: " + localVersion);
            return;
        }
        if (string.IsNullOrWhiteSpace(server)) throw new InvalidOperationException("TA_MCP_SERVER mancante.");
        var endpointPort = int.Parse(Environment.GetEnvironmentVariable("TA_MCP_PORT") ?? "5443");
        var addresses = await System.Net.Dns.GetHostAddressesAsync(server);
        Console.WriteLine("DNS: " + string.Join(", ", addresses.Select(a => a.ToString())));
        var connection = new TA.Transport.ServerConnection(server, endpointPort);
        var version = await connection.SendAsync<string>(new TA.Transport.Request("Info"));
        Console.WriteLine("OK: autenticazione mutua, cifratura, firma e autorizzazione accettate. Versione servizio: " + version);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
        Environment.ExitCode = 1;
    }
    return;
}
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
if (local) builder.Services.AddSingleton<IReadOnlySource, LocalSource>();
else {
var host = Environment.GetEnvironmentVariable("TA_MCP_SERVER")
    ?? throw new InvalidOperationException("Impostare TA_MCP_SERVER con il DNS del servizio.");
var port = int.Parse(Environment.GetEnvironmentVariable("TA_MCP_PORT") ?? "5443");
builder.Services.AddSingleton<IReadOnlySource>(new WindowsSource(host, port));
}
builder.Services.AddSingleton<IFolderArchiveSource>(sp => (IFolderArchiveSource)sp.GetRequiredService<IReadOnlySource>());
builder.Services.AddSingleton<IIpripiSource>(sp => (IIpripiSource)sp.GetRequiredService<IReadOnlySource>());
builder.Services.AddSingleton<IGeologyAnalysisSource>(sp => (IGeologyAnalysisSource)sp.GetRequiredService<IReadOnlySource>());
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<CommesseTools>().WithTools<GeologyTools>().WithTools<GeologyAnalysisTools>().WithTools<IpripiTools>().WithTools<FolderArchiveTools>();
await builder.Build().RunAsync();
