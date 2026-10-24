using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
namespace TA.Updater;
public sealed record PackageFile(string Path, long Length, string Sha256);
public sealed record PackageManifest(int SchemaVersion, string Component, string Version, List<PackageFile> Files);
public static class UpdatePackage
{
    public static string SafePath(string root, string relative)
    {
        var segments = relative.Replace('\\', '/').Split('/');
        if (string.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative) || segments.Any(s => s is "" or "." or ".." || s.Contains(':') || s.EndsWith(' ') || s.EndsWith('.')))
            throw new InvalidDataException("Percorso del pacchetto non valido: " + relative);
        if (segments.Any(s => s.StartsWith("unins", StringComparison.OrdinalIgnoreCase) || s.StartsWith(".ta-", StringComparison.OrdinalIgnoreCase)
            || new[] { "UpdateCache", "Logs", "server-settings.json", "appsettings.json" }.Contains(s, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Il pacchetto tenta di sostituire dati o configurazione: " + relative);
        var fullRoot = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Percorso esterno.");
        var cursor = full;
        while (cursor != null && cursor.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Collegamento escluso dal percorso di aggiornamento.");
            cursor = System.IO.Path.GetDirectoryName(cursor);
        }
        return full;
    }
    public static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static PackageManifest Stage(string zipPath, string destination, string component, string version)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > 4000 || zip.Entries.Sum(e => e.Length) > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Pacchetto troppo grande.");
        if (zip.Entries.Count(e => e.FullName == "update-manifest.json") != 1) throw new InvalidDataException("Manifest assente o duplicato.");
        var entry = zip.GetEntry("update-manifest.json") ?? throw new InvalidDataException("Manifest assente.");
        if (entry.Length > 4 * 1024 * 1024) throw new InvalidDataException("Manifest troppo grande.");
        using var source = entry.Open();
        var manifest = JsonSerializer.Deserialize<PackageManifest>(source) ?? throw new InvalidDataException("Manifest vuoto.");
        if (manifest.SchemaVersion != 1 || manifest.Component != component || manifest.Version != version || manifest.Files == null || manifest.Files.Count == 0)
            throw new InvalidDataException("Pacchetto per versione o componente diverso.");
        var expected = manifest.Files.ToDictionary(f => "payload/" + f.Path.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(destination);
        foreach (var item in zip.Entries)
        {
            if (item.FullName == "update-manifest.json") continue;
            if (!seen.Add(item.FullName) || !expected.TryGetValue(item.FullName, out var spec)) throw new InvalidDataException("File non dichiarato o duplicato: " + item.FullName);
            if (((item.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Collegamento nel pacchetto.");
            if (spec.Length != item.Length || spec.Length < 0 || !System.Text.RegularExpressions.Regex.IsMatch(spec.Sha256, "^[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("Metadati del pacchetto non validi.");
            var target = SafePath(destination, spec.Path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var input = item.Open()) input.CopyTo(output);
            if (!Hash(target).Equals(spec.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Impronta file non valida: " + spec.Path);
        }
        if (seen.Count != expected.Count) throw new InvalidDataException("File del pacchetto mancanti.");
        var exe = component == "Server" ? "TA.Server.exe" : "TA-Gestione Cartelle Commesse.exe";
        if (!manifest.Files.Any(f => f.Path == exe) || !manifest.Files.Any(f => f.Path == "Updater/TA.Updater.exe"))
            throw new InvalidDataException("Pacchetto incompleto.");
        return manifest;
    }
}
