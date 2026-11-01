namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Richieste esplicite: nessun timer, scansione o campo contenuto nell'inventario.
public sealed record DocumentQuery(string FolderId, string RelativePath, string? ArchiveEntry = null, int Offset = 0, int Length = 12000, string? Version = null);
public sealed record DocumentListQuery(string FolderId, string RelativePath = "", string? ArchiveDirectory = null, string Name = "", string Extension = "", int Offset = 0, int Limit = 50);
public sealed record DocumentEntry(string Name, string RelativePath, string? ArchiveEntry, bool IsDirectory, bool IsArchive, long? Bytes);
public sealed record DocumentListPage(string FolderId, string RelativePath, string? ArchiveDirectory, int Total, int Offset, int? NextOffset, IReadOnlyList<DocumentEntry> Entries, string Note);
public sealed record DocumentPage(string FolderId, string Name, string RelativePath, string? ArchiveEntry, string Version, DateTime ReadUtc, int TotalExtractedCharacters, int Offset, int? NextOffset, string Text, bool Truncated, IReadOnlyList<string> Warnings);
public static class DocumentValidation
{
    public static void Id(string id) { if(id.Length!=64 || !id.All(Uri.IsHexDigit))throw new ArgumentException("ID cartella non valido: usare cerca_cartelle."); }
    public static void Relative(string value)
    {
        if(value.Length>4096 || value.Contains(':') || value.StartsWith('/') || value.StartsWith('\\') || value.Any(char.IsControl)
            || value.Replace('\\','/').Split('/').Any(x=>x is "." or ".." || x.EndsWith('.') || x.EndsWith(' ')))throw new ArgumentException("Percorso relativo non valido.");
    }
    public static void Validate(DocumentQuery q)
    {
        Id(q.FolderId);Relative(q.RelativePath);if(q.ArchiveEntry!=null)Relative(q.ArchiveEntry);
        if(q.Offset<0 || q.Offset>200000 || q.Length is <1 or >24000 || (q.Offset>0 && q.Version==null))throw new ArgumentException("Pagina non valida: massimo 24000 caratteri, version obbligatoria dopo la prima pagina.");
        if(q.Version!=null && (q.Version.Length!=64 || !q.Version.All(Uri.IsHexDigit)))throw new ArgumentException("Versione documento non valida.");
    }
    public static void Validate(DocumentListQuery q)
    {
        Id(q.FolderId);Relative(q.RelativePath);if(q.ArchiveDirectory!=null)Relative(q.ArchiveDirectory);
        if(q.Offset<0 || q.Offset>1000000 || q.Limit is <1 or >100 || q.Name.Length>200 || q.Extension.Length>20 || q.Extension.IndexOfAny(['/', '\\', ':'])>=0)throw new ArgumentException("Filtro o pagina non validi.");
    }
}
