namespace TA_Gestione_Cartelle_Commesse.Services;

// Una data Win32 non rappresentabile non deve impedire il censimento di nomi e dimensioni.
public static class MetadataDates
{
    public static DateTime? Read(Func<DateTime?> read, string path, ICollection<string> issues)
    {
        try { return read(); }
        catch (ArgumentOutOfRangeException)
        {
            var issue = "Data ultima modifica non valida, valore non disponibile: " + path;
            if (!issues.Contains(issue)) issues.Add(issue);
            return null;
        }
    }
}
