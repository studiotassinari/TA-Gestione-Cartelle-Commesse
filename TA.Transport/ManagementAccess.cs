using System.Collections.Concurrent;
using System.Security.Principal;
namespace TA.Transport;

// Il confronto usa il SID del token autenticato, mai nomi o ruoli inviati dal client.
public static class ManagementAccess
{
    // Elenco vuoto finché il servizio non carica la configurazione: nessun account è scritto nel codice
    // e senza configurazione nessuno ottiene la gestione straordinaria.
    private static string[] accounts = [];
    public static IReadOnlyList<string> Accounts => Volatile.Read(ref accounts);
    private static readonly ConcurrentDictionary<string,string> Resolved = new();
    public static void Configure(IEnumerable<string>? values)
    {
        Volatile.Write(ref accounts, (values ?? Array.Empty<string>()).Select(v => (v ?? "").Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        Resolved.Clear();
    }
    public static bool Allows(WindowsIdentity identity) => identity.IsAuthenticated && AllowsSid(identity.User?.Value, Resolve);
    public static bool AllowsSid(string? sid, Func<string,string?> resolve) => sid!=null && Accounts.Any(account=>sid==resolve(account));
    private static string? Resolve(string account)
    {
        try {return Resolved.GetOrAdd(account,a=>((SecurityIdentifier)new NTAccount(a).Translate(typeof(SecurityIdentifier))).Value);}
        catch(IdentityNotMappedException){return null;}
        catch(SystemException){return null;}
    }
}
