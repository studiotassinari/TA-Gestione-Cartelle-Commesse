using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TA.Server;

if(args.Contains("--document-worker")){await TA.Server.Documents.DocumentWorker.RunAsync();return;}

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
    || new[] { settings.JobsRoot, settings.BidsRoot, settings.ArchivedJobsRoot }.Any(p => !p.StartsWith(@"\\"))
    || string.IsNullOrWhiteSpace(settings.ArchiveFolder))
    throw new InvalidOperationException("Configurare gruppo, porta TCP dedicata e percorsi UNC in " + settingsFile);
using (var serviceIdentity = System.Security.Principal.WindowsIdentity.GetCurrent())
    StartupLog.Write($"Configurazione valida; account servizio={serviceIdentity.Name}; gruppo autorizzato={settings.AllowedGroup}; versione={System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}.");
if (string.IsNullOrWhiteSpace(settings.DatabaseFolder))
    settings.DatabaseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tassinari", "CommesseServer", "Data");
settings.MaxConcurrentScans = Math.Clamp(settings.MaxConcurrentScans, 1, 4);
ScanConcurrency.Configure(settings.MaxConcurrentScans);
_ = GeologyStorage.Root(settings);
if(settings.LocalStorageOnly)
{
    var runtime=Path.GetFullPath(settings.ArchiveFolder);
    if(new Uri(runtime).IsUnc || new DriveInfo(Path.GetPathRoot(runtime)!).DriveType==DriveType.Network || !settings.BackupFolder.StartsWith(@"\\") || !string.IsNullOrEmpty(settings.GeologyCatalogPath))
        throw new InvalidOperationException("Modalità locale: archivio operativo su disco locale, backup UNC e nessun catalogo esterno.");
    Directory.CreateDirectory(runtime);
}
var temporaryFolder=Path.Combine(settings.InventoryFolder,"Temp");
Directory.CreateDirectory(temporaryFolder);
Environment.SetEnvironmentVariable("TEMP",temporaryFolder);
Environment.SetEnvironmentVariable("TMP",temporaryFolder);
builder.Services.AddWindowsService(options => options.ServiceName = "TACommesseServer");
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(new ChangeHub(false));
builder.Services.AddSingleton<AnalysisService>();
builder.Services.AddSingleton<FileSearchService>();
builder.Services.AddSingleton<GeologyService>();
builder.Services.AddSingleton<IpripiService>();
builder.Services.AddSingleton<GeologyIndexer>();
builder.Services.AddSingleton<IpripiIndexer>();
builder.Services.AddSingleton<TA.Server.Documents.DocumentService>();
builder.Services.AddSingleton<TemplateCreationService>();
builder.Services.AddSingleton<ScanCoordinator>();
builder.Services.AddSingleton<TA.Server.Business.BusinessService>();
builder.Services.AddHostedService<TA.Server.Business.BusinessBackupService>();
builder.Services.AddHostedService<DailyDatabaseBackupService>();
builder.Services.AddHostedService(p => p.GetRequiredService<ScanCoordinator>());
builder.Services.AddHostedService<TcpService>();
builder.Services.AddHostedService<LocalMcpService>();
// Gli eventi filesystem non avviano scansioni fuori dalla scansione iniziale e giornaliera.
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
