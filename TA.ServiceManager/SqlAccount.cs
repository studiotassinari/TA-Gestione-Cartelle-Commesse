namespace TA.ServiceManager;

// Collegamento al database comune con GeCo: il servizio usa sempre un account SQL (utente e password),
// mai l'account Windows con cui gira. Gli utenti invece si riconoscono con il proprio account Windows.
public sealed record SqlAccount(string Server = "", string Database = "", string User = "", string Password = "")
{
    public bool Empty => Server.Length == 0 && Database.Length == 0 && User.Length == 0 && Password.Length == 0;

    // Legge una stringa di connessione salvata. Le chiavi equivalenti sono quelle accettate da SQL Server.
    public static SqlAccount Parse(string? connectionString)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in Pairs(connectionString ?? "")) values[key] = value;
        string Any(params string[] keys) => keys.Select(k => values.GetValueOrDefault(k, "")).FirstOrDefault(v => v.Length > 0) ?? "";
        return new(Any("Server", "Data Source", "Address", "Addr", "Network Address"), Any("Database", "Initial Catalog"), Any("User ID", "UID", "User"), Any("Password", "PWD"));
    }

    // Coppie chiave=valore separate da punto e virgola; un valore tra virgolette può contenere punti e virgola.
    private static IEnumerable<(string Key, string Value)> Pairs(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            int equals = text.IndexOf('=', i);
            if (equals < 0) yield break;
            string key = text[i..equals].Trim(' ', ';');
            i = equals + 1;
            while (i < text.Length && text[i] == ' ') i++;
            var value = new System.Text.StringBuilder();
            if (i < text.Length && text[i] is '"' or '\'')
            {
                char quote = text[i++];
                while (i < text.Length)
                {
                    if (text[i] == quote) { if (i + 1 < text.Length && text[i + 1] == quote) { value.Append(quote); i += 2; continue; } i++; break; }
                    value.Append(text[i++]);
                }
                while (i < text.Length && text[i] != ';') i++;
            }
            else
            {
                while (i < text.Length && text[i] != ';') value.Append(text[i++]);
                value = new System.Text.StringBuilder(value.ToString().Trim());
            }
            i++;
            if (key.Length > 0) yield return (key, value.ToString());
        }
    }

    private static string Quote(string value) => value.IndexOfAny(new[] { ';', '"', '\'', '=', ' ' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";

    public string ToConnectionString() => Empty ? "" :
        $"Server={Quote(Server.Trim())};Database={Quote(Database.Trim())};User ID={Quote(User.Trim())};Password={Quote(Password)};TrustServerCertificate=True";

    public IEnumerable<string> Problems()
    {
        if (Empty) yield break;
        if (Server.Trim().Length == 0 || Database.Trim().Length == 0) yield return "Database comune: indicare il server SQL e il nome del database.";
        if (User.Trim().Length == 0 || Password.Length == 0) yield return "Database comune: indicare utente e password dell'account SQL (il servizio non si collega con l'account Windows).";
    }
}
