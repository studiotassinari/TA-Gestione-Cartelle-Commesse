namespace TA.Server;
// Limite della ricerca dei contenitori fisici; il contenuto degli archivi ammessi resta inventariabile.
public static class ArchiveScope
{
    public static string Root(string configured)=>Path.GetFileName(configured.TrimEnd('\\','/')).Equals("archivio",StringComparison.OrdinalIgnoreCase)
        ?Path.Combine(configured,"_lavori"):configured;
    public static bool Contains(string root,string path)
    {
        var relative=Path.GetRelativePath(Root(root),path);
        var parts=relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        return !Path.IsPathRooted(relative)&&parts.Length<=2&&!parts.Any(p=>p is "." or "..")
            && new[]{".zip",".7z"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    }
}
