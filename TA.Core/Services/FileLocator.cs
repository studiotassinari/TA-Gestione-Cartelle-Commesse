using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA_Gestione_Cartelle_Commesse.Services;

public static class FileLocator
{
    public static FileLocation Locate(InventoryUnit unit, InventoryEntry entry)
    {
        // Una radice archivio rappresenta un file, non una cartella in cui aggiungere il nome ZIP una seconda volta.
        var archiveUnit = unit.Category == "A" && FileInventoryReader.IsArchive(unit.Path);
        static string Join(string root, string relative) => Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var physical = archiveUnit ? unit.Path : Join(unit.Path, entry.Container ?? entry.RelativePath);
        var internalPath = entry.Container == null ? null : entry.RelativePath;
        var name = entry.RelativePath.Replace('\\','/').Split('/').Last();
        var relative = entry.RelativePath.Replace('\\','/');
        var slash = relative.LastIndexOf('/');
        var parent = slash < 0 ? "" : relative[..slash];
        // Le voci interne ZIP possono distinguere maiuscole e minuscole, a differenza dei percorsi Windows.
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { physical.ToUpperInvariant(), internalPath }))));
        return new(id, name, Path.GetExtension(name).ToLowerInvariant(), physical,
            internalPath == null ? null : physical, internalPath, parent);
    }
}
