using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.ServiceProcess;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
namespace TA.Updater;

public sealed class UpdateEngine(string component, string root, Action<string, bool> progress)
{
    private const string Repository = "studiotassinari/TA-Gestione-Cartelle-Commesse";
    private readonly string root = Path.GetFullPath(root).TrimEnd('\\');
    private string Cache => component == "Server" ? Path.Combine(root, "UpdateCache") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tassinari", "CommesseClient", "Updates");
    private string Exe => Path.Combine(root, component == "Server" ? "TA.Server.exe" : "TA-Gestione Cartelle Commesse.exe");
    private string Interface => component == "Server" ? Path.Combine(root, "Manager", "TA.ServiceManager.exe") : Exe;
    private void Note(string message, bool show = true)
    {
        Directory.CreateDirectory(Cache); File.AppendAllText(Path.Combine(Cache, "update.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        progress(message, show);
    }
    public static bool IsAdministrator()
    { using var user = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator); }
    public static string FindRoot(string component)
    {
        if (component == "Server")
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TACommesseServer");
            var command = key?.GetValue("ImagePath") as string ?? throw new IOException("Servizio TACommesseServer non installato.");
            var match = System.Text.RegularExpressions.Regex.Match(command, "^\"(?<path>[^\"]+)\"|^(?<path>.+?\\.exe)(?:\\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) throw new IOException("Percorso del servizio non valido.");
            return Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(match.Groups["path"].Value))!;
        }
        using var client = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{D0B63D8F-FF51-4B68-9553-4B49A78C7B3A}_is1");
        return client?.GetValue("InstallLocation") as string ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TA-Gestione Cartelle Commesse");
    }
    public async Task<bool> Run()
    {
        if (component is not ("Client" or "Server") || !File.Exists(Exe) || !File.Exists(Path.Combine(root, "TA.Contracts.dll")))
            throw new IOException("Installazione TA non trovata nella cartella selezionata.");
        if (!Path.GetFullPath(FindRoot(component)).TrimEnd('\\').Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("La cartella non coincide con l'installazione registrata.");
        if (component == "Server" && !IsAdministrator()) throw new UnauthorizedAccessException("L'aggiornamento server richiede privilegi amministrativi.");
        Directory.CreateDirectory(Cache);
        using var lease = new FileStream(Path.Combine(Cache, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        bool reopen = false, wasRunning = false;
        try
        {
            if (FileTransaction.Pending(Cache) is { } interrupted)
            {
                Note("Ripristino dell'aggiornamento interrotto...");
                reopen = CloseInterfaces();
                FileTransaction.Recover(root, Cache, () => { if (component == "Server") StopService(); });
                if (component == "Server" && interrupted.ServiceWasRunning) StartService();
                Note("Versione precedente ripristinata.");
            }
            var current = new Version(FileVersionInfo.GetVersionInfo(Exe).FileVersion!);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TA-Direct-Updater/2.7");
            using var reply = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest").WaitAsync(TimeSpan.FromSeconds(30));
            reply.EnsureSuccessStatusCode();
            using var data = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
            var release = data.RootElement;
            var tag = release.GetProperty("tag_name").GetString()!;
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()
                || !System.Text.RegularExpressions.Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$")) throw new IOException("Release stabile non valida.");
            var version = tag[1..]; var latest = new Version(version + ".0");
            if (latest <= current) { Note("Versione " + current.ToString(3) + " gia aggiornata.", false); return false; }
            if (latest.Major != current.Major) throw new IOException("Cambio di versione principale: aggiornamento automatico non applicato.");
            var name = $"TA-Commesse-{component}-Update-{version}.zip";
            var assets = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
            if (assets.Length != 1) throw new IOException("Pacchetto di aggiornamento diretto non disponibile.");
            var asset = assets[0]; var url = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
            var digest = asset.GetProperty("digest").GetString()!;
            if (asset.GetProperty("browser_download_url").GetString() != url || !System.Text.RegularExpressions.Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
                throw new IOException("Origine o impronta del pacchetto non valida.");
            var size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > 1024L * 1024 * 1024) throw new IOException("Dimensione pacchetto non valida.");
            var work = Path.Combine(Cache, "download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
            var zip = Path.Combine(work, name);
            Note("Download dei file " + component + " " + version + "...");
            using (var download = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                download.EnsureSuccessStatusCode();
                if (download.RequestMessage?.RequestUri?.Scheme != "https") throw new IOException("Download non cifrato rifiutato.");
                using var source = await download.Content.ReadAsStreamAsync();
                using var target = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[131072]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                { total += read; if (total > size) throw new IOException("Download oltre la dimensione attesa."); await target.WriteAsync(buffer.AsMemory(0, read)); }
                if (total != size) throw new IOException("Download incompleto.");
            }
            if (!UpdatePackage.Hash(zip).Equals(digest[7..], StringComparison.OrdinalIgnoreCase)) throw new IOException("Impronta del download errata: nessun file installato modificato.");
            Note("Verifica dei file del pacchetto...");
            var stage = Path.Combine(work, "stage");
            var manifest = UpdatePackage.Stage(zip, stage, component, version);
            foreach (var file in manifest.Files) UpdatePackage.SafePath(root, file.Path);
            Note("Chiusura dell'interfaccia e preparazione del backup...");
            reopen |= CloseInterfaces();
            if (component == "Server") { using var service = new ServiceController("TACommesseServer"); wasRunning = service.Status != ServiceControllerStatus.Stopped; StopService(); }
            Note("Sostituzione dei soli file aggiornati...");
            try
            {
                int replaced = FileTransaction.Apply(root, stage, manifest, Cache, wasRunning, () =>
                {
                    if (new Version(FileVersionInfo.GetVersionInfo(Exe).FileVersion!) != latest) throw new IOException("Versione installata diversa dal pacchetto.");
                    if (component == "Server" && wasRunning) { StartService(); Thread.Sleep(5000); using var service = new ServiceController("TACommesseServer"); if (service.Status != ServiceControllerStatus.Running) throw new IOException("Il servizio aggiornato non rimane in esecuzione."); }
                }, () => { if (component == "Server") StopService(); });
                UpdateDisplayVersion(version);
                Note($"Aggiornamento a {version} completato: {replaced} file sostituiti. Configurazione e dati conservati.");
            }
            catch
            {
                if (component == "Server" && wasRunning && FileTransaction.Pending(Cache) == null) StartService();
                throw;
            }
            return true;
        }
        catch (Exception ex)
        { Note("Aggiornamento non completato: " + ex.Message + (FileTransaction.Pending(Cache) != null ? " Ripristino pendente: riaprire l'aggiornatore." : " I file precedenti sono conservati o ripristinati.")); throw; }
        finally { if (reopen && FileTransaction.Pending(Cache) == null) Reopen(); }
    }
    private bool CloseInterfaces()
    {
        bool any = false;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Interface)))
        {
            using (process)
            {
                if (!string.Equals(process.MainModule?.FileName, Interface, StringComparison.OrdinalIgnoreCase)) continue;
                any = true; File.WriteAllText(Path.Combine(Cache, "exit-" + process.Id), "update");
                if (!process.CloseMainWindow()) EnumWindows((window, _) => { GetWindowThreadProcessId(window, out var id); if (id == process.Id && GetWindowTextLength(window) > 0) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); return true; }, IntPtr.Zero);
                if (!process.WaitForExit(30000)) { File.Delete(Path.Combine(Cache, "exit-" + process.Id)); throw new IOException("Interfaccia ancora occupata: chiuderla con Esci e riprovare. Nessuna terminazione forzata."); }
            }
        }
        return any;
    }
    private void Reopen()
    { var start = new ProcessStartInfo(Interface) { UseShellExecute = false }; start.Environment["TA_SKIP_UPDATE_ONCE"] = "1"; Process.Start(start); }
    private static void StopService()
    {
        using var service = new ServiceController("TACommesseServer");
        if (service.Status == ServiceControllerStatus.Stopped) return;
        if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(90));
    }
    private static void StartService()
    { using var service = new ServiceController("TACommesseServer"); service.Refresh(); if (service.Status != ServiceControllerStatus.Running) { service.Start(); service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60)); } }
    private void UpdateDisplayVersion(string version)
    {
        try
        {
            var hive = component == "Server" ? Registry.LocalMachine : Registry.CurrentUser;
            var id = component == "Server" ? "{07E5CE80-53A7-4B01-A6C7-E2898FBDF247}" : "{D0B63D8F-FF51-4B68-9553-4B49A78C7B3A}";
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + id + "_is1", true);
            key?.SetValue("DisplayVersion", version);
        }
        catch (Exception ex) { Note("File aggiornati; versione nell'elenco App non aggiornata: " + ex.Message); }
    }
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int process);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
