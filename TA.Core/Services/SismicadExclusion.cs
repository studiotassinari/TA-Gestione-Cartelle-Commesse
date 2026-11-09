namespace TA_Gestione_Cartelle_Commesse.Services;

// Manuale Concrete Sismicad 12, cap. 17: main.scj identifica la cartella della commessa.
public static class SismicadExclusion
{
    public static bool IsProject(string directory)=>File.Exists(Path.Combine(directory,"main.scj"));
    public static string[] ArchiveRoots(IEnumerable<string?> names)
    {
        return names.Select(n=>(n??"").Replace('\\','/')).Where(n=>!n.StartsWith('/')&&!n.Split('/').Any(p=>p is "." or ".."||p.Contains(':')))
            .Where(n=>n.Split('/').Last().Equals("main.scj",StringComparison.OrdinalIgnoreCase))
            .Select(n=>n.Contains('/')?n[..n.LastIndexOf('/')]:"").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static bool Contains(string key,IEnumerable<string> roots)=>roots.Any(r=>r.Length==0||key.Equals(r,StringComparison.OrdinalIgnoreCase)||key.StartsWith(r+"/",StringComparison.OrdinalIgnoreCase));
}
