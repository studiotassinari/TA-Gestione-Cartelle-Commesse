namespace TA.Server;
// Limite della ricerca dei contenitori fisici; il contenuto degli archivi ammessi resta inventariabile.
public static class ArchiveScope
{
    // Compatibilità con le configurazioni precedenti che indicavano soltanto «archivio».
    // Una radice già specifica viene mantenuta, senza aggiungere un secondo _lavori.
    public static string Root(string configured)=>Path.GetFileName(configured.TrimEnd('\\','/')).Equals("archivio",StringComparison.OrdinalIgnoreCase)
        ?Path.Combine(configured,"_lavori"):configured;
    public static bool Contains(string root,string path)
    {
        // Sono ammessi file direttamente nella radice o in una sua sottocartella immediata.
        // Il limite riguarda dove cercare ZIP/7z, non la profondità delle voci al loro interno.
        var relative=Path.GetRelativePath(Root(root),path);
        var parts=relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        return !Path.IsPathRooted(relative)&&parts.Length<=2&&!parts.Any(p=>p is "." or "..")
            && new[]{".zip",".7z"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    }
}
