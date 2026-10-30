namespace TA_Gestione_Cartelle_Commesse.Models;

// Traduce soltanto la condivisione aziendale quando viene aperta dal PC dell'utente.
public static class ClientPaths
{
    public static string ForExplorer(string path)
    {
        foreach (var root in new[] { @"\\ingtassinari.it\data", @"\\ingtassinari\data" })
        {
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return @"Z:\";
            if (path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return @"Z:\" + path[(root.Length + 1)..];
        }
        return path;
    }
}
