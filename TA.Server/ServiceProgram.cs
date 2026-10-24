using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TA.Server;

StartupLog.Write("Avvio processo servizio: lettura configurazione.");
try
{
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Logging.AddProvider(new StartupLogProvider());
// Le impostazioni in ProgramData sopravvivono agli aggiornamenti dei binari.
var settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tassinari", "CommesseServer", "server-settings.json");
builder.Configuration.AddJsonFile(settingsFile, optional: true, reloadOnChange: false).AddEnvironmentVariables("TA_");
var settings = builder.Configuration.GetSection("Server").Get<ServerSettings>() ?? new();
if (string.IsNullOrWhiteSpace(settings.AllowedGroup)
    || settings.Port is < 1024 or > 65535 or 8080 or 8443
    || new[] { settings.JobsRoot, settings.BidsRoot, settings.ArchivedJobsRoot, settings.ArchiveFolder }.Any(p => !p.StartsWith(@"\\")))
    throw new InvalidOperationException("Configurare gruppo, porta TCP dedicata e percorsi UNC in " + settingsFile);
using (var serviceIdentity = System.Security.Principal.WindowsIdentity.GetCurrent())
    StartupLog.Write($"Configurazione valida; account servizio={serviceIdentity.Name}; gruppo autorizzato={settings.AllowedGroup}; versione={System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}.");
builder.Services.AddWindowsService(options => options.ServiceName = "TACommesseServer");
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<ChangeHub>();
builder.Services.AddSingleton<TemplateCreationService>();
builder.Services.AddSingleton<ScanCoordinator>();
builder.Services.AddHostedService(p => p.GetRequiredService<ScanCoordinator>());
builder.Services.AddHostedService<TcpService>();
builder.Services.AddHostedService<FolderWatchService>();
using var host = builder.Build();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() => StartupLog.Write("Host avviato; attendere TCP pronto per la disponibilita delle connessioni."));
lifetime.ApplicationStopping.Register(() => StartupLog.Write("Arresto richiesto; chiusura connessioni e scansioni."));
await host.RunAsync();
StartupLog.Write("Servizio terminato.");
}
catch (Exception ex)
{
    StartupLog.Write("ERRORE FATALE DI AVVIO/ESECUZIONE: " + ex);
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
