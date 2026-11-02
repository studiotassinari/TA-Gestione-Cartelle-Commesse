using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Server;

public static class GeologyStorage
{
    public static string Root(ServerSettings settings)
    {
        var root = Path.GetFullPath(Path.Combine(settings.ArchiveFolder, "geologia"));
        EnsureOutput(settings, root);
        return root;
    }
    public static string CatalogPath(ServerSettings settings)
    {
        var file = string.IsNullOrWhiteSpace(settings.GeologyCatalogPath) ? Path.Combine(Root(settings), "catalogo.json") : Path.GetFullPath(settings.GeologyCatalogPath);
        EnsureOutput(settings, file); return file;
    }
    private static void EnsureOutput(ServerSettings settings, string output)
    {
        foreach (var value in new[] { settings.JobsRoot, settings.BidsRoot, settings.ArchivedJobsRoot }.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var root = Path.GetFullPath(value).TrimEnd('\\', '/');
            if (output.Equals(root, StringComparison.OrdinalIgnoreCase) || output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Il catalogo geologico deve essere salvato fuori dalle sorgenti in sola lettura.");
        }
    }
    public static void Atomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) JsonSerializer.Serialize(file, value);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record GeologicalUnitIndex(string Generation, List<GeologicalReport> Reports);
public sealed record GeologicalPositions(List<GeologicalPosition> Positions);
